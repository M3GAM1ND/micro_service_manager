using System.Text;
using MicroserviceManager.Core;

namespace MicroserviceManager.UI.Controls;

/// <summary>
/// Console-style view of a <see cref="LogBuffer"/>: colored by level, wraps long lines, follows new output,
/// supports selecting lines and copying. Only visible lines are drawn, so large logs stay cheap.
/// </summary>
internal sealed class LogView : ScrollPanel
{
    private LogBuffer? _buffer;
    private long _seenVersion = -1;
    private long _seenDropped;
    private readonly List<LogLine> _all = new();
    private readonly List<int> _visible = new();
    private int[] _rowStart = Array.Empty<int>();
    private int _totalRows;
    private int _cols = 80;
    private int _charW = 8;
    private int _lineH = 16;
    private bool _errorsOnly;
    private bool _follow = true;
    private int _anchor = -1;
    private int _caret = -1;
    private bool _selecting;
    private readonly ContextMenuStrip _menu = new() { Renderer = new DarkMenuRenderer(), ShowImageMargin = false };

    public LogView()
    {
        Font = Theme.Mono(9.25f);
        MeasureFont();
        _menu.Items.Add("Copy", null, (_, _) => CopySelection());
        _menu.Items.Add("Copy all", null, (_, _) => CopyAll());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Clear", null, (_, _) => ClearRequested?.Invoke());
        foreach (ToolStripItem item in _menu.Items) item.ForeColor = Theme.Text;
        ContextMenuStrip = _menu;
    }

    public event Action? FollowChanged;
    public event Action? ClearRequested;

    public string EmptyText { get; set; } = "No output yet.";

    public LogBuffer? Buffer
    {
        get => _buffer;
        set
        {
            if (ReferenceEquals(_buffer, value)) return;
            _buffer = value;
            _seenVersion = -1;
            _anchor = _caret = -1;
            Follow = true;
            Rebuild();
        }
    }

    public bool ErrorsOnly
    {
        get => _errorsOnly;
        set { _errorsOnly = value; _anchor = _caret = -1; _seenVersion = -1; Rebuild(); }
    }

    public bool Follow
    {
        get => _follow;
        set
        {
            if (_follow == value) return;
            _follow = value;
            if (value) ScrollToBottom();
            FollowChanged?.Invoke();
        }
    }

    private int Pad => LogicalToDeviceUnits(14);
    protected override int ContentHeight => _totalRows * _lineH + Pad * 2;
    protected override int WheelStep => _lineH * 3;

    /// <summary>Called by the UI timer; cheap when nothing changed.</summary>
    public void Poll()
    {
        if (_buffer != null && _buffer.Version != _seenVersion) Rebuild();
    }

    private void Rebuild()
    {
        if (_buffer is null)
        {
            _all.Clear();
            _visible.Clear();
            Recalculate();
            Invalidate();
            return;
        }

        long dropped = _buffer.Dropped;
        long droppedNow = Math.Max(0, dropped - _seenDropped);
        _seenDropped = dropped;
        _seenVersion = _buffer.Version;
        _buffer.CopyTo(_all);

        _visible.Clear();
        for (int i = 0; i < _all.Count; i++)
        {
            if (!_errorsOnly || _all[i].Level is LogLevel.Error or LogLevel.Warning) _visible.Add(i);
        }
        Recalculate();

        if (_follow) ScrollToBottom();
        else if (droppedNow > 0 && !_errorsOnly) ScrollTo(ScrollY - (int)droppedNow * _lineH); // keep the same lines in view
        Invalidate();
    }

    private void MeasureFont()
    {
        var size = TextRenderer.MeasureText("MMMMMMMMMM", Font, Size.Empty, TextFormatFlags.NoPadding);
        _charW = Math.Max(1, size.Width / 10);
        _lineH = size.Height + LogicalToDeviceUnits(3);
    }

    private void Recalculate()
    {
        _cols = Math.Max(20, (Width - Pad * 2 - ScrollbarWidth) / _charW);
        if (_rowStart.Length < _visible.Count + 1) _rowStart = new int[_visible.Count + 64];
        int rows = 0;
        for (int i = 0; i < _visible.Count; i++)
        {
            _rowStart[i] = rows;
            int len = _all[_visible[i]].Text.Length;
            rows += Math.Max(1, (len + _cols - 1) / _cols);
        }
        _rowStart[_visible.Count] = rows;
        _totalRows = rows;
    }

    protected override void OnResize(EventArgs e)
    {
        Recalculate();
        base.OnResize(e);
        if (_follow) ScrollToBottom();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        MeasureFont();
        Recalculate();
    }

    protected override void OnScrolled()
    {
        bool atBottom = AtBottom;
        if (atBottom != _follow)
        {
            _follow = atBottom;
            FollowChanged?.Invoke();
        }
    }

    private int LineAtRow(int row)
    {
        int lo = 0, hi = _visible.Count - 1, found = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_rowStart[mid] <= row) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }

    private int LineAtPoint(Point p)
    {
        if (_visible.Count == 0) return -1;
        int row = (p.Y + ScrollY - Pad) / _lineH;
        if (row < 0) return 0;
        if (row >= _totalRows) return _visible.Count - 1;
        return LineAtRow(row);
    }

    protected override void PaintContent(Graphics g)
    {
        g.Clear(Theme.Surface);
        if (_visible.Count == 0)
        {
            string text = _buffer is null ? EmptyText : _errorsOnly ? "No errors or warnings." : EmptyText;
            Theme.DrawText(g, text, Theme.Ui(9.5f), Theme.TextFaint, new Rectangle(Pad, Pad, Width - Pad * 2, _lineH * 2), TextFormatFlags.Left | TextFormatFlags.Top);
            return;
        }

        int firstRow = Math.Max(0, (ScrollY - Pad) / _lineH);
        int lastRow = (ScrollY + Height) / _lineH + 1;
        int line = LineAtRow(firstRow);
        int selFrom = Math.Min(_anchor, _caret), selTo = Math.Max(_anchor, _caret);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Left | TextFormatFlags.Top;

        using var selBrush = new SolidBrush(Theme.AccentSoft);
        for (; line < _visible.Count && _rowStart[line] <= lastRow; line++)
        {
            var entry = _all[_visible[line]];
            int y = Pad + _rowStart[line] * _lineH - ScrollY;
            int rows = _rowStart[line + 1] - _rowStart[line];

            if (_anchor >= 0 && line >= selFrom && line <= selTo)
                g.FillRectangle(selBrush, 0, y, Width - ScrollbarWidth, rows * _lineH);
            else if (entry.Level == LogLevel.Error)
                using (var b = new SolidBrush(Theme.WithAlpha(Theme.Danger, 18))) g.FillRectangle(b, 0, y, Width - ScrollbarWidth, rows * _lineH);

            Color color = Theme.LogColor(entry.Level);
            string text = entry.Text;
            for (int r = 0; r < rows; r++)
            {
                int start = r * _cols;
                string part = start < text.Length ? text.Substring(start, Math.Min(_cols, text.Length - start)) : "";
                TextRenderer.DrawText(g, part, Font, new Point(Pad, y + r * _lineH + LogicalToDeviceUnits(1)), color, flags);
            }
        }
    }

    // ---------------------------------------------------------------- selection & copy

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (IsOverScrollbar(e.Location) || e.Button != MouseButtons.Left) return;
        int line = LineAtPoint(e.Location);
        if (line < 0) return;
        if ((ModifierKeys & Keys.Shift) != 0 && _anchor >= 0) _caret = line;
        else _anchor = _caret = line;
        _selecting = true;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_selecting) return;
        if (e.Y < 0) ScrollTo(ScrollY - _lineH);
        else if (e.Y > Height) ScrollTo(ScrollY + _lineH);
        int line = LineAtPoint(e.Location);
        if (line >= 0 && line != _caret) { _caret = line; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_selecting) { _selecting = false; Capture = false; }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Control && e.KeyCode == Keys.C) CopySelection();
        else if (e.Control && e.KeyCode == Keys.A) { _anchor = 0; _caret = _visible.Count - 1; Invalidate(); }
        else if (e.KeyCode == Keys.Escape) { _anchor = _caret = -1; Invalidate(); }
        else if (e.KeyCode == Keys.PageDown) ScrollTo(ScrollY + ViewportHeight - _lineH);
        else if (e.KeyCode == Keys.PageUp) ScrollTo(ScrollY - ViewportHeight + _lineH);
        else if (e.KeyCode == Keys.Down) ScrollTo(ScrollY + _lineH);
        else if (e.KeyCode == Keys.Up) ScrollTo(ScrollY - _lineH);
        else if (e.KeyCode == Keys.End) ScrollToBottom();
        else if (e.KeyCode == Keys.Home) ScrollTo(0);
    }

    public bool HasSelection => _anchor >= 0;

    public void CopySelection()
    {
        if (_anchor < 0) { CopyAll(); return; }
        int from = Math.Max(0, Math.Min(_anchor, _caret)), to = Math.Min(_visible.Count - 1, Math.Max(_anchor, _caret));
        var sb = new StringBuilder();
        for (int i = from; i <= to; i++) sb.AppendLine(_all[_visible[i]].Text);
        SetClipboard(sb.ToString());
    }

    public void CopyAll()
    {
        var sb = new StringBuilder();
        foreach (int i in _visible) sb.AppendLine(_all[i].Text);
        SetClipboard(sb.ToString());
    }

    private static void SetClipboard(string text)
    {
        if (text.Length == 0) return;
        try { Clipboard.SetText(text); } catch { /* clipboard busy */ }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _menu.Dispose();
        base.Dispose(disposing);
    }
}
