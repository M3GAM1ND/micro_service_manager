using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroserviceManager.Core;

public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
    public int LogPanelHeight { get; set; }
}

/// <summary>User settings, stored in %LOCALAPPDATA%\MicroserviceManager\settings.json (never in Git).</summary>
public sealed class AppSettings
{
    public string EnvFilePath { get; set; } = "";
    public string MicroservicesRootPath { get; set; } = "";
    public string DatabaseName { get; set; } = "Fre8_Database";
    public string RemoteEnvironment { get; set; } = "QA";
    public RunMode RunMode { get; set; } = RunMode.Lite;

    /// <summary>Run services with Workstation GC (much less RAM, no difference for local development).</summary>
    public bool LowMemoryMode { get; set; } = true;

    /// <summary>Project file path → environment.ts key, for services where you picked the key yourself.</summary>
    public Dictionary<string, string> EnvKeyOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Project file paths that were running locally last time.</summary>
    public List<string> LastLocalServices { get; set; } = new();

    public WindowPlacement? Window { get; set; }

    [JsonIgnore]
    public bool PathsAreValid => File.Exists(EnvFilePath) && Directory.Exists(MicroservicesRootPath);
}

public static class AppPaths
{
    public static string DataFolder { get; } = Init();

    public static string SettingsFile => Path.Combine(DataFolder, "settings.json");
    public static string EnvironmentsFile => Path.Combine(DataFolder, "environments.json");
    public static string JournalFile => Path.Combine(DataFolder, "session.json");

    private static string Init()
    {
        string folder = Environment.GetEnvironmentVariable("MSM_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicroserviceManager");
        Directory.CreateDirectory(folder);
        return folder;
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
    }

    public static void Write<T>(string path, T value)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

public static class SettingsStore
{
    public static AppSettings Load()
    {
        AppSettings settings;
        try { settings = Json.Read<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings(); }
        catch { settings = new AppSettings(); }

        // Re-create with a case-insensitive comparer (the deserializer uses the default one).
        settings.EnvKeyOverrides = new Dictionary<string, string>(settings.EnvKeyOverrides, StringComparer.OrdinalIgnoreCase);

        // v1 only had "QA" and "UAT" (which was UAT3).
        if (string.Equals(settings.RemoteEnvironment, "UAT", StringComparison.OrdinalIgnoreCase))
            settings.RemoteEnvironment = "UAT3";

        return settings;
    }

    public static void Save(AppSettings settings)
    {
        try { Json.Write(AppPaths.SettingsFile, settings); }
        catch { /* settings are a convenience; never crash over them */ }
    }
}

/// <summary>Reads and writes text files while keeping their encoding and BOM exactly as they were.</summary>
public static class TextFile
{
    public static (string Text, Encoding Encoding) Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bool utf8Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        Encoding encoding = reader.CurrentEncoding is UTF8Encoding ? new UTF8Encoding(utf8Bom) : reader.CurrentEncoding;
        return (text, encoding);
    }

    /// <summary>Writes the file, retrying briefly if an editor or file watcher has it locked.</summary>
    public static void Write(string path, string text, Encoding encoding)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.WriteAllText(path, text, encoding);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(120 * attempt);
            }
        }
    }
}
