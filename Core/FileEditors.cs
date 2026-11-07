using System.Text;
using System.Text.RegularExpressions;

namespace MicroserviceManager.Core;

/// <summary>One value the app changed: what it was before, and what the app wrote.</summary>
public sealed class ValueChange
{
    public string Original { get; set; } = "";
    public string Written { get; set; } = "";
}

/// <summary>A <c>key: 'value'</c> pair found in environment.ts.</summary>
public sealed record TsEntry(string Key, int Occurrence, int ValueStart, int ValueLength, char Quote, string RawValue)
{
    public string Id => $"{Key}#{Occurrence}";
}

/// <summary>
/// Edits individual values inside an Angular environment.ts file. The file itself always stays the
/// one in your repo: new keys your team adds survive, and only values the app changed are put back.
/// </summary>
public static partial class TsEnvironmentFile
{
    [GeneratedRegex(@"(?<![\w$])(?<kq>['""]?)(?<key>[A-Za-z_$][\w$]*)\k<kq>\s*:\s*(?<q>['""`])(?<val>(?:\\.|(?!\k<q>)[^\\\r\n])*)\k<q>")]
    private static partial Regex PairRegex();

    public static List<TsEntry> Parse(string content)
    {
        bool[] code = CodeMask(content);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var entries = new List<TsEntry>();

        foreach (Match m in PairRegex().Matches(content))
        {
            if (!code[m.Index]) continue; // inside a comment or a string

            string key = m.Groups["key"].Value;
            occurrences.TryGetValue(key, out int n);
            occurrences[key] = n + 1;

            Group v = m.Groups["val"];
            entries.Add(new TsEntry(key, n, v.Index, v.Length, m.Groups["q"].Value[0], v.Value));
        }
        return entries;
    }

    /// <summary>
    /// Rewrites values. <paramref name="desired"/> gets each entry plus its original value (as it is in your repo)
    /// and returns the raw text to write, or null to keep the original. <paramref name="journal"/> is updated
    /// so the change can be undone later, even after a crash.
    /// </summary>
    public static string Apply(string content, Func<TsEntry, string, string?> desired, Dictionary<string, ValueChange> journal)
    {
        var entries = Parse(content);
        var sb = new StringBuilder(content.Length + 256);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int pos = 0;

        foreach (var entry in entries)
        {
            seen.Add(entry.Id);
            string current = entry.RawValue;
            string original = current;

            if (journal.TryGetValue(entry.Id, out var change))
            {
                if (current == change.Written) original = change.Original;
                else journal.Remove(entry.Id); // edited by someone else since: their value wins
            }

            string target = desired(entry, original) ?? original;

            if (target != original) journal[entry.Id] = new ValueChange { Original = original, Written = target };
            else journal.Remove(entry.Id);

            if (target != current)
            {
                sb.Append(content, pos, entry.ValueStart - pos).Append(target);
                pos = entry.ValueStart + entry.ValueLength;
            }
        }

        foreach (string stale in journal.Keys.Where(k => !seen.Contains(k)).ToList()) journal.Remove(stale);

        sb.Append(content, pos, content.Length - pos);
        return sb.ToString();
    }

    public static string Escape(string value, char quote) =>
        value.Replace("\\", "\\\\").Replace(quote.ToString(), "\\" + quote);

    /// <summary>true for characters that are code (not inside a comment or string literal).</summary>
    private static bool[] CodeMask(string s)
    {
        var mask = new bool[s.Length + 1];
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            char next = i + 1 < s.Length ? s[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (c == '/' && next == '*')
            {
                int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? s.Length : end + 2;
                continue;
            }
            if (c is '\'' or '"' or '`')
            {
                mask[i] = true; // the opening quote counts as code, so quoted keys are found
                char quote = c;
                i++;
                while (i < s.Length && s[i] != quote)
                {
                    if (s[i] == '\\') i++;
                    else if (s[i] == '\n' && quote != '`') break;
                    i++;
                }
                i++;
                continue;
            }
            mask[i] = true;
            i++;
        }
        return mask;
    }
}

/// <summary>Changes the database name inside connection strings in appsettings*.json.</summary>
public static partial class AppSettingsFile
{
    [GeneratedRegex(@"(?<![\w])(?:Database|Initial\s+Catalog)\s*=\s*(?<v>[^;""\r\n\\]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DatabaseRegex();

    [GeneratedRegex(@"^[\w.\-]{1,128}$")]
    private static partial Regex ValidNameRegex();

    public static bool IsValidDatabaseName(string name) => ValidNameRegex().IsMatch(name);

    /// <summary>Sets every database name to <paramref name="database"/>, or puts the originals back when it is null.</summary>
    public static string Apply(string content, string? database, Dictionary<string, ValueChange> journal)
    {
        var sb = new StringBuilder(content.Length + 64);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int pos = 0;
        int occurrence = 0;

        foreach (Match m in DatabaseRegex().Matches(content))
        {
            string id = (occurrence++).ToString();
            seen.Add(id);
            Group v = m.Groups["v"];
            string current = v.Value;
            string original = current;

            if (journal.TryGetValue(id, out var change))
            {
                if (current == change.Written) original = change.Original;
                else journal.Remove(id);
            }

            // Keep any trailing spaces the original had, e.g. "Database=Foo ;"
            string target = database is null ? original : database + current[current.TrimEnd().Length..];

            if (target != original) journal[id] = new ValueChange { Original = original, Written = target };
            else journal.Remove(id);

            if (target != current)
            {
                sb.Append(content, pos, v.Index - pos).Append(target);
                pos = v.Index + v.Length;
            }
        }

        foreach (string stale in journal.Keys.Where(k => !seen.Contains(k)).ToList()) journal.Remove(stale);

        sb.Append(content, pos, content.Length - pos);
        return sb.ToString();
    }
}

/// <summary>
/// Every value the app has changed and not yet put back, saved to disk before each edit.
/// If the app is killed, the next start uses this to restore your files.
/// </summary>
public sealed class SessionJournal
{
    public Dictionary<string, Dictionary<string, ValueChange>> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEmpty => Files.Values.All(f => f.Count == 0);

    public Dictionary<string, ValueChange> For(string path)
    {
        if (!Files.TryGetValue(path, out var changes))
        {
            changes = new Dictionary<string, ValueChange>(StringComparer.Ordinal);
            Files[path] = changes;
        }
        return changes;
    }

    public static SessionJournal Load()
    {
        try
        {
            var loaded = Json.Read<SessionJournal>(AppPaths.JournalFile);
            if (loaded is null) return new SessionJournal();
            var journal = new SessionJournal();
            foreach (var (path, changes) in loaded.Files)
                journal.Files[path] = new Dictionary<string, ValueChange>(changes, StringComparer.Ordinal);
            return journal;
        }
        catch
        {
            return new SessionJournal();
        }
    }

    public void Save()
    {
        foreach (string empty in Files.Where(f => f.Value.Count == 0).Select(f => f.Key).ToList()) Files.Remove(empty);
        if (Files.Count == 0)
        {
            try { File.Delete(AppPaths.JournalFile); } catch { }
            return;
        }
        Json.Write(AppPaths.JournalFile, this);
    }
}
