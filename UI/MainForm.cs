using System.Diagnostics;
using MicroserviceManager.Core;
using MicroserviceManager.UI.Controls;

namespace MicroserviceManager.UI;

internal sealed class MainForm : Form
{
    private readonly Workspace _ws;

    private readonly HeaderBar _header = new();
    private readonly FlatButton _rescan = new() { Kind = ButtonKind.Ghost, Icon = Theme.IconSync, Text = "Rescan" };
    private readonly FlatButton _settings = new() { Kind = ButtonKind.Ghost, Icon = Theme.IconSettings, Text = "Settings" };

    private readonly SmoothPanel _toolbar = new();
    private readonly Label _envLabel = SmallLabel("REMOTE");
    private readonly SegmentedControl _envSwitch = new();
    private readonly Label _dbLabel = SmallLabel("DATABASE");
    private readonly TextField _database = new() { Placeholder = "Database name" };
    private readonly TextField _search = new() { Icon = Theme.IconSearch, Placeholder = "Filter services   Ctrl+F" };
    private readonly FlatButton _lastSession = new() { Kind = ButtonKind.Ghost, Icon = Theme.IconHistory, Text = "Last session" };
    private readonly FlatButton _startLocal = new() { Kind = ButtonKind.Primary, Icon = Theme.IconPlay, Text = "Start local" };
    private readonly FlatButton _stopAll = new() { Kind = ButtonKind.Danger, Icon = Theme.IconStop, Text = "Stop all" };

    private readonly SplitContainer _split = new();
    private readonly ServiceListView _list = new();
    private readonly LogHeader _logHeader = new();
    private readonly LogView _log = new();
    private readonly FooterBar _status = new();

    private readonly ContextMenuStrip _rowMenu = new() { Renderer = new DarkMenuRenderer(), ShowImageMargin = false };
    private readonly ToolTip _tips = new() { InitialDelay = 400 };
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 150 };
    private readonly System.Windows.Forms.Timer _metricsTimer = new() { Interval = 2000 };

    private FileSystemWatcher? _profilesWatcher;
    private volatile bool _profilesChanged;
    private DateTime _profilesChangedAt;
    private long _seenVersion = -1;
    private int _seenSignature;
    private bool _showActivity = true;
    private bool _initialized;
    private bool _closing;

    public MainForm(Workspace workspace)
    {
        _ws = workspace;
        SuspendLayout();
        Theme.StyleWindow(this);
        Text = "Microservice Manager";
        AutoScaleMode = AutoScaleMode.None; // sizes are already DPI-scaled in code
        StartPosition = FormStartPosition.CenterScreen;
        // Never start bigger than the screen (e.g. 150% scaling on a 1080p display).
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        MinimumSize = new Size(Math.Min(LogicalToDeviceUnits(960), area.Width), Math.Min(LogicalToDeviceUnits(600), area.Height));
        Size = new Size(Math.Min(LogicalToDeviceUnits(1280), area.Width - LogicalToDeviceUnits(24)),
                        Math.Min(LogicalToDeviceUnits(820), area.Height - LogicalToDeviceUnits(24)));
        KeyPreview = true;

        BuildHeader();
        BuildToolbar();
        var content = BuildContent();
        _status.Dock = DockStyle.Bottom;
        _status.Height = LogicalToDeviceUnits(34);
        _status.NoticeClicked += () => ShowActivity(true);

        // Docking order: the fill control first, then edges from the inside out.
        Controls.Add(content);
        Controls.Add(_status);
        Controls.Add(_toolbar);
        Controls.Add(_header);
        ResumeLayout(true);

        _uiTimer.Tick += (_, _) => RefreshUi(force: false);
        _metricsTimer.Tick += (_, _) => _ws.SampleMetrics();
        RestorePlacement();
    }

    // ================================================================== building the window

    private static Label SmallLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.UiSemibold(8f),
        ForeColor = Theme.TextFaint,
        BackColor = Color.Transparent,
    };

    private void BuildHeader()
    {
        _header.Dock = DockStyle.Top;
        _header.Height = LogicalToDeviceUnits(70);
        _header.Controls.Add(_rescan);
        _header.Controls.Add(_settings);
        _header.Layout += (_, _) =>
        {
            int x = _header.Width - LogicalToDeviceUnits(18);
            foreach (var b in new[] { _settings, _rescan })
            {
                b.Width = b.PreferredWidth();
                x -= b.Width;
                b.Location = new Point(x, (_header.Height - b.Height) / 2);
                x -= LogicalToDeviceUnits(4);
            }
        };
        _rescan.Click += (_, _) => Rescan();
        _settings.Click += (_, _) => OpenSettings();
        _tips.SetToolTip(_rescan, "Scan the services folder again (F5)");
    }

    private void BuildToolbar()
    {
        _toolbar.Dock = DockStyle.Top;
        _toolbar.Height = LogicalToDeviceUnits(58);
        foreach (Control c in new Control[] { _envLabel, _envSwitch, _dbLabel, _database, _search, _lastSession, _startLocal, _stopAll })
            _toolbar.Controls.Add(c);
        _toolbar.Layout += (_, _) => LayoutToolbar();

        _envSwitch.SelectedIndexChanged += (_, _) =>
        {
            if (_envSwitch.SelectedItem is { } env) Guard(() => _ws.SetEnvironment(env));
        };
        _database.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            CommitDatabase();
            _list.Focus();
        };
        _database.Leave += (_, _) => CommitDatabase();
        _search.TextChanged += (_, _) => ApplyFilter();
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { _search.Text = ""; e.SuppressKeyPress = true; }
        };
        _startLocal.Click += (_, _) => StartLocal();
        _stopAll.Click += async (_, _) => await GuardAsync(_ws.StopAllAsync);
        _lastSession.Click += (_, _) => Guard(_ws.StartLastSession);

        _tips.SetToolTip(_envSwitch, "Where Angular sends requests for services that aren't running locally");
        _tips.SetToolTip(_database, "Database used by services running locally (press Enter to apply)");
        _tips.SetToolTip(_startLocal, "Start every ticked service that isn't running yet");
        _tips.SetToolTip(_stopAll, "Stop everything and put environment.ts and appsettings back to your repo's values");
    }

    private void LayoutToolbar()
    {
        int S(int v) => LogicalToDeviceUnits(v);
        int h = _toolbar.Height;
        int Center(Control c) => (h - c.Height) / 2;

        int x = S(20);
        _envLabel.Location = new Point(x, Center(_envLabel));
        x += _envLabel.Width + S(10);
        _envSwitch.Location = new Point(x, Center(_envSwitch));
        x += _envSwitch.Width + S(26);
        _dbLabel.Location = new Point(x, Center(_dbLabel));
        x += _dbLabel.Width + S(10);
        _database.SetBounds(x, (h - S(34)) / 2, S(200), S(34));
        x += _database.Width + S(16);

        int right = _toolbar.Width - S(20);
        foreach (var b in new[] { _stopAll, _startLocal, _lastSession })
        {
            if (!b.Visible) continue;
            b.Width = b.PreferredWidth();
            right -= b.Width;
            b.Location = new Point(right, Center(b));
            right -= S(8);
        }

        int searchW = Math.Clamp(right - S(8) - x, S(120), S(300));
        _search.SetBounds(x, (h - S(34)) / 2, searchW, S(34));
    }

    private Control BuildContent()
    {
        int S(int v) => LogicalToDeviceUnits(v);
        var content = new SmoothPanel { Dock = DockStyle.Fill, Padding = new Padding(S(18), S(2), S(18), S(4)) };

        _split.Dock = DockStyle.Fill;
        _split.Orientation = Orientation.Horizontal;
        _split.FixedPanel = FixedPanel.Panel2;
        _split.SplitterWidth = S(12);
        _split.BackColor = Theme.Background;
        _split.Panel1.BackColor = Theme.Background;
        _split.Panel2.BackColor = Theme.Background;
        // Panel min sizes are applied in ApplySavedSplitter(), once the window has its real size:
        // setting them now (while the control is still 150×100) throws at 125%+ display scaling.
        _split.TabStop = false;
        _split.Paint += (_, e) =>
        {
            var r = _split.SplitterRectangle;
            int w = S(44);
            Theme.Smooth(e.Graphics);
            Theme.FillRound(e.Graphics, Theme.BorderStrong, new RectangleF(r.X + (r.Width - w) / 2f, r.Y + r.Height / 2f - S(2), w, S(4)), S(2));
        };

        var listCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(S(6)) };
        _list.Dock = DockStyle.Fill;
        listCard.Controls.Add(_list);
        _split.Panel1.Controls.Add(listCard);

        var logCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(S(6)) };
        _log.Dock = DockStyle.Fill;
        _logHeader.Dock = DockStyle.Top;
        _logHeader.Height = S(46);
        logCard.Controls.Add(_log);
        logCard.Controls.Add(_logHeader);
        _split.Panel2.Controls.Add(logCard);
        content.Controls.Add(_split);

        _list.EmptyText = "Scanning your services…";
        _list.ToggleLocal += svc => Guard(() => _ws.SetLocal(svc, !svc.IsLocal));
        _list.ToggleAll += local => Guard(() => _ws.SetLocal(FilteredServices(), local));
        _list.SelectionChanged += svc => ShowLogsFor(svc);
        _list.ActionClicked += OnRowAction;
        _list.MenuRequested += ShowRowMenu;

        _log.EmptyText = "Output from the selected service appears here.";
        _log.FollowChanged += () => _logHeader.Follow.Toggled = _log.Follow;
        _log.ClearRequested += ClearShownLog;
        _logHeader.Follow.Click += (_, _) => _log.Follow = !_log.Follow;
        _logHeader.ErrorsOnly.Click += (_, _) => { _log.ErrorsOnly = !_log.ErrorsOnly; _logHeader.ErrorsOnly.Toggled = _log.ErrorsOnly; };
        _logHeader.Copy.Click += (_, _) => { _log.CopySelection(); _status.SetNotice(_log.HasSelection ? "Copied selected lines." : "Copied the whole log.", false); };
        _logHeader.Clear.Click += (_, _) => ClearShownLog();
        _logHeader.Activity.Click += (_, _) => ShowActivity(!_showActivity);
        _tips.SetToolTip(_logHeader.Activity, "What the manager did: files changed, keys matched, warnings");
        _tips.SetToolTip(_logHeader.ErrorsOnly, "Only errors and warnings");
        _tips.SetToolTip(_logHeader.Follow, "Keep scrolling to the newest output");
        _tips.SetToolTip(_logHeader.Copy, "Copy selected lines (or everything)");
        _tips.SetToolTip(_logHeader.Clear, "Clear this output");
        return content;
    }

    // ================================================================== startup

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplySavedSplitter();
        _header.Subtitle = _ws.Settings.MicroservicesRootPath;
        _log.Buffer = _ws.ActivityLog;
        _toolbar.Enabled = false;
        try
        {
            await Task.Run(_ws.Initialize);
        }
        catch (Exception ex)
        {
            _status.SetNotice("Startup problem: " + ex.Message, true);
        }
        _toolbar.Enabled = true;
        _initialized = true;

        _database.Text = _ws.Settings.DatabaseName;
        PopulateEnvironments();
        WatchProfiles();
        RefreshUi(force: true);
        _uiTimer.Start();
        _metricsTimer.Start();
        _list.Focus();
    }

    private void PopulateEnvironments()
    {
        var names = _ws.Profiles.Select(p => p.Name).ToList();
        if (!names.SequenceEqual(_envSwitch.Items)) _envSwitch.Items = names;
        _envSwitch.SelectedIndex = names.FindIndex(n => string.Equals(n, _ws.CurrentProfile.Name, StringComparison.OrdinalIgnoreCase));
        LayoutToolbar();
    }

    /// <summary>environments.json is re-read as soon as you save it.</summary>
    private void WatchProfiles()
    {
        try
        {
            _profilesWatcher = new FileSystemWatcher(AppPaths.DataFolder, Path.GetFileName(AppPaths.EnvironmentsFile))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler changed = (_, _) => { _profilesChangedAt = DateTime.Now; _profilesChanged = true; };
            _profilesWatcher.Changed += changed;
            _profilesWatcher.Created += changed;
            _profilesWatcher.Renamed += (_, _) => { _profilesChangedAt = DateTime.Now; _profilesChanged = true; };
        }
        catch { /* optional */ }
    }

    // ================================================================== refresh loop

    private void RefreshUi(bool force)
    {
        if (_closing) return;

        if (_profilesChanged && (DateTime.Now - _profilesChangedAt).TotalMilliseconds > 400)
        {
            _profilesChanged = false;
            Guard(() =>
            {
                _ws.LoadProfiles();
                _ws.ApplyRouting();
                PopulateEnvironments();
                _status.SetNotice("environments.json reloaded and applied.", false);
            });
        }

        long version = _ws.Version;
        int signature = StateSignature();
        if (force || version != _seenVersion || signature != _seenSignature)
        {
            _seenVersion = version;
            _seenSignature = signature;
            if (_list.Selected != null && !_ws.Services.Contains(_list.Selected)) _list.Selected = null;
            ApplyFilter();
            UpdateToolbarState();
            UpdateLogHeader();
            UpdateTotals();
            if (_envSwitch.SelectedItem is { } shown && !string.Equals(shown, _ws.CurrentProfile.Name, StringComparison.OrdinalIgnoreCase))
                PopulateEnvironments();
        }
        else if (_ws.Services.Any(s => s.State is ServiceState.Building or ServiceState.Starting or ServiceState.Queued or ServiceState.Stopping))
        {
            _list.Invalidate(); // pulse animation
        }

        _log.Poll();
        while (_ws.TryDequeueNotice(out var notice)) _status.SetNotice(notice.Text, notice.IsError);
        _status.Tick();
    }

    private int StateSignature()
    {
        int h = 17;
        foreach (var s in _ws.Services)
            h = unchecked(h * 31 + (int)s.State * 4 + (s.IsLocal ? 1 : 0) + (s.InVisualStudio ? 2 : 0));
        return h;
    }

    private List<ServiceInfo> FilteredServices()
    {
        string q = _search.Text.Trim();
        if (q.Length == 0) return _ws.Services;
        return _ws.Services.Where(s =>
            s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            (s.EnvKey?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            s.LocalUrl.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void ApplyFilter()
    {
        _list.RemoteName = _ws.CurrentProfile.Name;
        _list.EmptyText = !_initialized ? "Scanning your services…"
            : _ws.Services.Count == 0
                ? $"No services found in {_ws.Settings.MicroservicesRootPath}\n\nEach service needs Properties\\launchSettings.json with a \"Project\" profile.\nCheck Settings, or press F5 to scan again."
                : "No services match your filter.";
        _list.SetItems(FilteredServices());
    }

    private void UpdateToolbarState()
    {
        var services = _ws.Services;
        int local = services.Count(s => s.IsLocal);
        int startable = services.Count(s => s.IsLocal && !s.IsActive && !s.InVisualStudio);
        int lastCount = services.Count(s => _ws.Settings.LastLocalServices.Contains(s.ProjectFilePath, StringComparer.OrdinalIgnoreCase));

        _startLocal.Text = startable > 0 ? $"Start {startable} local" : "Start local";
        _startLocal.Enabled = startable > 0;
        _stopAll.Enabled = local > 0 || services.Any(s => s.IsActive);

        bool showLast = local == 0 && lastCount > 0;
        _lastSession.Text = $"Last session ({lastCount})";
        if (_lastSession.Visible != showLast) _lastSession.Visible = showLast;
        _tips.SetToolTip(_lastSession, "Tick and start the services you had running last time");
        LayoutToolbar();
    }

    private void UpdateTotals()
    {
        var running = _ws.Services.Where(s => s.IsActive).ToList();
        double cpu = running.Sum(s => s.CpuPercent);
        long memory = running.Sum(s => s.MemoryBytes);
        string summary = running.Count == 0
            ? $"{_ws.Services.Count} services · nothing running · remote: {_ws.CurrentProfile.Name}"
            : $"{running.Count(s => s.State == ServiceState.Running)} running · {cpu:0}% CPU · {Theme.Bytes(memory)}";
        _status.SetTotals(summary, $"Manager {Theme.Bytes(_ws.ManagerMemoryBytes)}");
    }

    // ================================================================== logs panel

    private void ShowLogsFor(ServiceInfo? svc)
    {
        if (svc is null) { ShowActivity(true); return; }
        _showActivity = false;
        _log.Buffer = svc.Log;
        UpdateLogHeader();
    }

    private void ShowActivity(bool show)
    {
        _showActivity = show || _list.Selected is null;
        _log.Buffer = _showActivity ? _ws.ActivityLog : _list.Selected!.Log;
        UpdateLogHeader();
    }

    private void UpdateLogHeader()
    {
        _logHeader.Activity.Toggled = _showActivity;
        if (_showActivity || _list.Selected is not { } svc)
        {
            _logHeader.Set("Activity", "What the manager changed and why", Theme.Accent);
            _log.EmptyText = "Nothing yet.";
            return;
        }
        string sub = Theme.StateText(svc, _ws.CurrentProfile.Name);
        if (svc.IsActive || svc.InVisualStudio) sub += "  ·  " + svc.LocalUrl;
        if (svc.Log.ErrorCount > 0) sub += $"  ·  {svc.Log.ErrorCount} error line(s)";
        _logHeader.Set(svc.Name, sub, Theme.StateColor(svc));
        _log.EmptyText = svc.IsActive ? "Waiting for output…" : "Press ▶ to start this service here — its output appears in this panel.";
    }

    private void ClearShownLog()
    {
        _log.Buffer?.Clear();
        _log.Poll();
    }

    // ================================================================== actions

    private void StartLocal()
    {
        if (!_ws.Services.Any(s => s.IsLocal))
        {
            _status.SetNotice("Tick the services you want to run locally first (or press ▶ on a row).", false);
            return;
        }
        Guard(_ws.StartLocalServices);
    }

    private async void OnRowAction(ServiceInfo svc, RowAction action, Point location)
    {
        switch (action)
        {
            case RowAction.StartStop:
                if (svc.IsActive) await GuardAsync(() => _ws.StopAsync(svc));
                else { Guard(() => _ws.Start(svc)); ShowLogsFor(svc); }
                break;
            case RowAction.Restart:
                ShowLogsFor(svc);
                await GuardAsync(() => _ws.RestartAsync(svc));
                break;
            case RowAction.Browse:
                OpenShell(svc.BrowseUrl);
                break;
            case RowAction.More:
                ShowRowMenu(svc, location);
                break;
        }
    }

    private void ShowRowMenu(ServiceInfo svc, Point location)
    {
        _rowMenu.Items.Clear();
        ToolStripMenuItem Add(string text, Action onClick, bool enabled = true)
        {
            var item = new ToolStripMenuItem(text) { Enabled = enabled, ForeColor = Theme.Text, Font = Theme.Ui(9.5f), Padding = new Padding(2, 5, 2, 5) };
            item.Click += (_, _) => onClick();
            _rowMenu.Items.Add(item);
            return item;
        }

        Add(svc.IsActive ? "Stop" : "Start here", () => OnRowAction(svc, RowAction.StartStop, location));
        Add("Restart", () => OnRowAction(svc, RowAction.Restart, location),
            svc.State is ServiceState.Running or ServiceState.Starting or ServiceState.Crashed);
        Add("Open in browser", () => OpenShell(svc.BrowseUrl), svc.State == ServiceState.Running || svc.InVisualStudio);
        Add("Open in Visual Studio", async () => await GuardAsync(() => _ws.OpenInVisualStudioAsync(svc)));
        _rowMenu.Items.Add(new ToolStripSeparator());
        Add(svc.IsLocal ? $"Point Angular back at {_ws.CurrentProfile.Name}" : "Point Angular here without starting",
            () => Guard(() => _ws.SetLocal(svc, !svc.IsLocal)));
        Add("Set environment key…", () => PickEnvKey(svc));
        Add("Copy local URL", () => { try { Clipboard.SetText(svc.LocalUrl); } catch { } });
        Add("Open project folder", () => OpenShell(svc.ProjectDirectory));
        _rowMenu.Items.Add(new ToolStripSeparator());
        Add("Clear output", () => { svc.Log.Clear(); _log.Poll(); });

        _rowMenu.Show(_list, location);
    }

    private void PickEnvKey(ServiceInfo svc)
    {
        using var dialog = new KeyPickerForm(svc, _ws);
        if (dialog.ShowDialog(this) == DialogResult.OK) Guard(() => _ws.SetEnvKey(svc, dialog.SelectedKey));
    }

    private void CommitDatabase()
    {
        if (!_initialized) return;
        if (_ws.SetDatabase(_database.Text, out string? error))
        {
            _database.HasError = false;
            _database.Text = _ws.Settings.DatabaseName;
        }
        else
        {
            _database.HasError = true;
            _status.SetNotice("Database name: " + error, true);
        }
    }

    private void Rescan()
    {
        if (!_initialized) return;
        Guard(() =>
        {
            _ws.Rescan();
            _status.SetNotice($"Found {_ws.Services.Count} services.", false);
        });
    }

    private async void OpenSettings()
    {
        var settings = _ws.Settings;
        using var dialog = new SettingsForm(settings, firstRun: false);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        bool pathsChanged = !string.Equals(settings.EnvFilePath, dialog.EnvFilePath, StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(settings.MicroservicesRootPath, dialog.ServicesRoot, StringComparison.OrdinalIgnoreCase);
        if (pathsChanged)
        {
            int active = _ws.Services.Count(s => s.IsActive);
            if (active > 0 && MessageBox.Show(this, $"Changing folders stops the {active} running service(s). Continue?",
                    "Settings", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            // ReloadAsync puts back every file it changed (by their recorded paths) before scanning the new folder.
            settings.EnvFilePath = dialog.EnvFilePath;
            settings.MicroservicesRootPath = dialog.ServicesRoot;
            SettingsStore.Save(settings);
            _list.Selected = null;
            await GuardAsync(_ws.ReloadAsync);
            _header.Subtitle = settings.MicroservicesRootPath;
            PopulateEnvironments();
            _status.SetNotice($"Settings saved. Found {_ws.Services.Count} services.", false);
        }
        else
        {
            SettingsStore.Save(settings);
            _status.SetNotice("Settings saved. Run settings apply the next time a service starts.", false);
        }
        RefreshUi(force: true);
    }

    private void OpenShell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { _status.SetNotice($"Couldn't open {target}: {ex.Message}", true); }
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { _status.SetNotice(ex.Message, true); }
        RefreshUi(force: true);
    }

    private async Task GuardAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _status.SetNotice(ex.Message, true); }
        RefreshUi(force: true);
    }

    public void ShowError(Exception ex) => _status.SetNotice("Unexpected error: " + ex.Message, true);

    // ================================================================== keyboard

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.F:
                _search.SelectAllText();
                return true;
            case Keys.F5:
                Rescan();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ================================================================== window placement & closing

    private void RestorePlacement()
    {
        var p = _ws.Settings.Window;
        if (p is null || p.Width < 400 || p.Height < 300) return;
        var bounds = new Rectangle(p.X, p.Y, p.Width, p.Height);
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds))) return;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        if (p.Maximized) WindowState = FormWindowState.Maximized;
    }

    private void ApplySavedSplitter()
    {
        int S(int v) => LogicalToDeviceUnits(v);
        try
        {
            int available = _split.Height - _split.SplitterWidth;
            int min1 = Math.Min(S(170), available / 2), min2 = Math.Min(S(130), available / 2);
            int logHeight = _ws.Settings.Window?.LogPanelHeight ?? 0;
            if (logHeight <= 0) logHeight = (int)(_split.Height * 0.38);
            logHeight = Math.Clamp(logHeight, min2, Math.Max(min2, available - min1));

            _split.SplitterDistance = Math.Max(min1, available - logHeight);
            _split.Panel1MinSize = min1;
            _split.Panel2MinSize = min2;
        }
        catch (InvalidOperationException) { /* window too small for the preferred sizes; WinForms defaults are fine */ }
    }

    private void SavePlacement()
    {
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _ws.Settings.Window = new WindowPlacement
        {
            X = b.X, Y = b.Y, Width = b.Width, Height = b.Height,
            Maximized = WindowState == FormWindowState.Maximized,
            LogPanelHeight = _split.Height - _split.SplitterDistance - _split.SplitterWidth,
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closing && _initialized)
        {
            int active = _ws.Services.Count(s => s.IsActive);
            if (active > 0 && e.CloseReason == CloseReason.UserClosing)
            {
                var answer = MessageBox.Show(this,
                    $"{active} service(s) are still running.\n\nStop them and put environment.ts and appsettings back to your repo's values?",
                    "Close Microservice Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        _closing = true;
        _uiTimer.Stop();
        _metricsTimer.Stop();
        _profilesWatcher?.Dispose();
        SavePlacement();
        _ws.Shutdown();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uiTimer.Dispose();
            _metricsTimer.Dispose();
            _rowMenu.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
