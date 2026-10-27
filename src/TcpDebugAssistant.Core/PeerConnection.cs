using System.Net;
using System.Net.Sockets;

namespace TcpDebugAssistant.Core;

public sealed class PeerDataEventArgs : EventArgs
{
    public PeerDataEventArgs(PeerConnection peer, byte[] data)
    {
        Peer = peer;
        Data = data;
    }

    public PeerConnection Peer { get; }

    public byte[] Data { get; }
}

public sealed class PeerClosedEventArgs : EventArgs
{
    public PeerClosedEventArgs(PeerConnection peer, string reason)
    {
        Peer = peer;
        Reason = reason;
    }

    public PeerConnection Peer { get; }

    public string Reason { get; }
}

/// <summary>
/// 一条 TCP 连接。服务端 accept 到的每个客户端、客户端模式连上的服务器，
/// 都用一个 PeerConnection 表示，收发统计与关闭语义都在这里统一。
/// </summary>
public sealed class PeerConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private long _bytesSent;
    private long _bytesReceived;
    private int _closed;
    private Task? _receiveTask;

    internal PeerConnection(TcpClient client, string id)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _stream = client.GetStream();
        Id = id;
        var remote = (IPEndPoint?)client.Client.RemoteEndPoint;
        RemoteAddress = remote?.Address.ToString() ?? "unknown";
        RemotePort = remote?.Port ?? 0;
        ConnectedAt = DateTimeOffset.Now;
    }

    public string Id { get; }

    public string RemoteAddress { get; }

    public int RemotePort { get; }

    public DateTimeOffset ConnectedAt { get; }

    public string RemoteEndPoint => $"{RemoteAddress}:{RemotePort}";

    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    public long BytesSent => Interlocked.Read(ref _bytesSent);

    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public event EventHandler<PeerDataEventArgs>? DataReceived;

    public event EventHandler<PeerClosedEventArgs>? Closed;

    internal void Start()
    {
        _receiveTask = Task.Run(ReceiveLoopAsync);
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var read = await _stream.ReadAsync(buffer.AsMemory(), _cts.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    Close("对端关闭了连接");
                    return;
                }

                Interlocked.Add(ref _bytesReceived, read);
                var chunk = buffer.AsSpan(0, read).ToArray();
                try
                {
                    DataReceived?.Invoke(this, new PeerDataEventArgs(this, chunk));
                }
                catch
                {
                    // 订阅方异常不影响接收循环。
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 主动关闭。
        }
        catch (IOException ex)
        {
            Close($"连接中断：{ex.Message}");
        }
        catch (SocketException ex)
        {
            Close($"Socket 错误：{ex.SocketErrorCode}");
        }
        catch (ObjectDisposedException)
        {
            // 已释放。
        }
        finally
        {
            Close(null);
        }
    }

    /// <summary>发送数据，返回本次写入的字节数。</summary>
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

        if (IsClosed)
        {
            throw new InvalidOperationException("连接已关闭，无法发送");
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(data.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _bytesSent, data.Length);
            return data.Length;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            Close($"发送失败：{ex.Message}");
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>幂等关闭，只有第一次调用会触发 Closed 事件。</summary>
    public void Close(string? reason = null)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
        {
            return;
        }

        try
        {
            _cts.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            _stream.Dispose();
        }
        catch
        {
            // ignore
        }

        try
        {
            _client.Close();
        }
        catch
        {
            // ignore
        }

        try
        {
            Closed?.Invoke(this, new PeerClosedEventArgs(this, reason ?? "连接已关闭"));
        }
        catch
        {
            // 订阅方异常不影响关闭流程。
        }
    }

    public void Dispose()
    {
        Close(null);
        _sendLock.Dispose();
    }

    public override string ToString() => $"{Id} {RemoteEndPoint}";
}

