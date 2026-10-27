using System.Net;
using System.Net.Sockets;

namespace TcpDebugAssistant.Core;

/// <summary>
/// TCP 服务端：监听端口、接受多个客户端、单播 / 广播。
/// </summary>
public sealed class TcpServerHost : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<PeerConnection> _peers = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private int _counter;

    public event EventHandler<PeerConnection>? ClientConnected;

    public event EventHandler<PeerClosedEventArgs>? ClientDisconnected;

    public event EventHandler<PeerDataEventArgs>? DataReceived;

    public bool IsRunning => _listener is not null;

    public int Port { get; private set; }

    public IReadOnlyList<PeerConnection> Clients
    {
        get
        {
            lock (_gate)
            {
                return _peers.ToArray();
            }
        }
    }

    public int ClientCount
    {
        get
        {
            lock (_gate)
            {
                return _peers.Count;
            }
        }
    }

    /// <summary>开始监听。port 传 0 表示由系统分配随机端口（返回值从 Port 读取）。</summary>
    public void Start(int port, IPAddress? bindAddress = null)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("服务端已经在监听中");
        }

        var listener = new TcpListener(bindAddress ?? IPAddress.Any, port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start();

        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                RaiseError($"监听异常：{ex.SocketErrorCode}");
                continue;
            }

            client.NoDelay = true;
            var peer = new PeerConnection(client, "C" + Interlocked.Increment(ref _counter));
            peer.DataReceived += OnPeerData;
            peer.Closed += OnPeerClosed;

            lock (_gate)
            {
                _peers.Add(peer);
            }

            peer.Start();
            ClientConnected?.Invoke(this, peer);
        }
    }

    private void OnPeerData(object? sender, PeerDataEventArgs e) => DataReceived?.Invoke(this, e);

    private void OnPeerClosed(object? sender, PeerClosedEventArgs e)
    {
        if (sender is PeerConnection peer)
        {
            lock (_gate)
            {
                _peers.Remove(peer);
            }

            ClientDisconnected?.Invoke(this, e);
        }
    }

    public event EventHandler<string>? Error;

    private void RaiseError(string message) => Error?.Invoke(this, message);

    /// <summary>向指定客户端发送数据。</summary>
    public async Task<int> SendToAsync(string clientId, byte[] data, CancellationToken cancellationToken = default)
    {
        var peer = Clients.FirstOrDefault(p => p.Id == clientId)
                   ?? throw new InvalidOperationException($"客户端 {clientId} 不存在或已断开");
        return await peer.SendAsync(data, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>广播给所有在线客户端，返回成功发送的数量。</summary>
    public async Task<int> BroadcastAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        var peers = Clients;
        var sent = 0;
        foreach (var peer in peers)
        {
            try
            {
                await peer.SendAsync(data, cancellationToken).ConfigureAwait(false);
                sent++;
            }
            catch
            {
                // 单个客户端失败不影响其它客户端。
            }
        }

        return sent;
    }

    /// <summary>断开指定客户端。</summary>
    public void Disconnect(string clientId, string reason = "服务端主动断开")
    {
        var peer = Clients.FirstOrDefault(p => p.Id == clientId);
        peer?.Close(reason);
    }

    public void DisconnectAll(string reason = "服务端已停止")
    {
        foreach (var peer in Clients)
        {
            peer.Close(reason);
        }

        lock (_gate)
        {
            _peers.Clear();
        }
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch
        {
            // ignore
        }

        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        DisconnectAll();

        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _acceptTask = null;
        Port = 0;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
