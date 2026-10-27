using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TcpDebugAssistant.App;

/// <summary>配色与字体：浅色 / 深色两套，集中管理，样式只在一处改。</summary>
internal sealed class AppTheme
{
    public bool Dark { get; init; }
    public Color Bg { get; init; }
    public Color Surface { get; init; }
    public Color Sunken { get; init; }
    public Color Border { get; init; }
    public Color Text { get; init; }
    public Color Muted { get; init; }
    public Color Accent { get; init; }
    public Color AccentHover { get; init; }
    public Color OnAccent { get; init; }
    public Color Success { get; init; }
    public Color Warn { get; init; }
    public Color Danger { get; init; }
    public Color Selection { get; init; }
    public Color Header { get; init; }

    public static AppTheme Light { get; } = new()
    {
        Dark = false,
        Bg = Color.FromArgb(0xF3, 0xF5, 0xF9),
        Surface = Color.White,
        Sunken = Color.FromArgb(0xFA, 0xFB, 0xFE),
        Border = Color.FromArgb(0xDF, 0xE4, 0xEE),
        Text = Color.FromArgb(0x11, 0x18, 0x27),
        Muted = Color.FromArgb(0x66, 0x70, 0x85),
        Accent = Color.FromArgb(0x25, 0x63, 0xEB),
        AccentHover = Color.FromArgb(0x1D, 0x4E, 0xD8),
        OnAccent = Color.White,
        Success = Color.FromArgb(0x0F, 0x7B, 0x4F),
        Warn = Color.FromArgb(0xB5, 0x47, 0x08),
        Danger = Color.FromArgb(0xB4, 0x23, 0x18),
        Selection = Color.FromArgb(0xDB, 0xEA, 0xFE),
        Header = Color.FromArgb(0xEE, 0xF2, 0xF9)
    };

    public static AppTheme DarkTheme { get; } = new()
    {
        Dark = true,
        Bg = Color.FromArgb(0x0D, 0x10, 0x16),
        Surface = Color.FromArgb(0x16, 0x1A, 0x22),
        Sunken = Color.FromArgb(0x10, 0x14, 0x1B),
        Border = Color.FromArgb(0x27, 0x2D, 0x3A),
        Text = Color.FromArgb(0xE8, 0xEA, 0xF0),
        Muted = Color.FromArgb(0x96, 0xA0, 0xB2),
        Accent = Color.FromArgb(0x4C, 0x8D, 0xFF),
        AccentHover = Color.FromArgb(0x6D, 0xA3, 0xFF),
        OnAccent = Color.FromArgb(0x0A, 0x0F, 0x18),
        Success = Color.FromArgb(0x3D, 0xD5, 0x9B),
        Warn = Color.FromArgb(0xF5, 0xA5, 0x24),
        Danger = Color.FromArgb(0xF8, 0x71, 0x71),
        Selection = Color.FromArgb(0x1E, 0x38, 0x62),
        Header = Color.FromArgb(0x1B, 0x21, 0x2B)
    };
}

internal static class Theme
{
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static AppTheme Toggle() => Current = Current.Dark ? AppTheme.Light : AppTheme.DarkTheme;
}

internal static class Fonts
{
    private static readonly bool HasCascadia = Probe("Cascadia Mono") || Probe("Cascadia Code");

    public static Font Ui(float size, FontStyle style = FontStyle.Regular) => new("Microsoft YaHei UI", size, style);

    /// <summary>等宽字体：优先 Cascadia Mono / Consolas，用于日志与收发内容。</summary>
    public static Font Mono(float size, FontStyle style = FontStyle.Regular) =>
        new(HasCascadia ? "Cascadia Mono" : "Consolas", size, style);

    private static bool Probe(string family)
    {
        using var installed = new InstalledFontCollection();
        foreach (var f in installed.Families)
        {
            if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

internal static class Ui
{
    public static GraphicsPath Round(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>递归把主题应用到整棵控件树。</summary>
    public static void Apply(Control root, AppTheme t)
    {
        if (root is IThemed themed)
        {
            themed.ApplyTheme(t);
        }

        foreach (Control child in root.Controls)
        {
            Apply(child, t);
        }
    }
}

internal interface IThemed
{
    void ApplyTheme(AppTheme t);
}

/// <summary>1px 细线面板，用于顶栏 / 状态栏的分隔。</summary>
internal sealed class BorderedPanel : Panel, IThemed
{
    public bool BorderTop { get; set; }
    public bool BorderBottom { get; set; }
    public bool UseSunken { get; set; }

    public BorderedPanel()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    public void ApplyTheme(AppTheme t)
    {
        BackColor = UseSunken ? t.Sunken : t.Surface;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        using var bg = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(bg, ClientRectangle);
        using var pen = new Pen(t.Border);
        if (BorderTop)
        {
            e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        }

        if (BorderBottom)
        {
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}

/// <summary>卡片容器：圆角 + 1px 边框 + 小标题。</summary>
internal sealed class Card : Panel, IThemed
{
    private readonly Label _title = new();
    private readonly BorderedPanel _body = new();
    private AppTheme _theme = Theme.Current;

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    public Panel Body => _body;

    public Card(string title, int bodyPadding = 12, int titleHeight = 34)
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Padding = new Padding(1);
        Margin = new Padding(0);

        _title.Text = title;
        _title.Dock = DockStyle.Top;
        _title.Height = titleHeight;
        _title.Padding = new Padding(14, 0, 0, 0);
        _title.TextAlign = ContentAlignment.MiddleLeft;
        _title.Font = Fonts.Ui(9.5F, FontStyle.Bold);
        _title.Tag = "muted";

        _body.Dock = DockStyle.Fill;
        _body.Padding = new Padding(bodyPadding);
        _body.BorderTop = true;

        // 先加 Body 再加标题：Dock 按 z 序反向处理，标题先占位、Body 再填满。
        Controls.Add(_body);
        Controls.Add(_title);
    }

    public void ApplyTheme(AppTheme t)
    {
        _theme = t;
        BackColor = t.Bg;
        _title.BackColor = t.Surface;
        _title.ForeColor = t.Muted;
        _body.ApplyTheme(t);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = _theme;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Ui.Round(r, 10);
        using var bg = new SolidBrush(t.Surface);
        using var pen = new Pen(t.Border);
        e.Graphics.FillPath(bg, path);
        e.Graphics.DrawPath(pen, path);
    }
}

internal enum ButtonVariant
{
    Primary,
    Secondary,
    Ghost,
    Danger
}

/// <summary>扁平按钮：无系统边框，圆角，悬停 / 按下有反馈。</summary>
internal sealed class FlatButton : Button, IThemed
{
    private AppTheme _theme = Theme.Current;
    private bool _hover;
    private bool _down;

    public ButtonVariant Variant { get; set; } = ButtonVariant.Secondary;

    public int Radius { get; set; } = 8;

    public FlatButton()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Font = Fonts.Ui(9F);
    }

    public void ApplyTheme(AppTheme t)
    {
        _theme = t;
        BackColor = Color.Transparent;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _down = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _down = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _down = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = _theme;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Ui.Round(r, Radius);

        Color back;
        Color fore;
        Color? border = null;

        switch (Variant)
        {
            case ButtonVariant.Primary:
                back = _hover ? t.AccentHover : t.Accent;
                fore = t.OnAccent;
                break;
            case ButtonVariant.Danger:
                back = _hover ? Mix(t.Danger, t.Surface, 0.15) : t.Surface;
                fore = t.Danger;
                border = t.Danger;
                break;
            case ButtonVariant.Ghost:
                back = _hover ? t.Sunken : (Parent?.BackColor ?? t.Surface);
                fore = t.Text;
                break;
            default:
                back = _hover ? t.Sunken : t.Surface;
                fore = t.Text;
                border = t.Border;
                break;
        }

        if (_down)
        {
            back = Mix(back, Color.Black, 0.08);
        }

        if (!Enabled)
        {
            back = Mix(back, t.Surface, 0.5);
            fore = t.Muted;
        }

        using (var bg = new SolidBrush(back))
        {
            e.Graphics.FillPath(bg, path);
        }

        if (border is not null)
        {
            using var pen = new Pen(border.Value);
            e.Graphics.DrawPath(pen, path);
        }

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            r,
            fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static Color Mix(Color a, Color b, double ratio)
    {
        var r = (int)(a.R * (1 - ratio) + b.R * ratio);
        var g = (int)(a.G * (1 - ratio) + b.G * ratio);
        var bl = (int)(a.B * (1 - ratio) + b.B * ratio);
        return Color.FromArgb(r, g, bl);
    }
}

internal enum PillTone
{
    Neutral,
    Success,
    Warn,
    Danger,
    Accent
}

/// <summary>圆角状态标签。</summary>
internal sealed class PillLabel : Label, IThemed
{
    private AppTheme _theme = Theme.Current;

    public PillTone Tone { get; set; } = PillTone.Neutral;

    public PillLabel()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        AutoSize = false;
        Height = 26;
        Width = 150;
        Font = Fonts.Ui(9F, FontStyle.Bold);
        TextAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(10, 0, 8, 0);
    }

    public void ApplyTheme(AppTheme t)
    {
        _theme = t;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = _theme;
        var (fg, bg) = Tone switch
        {
            PillTone.Success => (t.Success, Mix(t.Success, t.Surface, t.Dark ? 0.82 : 0.88)),
            PillTone.Warn => (t.Warn, Mix(t.Warn, t.Surface, 0.85)),
            PillTone.Danger => (t.Danger, Mix(t.Danger, t.Surface, 0.86)),
            PillTone.Accent => (t.Accent, Mix(t.Accent, t.Surface, 0.86)),
            _ => (t.Muted, t.Sunken)
        };

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 2, Width - 1, Height - 5);
        using var path = Ui.Round(r, 8);
        using var brush = new SolidBrush(bg);
        e.Graphics.FillPath(brush, path);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(r.X + 10, r.Y, r.Width - 14, r.Height),
            fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static Color Mix(Color a, Color b, double ratio)
    {
        var r = (int)(a.R * (1 - ratio) + b.R * ratio);
        var g = (int)(a.G * (1 - ratio) + b.G * ratio);
        var bl = (int)(a.B * (1 - ratio) + b.B * ratio);
        return Color.FromArgb(r, g, bl);
    }
}

/// <summary>自绘 ListView：表头 / 行 / 选中态跟随主题；隔行浅色底。</summary>
internal sealed class ThemedListView : ListView, IThemed
{
    private AppTheme _theme = Theme.Current;

    public ThemedListView()
    {
        OwnerDraw = true;
        BorderStyle = BorderStyle.None;
        FullRowSelect = true;
        HideSelection = false;
        MultiSelect = false;
        Font = Fonts.Ui(9F);
    }

    public void ApplyTheme(AppTheme t)
    {
        _theme = t;
        BackColor = t.Sunken;
        ForeColor = t.Text;
        Invalidate();
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        var t = _theme;
        using (var bg = new SolidBrush(t.Header))
        {
            e.Graphics.FillRectangle(bg, e.Bounds);
        }

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        flags |= (e.Header?.TextAlign ?? HorizontalAlignment.Left) == HorizontalAlignment.Right
            ? TextFormatFlags.Right
            : (e.Header?.TextAlign ?? HorizontalAlignment.Left) == HorizontalAlignment.Center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;

        var r = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? string.Empty, Fonts.Ui(8.75F, FontStyle.Bold), r, t.Muted, flags);
        using var pen = new Pen(t.Border);
        e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        e.DrawDefault = false;
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        var t = _theme;
        var selected = e.Item?.Selected == true;
        var back = selected
            ? t.Selection
            : (e.ItemIndex % 2 == 0 ? t.Surface : t.Sunken);

        using (var bg = new SolidBrush(back))
        {
            e.Graphics.FillRectangle(bg, e.Bounds);
        }

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        flags |= e.ColumnIndex >= 2 ? TextFormatFlags.Right : TextFormatFlags.Left;
        var r = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height);
        var fore = selected ? (t.Dark ? t.Text : t.Text) : t.Text;
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? string.Empty, Font, r, fore, flags);
    }
}

/// <summary>品牌标记：圆角色块 + 双向箭头，表示收发两个方向。</summary>
internal sealed class BrandMark : Control, IThemed
{
    private AppTheme _theme = Theme.Current;

    public BrandMark()
    {
        SetStyle(
            ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor,
            true);
        Size = new Size(30, 30);
        BackColor = Color.Transparent;
    }

    public void ApplyTheme(AppTheme t)
    {
        _theme = t;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = _theme;
        e.Graphics.Clear(Parent?.BackColor ?? t.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Ui.Round(r, 8))
        using (var brush = new LinearGradientBrush(r, t.Accent, t.AccentHover, 45F))
        {
            e.Graphics.FillPath(brush, path);
        }

        using var pen = new Pen(Color.White, 1.8F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(pen, 8F, 11F, 20F, 11F);
        e.Graphics.DrawLine(pen, 20F, 11F, 16.5F, 7.5F);
        e.Graphics.DrawLine(pen, 20F, 11F, 16.5F, 14.5F);
        e.Graphics.DrawLine(pen, 22F, 19F, 10F, 19F);
        e.Graphics.DrawLine(pen, 10F, 19F, 13.5F, 15.5F);
        e.Graphics.DrawLine(pen, 10F, 19F, 13.5F, 22.5F);
    }
}

/// <summary>把主题套到原生控件上（Label / TextBox / ComboBox / CheckBox / NumericUpDown / Panel / SplitContainer）。</summary>
internal static class ThemePaint
{
    public static void Apply(Control root, AppTheme t)
    {
        foreach (Control c in root.Controls)
        {
            Style(c, t);
            Apply(c, t);
        }
    }

    private static void Style(Control c, AppTheme t)
    {
        switch (c)
        {
            case Card:
            case FlatButton:
            case PillLabel:
            case ThemedListView:
            case BorderedPanel:
            case BrandMark:
                return;
            case Label label:
                label.ForeColor = IsMuted(label) ? t.Muted : t.Text;
                label.BackColor = Color.Transparent;
                break;
            case CheckBox box:
                box.ForeColor = t.Text;
                box.BackColor = Color.Transparent;
                break;
            case TextBoxBase text:
                // TextBox / RichTextBox 统一上色（RichTextBox 只读时默认系统白底）
                text.BackColor = t.Sunken;
                text.ForeColor = t.Text;
                break;
            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = t.Surface;
                combo.ForeColor = t.Text;
                break;
            case NumericUpDown num:
                num.BackColor = t.Surface;
                num.ForeColor = t.Text;
                num.BorderStyle = BorderStyle.FixedSingle;
                if (num.Controls.Count > 1)
                {
                    Style(num.Controls[1], t);
                }

                break;
            case SplitContainer split:
                split.BackColor = t.Bg;
                break;
            case Panel panel:
                switch (panel.Tag as string)
                {
                    case "bg":
                        panel.BackColor = t.Bg;
                        break;
                    case "surface":
                        panel.BackColor = t.Surface;
                        break;
                    case "sunken":
                        panel.BackColor = t.Sunken;
                        break;
                    case "transparent":
                        panel.BackColor = Color.Transparent;
                        break;
                }

                break;
        }
    }

    private static bool IsMuted(Control c) => (c.Tag as string) == "muted";
}
/// <summary>下拉选择控件：系统 ComboBox 在深色模式下会留白底，这里自绘，浅/深色都统一。</summary>
internal sealed class SelectField : Control, IThemed
{
    private int _index = -1;
    private bool _hot;
    private AppTheme _theme = Theme.Current;

    public SelectField()
    {
        SetStyle(
            ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
            ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor,
            true);
        Size = new Size(120, 26);
        Font = Fonts.Ui(9F);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    public event EventHandler? SelectedIndexChanged;

    public List<object> Items { get; } = new();

    public ComboBoxStyle DropDownStyle { get; set; } = ComboBoxStyle.DropDownList;

    public int SelectedIndex
    {
        get => _index;
        set
        {
            var next = value >= 0 && value < Items.Count ? value : -1;
            if (next == _index)
            {
                return;
            }

            _index = next;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedText => _index >= 0 && _index < Items.Count
        ? Items[_index]?.ToString() ?? string.Empty
        : string.Empty;

    public void ApplyTheme(AppTheme t)
    {
        _theme = t;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        ShowMenu();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter or Keys.Down)
        {
            ShowMenu();
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = _theme;
        e.Graphics.Clear(Parent?.BackColor ?? t.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), 6);
        using (var bg = new SolidBrush(t.Surface))
        {
            e.Graphics.FillPath(bg, path);
        }

        using (var pen = new Pen(_hot ? t.Accent : t.Border))
        {
            e.Graphics.DrawPath(pen, path);
        }

        TextRenderer.DrawText(
            e.Graphics,
            SelectedText,
            Font,
            new Rectangle(10, 0, Width - 32, Height),
            t.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var cx = Width - 15;
        var cy = Height / 2;
        using var chevron = new Pen(t.Muted, 1.6F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(chevron, cx - 4, cy - 3, cx, cy + 1);
        e.Graphics.DrawLine(chevron, cx + 4, cy - 3, cx, cy + 1);
    }

    private void ShowMenu()
    {
        var t = Theme.Current;
        var menu = new ContextMenuStrip
        {
            Font = Fonts.Ui(9F),
            ShowImageMargin = false,
            BackColor = t.Surface,
            ForeColor = t.Text,
            Renderer = new ToolStripProfessionalRenderer(new MenuColors(t))
        };

        for (var i = 0; i < Items.Count; i++)
        {
            var index = i;
            var item = new ToolStripMenuItem(Items[i]?.ToString() ?? string.Empty)
            {
                ForeColor = t.Text,
                BackColor = t.Surface,
                Padding = new Padding(8, 4, 8, 4)
            };

            if (i == _index)
            {
                item.Font = Fonts.Ui(9F, FontStyle.Bold);
            }

            item.Click += (_, _) => SelectedIndex = index;
            menu.Items.Add(item);
        }

        menu.Show(this, new Point(0, Height + 2));
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        private readonly AppTheme _t;

        public MenuColors(AppTheme t) => _t = t;

        public override Color ToolStripDropDownBackground => _t.Surface;
        public override Color ImageMarginGradientBegin => _t.Surface;
        public override Color ImageMarginGradientMiddle => _t.Surface;
        public override Color ImageMarginGradientEnd => _t.Surface;
        public override Color MenuBorder => _t.Border;
        public override Color MenuItemBorder => _t.Accent;
        public override Color MenuItemSelected => _t.Selection;
        public override Color MenuItemSelectedGradientBegin => _t.Selection;
        public override Color MenuItemSelectedGradientEnd => _t.Selection;
    }
}
