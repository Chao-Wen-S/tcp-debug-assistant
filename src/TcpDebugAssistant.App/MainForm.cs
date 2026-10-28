using System.Diagnostics;
using System.Net;
using System.Text;
using TcpDebugAssistant.Core;

namespace TcpDebugAssistant.App;

public sealed partial class MainForm : Form
{
    private const string RepoUrl = "https://github.com/Chao-Wen-S/tcp-debug-assistant";
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

    private readonly SelectField _modeBox = new();
    private readonly TextBox _hostBox = new();
    private readonly NumberField _portBox = new();
    private readonly FlatButton _connectBtn = new();
    private readonly PillLabel _stateLabel = new();
    private readonly CheckBox _autoReconnectChk = new();
    private readonly NumberField _reconnectBox = new();

    private readonly SplitContainer _mainSplit = new();
    private readonly SplitContainer _rightSplit = new();
    private readonly ThemedListView _clientList = new();
    private readonly CheckBox _broadcastChk = new();
    private readonly Label _clientHint = new();

    private readonly SelectField _recvFormatBox = new();
    private readonly CheckBox _timestampChk = new();
    private readonly CheckBox _autoScrollChk = new();
    private readonly RichTextBox _recvBox = new();

    private readonly SelectField _sendFormatBox = new();
    private readonly SelectField _lineEndingBox = new();
    private readonly NumberField _autoSendInterval = new();
    private readonly CheckBox _autoSendChk = new();
    private readonly TextBox _sendBox = new();
    private readonly FlatButton _sendBtn = new();

    private readonly Label _stateItem = new();
    private readonly Label _txItem = new();
    private readonly Label _rxItem = new();
    private readonly Label _connItem = new();
    private readonly Label _tipItem = new();

    private readonly BorderedPanel _header = new();
    private readonly BorderedPanel _statusBar = new();
    private readonly Panel _configHost = new();
    private readonly FlatButton _themeBtn = new();
    private readonly FlatButton _githubBtn = new();

    public MainForm()
    {
        Text = "TCP 调试助手 v1.2";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1240, 800);
        MinimumSize = new Size(1040, 700);
        Font = Fonts.Ui(9F);
        DoubleBuffered = true;
        BackColor = Theme.Current.Bg;

        // Dock 自高索引向低索引处理：后加入的面板先占边，所以 Fill 排在最前、顶栏排在最后。
        BuildMainArea();
        BuildStatusBar();
        BuildConfigCard();
        BuildHeader();

        WireEvents();

        _modeBox.SelectedIndex = 0;
        ApplyMode();
        ApplyTheme();

        _flushTimer.Tick += (_, _) => FlushPending();
        _flushTimer.Start();
    }

    /// <summary>刷新整套配色（浅色 / 深色切换时调用）。</summary>
    internal void ApplyTheme()
    {
        var t = Theme.Current;
        BackColor = t.Bg;
        _themeBtn.Text = t.Dark ? "☀  浅色" : "🌙  深色";
        Ui.Apply(this, t);
        ThemePaint.Apply(this, t);
        ApplyTitleBarTheme(t.Dark);
        Invalidate(true);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    /// <summary>让系统标题栏跟随浅色 / 深色主题（Windows 10 1809+）。</summary>
    private void ApplyTitleBarTheme(bool dark)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var value = dark ? 1 : 0;
        try
        {
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE（新版），19 = 旧版编号
            if (DwmSetWindowAttribute(Handle, 20, ref value, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(Handle, 19, ref value, sizeof(int));
            }

            const uint flags = 0x0001 | 0x0002 | 0x0004 | 0x0020; // NOSIZE | NOMOVE | NOZORDER | FRAMECHANGED
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, flags);
        }
        catch (DllNotFoundException)
        {
            // 非 Windows 或精简系统：忽略
        }
        catch (EntryPointNotFoundException)
        {
            // 老系统没有该接口：忽略
        }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBarTheme(Theme.Current.Dark);
    }

    private void BuildHeader()
    {
        _header.Dock = DockStyle.Top;
        _header.Height = 64;
        _header.BorderBottom = true;

        var mark = new BrandMark { Location = new Point(18, 16), Size = new Size(32, 32) };

        var title = new Label
        {
            Text = "TCP 调试助手",
            Font = Fonts.Ui(12.5F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(62, 7)
        };

        var titleWidth = TextRenderer.MeasureText(title.Text, title.Font).Width;
        var version = new ChipLabel("v1.2", true) { Location = new Point(62 + titleWidth + 10, 13) };

        // 能力标记改成小号标签，比一整行灰字更清爽。
        var chips = new[]
        {
            new ChipLabel("客户端 / 服务端"),
            new ChipLabel("HEX"),
            new ChipLabel("定时发送"),
            new ChipLabel("多客户端广播")
        };
        var chipX = 62;
        foreach (var chip in chips)
        {
            chip.Location = new Point(chipX, 35);
            chipX += chip.Width + 6;
        }

        _themeBtn.Text = "🌙  深色";
        _themeBtn.Variant = ButtonVariant.Ghost;
        _themeBtn.Size = new Size(98, 30);
        _themeBtn.Click += (_, _) =>
        {
            Theme.Toggle();
            ApplyTheme();
        };

        _githubBtn.Text = "GitHub  ↗";
        _githubBtn.Variant = ButtonVariant.Ghost;
        _githubBtn.Size = new Size(98, 30);
        _githubBtn.Click += (_, _) => OpenRepo();

        // 右上角按钮按父容器宽度手工定位 + 锚定，避免流式布局在缩放时错位。
        void PlaceHeaderRight()
        {
            var right = _header.ClientSize.Width - 18;
            _themeBtn.Location = new Point(right - _themeBtn.Width, 17);
            _githubBtn.Location = new Point(right - _themeBtn.Width - 8 - _githubBtn.Width, 17);
        }

        _themeBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _githubBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        _header.Controls.Add(mark);
        _header.Controls.Add(title);
        _header.Controls.Add(version);
        foreach (var chip in chips)
        {
            _header.Controls.Add(chip);
        }

        _header.Controls.Add(_githubBtn);
        _header.Controls.Add(_themeBtn);
        _header.Resize += (_, _) => PlaceHeaderRight();
        Controls.Add(_header);
        PlaceHeaderRight();
    }

    private static void OpenRepo()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
        }
        catch
        {
            // 浏览器启动失败时忽略。
        }
    }
    private void BuildConfigCard()
    {
        _configHost.Dock = DockStyle.Top;
        _configHost.Height = 154;
        _configHost.Padding = new Padding(18, 16, 18, 8);
        _configHost.Tag = "bg";

        var card = new Card("连接设置", 16, 32) { Dock = DockStyle.Fill };
        var rowWidth = ClientSize.Width - 18 * 2 - 2 - 16 * 2;

        var row1 = new Panel { Dock = DockStyle.Top, Height = 38, Tag = "surface" };
        var row2 = new Panel { Dock = DockStyle.Top, Height = 30, Tag = "surface" };

        var modeLabel = new Label { Text = "模式", AutoSize = true, Location = new Point(2, 12) };
        _modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeBox.Items.AddRange(new object[] { "TCP 客户端", "TCP 服务端" });
        _modeBox.Location = new Point(44, 6);
        _modeBox.Width = 140;

        var hostLabel = new Label { Text = "服务器地址", AutoSize = true, Location = new Point(202, 12) };
        _hostBox.Location = new Point(290, 7);
        _hostBox.Width = 172;
        _hostBox.BorderStyle = BorderStyle.FixedSingle;
        _hostBox.Text = "127.0.0.1";

        var portLabel = new Label { Text = "端口", AutoSize = true, Location = new Point(476, 12) };
        _portBox.Location = new Point(514, 4);
        _portBox.Width = 92;
        _portBox.Minimum = 1;
        _portBox.Maximum = 65535;
        _portBox.Value = 8080;

        _connectBtn.Text = "连接";
        _connectBtn.Variant = ButtonVariant.Primary;
        _connectBtn.Font = Fonts.Ui(9F, FontStyle.Bold);
        _connectBtn.Size = new Size(120, 30);
        _connectBtn.Location = new Point(622, 3);

        _stateLabel.Text = "未连接";
        _stateLabel.Tone = PillTone.Neutral;
        _stateLabel.ShowDot = true;
        _stateLabel.AutoFit = true;
        _stateLabel.Height = 30;
        _stateLabel.FitToText();
        _stateLabel.Location = new Point(Math.Max(754, rowWidth - _stateLabel.Width), 3);
        void RelayoutState() => _stateLabel.Left = Math.Max(0, row1.ClientSize.Width - _stateLabel.Width);
        row1.Resize += (_, _) => RelayoutState();
        _stateLabel.SizeChanged += (_, _) => RelayoutState();

        row1.Controls.AddRange(new Control[] { modeLabel, _modeBox, hostLabel, _hostBox, portLabel, _portBox, _connectBtn, _stateLabel });

        _autoReconnectChk.Text = "断线自动重连";
        _autoReconnectChk.AutoSize = true;
        _autoReconnectChk.Location = new Point(2, 5);

        var reconnectLabel = new Label { Text = "间隔", AutoSize = true, Location = new Point(146, 7) };
        _reconnectBox.Location = new Point(182, 1);
        _reconnectBox.Width = 86;
        _reconnectBox.Minimum = 200;
        _reconnectBox.Maximum = 600000;
        _reconnectBox.Increment = 500;
        _reconnectBox.Value = 2000;

        var msLabel = new Label { Text = "毫秒", AutoSize = true, Location = new Point(278, 7) };

        var tip = new Label
        {
            Text = "客户端模式主动连接服务器；服务端模式监听本地端口，可多客户端接入，支持单发与广播。",
            AutoSize = false,
            AutoEllipsis = true,
            Height = 20,
            Location = new Point(330, 5),
            Tag = "muted"
        };
        row2.Resize += (_, _) => tip.Width = Math.Max(120, row2.ClientSize.Width - tip.Left);

        row2.Controls.AddRange(new Control[] { _autoReconnectChk, reconnectLabel, _reconnectBox, msLabel, tip });

        // Dock 自后向前处理：row2 先加入，row1 后加入才能排在最上面。
        card.Body.Controls.Add(row2);
        card.Body.Controls.Add(row1);

        _configHost.Controls.Add(card);
        Controls.Add(_configHost);
    }

    private void BuildMainArea()
    {
        _mainSplit.Dock = DockStyle.Fill;
        _mainSplit.Orientation = Orientation.Vertical;
        _mainSplit.SplitterWidth = 14;
        _mainSplit.FixedPanel = FixedPanel.Panel1;
        _mainSplit.Size = new Size(1208, 560);
        _mainSplit.Panel1MinSize = 280;
        _mainSplit.Panel2MinSize = 520;
        _mainSplit.Panel1.Tag = "bg";
        _mainSplit.Panel1.Padding = new Padding(18, 14, 0, 18);
        _mainSplit.Panel2.Tag = "bg";
        _mainSplit.Panel2.Padding = new Padding(0, 14, 18, 18);

        var clientCard = new Card("已连接客户端", 14, 32) { Dock = DockStyle.Fill };

        _clientList.Dock = DockStyle.Fill;
        _clientList.View = View.Details;
        _clientList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _clientList.Columns.Add("客户端", 74);
        _clientList.Columns.Add("远端地址", 140);
        _clientList.Columns.Add("接收", 84, HorizontalAlignment.Right);
        _clientList.Columns.Add("发送", 84, HorizontalAlignment.Right);

        var clientBottom = new Panel { Dock = DockStyle.Bottom, Height = 90, Tag = "surface" };

        var disconnectBtn = new FlatButton
        {
            Text = "断开选中",
            Variant = ButtonVariant.Secondary,
            Size = new Size(92, 30),
            Location = new Point(0, 4)
        };
        disconnectBtn.Click += (_, _) => DisconnectSelectedClient();

        var disconnectAllBtn = new FlatButton
        {
            Text = "全部断开",
            Variant = ButtonVariant.Danger,
            Size = new Size(92, 30),
            Location = new Point(100, 4)
        };
        disconnectAllBtn.Click += (_, _) => _server.DisconnectAll("服务端主动断开全部客户端");

        _broadcastChk.Text = "广播发送";
        _broadcastChk.Checked = true;
        _broadcastChk.AutoSize = true;
        _broadcastChk.Location = new Point(206, 13);

        _clientHint.Text = "双击可断开客户端；未选中时按广播规则发送。";
        _clientHint.Location = new Point(0, 50);
        _clientHint.Size = new Size(330, 34);
        _clientHint.Font = Fonts.Ui(8F);
        _clientHint.Tag = "muted";

        clientBottom.Controls.AddRange(new Control[] { disconnectBtn, disconnectAllBtn, _broadcastChk, _clientHint });

        clientCard.Body.Controls.Add(_clientList);
        clientCard.Body.Controls.Add(clientBottom);
        _mainSplit.Panel1.Controls.Add(clientCard);

        _rightSplit.Dock = DockStyle.Fill;
        _rightSplit.Orientation = Orientation.Horizontal;
        _rightSplit.SplitterWidth = 14;
        _rightSplit.Size = new Size(720, 560);
        _rightSplit.Panel1MinSize = 180;
        _rightSplit.Panel2MinSize = 170;
        _rightSplit.Panel1.Tag = "bg";
        _rightSplit.Panel2.Tag = "bg";
        _rightSplit.Panel1.Controls.Add(BuildReceiveCard());
        _rightSplit.Panel2.Controls.Add(BuildSendCard());

        _mainSplit.Panel2.Controls.Add(_rightSplit);
        Controls.Add(_mainSplit);
    }
    private Card BuildReceiveCard()
    {
        var card = new Card("接收区", 14, 32) { Dock = DockStyle.Fill };

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Tag = "surface"
        };

        var formatLabel = new Label { Text = "显示", AutoSize = true, Margin = new Padding(0, 14, 6, 0) };

        _recvFormatBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _recvFormatBox.Items.AddRange(new object[] { "文本", "HEX" });
        _recvFormatBox.SelectedIndex = 0;
        _recvFormatBox.Width = 78;
        _recvFormatBox.Margin = new Padding(0, 10, 16, 0);

        _timestampChk.Text = "时间戳";
        _timestampChk.Checked = true;
        _timestampChk.AutoSize = true;
        _timestampChk.Margin = new Padding(0, 13, 16, 0);

        _autoScrollChk.Text = "自动滚动";
        _autoScrollChk.Checked = true;
        _autoScrollChk.AutoSize = true;
        _autoScrollChk.Margin = new Padding(0, 13, 18, 0);

        var clearBtn = new FlatButton
        {
            Text = "清空",
            Variant = ButtonVariant.Soft,
            Size = new Size(58, 28),
            Margin = new Padding(0, 7, 8, 0)
        };
        clearBtn.Click += (_, _) => _recvBox.Clear();

        var saveBtn = new FlatButton
        {
            Text = "保存日志",
            Variant = ButtonVariant.Soft,
            Size = new Size(96, 28),
            Margin = new Padding(0, 7, 0, 0)
        };
        saveBtn.Click += (_, _) => SaveLog();

        toolbar.Controls.AddRange(new Control[] { formatLabel, _recvFormatBox, _timestampChk, _autoScrollChk, clearBtn, saveBtn });

        _recvBox.Dock = DockStyle.Fill;
        _recvBox.ReadOnly = true;
        _recvBox.BorderStyle = BorderStyle.None;
        _recvBox.Font = Fonts.Mono(9.75F);
        _recvBox.WordWrap = true;
        _recvBox.DetectUrls = false;
        _recvBox.HideSelection = false;
        _recvBox.ScrollBars = RichTextBoxScrollBars.Vertical;

        card.Body.Controls.Add(_recvBox);
        card.Body.Controls.Add(toolbar);
        return card;
    }
    private Card BuildSendCard()
    {
        var card = new Card("发送区", 14, 32) { Dock = DockStyle.Fill };

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, Tag = "surface" };

        var sendHost = new Panel
        {
            Dock = DockStyle.Right,
            Width = 172,
            Padding = new Padding(10, 12, 0, 12),
            Tag = "surface"
        };

        _sendBtn.Text = "发送  Ctrl+Enter";
        _sendBtn.Variant = ButtonVariant.Primary;
        _sendBtn.Font = Fonts.Ui(9F, FontStyle.Bold);
        _sendBtn.Dock = DockStyle.Fill;
        _sendBtn.Click += async (_, _) => await SendCurrentAsync();
        sendHost.Controls.Add(_sendBtn);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Tag = "surface"
        };

        var sendFormatLabel = new Label { Text = "编码", AutoSize = true, Margin = new Padding(0, 20, 6, 0) };

        _sendFormatBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _sendFormatBox.Items.AddRange(new object[] { "文本", "HEX" });
        _sendFormatBox.SelectedIndex = 0;
        _sendFormatBox.Width = 84;
        _sendFormatBox.Margin = new Padding(0, 16, 12, 0);

        var lineEndingLabel = new Label { Text = "行尾", AutoSize = true, Margin = new Padding(0, 20, 6, 0) };

        _lineEndingBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _lineEndingBox.Items.AddRange(new object[] { "无行尾", "CRLF (\\r\\n)", "LF (\\n)", "CR (\\r)" });
        _lineEndingBox.SelectedIndex = 1;
        _lineEndingBox.Width = 118;
        _lineEndingBox.Margin = new Padding(0, 16, 12, 0);

        _autoSendInterval.Minimum = 10;
        _autoSendInterval.Maximum = 600000;
        _autoSendInterval.Increment = 100;
        _autoSendInterval.Value = 1000;
        _autoSendInterval.Width = 78;
        _autoSendInterval.Margin = new Padding(0, 16, 6, 0);

        var msLabel = new Label { Text = "ms", AutoSize = true, Margin = new Padding(0, 20, 8, 0) };

        _autoSendChk.Text = "定时发送";
        _autoSendChk.AutoSize = true;
        _autoSendChk.Margin = new Padding(0, 19, 10, 0);

        var clearSendBtn = new FlatButton
        {
            Text = "清空",
            Variant = ButtonVariant.Soft,
            Size = new Size(58, 28),
            Margin = new Padding(0, 13, 0, 0)
        };
        clearSendBtn.Click += (_, _) => _sendBox.Clear();

        flow.Controls.AddRange(new Control[]
        {
            sendFormatLabel, _sendFormatBox, lineEndingLabel, _lineEndingBox,
            _autoSendInterval, msLabel, _autoSendChk, clearSendBtn
        });

        bottom.Controls.Add(flow);
        bottom.Controls.Add(sendHost);

        _sendBox.Dock = DockStyle.Fill;
        _sendBox.Multiline = true;
        _sendBox.BorderStyle = BorderStyle.None;
        _sendBox.ScrollBars = ScrollBars.Vertical;
        _sendBox.Font = Fonts.Mono(10F);
        _sendBox.AcceptsReturn = true;
        _sendBox.Text = "Hello TCP!";

        card.Body.Controls.Add(_sendBox);
        card.Body.Controls.Add(bottom);
        return card;
    }
    private void BuildStatusBar()
    {
        _statusBar.Dock = DockStyle.Bottom;
        _statusBar.Height = 32;
        _statusBar.BorderTop = true;
        _statusBar.UseSunken = true;

        _stateItem.Text = "就绪";
        _txItem.Text = "发送 0 B";
        _rxItem.Text = "接收 0 B";
        _connItem.Text = "连接 0";

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(14, 0, 0, 0),
            Tag = "transparent"
        };

        flow.Controls.AddRange(new Control[]
        {
            StatusText(_stateItem),
            DotSeparator(),
            StatusText(_txItem),
            DotSeparator(),
            StatusText(_rxItem),
            DotSeparator(),
            StatusText(_connItem)
        });

        _tipItem.Dock = DockStyle.Right;
        _tipItem.Width = 480;
        _tipItem.TextAlign = ContentAlignment.MiddleRight;
        _tipItem.Padding = new Padding(0, 0, 16, 0);
        _tipItem.Tag = "muted";
        _tipItem.Text = "Ctrl+Enter 发送 · F5 连接 / 监听";

        // 提示文字宽度按窗口自适应，避免窗口变窄时被状态文字压掉。
        _statusBar.Resize += (_, _) => _tipItem.Width = Math.Min(520, Math.Max(180, _statusBar.ClientSize.Width / 2));

        _statusBar.Controls.Add(flow);
        _statusBar.Controls.Add(_tipItem);
        Controls.Add(_statusBar);
    }

    private static Label StatusText(Label label)
    {
        label.AutoSize = true;
        label.Margin = new Padding(0, 8, 0, 0);
        return label;
    }

    private static Label DotSeparator() => new()
    {
        Text = "·",
        AutoSize = true,
        Margin = new Padding(11, 8, 11, 0),
        Tag = "muted"
    };
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
                _mainSplit.SplitterDistance = 424;
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
        // 左侧面板折叠后，右面板补回左边距，避免贴边出现空条。
        _mainSplit.Panel2.Padding = new Padding(server ? 0 : 18, 14, 18, 18);
        _connectBtn.Text = server ? "开始监听" : "连接";
        _portBox.FocusEdit();
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
            _stateLabel.Tone = online ? PillTone.Success : PillTone.Warn;
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

