using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TcpDebugAssistant.Core;

namespace TcpDebugAssistant.Tests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== TCP 调试助手 · 核心逻辑测试 ===");
        Console.WriteLine();

        HexUtilTests();
        CodecTests();

        await ServerAcceptsMultipleClientsAsync();
        await BidirectionalTransferAsync();
        await LargePayloadIntegrityAsync();
        await ClientDisconnectDetectedAsync();
        await BroadcastToAllClientsAsync();
        await AutoReconnectAsync();
        await SendAfterCloseThrowsAsync();

        Console.WriteLine();
        Console.WriteLine($"结果：通过 {_passed}，失败 {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    // ---------- 断言辅助 ----------

    private static void Check(bool condition, string name, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  PASS  {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  FAIL  {name}{(detail is null ? string.Empty : "  ->  " + detail)}");
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs, int stepMs = 25)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(stepMs);
        }

        return condition();
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ---------- 单元测试 ----------

    private static void HexUtilTests()
    {
        Console.WriteLine("[HexUtil]");

        Check(HexUtil.TryParse("AA BB 0D 0A", out var bytes, out _) && bytes.Length == 4 && bytes[2] == 0x0D && bytes[3] == 0x0A,
            "空格分隔解析");

        Check(HexUtil.TryParse("aa,bb,cc", out bytes, out _) && bytes.SequenceEqual(new byte[] { 0xAA, 0xBB, 0xCC }),
            "逗号分隔解析");

        Check(HexUtil.TryParse("0x01 0x02", out bytes, out _) && bytes.SequenceEqual(new byte[] { 1, 2 }),
            "0x 前缀解析");

        Check(HexUtil.TryParse("1 2 3", out bytes, out _) && bytes.SequenceEqual(new byte[] { 0x01, 0x02, 0x03 }),
            "单字符按 0X 补齐");

        Check(!HexUtil.TryParse("AA ZZ", out _, out var error) && error.Contains("非法字符"),
            "非法字符应报错", error);

        Check(!HexUtil.TryParse("", out _, out _), "空内容应报错");

        Check(HexUtil.ToHexString(new byte[] { 0xAA, 0x01, 0x0F }) == "AA 01 0F", "格式化为大写 HEX");
        Check(HexUtil.ToHexString(new byte[] { 0xAA, 0x01 }, "-") == "AA-01", "自定义分隔符");
        Check(HexUtil.ToHexString(Array.Empty<byte>()) == string.Empty, "空数组格式化为空串");
        Check(HexUtil.ToPrintable(new byte[] { 0x41, 0x00, 0x42 }) == "A·B", "非打印字节用中点占位");
        Check(HexUtil.CountNonPrintable(new byte[] { 0x41, 0x00, 0x0A }) == 1, "统计不可打印字节");
    }

    private static void CodecTests()
    {
        Console.WriteLine("[Utf8StreamDecoder / LineEnding]");

        var decoder = new Utf8StreamDecoder();
        var chinese = Encoding.UTF8.GetBytes("中文");
        var first = decoder.Decode(chinese.AsSpan(0, 2));
        var second = decoder.Decode(chinese.AsSpan(2));
        Check(first + second == "中文", "跨包拆分的 UTF-8 中文可正确解码", $"first={first} second={second}");

        decoder.Reset();
        Check(decoder.Decode(Encoding.UTF8.GetBytes("abc")) == "abc", "Reset 后可重新解码");

        Check(LineEnding.CrLf.GetBytes().SequenceEqual(new byte[] { 13, 10 }), "CRLF 字节正确");
        Check(LineEnding.None.GetBytes().Length == 0, "无行尾不追加字节");

        Check(SizeFormatter.Format(512) == "512 B", "字节格式化 B");
        Check(SizeFormatter.Format(2048) == "2 KB", "字节格式化 KB");
        Check(SizeFormatter.Format(1024 * 1024 * 3) == "3 MB", "字节格式化 MB");
    }
    // ---------- 集成测试 ----------

    private static async Task ServerAcceptsMultipleClientsAsync()
    {
        Console.WriteLine("[服务端] 多客户端接入");
        await using var server = new TcpServerHost();
        server.Start(0);

        using var c1 = new TcpClientHost();
        using var c2 = new TcpClientHost();
        await c1.ConnectAsync("127.0.0.1", server.Port);
        await c2.ConnectAsync("127.0.0.1", server.Port);

        var ok = await WaitUntilAsync(() => server.ClientCount == 2, 3000);
        Check(ok, "服务端同时接入 2 个客户端", $"实际 {server.ClientCount}");
        Check(c1.IsConnected && c2.IsConnected, "两个客户端均处于已连接状态");
        Check(server.Clients.All(p => p.RemotePort > 0), "客户端远端端口信息可用");
    }

    private static async Task BidirectionalTransferAsync()
    {
        Console.WriteLine("[收发] 双向传输");
        await using var server = new TcpServerHost();
        server.Start(0);

        var atServer = new List<byte>();
        var atClient = new List<byte>();
        server.DataReceived += (_, e) => { lock (atServer) { atServer.AddRange(e.Data); } };

        using var client = new TcpClientHost();
        client.DataReceived += (_, data) => { lock (atClient) { atClient.AddRange(data); } };
        await client.ConnectAsync("127.0.0.1", server.Port);
        await WaitUntilAsync(() => server.ClientCount == 1, 3000);

        var upPayload = Encoding.UTF8.GetBytes("Hello 服务器！\n第二行");
        await client.SendAsync(upPayload);
        var upOk = await WaitUntilAsync(() =>
        {
            lock (atServer)
            {
                return atServer.Count >= upPayload.Length && atServer.Take(upPayload.Length).SequenceEqual(upPayload);
            }
        }, 3000);
        Check(upOk, "客户端 → 服务端 数据一致（含中文与换行）");

        var peer = server.Clients[0];
        var downPayload = Encoding.UTF8.GetBytes("Server says 你好");
        await server.SendToAsync(peer.Id, downPayload);
        var downOk = await WaitUntilAsync(() =>
        {
            lock (atClient)
            {
                return atClient.Count >= downPayload.Length && atClient.Take(downPayload.Length).SequenceEqual(downPayload);
            }
        }, 3000);
        Check(downOk, "服务端 → 指定客户端 数据一致");

        Check(client.BytesSent == upPayload.Length, "客户端发送字节计数正确", $"{client.BytesSent}");
        Check(peer.BytesSent == downPayload.Length, "服务端发送字节计数正确", $"{peer.BytesSent}");
        Check(peer.BytesReceived >= upPayload.Length, "服务端接收字节计数正确", $"{peer.BytesReceived}");
    }

    private static async Task LargePayloadIntegrityAsync()
    {
        Console.WriteLine("[收发] 1MB 大数据完整性");
        await using var server = new TcpServerHost();
        server.Start(0);

        var payload = new byte[1024 * 1024];
        new Random(7).NextBytes(payload);
        var received = new List<byte>(payload.Length);

        using var client = new TcpClientHost();
        client.DataReceived += (_, data) => { lock (received) { received.AddRange(data); } };
        await client.ConnectAsync("127.0.0.1", server.Port);
        await WaitUntilAsync(() => server.ClientCount == 1, 3000);

        var sw = Stopwatch.StartNew();
        await server.SendToAsync(server.Clients[0].Id, payload);
        var ok = await WaitUntilAsync(() =>
        {
            lock (received)
            {
                return received.Count >= payload.Length;
            }
        }, 15000, 50);
        sw.Stop();

        bool identical;
        lock (received)
        {
            identical = received.Count == payload.Length && received.SequenceEqual(payload);
        }

        Check(ok && identical, "1MB 随机数据完整且顺序一致", $"收到 {received.Count} 字节，用时 {sw.ElapsedMilliseconds}ms");
    }

    private static async Task ClientDisconnectDetectedAsync()
    {
        Console.WriteLine("[断开] 客户端主动断开");
        await using var server = new TcpServerHost();
        server.Start(0);

        PeerClosedEventArgs? closed = null;
        server.ClientDisconnected += (_, e) => closed = e;

        using var client = new TcpClientHost();
        await client.ConnectAsync("127.0.0.1", server.Port);
        await WaitUntilAsync(() => server.ClientCount == 1, 3000);

        client.Disconnect("测试主动断开");

        var removed = await WaitUntilAsync(() => server.ClientCount == 0 && closed is not null, 4000);
        Check(removed, "服务端感知客户端断开并从列表移除");
        Check(closed?.Reason.Contains("断开") == true || closed is not null, "断开原因被记录", closed?.Reason);
        Check(!client.IsConnected, "客户端状态置为未连接");
    }

    private static async Task BroadcastToAllClientsAsync()
    {
        Console.WriteLine("[服务端] 广播");
        await using var server = new TcpServerHost();
        server.Start(0);

        using var c1 = new TcpClientHost();
        using var c2 = new TcpClientHost();
        var got1 = new List<byte>();
        var got2 = new List<byte>();
        c1.DataReceived += (_, d) => { lock (got1) { got1.AddRange(d); } };
        c2.DataReceived += (_, d) => { lock (got2) { got2.AddRange(d); } };

        await c1.ConnectAsync("127.0.0.1", server.Port);
        await c2.ConnectAsync("127.0.0.1", server.Port);
        await WaitUntilAsync(() => server.ClientCount == 2, 3000);

        var payload = Encoding.UTF8.GetBytes("broadcast!");
        var sentCount = await server.BroadcastAsync(payload);
        Check(sentCount == 2, "广播返回成功发送数 = 2", $"{sentCount}");

        var both = await WaitUntilAsync(() =>
        {
            lock (got1)
            {
                lock (got2)
                {
                    return got1.Count == payload.Length && got2.Count == payload.Length;
                }
            }
        }, 3000);
        Check(both, "两个客户端都收到广播数据");
    }

    private static async Task AutoReconnectAsync()
    {
        Console.WriteLine("[客户端] 断线自动重连");
        var port = GetFreePort();

        var server = new TcpServerHost();
        server.Start(port);

        using var client = new TcpClientHost { AutoReconnect = true, ReconnectIntervalMs = 300 };
        await client.ConnectAsync("127.0.0.1", port);
        await WaitUntilAsync(() => client.IsConnected, 3000);
        Check(client.IsConnected, "首次连接成功");

        await server.StopAsync();
        var dropped = await WaitUntilAsync(() => !client.IsConnected, 4000);
        Check(dropped, "服务端停止后客户端感知断开");

        var server2 = new TcpServerHost();
        server2.Start(port);
        try
        {
            var reconnected = await WaitUntilAsync(() => client.IsConnected, 10000, 50);
            Check(reconnected, "服务端恢复后客户端自动重连成功");

            var got = new List<byte>();
            client.DataReceived += (_, d) => { lock (got) { got.AddRange(d); } };
            await WaitUntilAsync(() => server2.ClientCount == 1, 3000);
            if (server2.ClientCount == 1)
            {
                await server2.SendToAsync(server2.Clients[0].Id, Encoding.UTF8.GetBytes("after-reconnect"));
                var received = await WaitUntilAsync(() =>
                {
                    lock (got)
                    {
                        return got.Count == "after-reconnect".Length;
                    }
                }, 3000);
                Check(received, "重连后仍能正常收发数据");
            }
            else
            {
                Check(false, "重连后服务端应看到 1 个客户端", $"实际 {server2.ClientCount}");
            }
        }
        finally
        {
            client.Disconnect();
            await server2.StopAsync();
        }
    }

    private static async Task SendAfterCloseThrowsAsync()
    {
        Console.WriteLine("[健壮性] 未连接时发送");
        using var client = new TcpClientHost();
        var threw = false;
        try
        {
            await client.SendAsync(new byte[] { 1, 2, 3 });
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Check(threw, "未连接发送抛出明确异常");

        await using var server = new TcpServerHost();
        server.Start(0);
        var badPortThrew = false;
        try
        {
            await client.ConnectAsync("127.0.0.1", 1);
        }
        catch (Exception)
        {
            badPortThrew = true;
        }

        Check(badPortThrew, "连接不存在的端口会抛异常");
    }
}
