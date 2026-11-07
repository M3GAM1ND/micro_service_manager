using System.Text.Json;
using System.Text.RegularExpressions;

namespace MicroserviceManager.Core;

public static partial class ServiceScanner
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "packages", "TestResults", "dist", "wwwroot",
    };

    private static readonly HashSet<string> ExcludedNameParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Test", "Tests", "UnitTests", "IntegrationTests", "Entities", "DAL", "BL", "BLL", "Database",
    };

    /// <summary>Finds every runnable web project under <paramref name="root"/>.</summary>
    public static List<ServiceInfo> Scan(string root, List<string> notes)
    {
        var services = new List<ServiceInfo>();
        if (!Directory.Exists(root)) return services;

        foreach (string csproj in FindProjects(root))
        {
            string name = Path.GetFileNameWithoutExtension(csproj);
            if (IsExcludedName(name)) continue;

            LaunchProfile? profile;
            try { profile = ReadLaunchProfile(Path.GetDirectoryName(csproj)!); }
            catch (Exception ex)
            {
                notes.Add($"{name}: couldn't read Properties/launchSettings.json ({ex.Message}).");
                continue;
            }

            if (profile?.ApplicationUrl is not { Length: > 0 } urls)
            {
                if (LooksLikeWebProject(csproj))
                    notes.Add($"{name}: skipped — launchSettings.json has no \"Project\" profile with an applicationUrl.");
                continue;
            }

            var parsed = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string chosen = parsed.FirstOrDefault(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ?? parsed[0];

            services.Add(new ServiceInfo
            {
                Name = name,
                ProjectFilePath = csproj,
                Profile = profile,
                LocalUrl = ToLocalhost(chosen),
                Ports = parsed.Select(PortOf).Where(p => p > 0).Distinct().ToList(),
            });
        }

        // Same project name in two folders (e.g. an old copy): show where each one lives.
        foreach (var group in services.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            foreach (var svc in group)
                svc.DisplayHint = Path.GetRelativePath(root, svc.ProjectDirectory);
        }

        return services.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> FindProjects(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };

        while (pending.Count > 0)
        {
            string dir = pending.Pop();
            string[] files, subdirs;
            try
            {
                files = Directory.GetFiles(dir, "*.csproj", options);
                subdirs = Directory.GetDirectories(dir, "*", options);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (string f in files) yield return f;
            foreach (string sub in subdirs)
            {
                string folder = Path.GetFileName(sub);
                if (folder.StartsWith('.') || SkippedFolders.Contains(folder)) continue;
                pending.Push(sub);
            }
        }
    }

    internal static bool IsExcludedName(string name)
    {
        foreach (string part in NameSplitRegex().Split(name))
            if (ExcludedNameParts.Contains(part)) return true;

        return name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Test", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Entities", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("DAL", StringComparison.Ordinal)
            || name.EndsWith("BLL", StringComparison.Ordinal)
            || name.EndsWith("BL", StringComparison.Ordinal);
    }

    private static bool LooksLikeWebProject(string csproj)
    {
        try { return File.ReadAllText(csproj).Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public static LaunchProfile? ReadLaunchProfile(string projectDir)
    {
        string path = Path.Combine(projectDir, "Properties", "launchSettings.json");
        if (!File.Exists(path)) return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        if (!doc.RootElement.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var p in profiles.EnumerateObject())
        {
            var v = p.Value;
            if (v.ValueKind != JsonValueKind.Object) continue;
            if (!v.TryGetProperty("commandName", out var cmd) || !string.Equals(cmd.GetString(), "Project", StringComparison.OrdinalIgnoreCase))
                continue;

            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (v.TryGetProperty("environmentVariables", out var vars) && vars.ValueKind == JsonValueKind.Object)
            {
                foreach (var e in vars.EnumerateObject())
                    env[e.Name] = e.Value.ValueKind == JsonValueKind.String ? e.Value.GetString() ?? "" : e.Value.ToString();
            }

            return new LaunchProfile
            {
                Name = p.Name,
                ApplicationUrl = Str(v, "applicationUrl"),
                LaunchUrl = Str(v, "launchUrl"),
                CommandLineArgs = Str(v, "commandLineArgs"),
                WorkingDirectory = Str(v, "workingDirectory"),
                EnvironmentVariables = env,
            };
        }
        return null;

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }

    /// <summary>"http://*:5000" / "http://0.0.0.0:5000" → "http://localhost:5000".</summary>
    internal static string ToLocalhost(string url) => WildcardHostRegex().Replace(url.Trim(), "${scheme}localhost");

    internal static int PortOf(string url)
    {
        var m = PortRegex().Match(url);
        return m.Success && int.TryParse(m.Groups[1].Value, out int port) ? port : 0;
    }

    [GeneratedRegex(@"[._\-\s]+")]
    private static partial Regex NameSplitRegex();

    [GeneratedRegex(@"^(?<scheme>[a-z]+://)(?:\*|\+|0\.0\.0\.0|\[::\])(?=[:/]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex WildcardHostRegex();

    [GeneratedRegex(@"^[a-z]+://[^/]*:(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PortRegex();
}

/// <summary>
/// Works out which environment.ts key belongs to a service. Tries, in order: an exact name match
/// (case-insensitive, with or without "API"), known aliases, and the service name inside each URL
/// (".../frqa/LegalEntityService" → LegalEntity).
/// </summary>
public static partial class EnvKeyMatcher
{
    private static readonly string[] Suffixes = { "services", "service", "webapi", "api", "svc", "host", "uat1", "uat2", "uat3", "uat", "qa" };

    private static readonly HashSet<string> GenericNameParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "API", "Api", "WebApi", "WebAPI", "Service", "Services", "Web", "Host",
    };

    // Keys whose names don't follow the pattern (some typos are in the Angular app itself).
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["organizationmaster"] = "API",
        ["intercompany"] = "InterComapanyAPI",
        ["dispatch"] = "Dispatch",
        ["driverapp"] = "DriverApp",
        ["fleetmaintenance"] = "FleetMaintenanceAndSafetyAPI",
        ["documentuploader"] = "DocumentAPI",
    };

    public static string? Match(string projectName, IReadOnlyCollection<TsEntry> entries, IEnumerable<EnvironmentProfile> profiles)
    {
        var keys = entries.Select(e => e.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keys.Count == 0) return null;

        var bases = BaseNames(projectName);
        var normalizedBases = bases.Select(Normalize).Where(n => n.Length > 0).Distinct().ToList();

        string? best = null;
        int bestScore = 0;

        void Consider(string key, int score)
        {
            if (score > bestScore) { bestScore = score; best = key; }
        }

        // 1. Exact key names.
        foreach (string b in bases)
        {
            foreach (string candidate in new[] { b, b + "API", b + "Api", b + "ServiceAPI" })
            {
                string? hit = keys.FirstOrDefault(k => string.Equals(k, candidate, StringComparison.OrdinalIgnoreCase));
                if (hit != null) Consider(hit, 100);
            }
        }

        // 2. Known aliases.
        foreach (string n in normalizedBases)
        {
            if (Aliases.TryGetValue(n, out var alias))
            {
                string? hit = keys.FirstOrDefault(k => string.Equals(k, alias, StringComparison.OrdinalIgnoreCase));
                if (hit != null) Consider(hit, 95);
            }
        }

        // 3. Service name inside the URL, from your file and from the environment profiles.
        var urlValues = entries.Select(e => (e.Key, e.RawValue))
            .Concat(profiles.SelectMany(p => p.Values.Select(v => (v.Key, v.Value))));
        foreach (var (key, value) in urlValues)
        {
            string segment = Normalize(LastUrlSegment(value));
            if (segment.Length < 3) continue;
            string? realKey = keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (realKey is null) continue;
            foreach (string n in normalizedBases)
            {
                if (n == segment) Consider(realKey, 90);
                else if (Math.Min(n.Length, segment.Length) >= 5 && (n.EndsWith(segment) || segment.EndsWith(n))) Consider(realKey, 70);
            }
        }

        // 4. Normalized key names ("LegalEntityAPI" ≈ "LegalEntityService").
        foreach (string key in keys)
        {
            string nk = Normalize(key);
            if (nk.Length < 3) continue;
            foreach (string n in normalizedBases)
            {
                if (n == nk) Consider(key, 80);
                else if (Math.Min(n.Length, nk.Length) >= 5 && (nk.StartsWith(n) || n.StartsWith(nk))) Consider(key, 60);
            }
        }

        return best;
    }

    /// <summary>"Fre8.LegalEntity.API" → ["Fre8.LegalEntity.API", "Fre8.LegalEntity", "LegalEntity"].</summary>
    private static List<string> BaseNames(string projectName)
    {
        var result = new List<string> { projectName };
        var parts = projectName.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        while (parts.Count > 1 && GenericNameParts.Contains(parts[^1])) parts.RemoveAt(parts.Count - 1);
        if (parts.Count > 0)
        {
            result.Add(string.Join(".", parts));
            result.Add(parts[^1]);
        }

        string stripped = StripSuffixRegex().Replace(projectName, "");
        if (stripped.Length > 0) result.Add(stripped);

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static string Normalize(string s)
    {
        var chars = s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        string t = new(chars);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (string suffix in Suffixes)
            {
                if (t.Length > suffix.Length + 2 && t.EndsWith(suffix, StringComparison.Ordinal))
                {
                    t = t[..^suffix.Length];
                    changed = true;
                }
            }
        }
        return t;
    }

    private static string LastUrlSegment(string value)
    {
        if (!value.Contains("://", StringComparison.Ordinal)) return "";
        string path = value;
        int q = path.IndexOfAny(new[] { '?', '#' });
        if (q >= 0) path = path[..q];
        int scheme = path.IndexOf("://", StringComparison.Ordinal);
        int slash = path.IndexOf('/', scheme + 3);
        if (slash < 0) return "";
        var segments = path[(slash + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? "" : segments[^1];
    }

    [GeneratedRegex(@"(Service|Services|WebApi|API|Api)$")]
    private static partial Regex StripSuffixRegex();
}
