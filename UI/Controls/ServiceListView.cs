using MicroserviceManager.Core;

namespace MicroserviceManager.UI.Controls;

internal enum RowAction { None, Check, StartStop, Restart, Browse, More, Warning }

/// <summary>The services table: custom-drawn so it stays smooth and light with many rows.</summary>
internal sealed class ServiceListView : ScrollPanel
{
    private IReadOnlyList<ServiceInfo> _items = Array.Empty<ServiceInfo>();
    private ServiceInfo? _selected;
    private int _hoverRow = -1;
    private RowAction _hoverAction;
    private bool _hoverHeaderCheck;
    private readonly ToolTip _tip = new() { InitialDelay = 350, ReshowDelay = 80, AutoPopDelay = 8000 };
    private string? _tipText;

    public ServiceListView()
    {
        Font = Theme.Ui(9.5f);
    }

    public string RemoteName { get; set; } = "QA";
    public string EmptyText { get; set; } = "";

    public event Action<ServiceInfo>? ToggleLocal;
    public event Action<ServiceInfo, RowAction, Point>? ActionClicked;
    public event Action<ServiceInfo?>? SelectionChanged;
    public event Action<ServiceInfo, Point>? MenuRequested;
    public event Action<bool>? ToggleAll;

    private int RowHeight => LogicalToDeviceUnits(58);
    private int HeaderHeight => LogicalToDeviceUnits(38);
    protected override int TopInset => HeaderHeight;
    protected override int ContentHeight => _items.Count * RowHeight + LogicalToDeviceUnits(6);
    protected override int WheelStep => RowHeight;

    public ServiceInfo? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            if (value != null) ScrollIntoView(IndexOf(value));
            Invalidate();
            SelectionChanged?.Invoke(value);
        }
    }

    public void SetItems(IReadOnlyList<ServiceInfo> items)
    {
        _items = items;
        Invalidate();
    }

    // ================================================================== layout

    private readonly record struct Columns(
        Rectangle Check, Rectangle Name, Rectangle Endpoint, Rectangle Status, Rectangle Cpu, Rectangle Memory, Rectangle Actions);

    private Columns ColumnsFor(int y, int height)
    {
        int S(int v) => LogicalToDeviceUnits(v);
        int pad = S(18);
        int right = Width - ScrollbarWidth - S(6);
        int actionsW = S(4 * 30 + 3 * 2);
        int memW = S(78), cpuW = S(118), statusW = S(178), endpointW = S(138), checkW = S(30);

        int nameW = right - pad - checkW - S(10) - endpointW - statusW - cpuW - memW - actionsW - S(8);
        if (nameW < S(190)) { nameW += endpointW; endpointW = 0; }
        if (nameW < S(170)) { nameW += cpuW - S(64); cpuW = S(64); }

        int x = pad;
        Rectangle Take(int w) { var r = new Rectangle(x, y, w, height); x += w; return r; }
        var check = Take(checkW);
        x += S(10);
        var name = Take(nameW);
        var endpoint = Take(endpointW);
        var status = Take(statusW);
        var cpu = Take(cpuW);
        var memory = Take(memW);
        x = right - actionsW;
        var actions = Take(actionsW);
        return new Columns(check, name, endpoint, status, cpu, memory, actions);
    }

    private Rectangle RowBounds(int index) =>
        new(0, TopInset + index * RowHeight - ScrollY + LogicalToDeviceUnits(3), Width - ScrollbarWidth, RowHeight);

    private Rectangle CheckBox(Rectangle cell)
    {
        int s = LogicalToDeviceUnits(18);
        return new Rectangle(cell.X + (cell.Width - s) / 2, cell.Y + (cell.Height - s) / 2, s, s);
    }

    private IEnumerable<(RowAction Action, Rectangle Bounds)> ActionButtons(Rectangle cell)
    {
        int s = LogicalToDeviceUnits(30), gap = LogicalToDeviceUnits(2);
        int y = cell.Y + (cell.Height - s) / 2;
        int x = cell.X;
        foreach (var a in new[] { RowAction.StartStop, RowAction.Restart, RowAction.Browse, RowAction.More })
        {
            yield return (a, new Rectangle(x, y, s, s));
            x += s + gap;
        }
    }

    private static bool CanRestart(ServiceInfo s) => s.State is ServiceState.Running or ServiceState.Starting or ServiceState.Crashed;
    private static bool CanBrowse(ServiceInfo s) => s.State == ServiceState.Running || s.InVisualStudio;

    // ================================================================== painting

    protected override void PaintContent(Graphics g)
    {
        g.Clear(Theme.Surface);
        Theme.Smooth(g);

        if (_items.Count == 0)
        {
            Theme.DrawText(g, EmptyText, Theme.Ui(10f), Theme.TextMuted,
                new Rectangle(0, TopInset, Width, Math.Max(LogicalToDeviceUnits(120), ViewportHeight)),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
        else
        {
            var clip = g.Clip;
            g.SetClip(new Rectangle(0, TopInset, Width, ViewportHeight));
            int first = Math.Max(0, ScrollY / RowHeight - 1);
            int last = Math.Min(_items.Count - 1, (ScrollY + ViewportHeight) / RowHeight + 1);
            for (int i = first; i <= last; i++) PaintRow(g, _items[i], i, RowBounds(i));
            g.Clip = clip;
        }

        PaintHeader(g);
    }

    private void PaintHeader(Graphics g)
    {
        var header = new Rectangle(0, 0, Width, HeaderHeight);
        using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, header);
        using (var p = new Pen(Theme.Border)) g.DrawLine(p, LogicalToDeviceUnits(12), header.Bottom - 1, Width - LogicalToDeviceUnits(12), header.Bottom - 1);

        var c = ColumnsFor(0, HeaderHeight);
        var font = Theme.UiSemibold(8f);
        void Title(string t, Rectangle r) { if (r.Width > 0) Theme.DrawText(g, t, font, Theme.TextFaint, r); }

        int localCount = _items.Count(s => s.IsLocal);
        DrawCheck(g, CheckBox(c.Check), localCount > 0 && localCount == _items.Count, localCount > 0 && localCount < _items.Count, _hoverHeaderCheck);
        Title("SERVICE  ·  tick to run locally", c.Name);
        Title("ENDPOINT", c.Endpoint);
        Title("STATUS", c.Status);
        Title("CPU", c.Cpu);
        Title("MEMORY", c.Memory);
    }

    private void PaintRow(Graphics g, ServiceInfo s, int index, Rectangle row)
    {
        int S(int v) => LogicalToDeviceUnits(v);
        bool selected = ReferenceEquals(s, _selected);
        bool hover = index == _hoverRow;

        var bg = new RectangleF(row.X + S(8), row.Y + S(2), row.Width - S(14), row.Height - S(4));
        if (selected) Theme.FillRound(g, Theme.Selected, bg, S(9));
        else if (hover) Theme.FillRound(g, Theme.Hover, bg, S(9));
        if (s.IsLocal) Theme.FillRound(g, Theme.Accent, new RectangleF(bg.X + S(1), bg.Y + S(14), S(3), bg.Height - S(28)), S(2));

        var c = ColumnsFor(row.Y, row.Height);

        // Local checkbox
        DrawCheck(g, CheckBox(c.Check), s.IsLocal, false, hover && _hoverAction == RowAction.Check);

        // Name + key mapping
        var nameFont = Theme.UiSemibold(10.5f);
        var nameRect = new Rectangle(c.Name.X, row.Y + S(9), c.Name.Width - S(8), S(22));
        Theme.DrawText(g, s.Name, nameFont, Theme.Text, nameRect);
        if (s.DisplayHint is { } hint)
        {
            int nameW = TextRenderer.MeasureText(s.Name, nameFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            Theme.DrawText(g, hint, Theme.Ui(8.5f), Theme.TextFaint, new Rectangle(nameRect.X + nameW + S(8), nameRect.Y + S(1), Math.Max(0, nameRect.Width - nameW - S(8)), nameRect.Height));
        }

        var subRect = new Rectangle(c.Name.X, row.Y + S(31), c.Name.Width - S(8), S(18));
        if (s.EnvKey != null && s.EnvKeyFound)
        {
            string text = "→ " + s.EnvKey + (s.EnvKeyIsManual ? "   (set by you)" : "");
            Theme.DrawText(g, text, Theme.Mono(8.5f), s.IsLocal ? Theme.TextSoft : Theme.TextMuted, subRect);
        }
        else
        {
            Theme.DrawIcon(g, Theme.IconWarning, 8.5f, Theme.Warning, new Rectangle(subRect.X, subRect.Y, S(14), subRect.Height));
            Theme.DrawText(g, s.EnvKey is null ? "No environment.ts key — right-click → Set key" : $"\"{s.EnvKey}\" not in environment.ts",
                Theme.Ui(8.5f), Theme.Warning, new Rectangle(subRect.X + S(19), subRect.Y, subRect.Width - S(19), subRect.Height));
        }

        // Endpoint
        if (c.Endpoint.Width > 0)
        {
            string endpoint = s.LocalUrl.Replace("http://", "", StringComparison.OrdinalIgnoreCase).Replace("https://", "", StringComparison.OrdinalIgnoreCase);
            Theme.DrawText(g, endpoint, Theme.Mono(9f), s.IsLocal ? Theme.TextSoft : Theme.TextFaint, Inset(c.Endpoint, S(8)));
        }

        // Status pill
        PaintStatus(g, s, c.Status);

        // CPU (+ sparkline) and memory
        bool live = s.IsActive && s.MemoryBytes > 0;
        if (live)
        {
            int sparkW = c.Cpu.Width >= S(100) ? S(48) : 0;
            if (sparkW > 0) PaintSparkline(g, s.CpuHistory.Snapshot(), new Rectangle(c.Cpu.X, c.Cpu.Y + (c.Cpu.Height - S(22)) / 2, sparkW, S(22)));
            Theme.DrawText(g, $"{s.CpuPercent:0.0}%", Theme.Mono(9f), Theme.TextSoft, new Rectangle(c.Cpu.X + sparkW + S(8), c.Cpu.Y, c.Cpu.Width - sparkW - S(8), c.Cpu.Height));
            Theme.DrawText(g, Theme.Bytes(s.MemoryBytes), Theme.Mono(9f), Theme.TextSoft, c.Memory);
        }
        else
        {
            Theme.DrawText(g, "—", Theme.Mono(9f), Theme.TextFaint, new Rectangle(c.Cpu.X + (c.Cpu.Width >= S(100) ? S(56) : 0), c.Cpu.Y, S(40), c.Cpu.Height));
            Theme.DrawText(g, "—", Theme.Mono(9f), Theme.TextFaint, c.Memory);
        }

        // Action buttons
        foreach (var (action, r) in ActionButtons(c.Actions))
        {
            bool enabled = action switch
            {
                RowAction.Restart => CanRestart(s),
                RowAction.Browse => CanBrowse(s),
                _ => true,
            };
            bool hot = hover && _hoverAction == action && enabled;
            if (hot) Theme.FillRound(g, selected ? Theme.BorderStrong : Theme.Selected, r, S(7));

            (string glyph, Color color) = action switch
            {
                RowAction.StartStop when s.IsActive => (Theme.IconStop, hot ? Theme.Danger : Theme.Mix(Theme.TextMuted, Theme.Danger, 0.5f)),
                RowAction.StartStop => (Theme.IconPlay, hot ? Theme.Success : Theme.Mix(Theme.TextMuted, Theme.Success, 0.55f)),
                RowAction.Restart => (Theme.IconRestart, enabled ? (hot ? Theme.Text : Theme.TextMuted) : Theme.Border),
                RowAction.Browse => (Theme.IconGlobe, enabled ? (hot ? Theme.Text : Theme.TextMuted) : Theme.Border),
                _ => (Theme.IconMore, hot ? Theme.Text : Theme.TextMuted),
            };
            Theme.DrawIcon(g, glyph, 10f, color, r);
        }
    }

    private void PaintStatus(Graphics g, ServiceInfo s, Rectangle cell)
    {
        int S(int v) => LogicalToDeviceUnits(v);
        string text = Theme.StateText(s, RemoteName);
        Color color = Theme.StateColor(s);
        bool quiet = color == Theme.TextFaint;
        var font = Theme.Ui(8.75f);
        int textW = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding).Width;
        int h = S(24);
        int w = Math.Min(cell.Width - S(8), textW + S(30));
        var pill = new RectangleF(cell.X, cell.Y + (cell.Height - h) / 2f, w, h);

        Theme.FillRound(g, quiet ? Theme.SurfaceRaised : Theme.WithAlpha(color, 34), pill, h / 2f);
        Theme.DrawRound(g, quiet ? Theme.Border : Theme.WithAlpha(color, 70), pill, h / 2f);

        // Pulsing dot for in-between states.
        int alpha = 255;
        if (s.State is ServiceState.Building or ServiceState.Starting or ServiceState.Queued or ServiceState.Stopping)
            alpha = 110 + (int)(145 * (0.5 + 0.5 * Math.Sin(Environment.TickCount64 / 180.0)));
        float dot = S(7);
        using (var b = new SolidBrush(Color.FromArgb(alpha, quiet ? Theme.TextFaint : color)))
            g.FillEllipse(b, pill.X + S(10), pill.Y + (h - dot) / 2f, dot, dot);

        Theme.DrawText(g, text, font, quiet ? Theme.TextMuted : Theme.Mix(color, Theme.Text, 0.25f),
            new Rectangle((int)pill.X + S(22), (int)pill.Y, (int)pill.Width - S(26), h));
    }

    private void PaintSparkline(Graphics g, float[] values, Rectangle r)
    {
        using (var track = new Pen(Theme.Border)) g.DrawLine(track, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
        if (values.Length < 2) return;
        float max = Math.Max(10f, values.Max());
        int capacity = 48;
        float step = r.Width / (float)(capacity - 1);
        var pts = new PointF[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            float x = r.Right - (values.Length - 1 - i) * step;
            float y = r.Bottom - 1 - values[i] / max * (r.Height - 3);
            pts[i] = new PointF(x, y);
        }
        var fill = new PointF[pts.Length + 2];
        pts.CopyTo(fill, 0);
        fill[^2] = new PointF(pts[^1].X, r.Bottom);
        fill[^1] = new PointF(pts[0].X, r.Bottom);
        using (var b = new SolidBrush(Theme.WithAlpha(Theme.Accent, 46))) g.FillPolygon(b, fill);
        using (var p = new Pen(Theme.AccentHover, LogicalToDeviceUnits(1) * 1.4f)) g.DrawLines(p, pts);
    }

    private void DrawCheck(Graphics g, Rectangle r, bool isChecked, bool partial, bool hot)
    {
        float radius = LogicalToDeviceUnits(5);
        if (isChecked || partial)
        {
            Theme.FillRound(g, hot ? Theme.AccentHover : Theme.Accent, r, radius);
            if (isChecked) Theme.DrawIcon(g, Theme.IconCheck, 8f, Color.White, r);
            else
            {
                using var p = new Pen(Color.White, LogicalToDeviceUnits(2));
                g.DrawLine(p, r.X + r.Width * 0.28f, r.Y + r.Height / 2f, r.Right - r.Width * 0.28f, r.Y + r.Height / 2f);
            }
        }
        else
        {
            Theme.FillRound(g, Theme.SurfaceRaised, r, radius);
            Theme.DrawRound(g, hot ? Theme.TextMuted : Theme.BorderStrong, r, radius, 1.3f);
        }
    }

    private static Rectangle Inset(Rectangle r, int right) => new(r.X, r.Y, Math.Max(0, r.Width - right), r.Height);

    // ================================================================== input

    private (int Row, RowAction Action) HitTest(Point p)
    {
        if (p.Y < TopInset) return (-1, RowAction.None);
        int index = (p.Y - TopInset + ScrollY - LogicalToDeviceUnits(3)) / RowHeight;
        if (index < 0 || index >= _items.Count) return (-1, RowAction.None);

        var row = RowBounds(index);
        var c = ColumnsFor(row.Y, row.Height);
        if (Rectangle.Inflate(CheckBox(c.Check), LogicalToDeviceUnits(6), LogicalToDeviceUnits(6)).Contains(p)) return (index, RowAction.Check);
        foreach (var (action, r) in ActionButtons(c.Actions))
            if (r.Contains(p)) return (index, action);
        var s = _items[index];
        if (!(s.EnvKey != null && s.EnvKeyFound) && new Rectangle(c.Name.X, row.Y + LogicalToDeviceUnits(31), c.Name.Width, LogicalToDeviceUnits(18)).Contains(p))
            return (index, RowAction.Warning);
        return (index, RowAction.None);
    }

    private bool HeaderCheckHit(Point p)
    {
        if (p.Y >= TopInset) return false;
        var c = ColumnsFor(0, HeaderHeight);
        return Rectangle.Inflate(CheckBox(c.Check), LogicalToDeviceUnits(6), LogicalToDeviceUnits(6)).Contains(p);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsOverScrollbar(e.Location) || Capture && e.Button != MouseButtons.None) return;

        var (row, action) = HitTest(e.Location);
        bool headerCheck = HeaderCheckHit(e.Location);
        if (row != _hoverRow || action != _hoverAction || headerCheck != _hoverHeaderCheck)
        {
            _hoverRow = row;
            _hoverAction = action;
            _hoverHeaderCheck = headerCheck;
            Invalidate();
        }

        Cursor = action is RowAction.None or RowAction.Warning && !headerCheck ? Cursors.Default : Cursors.Hand;

        string? tip = null;
        if (headerCheck) tip = "Tick / untick all";
        else if (row >= 0)
        {
            var s = _items[row];
            tip = action switch
            {
                RowAction.Check => s.IsLocal ? "Point Angular back at the remote service (stops it if running)" : "Run locally: Angular and the database switch to your machine",
                RowAction.StartStop => s.IsActive ? "Stop" : "Start here (output shows below)",
                RowAction.Restart => CanRestart(s) ? "Restart" : null,
                RowAction.Browse => CanBrowse(s) ? "Open " + s.BrowseUrl : null,
                RowAction.More => "More actions",
                RowAction.Warning => "Angular keeps calling the remote service because no key in environment.ts matched this project. Right-click → Set environment key.",
                _ => null,
            };
        }
        if (tip != _tipText)
        {
            _tipText = tip;
            if (tip is null) _tip.Hide(this);
            else _tip.Show(tip, this, e.X + LogicalToDeviceUnits(12), e.Y + LogicalToDeviceUnits(18), 6000);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverRow = -1;
        _hoverAction = RowAction.None;
        _hoverHeaderCheck = false;
        _tipText = null;
        _tip.Hide(this);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (IsOverScrollbar(e.Location)) return;

        if (HeaderCheckHit(e.Location) && e.Button == MouseButtons.Left)
        {
            bool allLocal = _items.Count > 0 && _items.All(s => s.IsLocal);
            ToggleAll?.Invoke(!allLocal);
            return;
        }

        var (row, action) = HitTest(e.Location);
        if (row < 0) return;
        var svc = _items[row];

        if (e.Button == MouseButtons.Right)
        {
            Selected = svc;
            MenuRequested?.Invoke(svc, e.Location);
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        switch (action)
        {
            case RowAction.Check:
                ToggleLocal?.Invoke(svc);
                break;
            case RowAction.StartStop or RowAction.Restart or RowAction.Browse or RowAction.More:
                if (action == RowAction.Restart && !CanRestart(svc) || action == RowAction.Browse && !CanBrowse(svc)) return;
                Selected = svc;
                ActionClicked?.Invoke(svc, action, e.Location);
                break;
            default:
                Selected = svc;
                break;
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_items.Count == 0) return;
        int index = _selected is null ? -1 : IndexOf(_selected);

        switch (e.KeyCode)
        {
            case Keys.Down: Select(Math.Min(_items.Count - 1, index + 1)); break;
            case Keys.Up: Select(Math.Max(0, index - 1)); break;
            case Keys.Space when _selected != null: ToggleLocal?.Invoke(_selected); break;
            case Keys.Enter when _selected != null: ActionClicked?.Invoke(_selected, RowAction.StartStop, Point.Empty); break;
            case Keys.Apps when _selected != null:
                MenuRequested?.Invoke(_selected, new Point(LogicalToDeviceUnits(60), RowBounds(index).Bottom));
                break;
        }
    }

    private int IndexOf(ServiceInfo s)
    {
        for (int i = 0; i < _items.Count; i++) if (ReferenceEquals(_items[i], s)) return i;
        return -1;
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        Selected = _items[index];
    }

    private void ScrollIntoView(int index)
    {
        if (index < 0 || ViewportHeight <= 0) return;
        int top = index * RowHeight;
        if (top < ScrollY) ScrollTo(top);
        else if (top + RowHeight > ScrollY + ViewportHeight) ScrollTo(top + RowHeight - ViewportHeight + LogicalToDeviceUnits(6));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
