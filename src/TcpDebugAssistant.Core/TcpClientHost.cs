using System.Net.Sockets;

namespace TcpDebugAssistant.Core;

/// <summary>
/// TCP 客户端：连接、收发、主动断开，可选断线自动重连。
/// </summary>
public sealed class TcpClientHost : IDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _lifetimeCts;
    private CancellationTokenSource? _sessionCts;
    private Task? _background;
    private long _bytesSent;
    private long _bytesReceived;
    private volatile bool _userClosed = true;
    private volatile bool _connected;

    public string Host { get; private set; } = "127.0.0.1";

    public int Port { get; private set; } = 8080;

    public bool IsConnected => _connected;

    /// <summary>断线后是否自动重连。</summary>
    public bool AutoReconnect { get; set; }

    /// <summary>重连间隔（毫秒）。</summary>
    public int ReconnectIntervalMs { get; set; } = 2000;

    public long BytesSent => Interlocked.Read(ref _bytesSent);

    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public event EventHandler? Connected;

    public event EventHandler<string>? Disconnected;

    public event EventHandler<byte[]>? DataReceived;

    /// <summary>自动重连失败一次时触发，用于在界面上提示但不刷屏。</summary>
    public event EventHandler<string>? ReconnectFailed;

    public string EndPoint => $"{Host}:{Port}";

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("服务器地址不能为空", nameof(host));
        }

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "端口必须在 1-65535 之间");
        }

        Teardown(raiseDisconnected: false);
        Interlocked.Exchange(ref _bytesSent, 0);
        Interlocked.Exchange(ref _bytesReceived, 0);
        Host = host.Trim();
        Port = port;
        _userClosed = false;
        _lifetimeCts = new CancellationTokenSource();

        var lifetime = _lifetimeCts.Token;
        await ConnectCoreAsync(lifetime, cancellationToken).ConfigureAwait(false);
        _background = Task.Run(() => WatchAsync(lifetime), CancellationToken.None);
    }

    private async Task ConnectCoreAsync(CancellationToken lifetime, CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, cancellationToken);
        try
        {
            await client.ConnectAsync(Host, Port, linked.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            client.Dispose();
            throw;
        }

        var stream = client.GetStream();
        _client = client;
        _stream = stream;
        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _connected = true;
        Connected?.Invoke(this, EventArgs.Empty);

        var sessionToken = _sessionCts.Token;
        _ = Task.Run(() => ReceiveLoopAsync(client, stream, sessionToken), CancellationToken.None);
    }

    private async Task ReceiveLoopAsync(TcpClient client, NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        string reason = "连接已关闭";
        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                if (read <= 0)
                {
                    reason = "对端关闭了连接";
                    break;
                }

                Interlocked.Add(ref _bytesReceived, read);
                var chunk = buffer.AsSpan(0, read).ToArray();
                try
                {
                    DataReceived?.Invoke(this, chunk);
                }
                catch
                {
                    // 忽略订阅方异常。
                }
            }
        }
        catch (OperationCanceledException)
        {
            reason = "已断开";
        }
        catch (IOException ex)
        {
            reason = $"连接中断：{ex.Message}";
        }
        catch (SocketException ex)
        {
            reason = $"Socket 错误：{ex.SocketErrorCode}";
        }
        catch (ObjectDisposedException)
        {
            reason = "已断开";
        }
        finally
        {
            try
            {
                stream.Dispose();
            }
            catch
            {
                // ignore
            }

            client.Dispose();

            if (_stream == stream)
            {
                _stream = null;
                _client = null;
                _connected = false;
                Disconnected?.Invoke(this, reason);
            }
        }
    }

    /// <summary>连接意外断开时按间隔尝试重连，直到用户主动断开或连接恢复。</summary>
    private async Task WatchAsync(CancellationToken lifetime)
    {
        while (!lifetime.IsCancellationRequested)
        {
            await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
            if (_connected || _userClosed)
            {
                continue;
            }

            if (!AutoReconnect)
            {
                await Task.Delay(300, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            while (!lifetime.IsCancellationRequested && !_userClosed && !_connected)
            {
                try
                {
                    await Task.Delay(ReconnectIntervalMs, lifetime).ConfigureAwait(false);
                    if (_userClosed || _connected)
                    {
                        break;
                    }

                    await ConnectCoreAsync(lifetime, CancellationToken.None).ConfigureAwait(false);
                    break;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ReconnectFailed?.Invoke(this, ex.Message);
                }
            }
        }
    }

    public async Task<int> SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (data.Length == 0)
        {
            return 0;
        }

        if (!_connected || _stream is null)
        {
            throw new InvalidOperationException("尚未连接服务器");
        }

        var stream = _stream;
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(data.AsMemory(), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _bytesSent, data.Length);
            return data.Length;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            Teardown(raiseDisconnected: true, $"发送失败：{ex.Message}");
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>用户主动断开：不再自动重连。</summary>
    public void Disconnect(string reason = "已主动断开")
    {
        _userClosed = true;
        Teardown(raiseDisconnected: true, reason);
    }

    private void Teardown(bool raiseDisconnected, string? reason = null)
    {
        var wasConnected = _connected;
        _connected = false;

        try
        {
            _lifetimeCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            _sessionCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            _stream?.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _client?.Dispose();
        }
        catch
        {
            // ignore
        }

        _stream = null;
        _client = null;
        _sessionCts = null;
        _lifetimeCts = null;

        if (raiseDisconnected && wasConnected)
        {
            Disconnected?.Invoke(this, reason ?? "已断开");
        }
    }

    public void Dispose()
    {
        _userClosed = true;
        Teardown(raiseDisconnected: false);
        _sendLock.Dispose();
    }
}

