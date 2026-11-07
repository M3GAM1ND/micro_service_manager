using System.Drawing.Drawing2D;

namespace MicroserviceManager.UI.Controls;

/// <summary>Panel with flicker-free painting.</summary>
internal class SmoothPanel : Panel
{
    public SmoothPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
    }
}

/// <summary>Top bar: logo, app name, current folder, and buttons on the right.</summary>
internal sealed class HeaderBar : SmoothPanel
{
    private string _subtitle = "";

    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        int S(int v) => LogicalToDeviceUnits(v);

        var logo = new Rectangle(S(20), (Height - S(36)) / 2, S(36), S(36));
        using (var path = Theme.RoundRect(logo, S(10)))
        using (var brush = new LinearGradientBrush(logo, Theme.Accent, Theme.Violet, 45f))
            g.FillPath(brush, path);

        int cell = S(8), gap = S(4);
        int gx = logo.X + (logo.Width - cell * 2 - gap) / 2, gy = logo.Y + (logo.Height - cell * 2 - gap) / 2;
        for (int i = 0; i < 4; i++)
        {
            var r = new RectangleF(gx + i % 2 * (cell + gap), gy + i / 2 * (cell + gap), cell, cell);
            Theme.FillRound(g, Color.FromArgb(i == 3 ? 140 : 235, Color.White), r, S(2));
        }

        int tx = logo.Right + S(14);
        Theme.DrawText(g, "Microservice Manager", Theme.Display(13f), Theme.Text, new Rectangle(tx, logo.Y - S(3), Width / 2, S(24)));
        Theme.DrawText(g, _subtitle, Theme.Ui(8.75f), Theme.TextMuted, new Rectangle(tx, logo.Y + S(20), Width / 2, S(18)));
    }
}

/// <summary>Title row above the output: which log is shown, plus filter/follow/copy/clear.</summary>
internal sealed class LogHeader : SmoothPanel
{
    private string _title = "Activity";
    private string _subtitle = "";
    private Color _dot = Theme.TextFaint;

    public LogHeader()
    {
        BackColor = Theme.Surface;
        foreach (var b in new[] { Clear, Copy, Follow, ErrorsOnly, Activity }) Controls.Add(b);
        Follow.Toggled = true;
    }

    public FlatButton Activity { get; } = new() { Kind = ButtonKind.Ghost, Icon = Theme.IconList, Text = "Activity" };
    public FlatButton ErrorsOnly { get; } = new() { Kind = ButtonKind.Ghost, Icon = Theme.IconFilter, Text = "Errors" };
    public FlatButton Follow { get; } = new() { Kind = ButtonKind.Ghost, Icon = Theme.IconDown, Text = "Follow" };
    public FlatButton Copy { get; } = new() { Kind = ButtonKind.Ghost, Icon = Theme.IconCopy };
    public FlatButton Clear { get; } = new() { Kind = ButtonKind.Ghost, Icon = Theme.IconClear };

    public void Set(string title, string subtitle, Color dot)
    {
        if (title == _title && subtitle == _subtitle && dot == _dot) return;
        _title = title;
        _subtitle = subtitle;
        _dot = dot;
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int S(int v) => LogicalToDeviceUnits(v);
        int x = Width - S(10);
        foreach (var b in new[] { Clear, Copy, Follow, ErrorsOnly, Activity })
        {
            b.Height = S(30);
            b.Width = b.PreferredWidth();
            x -= b.Width;
            b.Location = new Point(x, (Height - b.Height) / 2);
            x -= S(4);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        int S(int v) => LogicalToDeviceUnits(v);
        float d = S(8);
        using (var b = new SolidBrush(_dot)) g.FillEllipse(b, S(16), (Height - d) / 2f, d, d);

        var font = Theme.UiSemibold(10.5f);
        int titleW = TextRenderer.MeasureText(_title, font, Size.Empty, TextFormatFlags.NoPadding).Width;
        int maxRight = Activity.Left - S(12);
        Theme.DrawText(g, _title, font, Theme.Text, new Rectangle(S(32), 0, Math.Min(titleW + 2, maxRight - S(32)), Height));
        int sx = S(32) + titleW + S(12);
        if (sx < maxRight)
            Theme.DrawText(g, _subtitle, Theme.Ui(9f), Theme.TextMuted, new Rectangle(sx, 0, maxRight - sx, Height));

        using var pen = new Pen(Theme.Border);
        g.DrawLine(pen, S(12), Height - 1, Width - S(12), Height - 1);
    }
}

/// <summary>Bottom bar: the last thing that happened on the left, totals on the right.</summary>
internal sealed class FooterBar : SmoothPanel
{
    private string _notice = "Ready";
    private bool _error;
    private DateTime _at = DateTime.Now;
    private string _summary = "";
    private string _manager = "";
    private bool _faded;

    public event Action? NoticeClicked;

    public void SetNotice(string text, bool isError)
    {
        _notice = text;
        _error = isError;
        _at = DateTime.Now;
        _faded = false;
        Invalidate();
    }

    public void SetTotals(string summary, string manager)
    {
        if (summary == _summary && manager == _manager) return;
        _summary = summary;
        _manager = manager;
        Invalidate();
    }

    /// <summary>Dims an old message so new ones stand out.</summary>
    public void Tick()
    {
        bool faded = (DateTime.Now - _at).TotalSeconds > (_error ? 30 : 10);
        if (faded != _faded) { _faded = faded; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        int S(int v) => LogicalToDeviceUnits(v);

        var managerFont = Theme.Ui(8.5f);
        int managerW = TextRenderer.MeasureText(_manager, managerFont, Size.Empty, TextFormatFlags.NoPadding).Width;
        int summaryW = TextRenderer.MeasureText(_summary, Theme.Ui(9f), Size.Empty, TextFormatFlags.NoPadding).Width;
        int right = Width - S(20);
        Theme.DrawText(g, _manager, managerFont, Theme.TextFaint, new Rectangle(right - managerW, 0, managerW + 2, Height));
        right -= managerW + S(18);
        Theme.DrawText(g, _summary, Theme.Ui(9f), Theme.TextMuted, new Rectangle(right - summaryW, 0, summaryW + 2, Height));
        right -= summaryW + S(24);

        Color dot = _error ? Theme.Danger : _faded ? Theme.TextFaint : Theme.Success;
        float d = S(7);
        using (var b = new SolidBrush(dot)) g.FillEllipse(b, S(22), (Height - d) / 2f, d, d);
        Color text = _faded ? Theme.TextFaint : _error ? Theme.Mix(Theme.Text, Theme.Danger, 0.45f) : Theme.TextSoft;
        Theme.DrawText(g, _notice.ReplaceLineEndings(" "), Theme.Ui(9f), text, new Rectangle(S(36), 0, Math.Max(0, right - S(36)), Height));
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.X < Width / 2) NoticeClicked?.Invoke();
    }
}

/// <summary>Selectable card with a radio or checkbox indicator, a title and a description.</summary>
internal sealed class ChoiceCard : Control
{
    private bool _checked;
    private bool _hover;

    public ChoiceCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Height = LogicalToDeviceUnits(68);
    }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsRadio { get; set; } = true;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        int S(int v) => LogicalToDeviceUnits(v);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, _checked ? Theme.Mix(Theme.SurfaceRaised, Theme.Accent, 0.08f) : _hover ? Theme.Hover : Theme.SurfaceRaised, r, S(9));
        Theme.DrawRound(g, _checked ? Theme.Accent : Theme.Border, r, S(9), _checked ? 1.5f : 1f);

        var box = new RectangleF(S(16), S(16), S(18), S(18));
        if (IsRadio)
        {
            using var pen = new Pen(_checked ? Theme.Accent : Theme.BorderStrong, 1.6f);
            g.DrawEllipse(pen, box);
            if (_checked)
                using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, RectangleF.Inflate(box, -S(4), -S(4)));
        }
        else if (_checked)
        {
            Theme.FillRound(g, Theme.Accent, box, S(5));
            Theme.DrawIcon(g, Theme.IconCheck, 8f, Color.White, Rectangle.Round(box));
        }
        else Theme.DrawRound(g, Theme.BorderStrong, box, S(5), 1.4f);

        int tx = S(46);
        Theme.DrawText(g, Title, Theme.UiSemibold(10f), Theme.Text, new Rectangle(tx, S(12), Width - tx - S(14), S(22)));
        Theme.DrawText(g, Description, Theme.Ui(8.75f), Theme.TextMuted, new Rectangle(tx, S(34), Width - tx - S(14), Height - S(38)),
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnClick(EventArgs e)
    {
        Checked = IsRadio || !_checked;
        base.OnClick(e);
    }
}
