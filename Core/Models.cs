namespace MicroserviceManager.Core;

public enum ServiceState
{
    Stopped,
    Queued,
    Building,
    Starting,
    Running,
    Stopping,
    Crashed,
    BuildFailed,
    PortBusy,
}

public enum RunMode
{
    /// <summary>Build with <c>dotnet build</c>, then run the compiled app directly. Saves ~50 MB of RAM per service.</summary>
    Lite,

    /// <summary>Run with <c>dotnet run</c>, exactly like a terminal would.</summary>
    DotnetRun,
}

/// <summary>The first "Project" profile from Properties/launchSettings.json.</summary>
public sealed class LaunchProfile
{
    public string Name { get; init; } = "";
    public string? ApplicationUrl { get; init; }
    public string? LaunchUrl { get; init; }
    public string? CommandLineArgs { get; init; }
    public string? WorkingDirectory { get; init; }
    public Dictionary<string, string> EnvironmentVariables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ServiceInfo
{
    public required string Name { get; init; }
    public required string ProjectFilePath { get; init; }
    public required LaunchProfile Profile { get; init; }

    /// <summary>The URL written into environment.ts when the service runs locally (http preferred).</summary>
    public required string LocalUrl { get; init; }

    public required IReadOnlyList<int> Ports { get; init; }

    /// <summary>Shown next to the name when two projects share a name.</summary>
    public string? DisplayHint { get; set; }

    public string ProjectDirectory => Path.GetDirectoryName(ProjectFilePath)!;

    // ---- Routing ----
    public bool IsLocal { get; set; }
    public bool InVisualStudio { get; set; }
    public string? EnvKey { get; set; }
    public bool EnvKeyIsManual { get; set; }
    public bool EnvKeyFound { get; set; }

    // ---- Runtime (written from background threads, read by the UI) ----
    public volatile ServiceState State;
    public int? ExitCode { get; set; }
    public DateTime? RunningSince { get; set; }
    public double CpuPercent { get; set; }
    public long MemoryBytes { get; set; }
    public readonly MetricHistory CpuHistory = new(48);
    public LogBuffer Log { get; } = new();

    public bool IsActive => State is ServiceState.Queued or ServiceState.Building or ServiceState.Starting
        or ServiceState.Running or ServiceState.Stopping;

    public string BrowseUrl
    {
        get
        {
            string baseUrl = LocalUrl.TrimEnd('/');
            string? launch = Profile.LaunchUrl;
            if (string.IsNullOrWhiteSpace(launch)) return baseUrl;
            if (launch.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return launch;
            return $"{baseUrl}/{launch.TrimStart('/')}";
        }
    }
}

/// <summary>Fixed-size ring of samples for sparklines.</summary>
public sealed class MetricHistory
{
    private readonly float[] _values;
    private int _next;
    private int _count;

    public MetricHistory(int capacity) => _values = new float[capacity];

    public int Capacity => _values.Length;

    public void Add(float value)
    {
        lock (_values)
        {
            _values[_next] = value;
            _next = (_next + 1) % _values.Length;
            if (_count < _values.Length) _count++;
        }
    }

    public void Clear()
    {
        lock (_values) { _next = 0; _count = 0; }
    }

    /// <summary>Oldest → newest.</summary>
    public float[] Snapshot()
    {
        lock (_values)
        {
            var result = new float[_count];
            int start = (_next - _count + _values.Length) % _values.Length;
            for (int i = 0; i < _count; i++) result[i] = _values[(start + i) % _values.Length];
            return result;
        }
    }
}

public readonly record struct Notice(string Text, bool IsError);
