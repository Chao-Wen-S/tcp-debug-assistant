using System.Net;
using System.Net.Sockets;
using System.Text;
using TcpDebugAssistant.Core;

namespace TcpDebugAssistant.App;

public sealed partial class MainForm
{
    /// <summary>
    /// 界面级自动化自检：直接在真实窗体控件上跑完整收发流程（客户端 / 服务端双模式）。
    /// 必须在窗体 Shown 之后、UI 线程上调用。
    /// </summary>
    internal async Task<(string Log, int Failed)> RunSelfTestAsync()
    {
        var sb = new StringBuilder();
        var failed = 0;

        void Check(bool ok, string name, string? detail = null)
        {
            if (ok)
            {
                sb.AppendLine("  PASS  " + name);
            }
            else
            {
                failed++;
                sb.AppendLine("  FAIL  " + name + (detail is null ? string.Empty : "  -> " + detail));
            }
        }

        static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 4000)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(30);
            }

            return condition();
        }

        static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        sb.AppendLine("TCP 调试助手 - 界面自检");
        sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine();
        sb.AppendLine("[客户端模式]");

        await using var echo = new TcpServerHost();
        echo.DataReceived += async (_, e) =>
        {
            try
            {
                await e.Peer.SendAsync(e.Data).ConfigureAwait(false);
            }
            catch
            {
                // 回显失败忽略
            }
        };
        echo.Start(0);

        _modeBox.SelectedIndex = 0;
        await Task.Delay(200);
        _recvBox.Clear();
        _sendFormatBox.SelectedIndex = 0;
        _lineEndingBox.SelectedIndex = 1;
        _timestampChk.Checked = false;
        _autoSendChk.Checked = false;
        _hostBox.Text = "127.0.0.1";
        _portBox.Value = echo.Port;

        await ToggleConnectionAsync();
        Check(await WaitForAsync(() => _client.IsConnected), "客户端连接回显服务器");
        Check(_connectBtn.Text == "断开", "连接成功后按钮变为“断开”", _connectBtn.Text);

        var sentBeforeText = Interlocked.Read(ref _totalSent);
        _sendBox.Text = "ping-中文-abcd";
        await SendCurrentAsync();
        Check(Interlocked.Read(ref _totalSent) == sentBeforeText + Encoding.UTF8.GetByteCount("ping-中文-abcd") + 2,
            "文本发送（UTF-8 + CRLF）字节数正确", "sent=" + (Interlocked.Read(ref _totalSent) - sentBeforeText));
        Check(await WaitForAsync(() => _recvBox.Text.Contains("ping-中文-abcd")),
            "服务端回显被完整接收（含中文）", Trunc(_recvBox.Text));

        var sentBeforeHex = Interlocked.Read(ref _totalSent);
        _sendFormatBox.SelectedIndex = 1;
        _sendBox.Text = "AA BB 0C 0D";
        await SendCurrentAsync();
        Check(Interlocked.Read(ref _totalSent) == sentBeforeHex + 4,
            "HEX 发送 4 字节", "sent=" + (Interlocked.Read(ref _totalSent) - sentBeforeHex));

        var sentBeforeBad = Interlocked.Read(ref _totalSent);
        _sendBox.Text = "ZZ";
        await SendCurrentAsync();
        Check(Interlocked.Read(ref _totalSent) == sentBeforeBad, "非法 HEX 被拒绝发送");
        Check(_stateLabel.Text.Contains("HEX"), "非法 HEX 提示可见", _stateLabel.Text);

        _sendFormatBox.SelectedIndex = 0;
        _lineEndingBox.SelectedIndex = 0;
        _sendBox.Text = "tick";
        var sentBeforeAuto = Interlocked.Read(ref _totalSent);
        _autoSendInterval.Value = 50;
        _autoSendChk.Checked = true;
        await Task.Delay(450);
        _autoSendChk.Checked = false;
        var autoDelta = Interlocked.Read(ref _totalSent) - sentBeforeAuto;
        Check(autoDelta >= 5, "定时发送持续发送", "delta=" + autoDelta);

        _client.Disconnect("自检结束");
        Check(await WaitForAsync(() => !_client.IsConnected), "主动断开后连接状态为未连接");
        Check(!_client.AutoReconnect, "主动断开不触发自动重连");

        sb.AppendLine();
        sb.AppendLine("[服务端模式]");
        _modeBox.SelectedIndex = 1;
        Check(await WaitForAsync(() => IsServerMode), "切换到服务端模式");
        Check(!_mainSplit.Panel1Collapsed, "服务端模式显示客户端列表");

        var listenPort = FreePort();
        _portBox.Value = listenPort;
        await ToggleConnectionAsync();
        Check(_server.IsRunning, "服务端开始监听");
        Check(_server.Port == listenPort, "监听端口与设置一致", _server.Port + " vs " + listenPort);
        Check(_connectBtn.Text == "停止监听", "监听中按钮变为“停止监听”", _connectBtn.Text);

        using var probe = new TcpClientHost();
        var probeBytes = new List<byte>();
        var probeLock = new object();
        probe.DataReceived += (_, data) =>
        {
            lock (probeLock)
            {
                probeBytes.AddRange(data);
            }
        };
        await probe.ConnectAsync("127.0.0.1", listenPort);
        Check(await WaitForAsync(() => _clientList.Items.Count == 1), "客户端接入后列表出现 1 行",
            "rows=" + _clientList.Items.Count);

        _recvBox.Clear();
        await probe.SendAsync(Encoding.UTF8.GetBytes("from-probe"));
        Check(await WaitForAsync(() => _recvBox.Text.Contains("from-probe")), "收到客户端数据",
            Trunc(_recvBox.Text));

        string TakeProbeText()
        {
            lock (probeLock)
            {
                var text = Encoding.UTF8.GetString(probeBytes.ToArray());
                probeBytes.Clear();
                return text;
            }
        }

        _sendFormatBox.SelectedIndex = 0;
        _lineEndingBox.SelectedIndex = 0;
        _broadcastChk.Checked = true;
        _sendBox.Text = "to-client";
        await SendCurrentAsync();
        Check(await WaitForAsync(() => TakeProbeText().Contains("to-client")), "广播发送送达客户端");

        if (_clientList.Items.Count > 0)
        {
            _clientList.Items[0].Selected = true;
        }

        _broadcastChk.Checked = false;
        _sendBox.Text = "single-shot";
        await SendCurrentAsync();
        Check(await WaitForAsync(() => TakeProbeText().Contains("single-shot")), "单发选中客户端送达");

        var peerId = _server.Clients.Count > 0 ? _server.Clients[0].Id : string.Empty;
        if (peerId.Length > 0)
        {
            _server.Disconnect(peerId, "自检断开");
        }

        Check(await WaitForAsync(() => _clientList.Items.Count == 0), "服务端断开客户端后列表清空",
            "rows=" + _clientList.Items.Count);

        await ToggleConnectionAsync();
        Check(await WaitForAsync(() => !_server.IsRunning), "停止监听成功");
        Check(_connectBtn.Text == "开始监听", "停止监听后按钮复位", _connectBtn.Text);

        sb.AppendLine();
        sb.AppendLine($"结果：通过 {CountPass(sb.ToString())}，失败 {failed}");
        return (sb.ToString(), failed);
    }

    private static int CountPass(string log)
    {
        var count = 0;
        foreach (var line in log.Split('\n'))
        {
            if (line.Contains("  PASS  "))
            {
                count++;
            }
        }

        return count;
    }

    private static string Trunc(string text)
    {
        var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= 120 ? flat : flat[..120] + "…";
    }

    /// <summary>供截图脚本使用：切换到服务端模式。</summary>
    internal void SelectServerMode() => _modeBox.SelectedIndex = 1;

    /// <summary>供截图脚本使用：填入演示数据，让界面截图有真实通讯内容。</summary>
    internal void ApplyDemoScenario(bool server)
    {
        if (server)
        {
            _stateLabel.Text = "监听中 0.0.0.0:9000";
            _stateLabel.Tone = PillTone.Success;
            _stateItem.Text = "监听中 0.0.0.0:9000";
            _connectBtn.Text = "停止监听";
            _portBox.Value = 9000;
            _recvBox.Text =
                "[09:41:02.118] [系统] 开始监听 0.0.0.0:9000，等待客户端接入…\r\n" +
                "[09:41:07.326] [系统] 客户端接入：C1 127.0.0.1:53114\r\n" +
                "[09:41:07.328] [系统] 客户端接入：C2 127.0.0.1:53115\r\n" +
                "[09:41:07.329] [系统] 客户端接入：C3 127.0.0.1:53116\r\n" +
                "[09:41:11.540] [C1 127.0.0.1:53114] {\"dev\":\"sensor-01\",\"temp\":23.5}\r\n" +
                "[09:41:11.742] [C2 127.0.0.1:53115] AT+VER?\r\n" +
                "[09:41:12.004] [C3 127.0.0.1:53116] 心跳包 #128\r\n" +
                "[09:41:13.276] [C1 127.0.0.1:53114] {\"dev\":\"sensor-01\",\"temp\":23.6}\r\n";
            _clientList.Items.Clear();
            AddDemoRow("C1", "127.0.0.1:53114", "1.2 KB", "560 B");
            AddDemoRow("C2", "127.0.0.1:53115", "24 B", "18 B");
            AddDemoRow("C3", "127.0.0.1:53116", "6.4 KB", "2.1 KB");
            _clientList.Items[0].Selected = true;
            _connItem.Text = "连接 3";
            Interlocked.Exchange(ref _totalSent, 2765);
            Interlocked.Exchange(ref _totalReceived, 7800);
            _tipItem.Text = "已广播 15 字节 → 3 个客户端";
        }
        else
        {
            _sendBox.Text = "hello tcp\r\n";
            _recvBox.Text =
                "[09:40:51.223] [系统] 已连接到 127.0.0.1:8080\r\n" +
                "[09:40:52.003] [服务器] hello tcp\r\n" +
                "[09:40:52.641] [服务器] ACK:4\r\n" +
                "[09:40:55.118] [服务器] 温度 23.5C 湿度 41%\r\n" +
                "[09:40:58.902] [服务器] 1A 2B 3C 4D 5E 6F\r\n";
            _stateLabel.Text = "已连接 127.0.0.1:8080";
            _stateLabel.Tone = PillTone.Success;
            _stateItem.Text = "已连接 127.0.0.1:8080";
            _connectBtn.Text = "断开";
            _connItem.Text = "连接 1";
            Interlocked.Exchange(ref _totalSent, 116);
            Interlocked.Exchange(ref _totalReceived, 96);
            _tipItem.Text = "已发送 11 字节 → 127.0.0.1:8080";
        }
    }

    private void AddDemoRow(string id, string remote, string rx, string tx)
    {
        var item = new ListViewItem(id) { Tag = id };
        item.SubItems.Add(remote);
        item.SubItems.Add(rx);
        item.SubItems.Add(tx);
        _clientList.Items.Add(item);
    }
}
