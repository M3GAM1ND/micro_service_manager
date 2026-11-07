using System.Collections.Concurrent;
using System.Diagnostics;

namespace MicroserviceManager.Core;

/// <summary>
/// Everything the window does goes through here: which services run locally, which remote environment
/// the rest point at, the database name, and starting/stopping. Every file edit is recorded in the
/// <see cref="SessionJournal"/> first, so it can always be undone.
/// </summary>
public sealed class Workspace : IDisposable
{
    private readonly Dictionary<ServiceInfo, ServiceRunner> _runners = new();
    private readonly ConcurrentQueue<Notice> _notices = new();
    private readonly MetricsSampler _sampler = new();
    private readonly object _fileLock = new();
    private SessionJournal _journal = new();
    private List<TsEntry> _envEntries = new();
    private readonly HashSet<string> _warnedKeys = new(StringComparer.OrdinalIgnoreCase);
    private long _version;

    public Workspace(AppSettings settings) => Settings = settings;

    public AppSettings Settings { get; }
    public List<ServiceInfo> Services { get; private set; } = new();
    public List<EnvironmentProfile> Profiles { get; private set; } = new();

    /// <summary>What the manager itself did: files changed, keys matched, warnings.</summary>
    public LogBuffer ActivityLog { get; } = new(2000);

    public IReadOnlyList<TsEntry> EnvEntries => _envEntries;

    public long Version => Interlocked.Read(ref _version);
    public double ManagerCpuPercent => _sampler.SelfCpuPercent;
    public long ManagerMemoryBytes => _sampler.SelfMemoryBytes;

    public EnvironmentProfile CurrentProfile =>
        Profiles.FirstOrDefault(p => string.Equals(p.Name, Settings.RemoteEnvironment, StringComparison.OrdinalIgnoreCase))
        ?? Profiles.FirstOrDefault()
        ?? new EnvironmentProfile { Name = "QA" };

    public bool TryDequeueNotice(out Notice notice) => _notices.TryDequeue(out notice);

    // ================================================================== startup

    public void Initialize()
    {
        RecoverPreviousSession();
        LoadProfiles();
        Rescan();
    }

    /// <summary>If the app was killed last time, put back everything it had changed.</summary>
    private void RecoverPreviousSession()
    {
        var leftover = SessionJournal.Load();
        _journal = leftover;
        if (leftover.IsEmpty) return;

        int restored = 0;
        foreach (string path in leftover.Files.Keys.ToList())
        {
            if (RevertFile(path)) restored++;
        }
        _journal.Save();
        Notify($"Restored {restored} file(s) that a previous session didn't get to put back.");
    }

    public void LoadProfiles()
    {
        Profiles = EnvironmentProfiles.Load(out string? error);
        if (error != null) Notify(error, isError: true);
        if (!Profiles.Any(p => string.Equals(p.Name, Settings.RemoteEnvironment, StringComparison.OrdinalIgnoreCase)))
            Settings.RemoteEnvironment = Profiles.FirstOrDefault()?.Name ?? "QA";
        Touch();
    }

    public void Rescan()
    {
        var running = Services.Where(s => s.IsActive || s.IsLocal).ToDictionary(s => s.ProjectFilePath, StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        var found = ServiceScanner.Scan(Settings.MicroservicesRootPath, notes);

        // Keep live state for services that are running / local.
        for (int i = 0; i < found.Count; i++)
        {
            if (running.TryGetValue(found[i].ProjectFilePath, out var existing)) found[i] = existing;
        }
        foreach (var gone in Services.Except(found).ToList())
        {
            if (_runners.TryGetValue(gone, out var r)) { r.KillNow(); _runners.Remove(gone); }
        }

        Services = found;
        foreach (string note in notes) ActivityLog.AppendSystem("⚠ " + note);
        ReadEnvKeys();
        MatchEnvKeys();
        ApplyRouting();
        ActivityLog.AppendSystem($"Found {Services.Count} services in {Settings.MicroservicesRootPath}");
        Touch();
    }

    private void ReadEnvKeys()
    {
        try
        {
            _envEntries = File.Exists(Settings.EnvFilePath)
                ? TsEnvironmentFile.Parse(TextFile.Read(Settings.EnvFilePath).Text)
                : new List<TsEntry>();
        }
        catch (Exception ex)
        {
            _envEntries = new List<TsEntry>();
            Notify($"Couldn't read environment.ts: {ex.Message}", isError: true);
        }
    }

    private void MatchEnvKeys()
    {
        var keys = new HashSet<string>(_envEntries.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var svc in Services)
        {
            if (Settings.EnvKeyOverrides.TryGetValue(svc.ProjectFilePath, out var manual))
            {
                svc.EnvKey = manual;
                svc.EnvKeyIsManual = true;
            }
            else
            {
                svc.EnvKey = EnvKeyMatcher.Match(svc.Name, _envEntries, Profiles);
                svc.EnvKeyIsManual = false;
            }
            svc.EnvKeyFound = svc.EnvKey != null && keys.Contains(svc.EnvKey);
        }
    }

    // ================================================================== routing

    public void SetEnvironment(string name)
    {
        if (string.Equals(Settings.RemoteEnvironment, name, StringComparison.OrdinalIgnoreCase)) return;
        Settings.RemoteEnvironment = name;
        SettingsStore.Save(Settings);
        LoadProfiles(); // pick up edits to environments.json
        ApplyRouting();
        Notify($"Remote services now point at {name}.");
    }

    public void SetLocal(ServiceInfo svc, bool local) => SetLocal(new[] { svc }, local);

    public void SetLocal(IEnumerable<ServiceInfo> services, bool local)
    {
        var changed = services.Where(s => s.IsLocal != local).ToList();
        if (changed.Count == 0) return;

        foreach (var svc in changed)
        {
            svc.IsLocal = local;
            if (!local)
            {
                svc.InVisualStudio = false;
                if (svc.IsActive) _ = RunnerFor(svc).StopAsync();
            }
        }
        ApplyRouting();
        foreach (var svc in changed) ApplyDatabase(svc);
        RememberLocalServices();

        if (changed.Count == 1)
        {
            var svc = changed[0];
            string where = local ? svc.LocalUrl : CurrentProfile.Name;
            Notify(svc.EnvKeyFound ? $"{svc.Name}: Angular now calls {where} ({svc.EnvKey})." : $"{svc.Name} is {(local ? "local" : "remote")}.");
        }
        else Notify(local ? $"{changed.Count} services now run locally." : $"{changed.Count} services point at {CurrentProfile.Name} again.");
        Touch();
    }

    public void SetEnvKey(ServiceInfo svc, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) Settings.EnvKeyOverrides.Remove(svc.ProjectFilePath);
        else Settings.EnvKeyOverrides[svc.ProjectFilePath] = key.Trim();
        SettingsStore.Save(Settings);
        MatchEnvKeys();
        ApplyRouting();
        Notify(svc.EnvKey is null ? $"{svc.Name}: no environment.ts key." : $"{svc.Name} → {svc.EnvKey}");
    }

    public bool SetDatabase(string name, out string? error)
    {
        name = name.Trim();
        error = null;
        if (name.Length > 0 && !AppSettingsFile.IsValidDatabaseName(name))
        {
            error = "Use letters, numbers, '_', '-' or '.' only.";
            return false;
        }
        if (name == Settings.DatabaseName) return true;

        Settings.DatabaseName = name;
        SettingsStore.Save(Settings);
        var local = Services.Where(s => s.IsLocal).ToList();
        foreach (var svc in local) ApplyDatabase(svc);

        int running = local.Count(s => s.State == ServiceState.Running);
        Notify(name.Length == 0
            ? "Database name cleared — appsettings files are left as they are in your repo."
            : running > 0 ? $"Database set to {name}. Restart running services to use it." : $"Database set to {name}.");
        Touch();
        return true;
    }

    /// <summary>
    /// Points environment.ts at localhost for local services and at the chosen remote environment for the rest.
    /// Works on your current file every time, so changes your team makes to it are never lost.
    /// </summary>
    public void ApplyRouting()
    {
        string path = Settings.EnvFilePath;
        if (!File.Exists(path))
        {
            Notify("environment.ts not found — open Settings to pick it.", isError: true);
            return;
        }

        var profile = CurrentProfile;
        var localUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var svc in Services.Where(s => s.IsLocal && s.EnvKey != null))
            localUrls.TryAdd(svc.EnvKey!, svc.LocalUrl);

        try
        {
            lock (_fileLock)
            {
                var (content, encoding) = TextFile.Read(path);
                var changes = _journal.For(path);
                string updated = TsEnvironmentFile.Apply(content, (entry, original) =>
                {
                    if (localUrls.TryGetValue(entry.Key, out var url)) return TsEnvironmentFile.Escape(url, entry.Quote);
                    return profile.Resolve(entry.Key, original, entry.Quote);
                }, changes);

                _journal.Save(); // journal first: a crash between these two lines is harmless
                if (updated != content) TextFile.Write(path, updated, encoding);
                _envEntries = TsEnvironmentFile.Parse(updated);
            }
        }
        catch (Exception ex)
        {
            Notify($"Couldn't update environment.ts: {ex.Message}", isError: true);
            return;
        }

        foreach (var svc in Services.Where(s => s.IsLocal && !s.EnvKeyFound))
        {
            if (!_warnedKeys.Add($"{svc.ProjectFilePath}|{svc.EnvKey}")) continue; // warn once
            Notify(svc.EnvKey is null
                ? $"{svc.Name}: no matching key in environment.ts, so Angular still calls the remote one. Right-click → Set environment key."
                : $"{svc.Name}: key \"{svc.EnvKey}\" isn't in environment.ts. Right-click → Set environment key.", isError: true);
        }

        string locals = string.Join(", ", Services.Where(s => s.IsLocal && s.EnvKeyFound).Select(s => s.EnvKey));
        ActivityLog.AppendSystem($"environment.ts → {profile.Name}" + (locals.Length > 0 ? $"; local: {locals}" : ""));
    }

    private void ApplyDatabase(ServiceInfo svc)
    {
        string? database = svc.IsLocal && Settings.DatabaseName.Length > 0 ? Settings.DatabaseName : null;
        foreach (string file in AppSettingsFiles(svc))
        {
            try
            {
                lock (_fileLock)
                {
                    var (content, encoding) = TextFile.Read(file);
                    var changes = _journal.For(file);
                    string updated = AppSettingsFile.Apply(content, database, changes);
                    _journal.Save();
                    if (updated != content)
                    {
                        TextFile.Write(file, updated, encoding);
                        ActivityLog.AppendSystem(database is null
                            ? $"{svc.Name}: restored {Path.GetFileName(file)}"
                            : $"{svc.Name}: {Path.GetFileName(file)} → database {database}");
                    }
                }
            }
            catch (Exception ex)
            {
                Notify($"{svc.Name}: couldn't update {Path.GetFileName(file)} ({ex.Message}).", isError: true);
            }
        }
    }

    private static IEnumerable<string> AppSettingsFiles(ServiceInfo svc)
    {
        var names = new List<string> { "appsettings.json", "appsettings.Development.json" };
        if (svc.Profile.EnvironmentVariables.TryGetValue("ASPNETCORE_ENVIRONMENT", out var env) && env.Length > 0)
            names.Add($"appsettings.{env}.json");
        return names.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => Path.Combine(svc.ProjectDirectory, n))
            .Where(File.Exists);
    }

    private bool RevertFile(string path)
    {
        try
        {
            lock (_fileLock)
            {
                if (!File.Exists(path)) { _journal.Files.Remove(path); return false; }
                var (content, encoding) = TextFile.Read(path);
                var changes = _journal.For(path);
                string updated = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? AppSettingsFile.Apply(content, null, changes)
                    : TsEnvironmentFile.Apply(content, (_, _) => null, changes);
                if (updated != content) TextFile.Write(path, updated, encoding);
                _journal.Files.Remove(path);
                return updated != content;
            }
        }
        catch (Exception ex)
        {
            Notify($"Couldn't restore {Path.GetFileName(path)}: {ex.Message}", isError: true);
            return false;
        }
    }

    // ================================================================== running

    private ServiceRunner RunnerFor(ServiceInfo svc)
    {
        if (!_runners.TryGetValue(svc, out var runner)) _runners[svc] = runner = new ServiceRunner(svc);
        return runner;
    }

    private RunOptions Options => new(Settings.RunMode, Settings.LowMemoryMode);

    public void Start(ServiceInfo svc)
    {
        if (svc.IsActive) return;
        svc.InVisualStudio = false;
        if (!svc.IsLocal) SetLocal(svc, true);
        RunnerFor(svc).Start(Options);
        Touch();
    }

    public void StartLocalServices()
    {
        foreach (var svc in Services.Where(s => s.IsLocal && !s.IsActive && !s.InVisualStudio)) Start(svc);
    }

    public void StartLastSession()
    {
        var last = new HashSet<string>(Settings.LastLocalServices, StringComparer.OrdinalIgnoreCase);
        var services = Services.Where(s => last.Contains(s.ProjectFilePath)).ToList();
        SetLocal(services, true);
        foreach (var svc in services) Start(svc);
    }

    /// <summary>Stops the service and points Angular back at the remote one.</summary>
    public async Task StopAsync(ServiceInfo svc)
    {
        await RunnerFor(svc).StopAsync();
        SetLocal(svc, false);
        Touch();
    }

    public async Task RestartAsync(ServiceInfo svc)
    {
        await RunnerFor(svc).StopAsync();
        RunnerFor(svc).Start(Options);
        Touch();
    }

    public async Task StopAllAsync()
    {
        var active = Services.Where(s => s.IsActive).ToList();
        await Task.WhenAll(active.Select(s => RunnerFor(s).StopAsync()));
        SetLocal(Services, false);
        Notify("All services stopped. environment.ts and appsettings files are back to your repo's values.");
    }

    public async Task OpenInVisualStudioAsync(ServiceInfo svc)
    {
        if (svc.IsActive) await RunnerFor(svc).StopAsync();
        svc.InVisualStudio = true;
        if (!svc.IsLocal) SetLocal(svc, true);
        else ApplyDatabase(svc);

        string target = FindSolution(svc) ?? svc.ProjectFilePath;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            Notify($"Opened {Path.GetFileName(target)}. environment.ts and the database already point at your local {svc.Name}.");
        }
        catch (Exception ex)
        {
            Notify($"Couldn't open Visual Studio: {ex.Message}", isError: true);
        }
        Touch();
    }

    /// <summary>The nearest .sln / .slnx above the project (inside the services folder), if there's exactly one.</summary>
    private string? FindSolution(ServiceInfo svc)
    {
        string root = Path.GetFullPath(Settings.MicroservicesRootPath).TrimEnd(Path.DirectorySeparatorChar);
        string? dir = svc.ProjectDirectory;
        for (int depth = 0; dir != null && depth < 4; depth++)
        {
            try
            {
                var solutions = Directory.GetFiles(dir, "*.sln").Concat(Directory.GetFiles(dir, "*.slnx")).ToList();
                if (solutions.Count == 1) return solutions[0];
                if (solutions.Count > 1) return null;
            }
            catch { return null; }
            if (string.Equals(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase)) break;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private void RememberLocalServices()
    {
        var local = Services.Where(s => s.IsLocal).Select(s => s.ProjectFilePath).ToList();
        if (local.Count == 0) return; // keep the last non-empty set for "Start last session"
        Settings.LastLocalServices = local;
        SettingsStore.Save(Settings);
    }

    // ================================================================== ticks

    /// <summary>Called every ~2 s by the UI: CPU/RAM numbers and "is it listening yet" checks.</summary>
    public void SampleMetrics()
    {
        var running = new List<(ServiceInfo, int)>();
        foreach (var (svc, runner) in _runners)
        {
            if (runner.RootPid is int pid) running.Add((svc, pid));
            else { svc.CpuPercent = 0; svc.MemoryBytes = 0; }
            runner.MarkRunningIfListening();
        }
        _sampler.Sample(running);
        Touch();
    }

    /// <summary>After the paths change in Settings: stop everything, put every file back, scan the new folder.</summary>
    public async Task ReloadAsync()
    {
        await Task.WhenAll(Services.Where(s => s.IsActive).Select(s => RunnerFor(s).StopAsync()));
        foreach (var svc in Services) { svc.IsLocal = false; svc.InVisualStudio = false; }
        foreach (string path in _journal.Files.Keys.ToList()) RevertFile(path);
        _journal.Save();
        _runners.Clear();
        _warnedKeys.Clear();
        Services = new List<ServiceInfo>();
        LoadProfiles();
        Rescan();
    }

    // ================================================================== shutdown

    /// <summary>Stops everything and puts every file back. Safe to call more than once.</summary>
    public void Shutdown()
    {
        foreach (var runner in _runners.Values) runner.KillNow();
        foreach (string path in _journal.Files.Keys.ToList()) RevertFile(path);
        _journal.Save();
        RememberLocalServices();
        SettingsStore.Save(Settings);
    }

    public void Dispose() => _sampler.Dispose();

    private void Notify(string text, bool isError = false)
    {
        _notices.Enqueue(new Notice(text, isError));
        ActivityLog.AppendSystem((isError ? "⚠ " : "") + text);
        Touch();
    }

    private void Touch() => Interlocked.Increment(ref _version);
}
