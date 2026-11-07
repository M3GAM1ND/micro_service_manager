using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MicroserviceManager.Core;

/// <summary>
/// Runs one service inside the manager (no console window) and streams its output into <see cref="ServiceInfo.Log"/>.
/// <para>Lite mode builds with <c>dotnet build</c> (one build at a time, so shared projects never hit file locks)
/// and then starts the compiled app directly with the launch profile's URL and environment variables.
/// That skips the <c>dotnet run</c> host process that otherwise stays in memory next to every service.</para>
/// </summary>
public readonly record struct RunOptions(RunMode Mode, bool LowMemory);

public sealed partial class ServiceRunner
{
    private static readonly SemaphoreSlim BuildGate = new(1, 1);

    private readonly ServiceInfo _svc;
    private readonly object _gate = new();
    private Process? _process;
    private CancellationTokenSource? _cts;
    private Task _runTask = Task.CompletedTask;
    private volatile bool _stopRequested;

    public ServiceRunner(ServiceInfo service) => _svc = service;

    public int? RootPid
    {
        get
        {
            var p = _process;
            try { return p is { HasExited: false } ? p.Id : null; }
            catch { return null; }
        }
    }

    public void Start(RunOptions options)
    {
        lock (_gate)
        {
            if (_svc.IsActive) return;
            _stopRequested = false;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _svc.ExitCode = null;
            _svc.RunningSince = null;
            _svc.CpuHistory.Clear();
            SetState(ServiceState.Queued);
            var token = _cts.Token;
            _runTask = Task.Run(() => RunAsync(options, token));
        }
    }

    /// <summary>Stops the service and waits (up to a few seconds) for it to exit.</summary>
    public async Task StopAsync()
    {
        Process? process;
        Task runTask;
        lock (_gate)
        {
            if (!_svc.IsActive) return;
            _stopRequested = true;
            SetState(ServiceState.Stopping);
            _cts?.Cancel();
            process = _process;
            runTask = _runTask;
        }

        if (process != null) Kill(process);
        try { await runTask.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
        if (process != null)
        {
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        }
        if (_svc.State == ServiceState.Stopping) SetState(ServiceState.Stopped);
    }

    /// <summary>Immediate kill for app shutdown; doesn't wait.</summary>
    public void KillNow()
    {
        _stopRequested = true;
        _cts?.Cancel();
        if (_process is { } p) Kill(p);
    }

    private async Task RunAsync(RunOptions options, CancellationToken ct)
    {
        var log = _svc.Log;
        try
        {
            var busy = Ports.InUse(_svc.Ports);
            if (busy.Count > 0)
            {
                log.AppendSystem($"✖ Port {string.Join(", ", busy)} is already in use — is {_svc.Name} running somewhere else (Visual Studio, a terminal)?");
                SetState(ServiceState.PortBusy);
                return;
            }

            if (options.Mode == RunMode.Lite)
            {
                await BuildGate.WaitAsync(ct);
                (int exitCode, string? output) build;
                try
                {
                    ct.ThrowIfCancellationRequested();
                    SetState(ServiceState.Building);
                    log.AppendSystem($"▸ Building {_svc.Name}…");
                    build = await BuildAsync(ct);
                }
                finally
                {
                    BuildGate.Release();
                }

                ct.ThrowIfCancellationRequested();
                if (build.exitCode != 0 || build.output is null)
                {
                    log.AppendSystem(build.exitCode != 0
                        ? $"✖ Build failed (exit code {build.exitCode}). Errors are highlighted above."
                        : "✖ Build finished but the output .dll wasn't found. Try Settings → Run mode → \"dotnet run\".");
                    SetState(ServiceState.BuildFailed);
                    return;
                }
                StartApp(WithOptions(CreateAppStartInfo(build.output), options));
            }
            else
            {
                log.AppendSystem($"▸ dotnet run — {_svc.Name}");
                StartApp(WithOptions(CreateDotnetRunStartInfo(), options));
            }
        }
        catch (OperationCanceledException)
        {
            SetState(ServiceState.Stopped);
        }
        catch (Exception ex)
        {
            log.AppendSystem($"✖ Couldn't start {_svc.Name}: {ex.Message}");
            _svc.ExitCode = null;
            SetState(ServiceState.Crashed);
        }
    }

    // ------------------------------------------------------------------ build

    [GeneratedRegex(@"^\s*(?<name>[^\s]+)\s+->\s+(?<path>.+\.dll)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OutputLineRegex();

    private async Task<(int ExitCode, string? OutputDll)> BuildAsync(CancellationToken ct)
    {
        var psi = BaseStartInfo("dotnet", $"build \"{_svc.ProjectFilePath}\" -nologo -v:minimal -clp:NoSummary", _svc.ProjectDirectory);
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1"; // don't leave idle build nodes in memory

        var outputs = new List<(string Name, string Path)>();
        using var process = new Process { StartInfo = psi };
        void OnLine(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            var m = OutputLineRegex().Match(e.Data);
            if (m.Success)
            {
                lock (outputs) outputs.Add((m.Groups["name"].Value, m.Groups["path"].Value.Trim()));
            }
            _svc.Log.Append(e.Data, isBuild: true);
        }
        process.OutputDataReceived += OnLine;
        process.ErrorDataReceived += OnLine;

        process.Start();
        ChildProcessGuard.Track(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
        process.WaitForExit(); // flush remaining output

        string? dll;
        lock (outputs)
        {
            dll = outputs.LastOrDefault(o => string.Equals(o.Name, _svc.Name, StringComparison.OrdinalIgnoreCase)).Path
                  ?? outputs.LastOrDefault(o => string.Equals(Path.GetFileNameWithoutExtension(o.Path), _svc.Name, StringComparison.OrdinalIgnoreCase)).Path
                  ?? (outputs.Count > 0 ? outputs[^1].Path : null);
        }
        if (dll != null && !Path.IsPathRooted(dll)) dll = Path.GetFullPath(Path.Combine(_svc.ProjectDirectory, dll));
        if (dll != null && !File.Exists(dll)) dll = null;
        return (process.ExitCode, dll);
    }

    // ------------------------------------------------------------------ run

    private ProcessStartInfo CreateAppStartInfo(string dll)
    {
        var profile = _svc.Profile;
        string appHost = OperatingSystem.IsWindows() ? Path.ChangeExtension(dll, ".exe") : Path.ChangeExtension(dll, null);
        string workingDir = ResolveWorkingDirectory(profile.WorkingDirectory);

        ProcessStartInfo psi = File.Exists(appHost)
            ? BaseStartInfo(appHost, profile.CommandLineArgs ?? "", workingDir)
            : BaseStartInfo("dotnet", $"\"{dll}\" {profile.CommandLineArgs}".TrimEnd(), workingDir);

        // What `dotnet run` does with the launch profile:
        foreach (var (name, value) in profile.EnvironmentVariables) psi.Environment[name] = Environment.ExpandEnvironmentVariables(value);
        if (!string.IsNullOrWhiteSpace(profile.ApplicationUrl)) psi.Environment["ASPNETCORE_URLS"] = profile.ApplicationUrl;
        psi.Environment["DOTNET_LAUNCH_PROFILE"] = profile.Name;

        _svc.Log.AppendSystem($"▸ Starting {Path.GetFileName(psi.FileName == "dotnet" ? dll : appHost)} on {profile.ApplicationUrl}");
        return psi;
    }

    private ProcessStartInfo CreateDotnetRunStartInfo()
    {
        var psi = BaseStartInfo("dotnet", $"run --project \"{_svc.ProjectFilePath}\"", _svc.ProjectDirectory);
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        return psi;
    }

    private string ResolveWorkingDirectory(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return _svc.ProjectDirectory;
        string expanded = configured.Replace("$(ProjectDir)", _svc.ProjectDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        expanded = Environment.ExpandEnvironmentVariables(expanded);
        string full = Path.IsPathRooted(expanded) ? expanded : Path.GetFullPath(Path.Combine(_svc.ProjectDirectory, expanded));
        return Directory.Exists(full) ? full : _svc.ProjectDirectory;
    }

    private void StartApp(ProcessStartInfo psi)
    {
        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        bool sawBuildError = false;

        process.OutputDataReceived += (_, e) => OnAppLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnAppLine(e.Data);
        process.Exited += (_, _) => OnExited(process, sawBuildError);

        void OnAppLine(string? line)
        {
            if (line is null) return;
            _svc.Log.Append(line);
            if (_svc.State == ServiceState.Starting)
            {
                if (line.Contains("Now listening on", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Application started", StringComparison.OrdinalIgnoreCase))
                {
                    MarkRunning();
                }
                else if (line.Contains(": error ", StringComparison.Ordinal))
                {
                    sawBuildError = true; // `dotnet run` compile error
                }
            }
        }

        lock (_gate)
        {
            if (_stopRequested) throw new OperationCanceledException();
            process.Start();
            _process = process;
            SetState(ServiceState.Starting);
        }
        ChildProcessGuard.Track(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    /// <summary>Called by the metrics tick when the port starts listening (for apps that log differently).</summary>
    public void MarkRunningIfListening()
    {
        if (_svc.State != ServiceState.Starting || _svc.Ports.Count == 0) return;
        if (Ports.InUse(_svc.Ports).Count > 0) MarkRunning();
    }

    private void MarkRunning()
    {
        if (_svc.State != ServiceState.Starting) return;
        _svc.RunningSince = DateTime.Now;
        SetState(ServiceState.Running);
        _svc.Log.AppendSystem($"✔ {_svc.Name} is up — {_svc.LocalUrl}");
    }

    private void OnExited(Process process, bool sawBuildError)
    {
        try { process.WaitForExit(); } catch { } // drain buffered output
        int code = 0;
        try { code = process.ExitCode; } catch { }

        lock (_gate)
        {
            if (!ReferenceEquals(_process, process)) return;
            _process = null;
        }
        _svc.RunningSince = null;
        _svc.CpuPercent = 0;
        _svc.MemoryBytes = 0;

        if (_stopRequested)
        {
            _svc.Log.AppendSystem("■ Stopped.");
            SetState(ServiceState.Stopped);
        }
        else
        {
            _svc.ExitCode = code;
            _svc.Log.AppendSystem($"✖ {_svc.Name} exited unexpectedly (exit code {code}).");
            SetState(sawBuildError ? ServiceState.BuildFailed : ServiceState.Crashed);
        }
        process.Dispose();
    }

    // ------------------------------------------------------------------ helpers

    private static ProcessStartInfo WithOptions(ProcessStartInfo psi, RunOptions options)
    {
        if (options.LowMemory)
        {
            // ASP.NET Core defaults to Server GC, which reserves heaps per CPU core. Workstation GC behaves
            // the same for local development and typically uses a fraction of the memory.
            psi.Environment["DOTNET_gcServer"] = "0";
        }
        return psi;
    }

    private static ProcessStartInfo BaseStartInfo(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["MSBUILDTERMINALLOGGER"] = "off";
        psi.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "false";
        return psi;
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    private void SetState(ServiceState state) => _svc.State = state;
}
