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

/// <summary>让原生控件（滚动条、下拉列表）跟随深色主题。失败时静默降级，不影响功能。</summary>
internal static class NativeDark
{
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int appMode);

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? subAppName, string? subIdList);

    private static bool _available = true;

    /// <summary>进程级：允许系统使用深色原生控件（滚动条、下拉框）。</summary>
    public static void TryEnable()
    {
        try
        {
            // 2 = AllowDark：由 SetWindowTheme 逐个控件决定深浅
            SetPreferredAppMode(2);
        }
        catch
        {
            _available = false;
        }
    }

    /// <summary>单个控件：深色用 DarkMode_Explorer，浅色用 Explorer。</summary>
    public static void Apply(Control control, bool dark)
    {
        if (!_available)
        {
            return;
        }

        try
        {
            if (!control.IsHandleCreated)
            {
                _ = control.Handle; // 触发句柄创建，否则 SetWindowTheme 无效
            }

            SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
            control.Invalidate();
        }
        catch
        {
            _available = false;
        }
    }
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
        using var path = Ui.Round(r, 12);
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
    Soft,
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
        SetStyle(
            ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        Font = Fonts.Ui(9F);
    }

    /// <summary>按钮所在容器的底色：圆角外的四角用它填平，避免残留系统底色。</summary>
    private Color Canvas => Parent?.BackColor is { } c && c.A != 0 ? c : _theme.Surface;

    public void ApplyTheme(AppTheme t)
    {
        _theme = t;
        // 不能用 Color.Transparent：Button 不支持透明背景，系统会先用黑色擦一遍，四角留黑块。
        BackColor = Canvas;
        Invalidate();
    }

    protected override void OnParentBackColorChanged(EventArgs e)
    {
        base.OnParentBackColorChanged(e);
        BackColor = Canvas;
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

        // 先铺满容器底色，再把圆角填充画在上面，四角才不会露系统底。
        using (var canvas = new SolidBrush(Canvas))
        {
            e.Graphics.FillRectangle(canvas, ClientRectangle);
        }

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
            case ButtonVariant.Soft:
                // 强调用的浅色按钮：淡主色底 + 主色文字，用于工具条里的轻操作。
                back = Mix(t.Accent, t.Surface, _hover ? (t.Dark ? 0.72 : 0.82) : (t.Dark ? 0.80 : 0.90));
                fore = t.Accent;
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

    /// <summary>是否在文字前画一个状态圆点（连接 / 监听状态用）。</summary>
    public bool ShowDot { get; set; }

    /// <summary>按文字自动调整宽度（状态胶囊用，避免出现一大截空壳）。</summary>
    public bool AutoFit { get; set; }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set
        {
            base.Text = value ?? string.Empty;
            if (AutoFit)
            {
                FitToText();
            }
        }
    }

    public void FitToText()
    {
        var size = TextRenderer.MeasureText(
            Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        Width = size.Width + (ShowDot ? 17 : 0) + 30;
    }

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
        using var path = Ui.Round(r, Math.Min(r.Height / 2, 12));
        using var brush = new SolidBrush(bg);
        e.Graphics.FillPath(brush, path);
        using (var edge = new Pen(Mix(fg, bg, 0.72)))
        {
            e.Graphics.DrawPath(edge, path);
        }

        var textLeft = r.X + 10;
        if (ShowDot)
        {
            const int dot = 7;
            using var dotBrush = new SolidBrush(fg);
            e.Graphics.FillEllipse(dotBrush, textLeft, r.Y + (r.Height - dot) / 2, dot, dot);
            textLeft += dot + 7;
        }

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(textLeft, r.Y, r.Right - textLeft - 8, r.Height),
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

        // ListView 没有 RowHeight 属性，用 1px 空图标列表把行高撑高，行距更透气。
        SmallImageList = new ImageList { ImageSize = new Size(1, 24) };
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

        if (selected && e.ColumnIndex == 0)
        {
            // 选中行左侧 3px 主色条，替代系统蓝色高亮。
            using var accent = new SolidBrush(t.Accent);
            e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top + 2, 3, e.Bounds.Height - 5);
        }

        using (var line = new Pen(t.Border))
        {
            e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
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


/// <summary>顶栏能力标记：小号圆角标签。</summary>
internal sealed class ChipLabel : Control, IThemed
{
    private AppTheme _theme = Theme.Current;

    public ChipLabel(string text, bool accent = false)
    {
        SetStyle(
            ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor,
            true);
        Text = text;
        Accent = accent;
        Font = Fonts.Ui(8.25F);
        Height = 21;
        Width = TextRenderer.MeasureText(text, Font).Width + 20;
        BackColor = Color.Transparent;
    }

    public bool Accent { get; }

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
        using var path = Ui.Round(r, 7);
        using (var bg = new SolidBrush(Accent ? Mix(t.Accent, t.Surface, t.Dark ? 0.80 : 0.90) : t.Sunken))
        {
            e.Graphics.FillPath(bg, path);
        }

        using (var pen = new Pen(Accent ? Mix(t.Accent, t.Surface, t.Dark ? 0.55 : 0.70) : t.Border))
        {
            e.Graphics.DrawPath(pen, path);
        }

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            r,
            Accent ? t.Accent : t.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private static Color Mix(Color a, Color b, double ratio)
    {
        var r = (int)(a.R * (1 - ratio) + b.R * ratio);
        var g = (int)(a.G * (1 - ratio) + b.G * ratio);
        var bl = (int)(a.B * (1 - ratio) + b.B * ratio);
        return Color.FromArgb(r, g, bl);
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
            case ChipLabel:
                return;
            case Label label:
                label.ForeColor = IsMuted(label) ? t.Muted : t.Text;
                label.BackColor = HasTag(label, "sunken") ? t.Sunken : Color.Transparent;
                break;
            case CheckBox box:
                box.ForeColor = t.Text;
                box.BackColor = Color.Transparent;
                break;
            case TextBoxBase text:
                // TextBox / RichTextBox 统一上色（RichTextBox 只读时默认系统白底）
                text.BackColor = t.Sunken;
                text.ForeColor = t.Text;
                NativeDark.Apply(text, t.Dark);
                break;
            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = t.Surface;
                combo.ForeColor = t.Text;
                NativeDark.Apply(combo, t.Dark);
                break;
            case NumericUpDown num:
                num.BackColor = t.Surface;
                num.ForeColor = t.Text;
                num.BorderStyle = BorderStyle.FixedSingle;
                // NumericUpDown 的内嵌编辑框是独立子控件，必须逐个上色，否则深色下仍是白底。
                foreach (Control child in num.Controls)
                {
                    Style(child, t);
                    if (child is TextBoxBase inner)
                    {
                        inner.BackColor = t.Sunken;
                        inner.ForeColor = t.Text;
                    }
                }

                NativeDark.Apply(num, t.Dark);

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

    private static bool IsMuted(Control c) => HasTag(c, "muted");

    private static bool HasTag(Control c, string tag) =>
        (c.Tag as string)?.Contains(tag, StringComparison.Ordinal) == true;
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

/// <summary>数字输入：自绘圆角字段 + 上下步进，避免系统 NumericUpDown 在深色下的白底。</summary>
internal sealed class NumberField : Control, IThemed
{
    private const int ArrowWidth = 20;

    private readonly TextBox _edit = new();
    private AppTheme _theme = Theme.Current;
    private int _value;
    private int _minimum;
    private int _maximum = 100;
    private int _increment = 1;
    private bool _sync;
    private bool _hot;

    public NumberField()
    {
        SetStyle(
            ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        Size = new Size(88, 28);
        Font = Fonts.Ui(9F);
        BackColor = Color.Transparent;

        _edit.BorderStyle = BorderStyle.None;
        _edit.TextAlign = HorizontalAlignment.Left;
        _edit.Font = Fonts.Ui(9F);
        _edit.TextChanged += (_, _) => OnTyped();
        _edit.LostFocus += (_, _) => SyncText();
        _edit.GotFocus += (_, _) => Invalidate();
        _edit.KeyDown += OnEditKeyDown;
        _edit.MouseWheel += (_, e) => Step(e.Delta > 0 ? _increment : -_increment);
        Controls.Add(_edit);
        SyncText();
    }

    public event EventHandler? ValueChanged;

    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum < _minimum)
            {
                _maximum = _minimum;
            }

            Reclamp();
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(value, _minimum);
            Reclamp();
        }
    }

    public int Increment
    {
        get => _increment;
        set => _increment = Math.Max(1, value);
    }

    public int Value
    {
        get => _value;
        set
        {
            var next = Math.Clamp(value, _minimum, _maximum);
            if (next == _value)
            {
                SyncText();
                return;
            }

            _value = next;
            SyncText();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void FocusEdit() => _edit.Focus();

    public void ApplyTheme(AppTheme t)
    {
        _theme = t;
        _edit.BackColor = t.Sunken;
        _edit.ForeColor = t.Text;
        NativeDark.Apply(_edit, t.Dark);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        LayoutEdit();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutEdit();
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
        if (e.X >= Width - ArrowWidth)
        {
            Step(e.Y < Height / 2 ? _increment : -_increment);
        }
        else
        {
            _edit.Focus();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Focused || _edit.Focused)
        {
            Step(e.Delta > 0 ? _increment : -_increment);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = _theme;
        e.Graphics.Clear(Parent?.BackColor ?? t.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), 6);
        using (var bg = new SolidBrush(t.Sunken))
        {
            e.Graphics.FillPath(bg, path);
        }

        using (var pen = new Pen(_hot || _edit.Focused ? t.Accent : t.Border))
        {
            e.Graphics.DrawPath(pen, path);
        }

        var cx = Width - ArrowWidth / 2 - 3;
        var cy = Height / 2;
        using var chevron = new Pen(t.Muted, 1.6F) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(chevron, cx - 4, cy - 3, cx, cy - 7);
        e.Graphics.DrawLine(chevron, cx + 4, cy - 3, cx, cy - 7);
        e.Graphics.DrawLine(chevron, cx - 4, cy + 3, cx, cy + 7);
        e.Graphics.DrawLine(chevron, cx + 4, cy + 3, cx, cy + 7);
    }

    private void LayoutEdit()
    {
        var h = Math.Max(12, _edit.PreferredHeight);
        var top = Math.Max(2, (Height - h) / 2);
        var width = Math.Max(20, Width - ArrowWidth - 14);
        _edit.SetBounds(10, top, width, h);
    }

    private void Reclamp()
    {
        var next = Math.Clamp(_value, _minimum, _maximum);
        if (next != _value)
        {
            _value = next;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        SyncText();
    }

    private void Step(int delta)
    {
        if (!Enabled)
        {
            return;
        }

        Value = _value + delta;
    }

    private void OnTyped()
    {
        if (_sync)
        {
            return;
        }

        if (!int.TryParse(_edit.Text.Trim(), out var typed))
        {
            return;
        }

        if (typed < _minimum || typed > _maximum)
        {
            return;
        }

        if (typed != _value)
        {
            _value = typed;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnEditKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up:
                Step(_increment);
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.Down:
                Step(-_increment);
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.Enter:
                SyncText();
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
        }
    }

    private void SyncText()
    {
        _sync = true;
        _edit.Text = _value.ToString();
        _sync = false;
        LayoutEdit();
    }
}