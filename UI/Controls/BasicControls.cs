using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace MicroserviceManager.UI.Controls;

internal enum ButtonKind { Primary, Secondary, Ghost, Danger }

/// <summary>Rounded, flat button with an optional icon glyph.</summary>
internal sealed class FlatButton : Control
{
    private bool _hover;
    private bool _pressed;
    private bool _toggled;

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Cursor = Cursors.Hand;
        Font = Theme.UiSemibold(9.5f);
        Height = LogicalToDeviceUnits(34);
        BackColor = Color.Transparent;
        TabStop = true;
    }

    [DefaultValue(ButtonKind.Secondary)]
    public ButtonKind Kind { get; set; } = ButtonKind.Secondary;

    [DefaultValue(null)]
    public string? Icon { get; set; }

    /// <summary>Shows the button as "on" (for toggles like Follow / Errors only).</summary>
    [DefaultValue(false)]
    public bool Toggled
    {
        get => _toggled;
        set { _toggled = value; Invalidate(); }
    }

    public int PreferredWidth()
    {
        int pad = LogicalToDeviceUnits(14);
        int iconW = Icon is null ? 0 : LogicalToDeviceUnits(16) + (string.IsNullOrEmpty(Text) ? 0 : LogicalToDeviceUnits(8));
        int textW = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        return Math.Max(Height, pad * 2 + iconW + textW - (string.IsNullOrEmpty(Text) ? pad : 0));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = LogicalToDeviceUnits(7);

        (Color fill, Color border, Color text) = Kind switch
        {
            ButtonKind.Primary => (Enabled ? (_pressed ? Theme.Accent : _hover ? Theme.AccentHover : Theme.Accent) : Theme.SurfaceRaised,
                                   Color.Empty, Enabled ? Color.White : Theme.TextFaint),
            ButtonKind.Danger => (_hover ? Theme.Mix(Theme.SurfaceRaised, Theme.Danger, 0.18f) : Theme.SurfaceRaised,
                                  _hover ? Theme.Mix(Theme.Border, Theme.Danger, 0.6f) : Theme.Border,
                                  Enabled ? Theme.Mix(Theme.Text, Theme.Danger, 0.55f) : Theme.TextFaint),
            ButtonKind.Ghost => (_toggled ? Theme.AccentSoft : _hover ? Theme.Hover : Color.Empty, Color.Empty,
                                 !Enabled ? Theme.TextFaint : _toggled ? Theme.AccentHover : _hover ? Theme.Text : Theme.TextMuted),
            _ => (_pressed ? Theme.Selected : _hover ? Theme.Hover : Theme.SurfaceRaised, Theme.Border, Enabled ? Theme.Text : Theme.TextFaint),
        };

        if (fill != Color.Empty) Theme.FillRound(g, fill, r, radius);
        if (border != Color.Empty) Theme.DrawRound(g, border, r, radius);
        if (Focused && ShowFocusCues) Theme.DrawRound(g, Theme.AccentHover, RectangleF.Inflate(r, -1.5f, -1.5f), radius - 1);

        int iconSize = LogicalToDeviceUnits(16);
        int gap = LogicalToDeviceUnits(8);
        bool hasText = !string.IsNullOrEmpty(Text);
        int textW = hasText ? TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width : 0;
        int contentW = (Icon != null ? iconSize : 0) + (Icon != null && hasText ? gap : 0) + textW;
        int x = (Width - contentW) / 2;

        if (Icon != null)
        {
            Theme.DrawIcon(g, Icon, 10f, text, new Rectangle(x, 0, iconSize, Height));
            x += iconSize + (hasText ? gap : 0);
        }
        if (hasText)
            Theme.DrawText(g, Text, Font, text, new Rectangle(x, 0, textW + 2, Height));
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) OnClick(EventArgs.Empty);
        base.OnKeyUp(e);
    }
}

/// <summary>Pill-shaped switch between a few options (QA | UAT1 | UAT3).</summary>
internal sealed class SegmentedControl : Control
{
    private List<string> _items = new();
    private int _selected = -1;
    private int _hover = -1;

    public SegmentedControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Theme.UiSemibold(9.5f);
        Height = LogicalToDeviceUnits(34);
        Cursor = Cursors.Hand;
    }

    public event EventHandler? SelectedIndexChanged;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<string> Items
    {
        get => _items;
        set { _items = value.ToList(); Width = PreferredWidth(); Invalidate(); }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); }
    }

    public string? SelectedItem => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    public int PreferredWidth()
    {
        int w = LogicalToDeviceUnits(6);
        foreach (string s in _items) w += SegmentWidth(s);
        return w;
    }

    private int SegmentWidth(string s) =>
        Math.Max(LogicalToDeviceUnits(54), TextRenderer.MeasureText(s, Font, Size.Empty, TextFormatFlags.NoPadding).Width + LogicalToDeviceUnits(26));

    private IEnumerable<(int Index, Rectangle Bounds)> Segments()
    {
        int x = LogicalToDeviceUnits(3);
        int pad = LogicalToDeviceUnits(3);
        for (int i = 0; i < _items.Count; i++)
        {
            int w = SegmentWidth(_items[i]);
            yield return (i, new Rectangle(x, pad, w, Height - pad * 2));
            x += w;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        float radius = LogicalToDeviceUnits(8);
        var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, Theme.SurfaceRaised, outer, radius);
        Theme.DrawRound(g, Theme.Border, outer, radius);

        foreach (var (i, r) in Segments())
        {
            bool selected = i == _selected;
            if (selected) Theme.FillRound(g, Theme.Accent, r, radius - 2);
            else if (i == _hover) Theme.FillRound(g, Theme.Hover, r, radius - 2);
            Theme.DrawText(g, _items[i], Font, selected ? Color.White : i == _hover ? Theme.Text : Theme.TextMuted, r,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int hit = Segments().Where(s => s.Bounds.Contains(e.Location)).Select(s => s.Index).DefaultIfEmpty(-1).First();
        if (hit != _hover) { _hover = hit; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        foreach (var (i, r) in Segments())
        {
            if (!r.Contains(e.Location) || i == _selected) continue;
            _selected = i;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
        base.OnMouseClick(e);
    }
}

/// <summary>Rounded text box with an optional leading icon and placeholder.</summary>
internal sealed class TextField : Control
{
    private readonly TextBox _box = new();
    private bool _error;

    public TextField()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = LogicalToDeviceUnits(34);
        Cursor = Cursors.IBeam;

        _box.BorderStyle = BorderStyle.None;
        _box.BackColor = Theme.SurfaceRaised;
        _box.ForeColor = Theme.Text;
        _box.Font = Theme.Ui(10f);
        _box.GotFocus += (_, _) => Invalidate();
        _box.LostFocus += (_, _) => Invalidate();
        _box.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
        _box.KeyDown += (_, e) => OnKeyDown(e);
        _box.Leave += (_, _) => OnLeave(EventArgs.Empty);
        Controls.Add(_box);
    }

    [DefaultValue(null)]
    public string? Icon { get; set; }

    [AllowNull]
    public override string Text
    {
        get => _box.Text;
        set => _box.Text = value ?? "";
    }

    [DefaultValue("")]
    public string Placeholder
    {
        get => _box.PlaceholderText;
        set => _box.PlaceholderText = value;
    }

    [DefaultValue(false)]
    public bool ReadOnly
    {
        get => _box.ReadOnly;
        set => _box.ReadOnly = value;
    }

    [DefaultValue(false)]
    public bool HasError
    {
        get => _error;
        set { _error = value; Invalidate(); }
    }

    public TextBox Inner => _box;

    public void SelectAllText() { _box.Focus(); _box.SelectAll(); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        int left = LogicalToDeviceUnits(Icon is null ? 11 : 34);
        int right = LogicalToDeviceUnits(10);
        _box.Width = Math.Max(10, Width - left - right);
        _box.Location = new Point(left, (Height - _box.PreferredHeight) / 2 + 1);
        base.OnLayout(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        float radius = LogicalToDeviceUnits(7);
        Theme.FillRound(g, Theme.SurfaceRaised, r, radius);
        Color border = _error ? Theme.Danger : _box.Focused ? Theme.Accent : Theme.Border;
        Theme.DrawRound(g, border, r, radius, _box.Focused || _error ? 1.4f : 1f);
        if (Icon != null)
            Theme.DrawIcon(g, Icon, 10f, _box.Focused ? Theme.TextSoft : Theme.TextFaint, new Rectangle(LogicalToDeviceUnits(10), 0, LogicalToDeviceUnits(18), Height));
    }

    protected override void OnClick(EventArgs e) { _box.Focus(); base.OnClick(e); }
    protected override void OnEnabledChanged(EventArgs e) { _box.Enabled = Enabled; Invalidate(); base.OnEnabledChanged(e); }
}

/// <summary>Rounded surface that hosts other controls.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
        Padding = new Padding(LogicalToDeviceUnits(1));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        float radius = LogicalToDeviceUnits(10);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRound(g, Theme.Surface, r, radius);
        Theme.DrawRound(g, Theme.Border, r, radius);
    }
}

/// <summary>Base for custom-drawn, vertically scrolling content with a slim overlay scrollbar.</summary>
internal abstract class ScrollPanel : Control
{
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartScroll;
    private bool _barHover;

    protected ScrollPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Surface;
        TabStop = true;
    }

    protected int ScrollY { get; private set; }
    protected abstract int ContentHeight { get; }
    protected virtual int TopInset => 0;
    protected virtual int WheelStep => LogicalToDeviceUnits(48);
    protected int ViewportHeight => Math.Max(0, Height - TopInset);
    protected int MaxScroll => Math.Max(0, ContentHeight - ViewportHeight);
    protected bool AtBottom => ScrollY >= MaxScroll - 2;
    protected int ScrollbarWidth => LogicalToDeviceUnits(12);

    public void ScrollTo(int y)
    {
        int clamped = Math.Clamp(y, 0, MaxScroll);
        if (clamped == ScrollY) return;
        ScrollY = clamped;
        OnScrolled();
        Invalidate();
    }

    public void ScrollToBottom() => ScrollTo(int.MaxValue);

    protected virtual void OnScrolled() { }

    protected abstract void PaintContent(Graphics g);

    protected override void OnPaint(PaintEventArgs e)
    {
        if (ScrollY > MaxScroll) ScrollY = MaxScroll;
        PaintContent(e.Graphics);
        PaintScrollbar(e.Graphics);
    }

    private Rectangle TrackBounds => new(Width - ScrollbarWidth, TopInset, ScrollbarWidth, ViewportHeight);

    private Rectangle ThumbBounds()
    {
        var track = TrackBounds;
        if (ContentHeight <= ViewportHeight || track.Height <= 0) return Rectangle.Empty;
        int h = Math.Max(LogicalToDeviceUnits(28), (int)(track.Height * (ViewportHeight / (double)ContentHeight)));
        int y = track.Y + (int)((track.Height - h) * (ScrollY / (double)Math.Max(1, MaxScroll)));
        return new Rectangle(track.X, y, track.Width, h);
    }

    private void PaintScrollbar(Graphics g)
    {
        var thumb = ThumbBounds();
        if (thumb.IsEmpty) return;
        Theme.Smooth(g);
        int w = _barHover || _dragging ? LogicalToDeviceUnits(7) : LogicalToDeviceUnits(4);
        var r = new RectangleF(thumb.Right - w - LogicalToDeviceUnits(3), thumb.Y + 2, w, thumb.Height - 4);
        Theme.FillRound(g, _dragging ? Theme.TextMuted : _barHover ? Theme.TextFaint : Theme.BorderStrong, r, w / 2f);
    }

    protected bool IsOverScrollbar(Point p) => !ThumbBounds().IsEmpty && TrackBounds.Contains(p);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(ScrollY - Math.Sign(e.Delta) * WheelStep * Math.Max(1, Math.Abs(e.Delta) / 120));
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left && IsOverScrollbar(e.Location))
        {
            var thumb = ThumbBounds();
            if (!thumb.Contains(e.Location))
            {
                double ratio = (e.Y - TopInset - thumb.Height / 2.0) / Math.Max(1, ViewportHeight - thumb.Height);
                ScrollTo((int)(ratio * MaxScroll));
            }
            _dragging = true;
            _dragStartY = e.Y;
            _dragStartScroll = ScrollY;
            Capture = true;
            return;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            var thumb = ThumbBounds();
            double perPixel = MaxScroll / (double)Math.Max(1, ViewportHeight - thumb.Height);
            ScrollTo(_dragStartScroll + (int)((e.Y - _dragStartY) * perPixel));
            return;
        }
        bool over = IsOverScrollbar(e.Location);
        if (over != _barHover) { _barHover = over; Invalidate(TrackBounds); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging) { _dragging = false; Capture = false; Invalidate(); return; }
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_barHover) { _barHover = false; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (ScrollY > MaxScroll) ScrollTo(MaxScroll);
    }
}
