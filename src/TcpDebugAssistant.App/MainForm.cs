using System.Net;
using System.Text;
using TcpDebugAssistant.Core;

namespace TcpDebugAssistant.App;

public sealed partial class MainForm : Form
{
    private const int MaxLogChars = 400_000;
    private const int TrimToChars = 200_000;

    private readonly TcpServerHost _server = new();
    private readonly TcpClientHost _client = new();
    private readonly Dictionary<string, Utf8StreamDecoder> _peerDecoders = new();
    private readonly Utf8StreamDecoder _clientDecoder = new();
    private readonly StringBuilder _pendingText = new();
    private readonly object _pendingLock = new();
    private readonly System.Windows.Forms.Timer _flushTimer = new() { Interval = 60 };
    private readonly System.Windows.Forms.Timer _autoSendTimer = new();

    private long _totalSent;
    private long _totalReceived;
    private bool _switchingMode;

    private readonly ComboBox _modeBox = new();
    private readonly TextBox _hostBox = new();
    private readonly NumericUpDown _portBox = new();
    private readonly Button _connectBtn = new();
    private readonly Label _stateLabel = new();
    private readonly CheckBox _autoReconnectChk = new();
    private readonly NumericUpDown _reconnectBox = new();

    private readonly SplitContainer _mainSplit = new();
    private readonly ListView _clientList = new();
    private readonly CheckBox _broadcastChk = new();
    private readonly Label _clientHint = new();

    private readonly ComboBox _recvFormatBox = new();
    private readonly CheckBox _timestampChk = new();
    private readonly CheckBox _autoScrollChk = new();
    private readonly RichTextBox _recvBox = new();

    private readonly ComboBox _sendFormatBox = new();
    private readonly ComboBox _lineEndingBox = new();
    private readonly NumericUpDown _autoSendInterval = new();
    private readonly CheckBox _autoSendChk = new();
    private readonly TextBox _sendBox = new();
    private readonly Button _sendBtn = new();

    private readonly ToolStripStatusLabel _stateItem = new();
    private readonly ToolStripStatusLabel _txItem = new();
    private readonly ToolStripStatusLabel _rxItem = new();
    private readonly ToolStripStatusLabel _connItem = new();
    private readonly ToolStripStatusLabel _tipItem = new();

    public MainForm()
    {
        Text = "TCP 调试助手 v1.0";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(940, 620);
        Font = new Font("Microsoft YaHei UI", 9F);
        DoubleBuffered = true;

        BuildTopPanel();
        BuildMainArea();
        BuildStatusStrip();
        // Dock 布局按 z 序反向处理：Fill 面板必须排在最后，否则会吞掉顶部/底部面板的空间。
        Controls.SetChildIndex(_mainSplit, 0);
        WireEvents();

        _modeBox.SelectedIndex = 0;
        ApplyMode();

        _flushTimer.Tick += (_, _) => FlushPending();
        _flushTimer.Start();
    }
    private void BuildTopPanel()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(10, 8, 10, 6), BackColor = Color.FromArgb(246, 247, 250) };

        var modeLabel = new Label { Text = "模式", AutoSize = true, Location = new Point(12, 14) };
        _modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeBox.Items.AddRange(new object[] { "TCP 客户端", "TCP 服务端" });
        _modeBox.Location = new Point(52, 11);
        _modeBox.Width = 120;

        var hostLabel = new Label { Text = "服务器地址", AutoSize = true, Location = new Point(190, 14) };
        _hostBox.Location = new Point(268, 11);
        _hostBox.Width = 170;
        _hostBox.Text = "127.0.0.1";

        var portLabel = new Label { Text = "端口", AutoSize = true, Location = new Point(452, 14) };
        _portBox.Location = new Point(490, 11);
        _portBox.Width = 90;
        _portBox.Minimum = 1;
        _portBox.Maximum = 65535;
        _portBox.Value = 8080;

        _connectBtn.Text = "连接";
        _connectBtn.Location = new Point(596, 10);
        _connectBtn.Size = new Size(108, 30);
        _connectBtn.Font = new Font(Font, FontStyle.Bold);

        _stateLabel.Text = "未连接";
        _stateLabel.AutoSize = true;
        _stateLabel.Location = new Point(716, 16);
        _stateLabel.ForeColor = Color.FromArgb(120, 120, 120);
        _stateLabel.Font = new Font(Font, FontStyle.Bold);

        _autoReconnectChk.Text = "断线自动重连";
        _autoReconnectChk.AutoSize = true;
        _autoReconnectChk.Location = new Point(52, 52);

        var reconnectLabel = new Label { Text = "间隔", AutoSize = true, Location = new Point(172, 54) };
        _reconnectBox.Location = new Point(208, 51);
        _reconnectBox.Width = 80;
        _reconnectBox.Minimum = 200;
        _reconnectBox.Maximum = 600000;
        _reconnectBox.Increment = 500;
        _reconnectBox.Value = 2000;

        var reconnectMs = new Label { Text = "ms", AutoSize = true, Location = new Point(292, 54) };

        var tip = new Label
        {
            Text = "客户端模式主动连接服务器；服务端模式监听本地端口，可同时接入多个客户端，支持单发与广播。",
            AutoSize = true,
            Location = new Point(330, 54),
            ForeColor = Color.FromArgb(110, 110, 110)
        };

        top.Controls.AddRange(new Control[]
        {
            modeLabel, _modeBox, hostLabel, _hostBox, portLabel, _portBox, _connectBtn, _stateLabel,
            _autoReconnectChk, reconnectLabel, _reconnectBox, reconnectMs, tip
        });

        Controls.Add(top);
        _topPanel = top;
    }

    private Panel _topPanel = new();
    private void BuildMainArea()
    {
        _mainSplit.Dock = DockStyle.Fill;
        _mainSplit.Orientation = Orientation.Vertical;
        _mainSplit.SplitterWidth = 6;
        // 先给一个足够大的初始尺寸，否则默认 150x150 会与 MinSize 冲突，设置 MinSize 时抛 SplitterDistance 异常。
        _mainSplit.Size = new Size(1160, 640);
        _mainSplit.Panel1MinSize = 250;
        _mainSplit.Panel2MinSize = 480;

        // ---- 左侧：客户端列表 ----
        var leftPanel = _mainSplit.Panel1;
        leftPanel.Padding = new Padding(8);

        _clientList.Dock = DockStyle.Fill;
        _clientList.View = View.Details;
        _clientList.FullRowSelect = true;
        _clientList.GridLines = true;
        _clientList.HideSelection = false;
        _clientList.MultiSelect = false;
        _clientList.Columns.Add("客户端", 60);
        _clientList.Columns.Add("远端地址", 150);
        _clientList.Columns.Add("接收", 64, HorizontalAlignment.Right);
        _clientList.Columns.Add("发送", 64, HorizontalAlignment.Right);

        var leftTitle = new Label
        {
            Text = "已连接客户端",
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var clientButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0)
        };

        var disconnectBtn = new Button { Text = "断开选中", Width = 86, Height = 28 };
        disconnectBtn.Click += (_, _) => DisconnectSelectedClient();
        var disconnectAllBtn = new Button { Text = "全部断开", Width = 86, Height = 28 };
        disconnectAllBtn.Click += (_, _) => _server.DisconnectAll("服务端主动断开全部客户端");
        _broadcastChk.Text = "广播发送";
        _broadcastChk.Checked = true;
        _broadcastChk.AutoSize = true;
        _broadcastChk.Margin = new Padding(8, 8, 0, 0);
        clientButtons.Controls.AddRange(new Control[] { disconnectBtn, disconnectAllBtn, _broadcastChk });

        _clientHint.Text = "双击列表项可直接断开该客户端。未选中时按广播规则发送。";
        _clientHint.Dock = DockStyle.Bottom;
        _clientHint.Height = 34;
        _clientHint.ForeColor = Color.FromArgb(120, 120, 120);

        leftPanel.Controls.Add(_clientList);
        leftPanel.Controls.Add(leftTitle);
        leftPanel.Controls.Add(clientButtons);
        leftPanel.Controls.Add(_clientHint);

        // ---- 右侧：接收区 + 发送区 ----
        var rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            Size = new Size(840, 600)
        };
        rightSplit.Panel1MinSize = 160;
        rightSplit.Panel2MinSize = 150;
        rightSplit.SplitterDistance = 340;

        var recvGroup = BuildReceiveGroup();
        rightSplit.Panel1.Controls.Add(recvGroup);

        var sendGroup = BuildSendGroup();
        rightSplit.Panel2.Controls.Add(sendGroup);

        _mainSplit.Panel2.Controls.Add(rightSplit);

        Controls.Add(_mainSplit);
        _rightSplit = rightSplit;
    }

    private SplitContainer _rightSplit = new();
    private GroupBox BuildReceiveGroup()
    {
        var group = new GroupBox { Text = "接收区", Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 8) };

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };

        _recvFormatBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _recvFormatBox.Items.AddRange(new object[] { "文本", "HEX" });
        _recvFormatBox.SelectedIndex = 0;
        _recvFormatBox.Width = 74;
        _recvFormatBox.Margin = new Padding(0, 4, 8, 0);

        _timestampChk.Text = "时间戳";
        _timestampChk.Checked = true;
        _timestampChk.AutoSize = true;
        _timestampChk.Margin = new Padding(0, 8, 10, 0);

        _autoScrollChk.Text = "自动滚动";
        _autoScrollChk.Checked = true;
        _autoScrollChk.AutoSize = true;
        _autoScrollChk.Margin = new Padding(0, 8, 10, 0);

        var clearBtn = new Button { Text = "清空", Width = 64, Height = 26, Margin = new Padding(0, 2, 8, 0) };
        clearBtn.Click += (_, _) => _recvBox.Clear();

        var saveBtn = new Button { Text = "保存日志", Width = 90, Height = 26, Margin = new Padding(0, 2, 0, 0) };
        saveBtn.Click += (_, _) => SaveLog();

        var formatLabel = new Label { Text = "显示", AutoSize = true, Margin = new Padding(0, 9, 6, 0) };

        toolbar.Controls.AddRange(new Control[] { formatLabel, _recvFormatBox, _timestampChk, _autoScrollChk, clearBtn, saveBtn });

        _recvBox.Dock = DockStyle.Fill;
        _recvBox.ReadOnly = true;
        _recvBox.BackColor = Color.White;
        _recvBox.Font = new Font("Consolas", 9.75F);
        _recvBox.WordWrap = true;
        _recvBox.DetectUrls = false;
        _recvBox.HideSelection = false;

        group.Controls.Add(_recvBox);
        group.Controls.Add(toolbar);
        return group;
    }

    private GroupBox BuildSendGroup()
    {
        var group = new GroupBox { Text = "发送区", Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 8) };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46 };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };

        _sendFormatBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _sendFormatBox.Items.AddRange(new object[] { "文本", "HEX" });
        _sendFormatBox.SelectedIndex = 0;
        _sendFormatBox.Width = 74;
        _sendFormatBox.Margin = new Padding(0, 11, 8, 0);

        _lineEndingBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _lineEndingBox.Items.AddRange(new object[] { "无行尾", "CRLF (\\r\\n)", "LF (\\n)", "CR (\\r)" });
        _lineEndingBox.SelectedIndex = 1;
        _lineEndingBox.Width = 108;
        _lineEndingBox.Margin = new Padding(0, 11, 12, 0);

        var sendFormatLabel = new Label { Text = "编码", AutoSize = true, Margin = new Padding(0, 15, 6, 0) };
        var lineEndingLabel = new Label { Text = "行尾", AutoSize = true, Margin = new Padding(0, 15, 6, 0) };

        _autoSendInterval.Minimum = 10;
        _autoSendInterval.Maximum = 600000;
        _autoSendInterval.Increment = 100;
        _autoSendInterval.Value = 1000;
        _autoSendInterval.Width = 76;
        _autoSendInterval.Margin = new Padding(0, 11, 4, 0);

        var msLabel = new Label { Text = "ms", AutoSize = true, Margin = new Padding(0, 15, 8, 0) };

        _autoSendChk.Text = "定时发送";
        _autoSendChk.AutoSize = true;
        _autoSendChk.Margin = new Padding(0, 14, 8, 0);

        var clearSendBtn = new Button { Text = "清空", Width = 60, Height = 28, Margin = new Padding(0, 6, 0, 0) };
        clearSendBtn.Click += (_, _) => _sendBox.Clear();

        _sendBtn.Text = "发送 (Ctrl+Enter)";
        _sendBtn.Dock = DockStyle.Right;
        _sendBtn.Width = 150;
        _sendBtn.Font = new Font(Font, FontStyle.Bold);
        _sendBtn.Click += async (_, _) => await SendCurrentAsync();

        flow.Controls.AddRange(new Control[]
        {
            sendFormatLabel, _sendFormatBox, lineEndingLabel, _lineEndingBox,
            _autoSendInterval, msLabel, _autoSendChk, clearSendBtn
        });

        bottom.Controls.Add(flow);
        bottom.Controls.Add(_sendBtn);

        _sendBox.Dock = DockStyle.Fill;
        _sendBox.Multiline = true;
        _sendBox.ScrollBars = ScrollBars.Vertical;
        _sendBox.Font = new Font("Consolas", 10F);
        _sendBox.AcceptsReturn = true;
        _sendBox.Text = "Hello TCP!";

        group.Controls.Add(_sendBox);
        group.Controls.Add(bottom);
        return group;
    }

    private void BuildStatusStrip()
    {
        var strip = new StatusStrip { SizingGrip = true, BackColor = Color.FromArgb(246, 247, 250) };
        _stateItem.Text = "就绪";
        _txItem.Text = "发送 0 B";
        _rxItem.Text = "接收 0 B";
        _connItem.Text = "连接 0";
        _tipItem.Spring = true;
        _tipItem.TextAlign = ContentAlignment.MiddleRight;
        _tipItem.ForeColor = Color.FromArgb(120, 120, 120);
        _tipItem.Text = "Ctrl+Enter 发送 · F5 连接/监听";

        strip.Items.AddRange(new ToolStripItem[] { _stateItem, new ToolStripSeparator(), _txItem, new ToolStripSeparator(), _rxItem, new ToolStripSeparator(), _connItem, _tipItem });
        Controls.Add(strip);
    }
    private void WireEvents()
    {
        _modeBox.SelectedIndexChanged += async (_, _) => await OnModeChangedAsync();
        _connectBtn.Click += async (_, _) => await ToggleConnectionAsync();
        _autoReconnectChk.CheckedChanged += (_, _) => _client.AutoReconnect = _autoReconnectChk.Checked;
        _reconnectBox.ValueChanged += (_, _) => _client.ReconnectIntervalMs = (int)_reconnectBox.Value;
        _autoSendChk.CheckedChanged += (_, _) => ToggleAutoSend();
        _autoSendInterval.ValueChanged += (_, _) => _autoSendTimer.Interval = (int)_autoSendInterval.Value;
        _autoSendTimer.Tick += async (_, _) => await SendCurrentAsync();

        _sendBox.KeyDown += async (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SendCurrentAsync();
            }
        };

        KeyPreview = true;
        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                e.SuppressKeyPress = true;
                await ToggleConnectionAsync();
            }
        };

        _server.ClientConnected += (_, peer) => RunOnUi(() =>
        {
            AddClientRow(peer);
            AppendSystem($"客户端接入：{peer.Id} {peer.RemoteEndPoint}");
            UpdateConnectionCount();
        });

        _server.ClientDisconnected += (_, e) => RunOnUi(() =>
        {
            RemoveClientRow(e.Peer.Id);
            AppendSystem($"客户端断开：{e.Peer.Id} {e.Peer.RemoteEndPoint}（{e.Reason}）");
            UpdateConnectionCount();
        });

        _server.DataReceived += (_, e) =>
        {
            Interlocked.Add(ref _totalReceived, e.Data.Length);
            FormatIncoming(e.Data, $"{e.Peer.Id} {e.Peer.RemoteEndPoint}", GetPeerDecoder(e.Peer.Id));
        };

        _server.Error += (_, message) => RunOnUi(() => SetState($"监听异常：{message}", false));

        _client.Connected += (_, _) => RunOnUi(() =>
        {
            SetState($"已连接 {_client.EndPoint}", true);
            _connectBtn.Text = "断开";
            UpdateConnectionCount();
        });

        _client.Disconnected += (_, reason) => RunOnUi(() =>
        {
            SetState($"已断开（{reason}）", false);
            _connectBtn.Text = "连接";
            _clientDecoder.Reset();
            UpdateConnectionCount();
            AppendSystem($"连接断开：{reason}" + (_autoReconnectChk.Checked ? $"，{_client.ReconnectIntervalMs}ms 后自动重连" : string.Empty));
        });

        _client.DataReceived += (_, data) =>
        {
            Interlocked.Add(ref _totalReceived, data.Length);
            FormatIncoming(data, "服务器", _clientDecoder);
        };

        _client.ReconnectFailed += (_, message) => RunOnUi(() => _tipItem.Text = $"自动重连失败：{message}");

        _clientList.DoubleClick += (_, _) => DisconnectSelectedClient();

        Load += (_, _) =>
        {
            if (_rightSplit.Height > 320)
            {
                _rightSplit.SplitterDistance = (int)(_rightSplit.Height * 0.60);
            }

            if (_mainSplit.Width > 900)
            {
                _mainSplit.SplitterDistance = 380;
            }
        };

        FormClosing += async (_, e) =>
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
            _flushTimer.Stop();
            _autoSendTimer.Stop();
            try
            {
                _client.Dispose();
                await _server.StopAsync();
            }
            catch
            {
                // 关闭阶段忽略异常。
            }
        };
    }

    private bool _closing;

    private async Task OnModeChangedAsync()
    {
        if (_switchingMode)
        {
            return;
        }

        _switchingMode = true;
        try
        {
            _autoSendChk.Checked = false;
            if (_server.IsRunning)
            {
                await _server.StopAsync();
                _clientList.Items.Clear();
                _peerDecoders.Clear();
            }

            if (_client.IsConnected)
            {
                _client.Disconnect("切换模式");
            }

            ApplyMode();
        }
        finally
        {
            _switchingMode = false;
            UpdateConnectionCount();
        }
    }

    private void ApplyMode()
    {
        var server = IsServerMode;
        _hostBox.Enabled = !server;
        _autoReconnectChk.Enabled = !server;
        _reconnectBox.Enabled = !server && _autoReconnectChk.Checked;
        _mainSplit.Panel1Collapsed = !server;
        _connectBtn.Text = server ? "开始监听" : "连接";
        _portBox.Focus();
        SetState(server ? "未监听" : "未连接", false);
        _tipItem.Text = server
            ? "监听本机端口，等待客户端接入；可对选中客户端单发，或广播给全部客户端。"
            : "填写服务器地址与端口后点击连接。";
    }
    private bool IsServerMode => _modeBox.SelectedIndex == 1;

    private async Task ToggleConnectionAsync()
    {
        _connectBtn.Enabled = false;
        try
        {
            if (IsServerMode)
            {
                if (_server.IsRunning)
                {
                    await _server.StopAsync();
                    _clientList.Items.Clear();
                    _peerDecoders.Clear();
                    _connectBtn.Text = "开始监听";
                    SetState("已停止监听", false);
                    AppendSystem("已停止监听");
                }
                else
                {
                    _server.Start((int)_portBox.Value);
                    _connectBtn.Text = "停止监听";
                    SetState($"监听中 0.0.0.0:{_server.Port}", true);
                    AppendSystem($"开始监听 0.0.0.0:{_server.Port}，等待客户端接入…");
                }
            }
            else if (_client.IsConnected)
            {
                _client.Disconnect();
            }
            else
            {
                _client.AutoReconnect = _autoReconnectChk.Checked;
                _client.ReconnectIntervalMs = (int)_reconnectBox.Value;
                SetState("连接中…", false);
                await _client.ConnectAsync(_hostBox.Text.Trim(), (int)_portBox.Value);
                AppendSystem($"已连接到 {_client.EndPoint}");
            }
        }
        catch (Exception ex)
        {
            SetState($"失败：{ex.Message}", false);
            AppendSystem($"操作失败：{ex.Message}");
            if (!IsServerMode)
            {
                _connectBtn.Text = "连接";
            }
        }
        finally
        {
            _connectBtn.Enabled = true;
            UpdateConnectionCount();
        }
    }

    private async Task SendCurrentAsync()
    {
        var raw = _sendBox.Text;
        var hexMode = _sendFormatBox.SelectedIndex == 1;
        byte[] payload;

        if (hexMode)
        {
            if (!HexUtil.TryParse(raw, out payload, out var error))
            {
                SetState($"HEX 解析失败：{error}", false);
                return;
            }
        }
        else
        {
            payload = System.Text.Encoding.UTF8.GetBytes(raw);
            var ending = (LineEnding)_lineEndingBox.SelectedIndex;
            if (ending != LineEnding.None)
            {
                var suffix = ending.GetBytes();
                var merged = new byte[payload.Length + suffix.Length];
                Buffer.BlockCopy(payload, 0, merged, 0, payload.Length);
                Buffer.BlockCopy(suffix, 0, merged, payload.Length, suffix.Length);
                payload = merged;
            }
        }

        if (payload.Length == 0)
        {
            SetState("发送内容为空", false);
            return;
        }

        try
        {
            if (IsServerMode)
            {
                if (!_server.IsRunning)
                {
                    SetState("请先开始监听", false);
                    return;
                }

                var selectedId = GetSelectedClientId();
                if (_broadcastChk.Checked || selectedId is null)
                {
                    var count = await _server.BroadcastAsync(payload);
                    if (count == 0)
                    {
                        SetState("没有在线客户端", false);
                        return;
                    }

                    Interlocked.Add(ref _totalSent, (long)payload.Length * count);
                    _tipItem.Text = $"已广播 {payload.Length} 字节 → {count} 个客户端";
                }
                else
                {
                    await _server.SendToAsync(selectedId, payload);
                    Interlocked.Add(ref _totalSent, payload.Length);
                    _tipItem.Text = $"已发送 {payload.Length} 字节 → {selectedId}";
                }
            }
            else
            {
                if (!_client.IsConnected)
                {
                    SetState("尚未连接服务器", false);
                    return;
                }

                await _client.SendAsync(payload);
                Interlocked.Add(ref _totalSent, payload.Length);
                _tipItem.Text = $"已发送 {payload.Length} 字节 → {_client.EndPoint}";
            }

            SetState(IsServerMode ? $"监听中 0.0.0.0:{_server.Port}" : $"已连接 {_client.EndPoint}", true);
        }
        catch (Exception ex)
        {
            SetState($"发送失败：{ex.Message}", false);
        }
    }

    private void FormatIncoming(byte[] data, string tag, Utf8StreamDecoder decoder)
    {
        string text;
        if (_recvFormatBox.SelectedIndex == 1)
        {
            text = HexUtil.ToHexString(data) + Environment.NewLine;
        }
        else
        {
            text = decoder.Decode(data);
            if (text.Length == 0)
            {
                return;
            }
        }

        var prefix = (_timestampChk.Checked ? $"[{DateTime.Now:HH:mm:ss.fff}] " : string.Empty) + $"[{tag}] ";
        lock (_pendingLock)
        {
            _pendingText.Append(prefix);
            if (text.Contains('\n'))
            {
                // 多行内容：每一行都带上来源前缀，便于排查。
                var lines = text.Replace("\r\n", "\n").Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i > 0)
                    {
                        _pendingText.Append(prefix);
                    }

                    _pendingText.Append(lines[i]);
                    if (i < lines.Length - 1)
                    {
                        _pendingText.Append("\r\n");
                    }
                }
            }
            else
            {
                _pendingText.Append(text).Append("\r\n");
            }
        }
    }

    private void AppendSystem(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [系统] {message}\r\n";
        lock (_pendingLock)
        {
            _pendingText.Append(line);
        }
    }

    private void FlushPending()
    {
        UpdateStats();
        UpdateClientRows();

        string chunk;
        lock (_pendingLock)
        {
            if (_pendingText.Length == 0)
            {
                return;
            }

            chunk = _pendingText.ToString();
            _pendingText.Clear();
        }

        _recvBox.AppendText(chunk);

        if (_recvBox.TextLength > MaxLogChars)
        {
            var text = _recvBox.Text;
            _recvBox.Text = "[…日志超过 40 万字符，已自动截断前面的内容…]\r\n" + text[^TrimToChars..];
        }

        if (_autoScrollChk.Checked)
        {
            _recvBox.SelectionStart = _recvBox.TextLength;
            _recvBox.ScrollToCaret();
        }
    }
    private Utf8StreamDecoder GetPeerDecoder(string peerId)
    {
        lock (_peerDecoders)
        {
            if (!_peerDecoders.TryGetValue(peerId, out var decoder))
            {
                decoder = new Utf8StreamDecoder();
                _peerDecoders[peerId] = decoder;
            }

            return decoder;
        }
    }

    private void AddClientRow(PeerConnection peer)
    {
        var item = new ListViewItem(peer.Id) { Tag = peer.Id };
        item.SubItems.Add(peer.RemoteEndPoint);
        item.SubItems.Add("0 B");
        item.SubItems.Add("0 B");
        _clientList.Items.Add(item);
        if (_clientList.SelectedItems.Count == 0)
        {
            item.Selected = true;
        }
    }

    private void RemoveClientRow(string peerId)
    {
        foreach (ListViewItem item in _clientList.Items)
        {
            if (string.Equals((string?)item.Tag, peerId, StringComparison.Ordinal))
            {
                _clientList.Items.Remove(item);
                break;
            }
        }

        lock (_peerDecoders)
        {
            _peerDecoders.Remove(peerId);
        }
    }

    private void UpdateClientRows()
    {
        if (_clientList.Items.Count == 0)
        {
            return;
        }

        var peers = _server.Clients.ToDictionary(p => p.Id);
        foreach (ListViewItem item in _clientList.Items)
        {
            var id = (string?)item.Tag;
            if (id is not null && peers.TryGetValue(id, out var peer))
            {
                item.SubItems[2].Text = SizeFormatter.Format(peer.BytesReceived);
                item.SubItems[3].Text = SizeFormatter.Format(peer.BytesSent);
            }
        }
    }

    private void DisconnectSelectedClient()
    {
        var id = GetSelectedClientId();
        if (id is null)
        {
            SetState("请先在左侧选择要断开的客户端", false);
            return;
        }

        _server.Disconnect(id, "服务端主动断开");
    }

    private string? GetSelectedClientId()
    {
        if (_clientList.SelectedItems.Count == 0)
        {
            return null;
        }

        return _clientList.SelectedItems[0].Tag as string;
    }

    private void SetState(string text, bool online)
    {
        RunOnUi(() =>
        {
            _stateLabel.Text = text;
            _stateLabel.ForeColor = online ? Color.FromArgb(22, 140, 76) : Color.FromArgb(150, 90, 30);
            _stateItem.Text = text;
        });
    }

    private void UpdateStats()
    {
        _txItem.Text = "发送 " + SizeFormatter.Format(Interlocked.Read(ref _totalSent));
        _rxItem.Text = "接收 " + SizeFormatter.Format(Interlocked.Read(ref _totalReceived));
    }

    private void UpdateConnectionCount()
    {
        RunOnUi(() =>
        {
            var count = IsServerMode ? _server.ClientCount : _client.IsConnected ? 1 : 0;
            _connItem.Text = "连接 " + count;
        });
    }

    private void ToggleAutoSend()
    {
        if (_autoSendChk.Checked)
        {
            _autoSendTimer.Interval = (int)_autoSendInterval.Value;
            _autoSendTimer.Start();
            _tipItem.Text = $"定时发送已开启：每 {(int)_autoSendInterval.Value}ms 发送一次";
        }
        else
        {
            _autoSendTimer.Stop();
            _tipItem.Text = "定时发送已关闭";
        }
    }

    private void SaveLog()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"tcp-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            Title = "保存接收日志"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _recvBox.Text, new System.Text.UTF8Encoding(true));
            _tipItem.Text = "日志已保存：" + dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {
            // 窗口正在关闭。
        }
        catch (InvalidOperationException)
        {
            // 句柄已销毁。
        }
    }
}
