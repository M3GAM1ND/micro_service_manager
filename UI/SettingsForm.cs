using System.Diagnostics;
using MicroserviceManager.Core;
using MicroserviceManager.UI.Controls;

namespace MicroserviceManager.UI;

/// <summary>Settings dialog; also the welcome screen the first time the app runs.</summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly bool _firstRun;
    private readonly TextField _env = new() { Placeholder = @"…\src\environments\environment.ts" };
    private readonly TextField _root = new() { Placeholder = "Folder that contains your microservices" };
    private readonly FlatButton _browseEnv = new() { Text = "Browse", Icon = Theme.IconFolder };
    private readonly FlatButton _browseRoot = new() { Text = "Browse", Icon = Theme.IconFolder };
    private readonly ChoiceCard _lite = new()
    {
        Title = "Fast & light  (recommended)",
        Description = "Builds, then runs the compiled app directly. Same behavior, about 4× less memory.",
    };
    private readonly ChoiceCard _dotnetRun = new()
    {
        Title = "dotnet run",
        Description = "Exactly what a terminal does. Use it if a service acts differently in fast mode.",
    };
    private readonly ChoiceCard _lowMemory = new()
    {
        IsRadio = false,
        Title = "Low-memory mode",
        Description = "Runs services with Workstation GC instead of Server GC: much less RAM, no difference for local work.",
    };
    private readonly FlatButton _editProfiles = new() { Text = "Edit environments", Icon = Theme.IconEdit };
    private readonly FlatButton _openData = new() { Kind = ButtonKind.Ghost, Text = "Open data folder", Icon = Theme.IconFolder };
    private readonly FlatButton _save = new() { Kind = ButtonKind.Primary, Text = "Save" };
    private readonly FlatButton _cancel = new() { Text = "Cancel" };
    private readonly Label _error = new() { AutoSize = false, ForeColor = Theme.Danger, BackColor = Color.Transparent };

    public SettingsForm(AppSettings settings, bool firstRun)
    {
        _settings = settings;
        _firstRun = firstRun;

        Theme.StyleWindow(this);
        AutoScaleMode = AutoScaleMode.None; // sizes are already DPI-scaled in code
        Text = firstRun ? "Welcome — Microservice Manager" : "Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = firstRun;
        StartPosition = firstRun ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
        ClientSize = new Size(S(660), S(600)); // height is fitted to the content in BuildLayout()
        AcceptButton = null;

        _env.Text = settings.EnvFilePath;
        _root.Text = settings.MicroservicesRootPath;
        _lite.Checked = settings.RunMode == RunMode.Lite;
        _dotnetRun.Checked = settings.RunMode == RunMode.DotnetRun;
        _lowMemory.Checked = settings.LowMemoryMode;
        _lite.CheckedChanged += (_, _) => { if (_lite.Checked) _dotnetRun.Checked = false; };
        _dotnetRun.CheckedChanged += (_, _) => { if (_dotnetRun.Checked) _lite.Checked = false; };

        _browseEnv.Click += (_, _) => BrowseEnv();
        _browseRoot.Click += (_, _) => BrowseRoot();
        _editProfiles.Click += (_, _) => EditProfiles();
        _openData.Click += (_, _) => Open(AppPaths.DataFolder);
        _save.Click += (_, _) => Save();
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        if (firstRun) _cancel.Text = "Exit";

        BuildLayout();
    }

    public string EnvFilePath => _env.Text.Trim().Trim('"');
    public string ServicesRoot => _root.Text.Trim().Trim('"');

    private int S(int v) => LogicalToDeviceUnits(v);

    private void BuildLayout()
    {
        int left = S(32), width = ClientSize.Width - left * 2;
        int y = S(24);

        Label Add(string text, Font font, Color color, int height)
        {
            var label = new Label { Text = text, Font = font, ForeColor = color, BackColor = Color.Transparent, AutoSize = false, Location = new Point(left, y), Size = new Size(width, height) };
            Controls.Add(label);
            y += height;
            return label;
        }

        Add(_firstRun ? "Welcome" : "Settings", Theme.Display(16f), Theme.Text, S(34));
        Add(_firstRun
                ? "Pick your Angular environment file and your microservices folder. You can change these any time."
                : "Paths, and how services run on your machine.",
            Theme.Ui(9.5f), Theme.TextMuted, S(22));
        y += S(14);

        void PathRow(string title, string hint, TextField field, FlatButton browse)
        {
            Add(title, Theme.UiSemibold(8f), Theme.TextFaint, S(20));
            browse.Width = browse.PreferredWidth();
            field.SetBounds(left, y, width - browse.Width - S(8), S(36));
            browse.SetBounds(left + width - browse.Width, y, browse.Width, S(36));
            Controls.Add(field);
            Controls.Add(browse);
            y += S(42);
            Add(hint, Theme.Ui(8.75f), Theme.TextFaint, S(20));
            y += S(10);
        }

        PathRow("ANGULAR ENVIRONMENT FILE", "Usually src\\environments\\environment.ts. Only the URLs that need to change are edited.", _env, _browseEnv);
        PathRow("MICROSERVICES FOLDER", "Every project here with Properties\\launchSettings.json shows up as a service.", _root, _browseRoot);

        Add("RUN SERVICES WITH", Theme.UiSemibold(8f), Theme.TextFaint, S(22));
        int half = (width - S(10)) / 2;
        _lite.SetBounds(left, y, half, S(88));
        _dotnetRun.SetBounds(left + half + S(10), y, width - half - S(10), S(88));
        Controls.Add(_lite);
        Controls.Add(_dotnetRun);
        y += S(98);
        _lowMemory.SetBounds(left, y, width, S(72));
        Controls.Add(_lowMemory);
        y += S(86);

        Add("ENVIRONMENTS", Theme.UiSemibold(8f), Theme.TextFaint, S(22));
        Add("QA, UAT1 and UAT3 live in environments.json on your PC (not in Git). Saved changes apply right away.",
            Theme.Ui(8.75f), Theme.TextMuted, S(20));
        y += S(6);
        _editProfiles.Width = _editProfiles.PreferredWidth();
        _openData.Width = _openData.PreferredWidth();
        _editProfiles.Location = new Point(left, y);
        _openData.Location = new Point(left + _editProfiles.Width + S(8), y);
        Controls.Add(_editProfiles);
        Controls.Add(_openData);
        y += _editProfiles.Height + S(40);

        _save.Width = Math.Max(S(96), _save.PreferredWidth());
        _cancel.Width = Math.Max(S(96), _cancel.PreferredWidth());
        _save.Location = new Point(left + width - _save.Width, y);
        _cancel.Location = new Point(_save.Left - S(8) - _cancel.Width, y);
        _error.SetBounds(left, y, _cancel.Left - left - S(12), _save.Height);
        _error.TextAlign = ContentAlignment.MiddleLeft;
        _error.Font = Theme.Ui(9f);
        Controls.Add(_error);
        Controls.Add(_cancel);
        Controls.Add(_save);
        y += _save.Height + S(22);

        // Fit the screen: on small or highly scaled displays the dialog scrolls instead of running off-screen.
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        int chrome = Height - ClientSize.Height;
        int height = Math.Min(y, area.Height - chrome - S(8));
        if (height < y) AutoScroll = true;
        ClientSize = new Size(ClientSize.Width + (AutoScroll ? SystemInformation.VerticalScrollBarWidth : 0), height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        int y = _save.Top - S(20);
        e.Graphics.DrawLine(pen, 0, y, ClientSize.Width, y);
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    private void BrowseEnv()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Pick your Angular environment file",
            Filter = "Angular environment (*.ts)|*.ts|All files (*.*)|*.*",
        };
        string current = EnvFilePath;
        if (File.Exists(current)) { dialog.InitialDirectory = Path.GetDirectoryName(current); dialog.FileName = Path.GetFileName(current); }
        if (dialog.ShowDialog(this) == DialogResult.OK) _env.Text = dialog.FileName;
    }

    private void BrowseRoot()
    {
        using var dialog = new FolderBrowserDialog { Description = "Folder that contains your microservices", UseDescriptionForTitle = true };
        if (Directory.Exists(ServicesRoot)) dialog.InitialDirectory = ServicesRoot;
        if (dialog.ShowDialog(this) == DialogResult.OK) _root.Text = dialog.SelectedPath;
    }

    private void EditProfiles()
    {
        EnvironmentProfiles.Load(out _); // creates the file with defaults the first time
        try { Process.Start(new ProcessStartInfo(AppPaths.EnvironmentsFile) { UseShellExecute = true })?.Dispose(); }
        catch
        {
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppPaths.EnvironmentsFile}\"") { UseShellExecute = true })?.Dispose(); }
            catch (Exception ex) { _error.Text = ex.Message; }
        }
    }

    private void Open(string folder)
    {
        try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { _error.Text = ex.Message; }
    }

    private void Save()
    {
        _env.HasError = _root.HasError = false;
        if (!File.Exists(EnvFilePath))
        {
            _env.HasError = true;
            _error.Text = "Pick your environment.ts file.";
            return;
        }
        if (!Directory.Exists(ServicesRoot))
        {
            _root.HasError = true;
            _error.Text = "Pick the folder that contains your microservices.";
            return;
        }

        _settings.RunMode = _dotnetRun.Checked ? RunMode.DotnetRun : RunMode.Lite;
        _settings.LowMemoryMode = _lowMemory.Checked;
        if (_firstRun)
        {
            _settings.EnvFilePath = EnvFilePath;
            _settings.MicroservicesRootPath = ServicesRoot;
            SettingsStore.Save(_settings);
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>Pick which environment.ts entry points at a service when it runs locally.</summary>
internal sealed class KeyPickerForm : Form
{
    private readonly TextField _filter = new() { Icon = Theme.IconSearch, Placeholder = "Filter keys" };
    private readonly ListBox _keys = new();
    private readonly FlatButton _use = new() { Kind = ButtonKind.Primary, Text = "Use this key" };
    private readonly FlatButton _cancel = new() { Text = "Cancel" };
    private readonly List<(string? Key, string Value)> _all = new();
    private readonly string? _current;

    public KeyPickerForm(ServiceInfo svc, Workspace ws)
    {
        Theme.StyleWindow(this);
        AutoScaleMode = AutoScaleMode.None; // sizes are already DPI-scaled in code
        Text = "Environment key";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(S(620), S(560));

        _current = svc.EnvKeyIsManual ? svc.EnvKey : null;
        string? detected = EnvKeyMatcher.Match(svc.Name, ws.EnvEntries, ws.Profiles);
        _all.Add((null, detected is null ? "Auto-detect — nothing matched yet" : $"Auto-detect — currently {detected}"));
        foreach (var entry in ws.EnvEntries.GroupBy(e => e.Key).Select(g => g.First()).OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            _all.Add((entry.Key, entry.RawValue));

        int left = S(28), width = ClientSize.Width - left * 2, y = S(24);
        Controls.Add(new Label { Text = $"Environment key for {svc.Name}", Font = Theme.Display(14f), ForeColor = Theme.Text, BackColor = Color.Transparent, Location = new Point(left, y), Size = new Size(width, S(32)) });
        y += S(34);
        Controls.Add(new Label { Text = "Which value in environment.ts should point at localhost while this service runs on your machine?", Font = Theme.Ui(9.25f), ForeColor = Theme.TextMuted, BackColor = Color.Transparent, Location = new Point(left, y), Size = new Size(width, S(40)) });
        y += S(46);
        _filter.SetBounds(left, y, width, S(36));
        Controls.Add(_filter);
        y += S(46);

        var frame = new Card { Location = new Point(left, y), Size = new Size(width, ClientSize.Height - y - S(84)), Padding = new Padding(S(6)) };
        _keys.Dock = DockStyle.Fill;
        _keys.BorderStyle = BorderStyle.None;
        _keys.BackColor = Theme.Surface;
        _keys.ForeColor = Theme.Text;
        _keys.DrawMode = DrawMode.OwnerDrawFixed;
        _keys.ItemHeight = S(44);
        _keys.IntegralHeight = false;
        _keys.DrawItem += DrawKey;
        _keys.DoubleClick += (_, _) => Accept();
        Theme.DarkScrollbars(_keys);
        frame.Controls.Add(_keys);
        Controls.Add(frame);

        int bottom = ClientSize.Height - S(24);
        _use.Width = _use.PreferredWidth();
        _cancel.Width = Math.Max(S(96), _cancel.PreferredWidth());
        _use.Location = new Point(left + width - _use.Width, bottom - _use.Height);
        _cancel.Location = new Point(_use.Left - S(8) - _cancel.Width, bottom - _cancel.Height);
        Controls.Add(_cancel);
        Controls.Add(_use);

        _use.Click += (_, _) => Accept();
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _filter.TextChanged += (_, _) => Fill();
        _filter.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _keys.Items.Count > 0) { _keys.Focus(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Enter) { Accept(); e.SuppressKeyPress = true; }
        };
        Fill();
    }

    /// <summary>The chosen key, or null for auto-detect.</summary>
    public string? SelectedKey { get; private set; }

    private int S(int v) => LogicalToDeviceUnits(v);

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _filter.Inner.Focus();
    }

    private void Fill()
    {
        string q = _filter.Text.Trim();
        _keys.BeginUpdate();
        _keys.Items.Clear();
        foreach (var item in _all)
        {
            if (q.Length == 0 || item.Key is null
                || item.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
                || item.Value.Contains(q, StringComparison.OrdinalIgnoreCase))
                _keys.Items.Add(item);
        }
        _keys.EndUpdate();
        int index = _keys.Items.Cast<(string? Key, string Value)>().ToList().FindIndex(i => i.Key == _current);
        _keys.SelectedIndex = q.Length > 0 && _keys.Items.Count > 1 ? 1 : Math.Max(0, index);
    }

    private void DrawKey(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var (key, value) = ((string? Key, string Value))_keys.Items[e.Index];
        var g = e.Graphics;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(Theme.Surface)) g.FillRectangle(bg, e.Bounds);
        Theme.Smooth(g);
        if (selected) Theme.FillRound(g, Theme.Selected, new RectangleF(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 4, e.Bounds.Height - 4), S(8));

        var top = new Rectangle(e.Bounds.X + S(14), e.Bounds.Y + S(5), e.Bounds.Width - S(28), S(20));
        var bottom = new Rectangle(e.Bounds.X + S(14), e.Bounds.Y + S(23), e.Bounds.Width - S(28), S(18));
        if (key is null)
        {
            Theme.DrawText(g, "Auto-detect", Theme.UiSemibold(10f), Theme.AccentHover, top);
            Theme.DrawText(g, value.Replace("Auto-detect — ", ""), Theme.Ui(8.75f), Theme.TextMuted, bottom);
        }
        else
        {
            Theme.DrawText(g, key, Theme.Mono(9.5f), Theme.Text, top);
            Theme.DrawText(g, value, Theme.Mono(8.5f), Theme.TextFaint, bottom);
        }
    }

    private void Accept()
    {
        if (_keys.SelectedItem is not ValueTuple<string?, string> item) return;
        SelectedKey = item.Item1;
        DialogResult = DialogResult.OK;
        Close();
    }
}
