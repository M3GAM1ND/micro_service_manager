using System.Text.RegularExpressions;

namespace MicroserviceManager.Core;

public enum LogLevel : byte
{
    Info,
    Debug,
    Warning,
    Error,
    Build,
    System,
}

public readonly record struct LogLine(string Text, LogLevel Level);

/// <summary>
/// Thread-safe ring buffer of log lines. Old lines are dropped once <see cref="Capacity"/> is reached,
/// so a chatty service can't make the manager grow without limit.
/// </summary>
public sealed partial class LogBuffer
{
    private readonly object _lock = new();
    private readonly LogLine[] _lines;
    private int _start;
    private int _count;
    private long _dropped;
    private long _version;
    private LogLevel _lastLevel = LogLevel.Info;

    public LogBuffer(int capacity = 5000) => _lines = new LogLine[capacity];

    public int Capacity => _lines.Length;

    public int Count { get { lock (_lock) return _count; } }

    /// <summary>Total number of lines dropped from the front since the last clear.</summary>
    public long Dropped { get { lock (_lock) return _dropped; } }

    /// <summary>Increments on every change; the UI repaints only when this moves.</summary>
    public long Version => Interlocked.Read(ref _version);

    public int ErrorCount { get; private set; }
    public int WarningCount { get; private set; }

    public void Append(string? raw, bool isBuild = false)
    {
        if (raw is null) return;
        string text = StripAnsi(raw).TrimEnd('\r');
        if (text.Contains('\t')) text = text.Replace("\t", "    ");
        lock (_lock)
        {
            LogLevel level = Classify(text, _lastLevel, isBuild);
            _lastLevel = level;
            AddLocked(new LogLine(text, level));
        }
    }

    public void AppendSystem(string text)
    {
        lock (_lock) AddLocked(new LogLine(text, LogLevel.System));
    }

    public void Clear()
    {
        lock (_lock)
        {
            _start = 0;
            _count = 0;
            _dropped = 0;
            ErrorCount = 0;
            WarningCount = 0;
            Interlocked.Increment(ref _version);
        }
    }

    public LogLine this[int index]
    {
        get
        {
            lock (_lock)
            {
                if ((uint)index >= (uint)_count) return default;
                return _lines[(_start + index) % _lines.Length];
            }
        }
    }

    public void CopyTo(List<LogLine> destination)
    {
        lock (_lock)
        {
            destination.Clear();
            destination.EnsureCapacity(_count);
            for (int i = 0; i < _count; i++) destination.Add(_lines[(_start + i) % _lines.Length]);
        }
    }

    private void AddLocked(LogLine line)
    {
        if (line.Level == LogLevel.Error) ErrorCount++;
        else if (line.Level == LogLevel.Warning) WarningCount++;

        if (_count < _lines.Length)
        {
            _lines[(_start + _count) % _lines.Length] = line;
            _count++;
        }
        else
        {
            _lines[_start] = line;
            _start = (_start + 1) % _lines.Length;
            _dropped++;
        }
        Interlocked.Increment(ref _version);
    }

    // ---------------------------------------------------------------- classification

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiRegex();

    [GeneratedRegex(@"\berror\s+[A-Z]{2,}\d+|\bBuild FAILED\b|^\s*error\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex BuildErrorRegex();

    [GeneratedRegex(@"\bwarning\s+[A-Z]{2,}\d+", RegexOptions.IgnoreCase)]
    private static partial Regex BuildWarningRegex();

    // Serilog "[12:00:00 ERR]", NLog/log4net "ERROR", etc.
    [GeneratedRegex(@"\[(?:[\d:.\s-]+\s)?(?<lvl>ERR|FTL|WRN|INF|DBG|VRB)\]|\b(?<word>FATAL|ERROR|CRITICAL|WARN|WARNING)\b")]
    private static partial Regex GenericLevelRegex();

    private static string StripAnsi(string s) => s.IndexOf('\x1B') < 0 ? s : AnsiRegex().Replace(s, "");

    internal static LogLevel Classify(string line, LogLevel previous, bool isBuild)
    {
        if (line.Length == 0) return previous;

        if (BuildErrorRegex().IsMatch(line)) return LogLevel.Error;
        if (BuildWarningRegex().IsMatch(line)) return LogLevel.Warning;
        if (isBuild) return LogLevel.Build;

        // ASP.NET Core console logger: "info: Category[0]" followed by indented message lines.
        if (line.Length >= 5 && line[4] == ':')
        {
            switch (line.AsSpan(0, 4))
            {
                case "fail":
                case "crit": return LogLevel.Error;
                case "warn": return LogLevel.Warning;
                case "info": return LogLevel.Info;
                case "dbug":
                case "trce": return LogLevel.Debug;
            }
        }

        if (char.IsWhiteSpace(line[0])) return previous; // continuation / stack trace

        if (line.StartsWith("Unhandled exception", StringComparison.OrdinalIgnoreCase)) return LogLevel.Error;

        var m = GenericLevelRegex().Match(line);
        if (m.Success)
        {
            string v = m.Groups["lvl"].Success ? m.Groups["lvl"].Value : m.Groups["word"].Value;
            return v switch
            {
                "ERR" or "FTL" or "FATAL" or "ERROR" or "CRITICAL" => LogLevel.Error,
                "WRN" or "WARN" or "WARNING" => LogLevel.Warning,
                "DBG" or "VRB" => LogLevel.Debug,
                _ => LogLevel.Info,
            };
        }

        if (line.Contains("Exception:", StringComparison.Ordinal)) return LogLevel.Error;
        return LogLevel.Info;
    }
}
