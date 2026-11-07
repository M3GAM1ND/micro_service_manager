using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using MicroserviceManager.Core;

namespace MicroserviceManager.UI;

/// <summary>Colors, fonts and drawing helpers shared by every control.</summary>
internal static class Theme
{
    public static readonly Color Background = Hex(0x0D1117);
    public static readonly Color Surface = Hex(0x141A22);
    public static readonly Color SurfaceRaised = Hex(0x1A212B);
    public static readonly Color Hover = Hex(0x1F2733);
    public static readonly Color Selected = Hex(0x222C3C);
    public static readonly Color Border = Hex(0x252D39);
    public static readonly Color BorderStrong = Hex(0x344154);
    public static readonly Color Text = Hex(0xE6EAF0);
    public static readonly Color TextSoft = Hex(0xC4CBD6);
    public static readonly Color TextMuted = Hex(0x8B95A5);
    public static readonly Color TextFaint = Hex(0x5B6574);
    public static readonly Color Accent = Hex(0x6E7BFF);
    public static readonly Color AccentHover = Hex(0x8591FF);
    public static readonly Color AccentSoft = Hex(0x232A4D);
    public static readonly Color Success = Hex(0x3FB950);
    public static readonly Color Warning = Hex(0xD9A231);
    public static readonly Color Danger = Hex(0xF2555A);
    public static readonly Color Info = Hex(0x58A6FF);
    public static readonly Color Violet = Hex(0xB083F0);

    private static readonly string UiFamily = FirstInstalled("Segoe UI Variable Text", "Segoe UI");
    private static readonly string DisplayFamily = FirstInstalled("Segoe UI Variable Display", "Segoe UI Semibold", "Segoe UI");
    private static readonly string MonoFamily = FirstInstalled("Cascadia Mono", "Cascadia Code", "Consolas");
    private static readonly string IconFamily = FirstInstalled("Segoe Fluent Icons", "Segoe MDL2 Assets");
    private static readonly Dictionary<(string, float, FontStyle), Font> Fonts = new();

    public static Font Ui(float size = 9.5f, FontStyle style = FontStyle.Regular) => Get(UiFamily, size, style);
    public static Font UiSemibold(float size = 9.5f) => Get(FirstInstalled("Segoe UI Semibold", UiFamily), size, FontStyle.Regular);
    public static Font Display(float size) => Get(DisplayFamily, size, FontStyle.Bold);
    public static Font Mono(float size = 9f) => Get(MonoFamily, size, FontStyle.Regular);
    public static Font Icons(float size = 10f) => Get(IconFamily, size, FontStyle.Regular);

    // Segoe MDL2 / Fluent icon glyphs
    public const string IconPlay = "";
    public const string IconStop = "";
    public const string IconRestart = "";
    public const string IconGlobe = "";
    public const string IconSettings = "";
    public const string IconSearch = "";
    public const string IconMore = "";
    public const string IconFolder = "";
    public const string IconCode = "";
    public const string IconCopy = "";
    public const string IconClear = "";
    public const string IconCheck = "";
    public const string IconWarning = "";
    public const string IconSync = "";
    public const string IconHistory = "";
    public const string IconFilter = "";
    public const string IconDown = "";
    public const string IconLink = "";
    public const string IconEdit = "";
    public const string IconList = "";

    public static Color StateColor(ServiceInfo s)
    {
        if (s.InVisualStudio && !s.IsActive) return Violet;
        return s.State switch
        {
            ServiceState.Running => Success,
            ServiceState.Starting or ServiceState.Stopping => Warning,
            ServiceState.Building or ServiceState.Queued => Info,
            ServiceState.Crashed or ServiceState.BuildFailed or ServiceState.PortBusy => Danger,
            _ => s.IsLocal ? Warning : TextFaint,
        };
    }

    public static string StateText(ServiceInfo s, string remoteName)
    {
        if (s.InVisualStudio && !s.IsActive) return "Visual Studio";
        return s.State switch
        {
            ServiceState.Running => s.RunningSince is { } since ? $"Running · {Uptime(DateTime.Now - since)}" : "Running",
            ServiceState.Starting => "Starting…",
            ServiceState.Building => "Building…",
            ServiceState.Queued => "Waiting to build",
            ServiceState.Stopping => "Stopping…",
            ServiceState.Crashed => s.ExitCode is int code ? $"Crashed ({code})" : "Crashed",
            ServiceState.BuildFailed => "Build failed",
            ServiceState.PortBusy => "Port in use",
            _ => s.IsLocal ? "Local · not running" : $"Remote · {remoteName}",
        };
    }

    public static string Uptime(TimeSpan t) =>
        t.TotalSeconds < 60 ? $"{(int)t.TotalSeconds}s" :
        t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes}m" :
        $"{(int)t.TotalHours}h {t.Minutes}m";

    public static string Bytes(long bytes) =>
        bytes <= 0 ? "—" :
        bytes < 1024L * 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0} MB" :
        $"{bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB";

    public static Color LogColor(LogLevel level) => level switch
    {
        LogLevel.Error => Hex(0xFF7B72),
        LogLevel.Warning => Hex(0xE3B341),
        LogLevel.Debug => TextFaint,
        LogLevel.Build => TextMuted,
        LogLevel.System => Hex(0x9DA7FF),
        _ => TextSoft,
    };

    // ---------------------------------------------------------------- drawing

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
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

    public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
    {
        using var path = RoundRect(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRound(Graphics g, Color color, RectangleF r, float radius, float width = 1f)
    {
        using var path = RoundRect(r, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds,
        TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter)
    {
        TextRenderer.DrawText(g, text, font, bounds, color, flags | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    public static void DrawIcon(Graphics g, string glyph, float size, Color color, Rectangle bounds)
    {
        TextRenderer.DrawText(g, glyph, Icons(size), bounds, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static Color WithAlpha(Color c, int alpha) => Color.FromArgb(alpha, c);

    // ---------------------------------------------------------------- windows chrome

    /// <summary>Dark title bar, matching caption color and rounded corners on Windows 10 1809+ / 11.</summary>
    public static void StyleWindow(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;
        form.Font = Ui();
        form.HandleCreated += (_, _) =>
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
            int on = 1;
            DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            int caption = ToColorRef(Background);
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR (Win11)
            int border = ToColorRef(Border);
            DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int)); // DWMWA_BORDER_COLOR (Win11)
            int round = 2;
            DwmSetWindowAttribute(form.Handle, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE = round
        };
    }

    /// <summary>Dark native scrollbars for standard controls (TextBox, ListBox).</summary>
    public static void DarkScrollbars(Control control)
    {
        control.HandleCreated += (_, _) =>
        {
            try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); } catch { }
        };
    }

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    private static Color Hex(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    private static Font Get(string family, float size, FontStyle style)
    {
        var key = (family, size, style);
        lock (Fonts)
        {
            if (!Fonts.TryGetValue(key, out var font)) Fonts[key] = font = new Font(family, size, style);
            return font;
        }
    }

    private static string FirstInstalled(params string[] families)
    {
        foreach (string name in families)
        {
            using var probe = new Font(name, 10f);
            if (string.Equals(probe.Name, name, StringComparison.OrdinalIgnoreCase)) return name;
        }
        return families[^1];
    }
}

/// <summary>Dark context menus.</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new Colors()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextFaint;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.TextMuted;
        base.OnRenderArrow(e);
    }

    private sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.SurfaceRaised;
        public override Color ImageMarginGradientBegin => Theme.SurfaceRaised;
        public override Color ImageMarginGradientMiddle => Theme.SurfaceRaised;
        public override Color ImageMarginGradientEnd => Theme.SurfaceRaised;
        public override Color MenuBorder => Theme.BorderStrong;
        public override Color MenuItemBorder => Theme.Selected;
        public override Color MenuItemSelected => Theme.Selected;
        public override Color MenuItemSelectedGradientBegin => Theme.Selected;
        public override Color MenuItemSelectedGradientEnd => Theme.Selected;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }
}
