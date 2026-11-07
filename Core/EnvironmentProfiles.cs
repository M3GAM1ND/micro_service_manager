namespace MicroserviceManager.Core;

public sealed class UrlRewrite
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

/// <summary>
/// A remote environment (QA, UAT1, UAT3, ...). Applied on top of your real environment.ts:
/// <see cref="Rewrite"/> swaps URL prefixes in every value (so keys your team adds later just work),
/// <see cref="Values"/> sets exact values for the few keys that don't follow the pattern.
/// </summary>
public sealed class EnvironmentProfile
{
    public string Name { get; set; } = "";
    public List<UrlRewrite> Rewrite { get; set; } = new();
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Resolve(string key, string originalRaw, char quote)
    {
        if (Values.TryGetValue(key, out var exact)) return TsEnvironmentFile.Escape(exact, quote);
        foreach (var rule in Rewrite)
        {
            if (rule.From.Length > 0 && originalRaw.StartsWith(rule.From, StringComparison.OrdinalIgnoreCase))
                return rule.To + originalRaw[rule.From.Length..];
        }
        return null;
    }
}

public sealed class EnvironmentProfileFile
{
    public List<EnvironmentProfile> Environments { get; set; } = new();
}

public static class EnvironmentProfiles
{
    /// <summary>Loads environments.json, creating it with the FRE8 defaults the first time.</summary>
    public static List<EnvironmentProfile> Load(out string? error)
    {
        error = null;
        string path = AppPaths.EnvironmentsFile;
        if (!File.Exists(path)) File.WriteAllText(path, DefaultFileContent);

        try
        {
            var file = Json.Read<EnvironmentProfileFile>(path);
            var list = file?.Environments.Where(e => !string.IsNullOrWhiteSpace(e.Name)).ToList() ?? new();
            foreach (var profile in list)
                profile.Values = new Dictionary<string, string>(profile.Values, StringComparer.OrdinalIgnoreCase);
            if (list.Count > 0) return list;
            error = "environments.json has no environments; using built-in defaults.";
        }
        catch (Exception ex)
        {
            error = $"Couldn't read environments.json ({ex.Message}); using built-in defaults.";
        }

        var defaults = System.Text.Json.JsonSerializer.Deserialize<EnvironmentProfileFile>(DefaultFileContent, Json.Options)!.Environments;
        foreach (var profile in defaults)
            profile.Values = new Dictionary<string, string>(profile.Values, StringComparer.OrdinalIgnoreCase);
        return defaults;
    }

    // No keys or codes in here on purpose: this file is in Git. Anything secret goes into
    // %LOCALAPPDATA%\MicroserviceManager\environments.json on your own machine.
    public const string DefaultFileContent = """
{
  // Remote environments for the Angular app. Edit freely: the app re-reads this file every time it applies.
  //
  //  "rewrite": URL prefixes that are swapped in EVERY value of your environment.ts. Because this works on
  //             your real file, keys your team adds later are switched automatically.
  //  "values":  exact values for keys that don't follow the URL pattern.
  //
  // Keys not mentioned stay exactly as they are in your repo's environment.ts.
  // You can add more environments (e.g. "UAT2"); they appear as buttons in the app.
  "environments": [
    {
      "name": "QA",
      "rewrite": [
        { "from": "https://fre8tmsqa.com/uat1/", "to": "https://fre8tmsqa.com/frqa/" },
        { "from": "https://fre8tmsqa.com/uat3/", "to": "https://fre8tmsqa.com/frqa/" }
      ],
      "values": {
        "ApplicationName": "FRE8",
        "WorkingEnvironment": "qa",
        "newTabInitialUrl": "/frqa/fre8",
        "applicationLink": "https://fre8tmsqa.com/frqa/fre8/",
        "freightLogo": "http://52.235.39.94/fre8/assets/images/logos/Fre8-removebg-preview.png"
        // "SignalrAPI": your repo's environment.ts already has the QA SignalR URL (with its ?code=...).
        //               Only add it here if your repo's file points somewhere else.
      }
    },
    {
      // ⚠ UAT1 was guessed from UAT3 by swapping "uat3" for "uat1". Check these values once.
      "name": "UAT1",
      "rewrite": [
        { "from": "https://fre8tmsqa.com/frqa/", "to": "https://fre8tmsqa.com/uat1/" },
        { "from": "https://fre8tmsqa.com/uat3/", "to": "https://fre8tmsqa.com/uat1/" }
      ],
      "values": {
        "ApplicationName": "FRE8 UAT1",
        "WorkingEnvironment": "UAT",
        "newTabInitialUrl": "/uat1/home",
        "applicationLink": "https://fre8tmsqa.com/uat1/home/",
        "freightLogo": "https://fre8tmsqa.com/uat1/fre8/assets/images/logos/Fre8-removebg-preview.png",
        "SignalrAPI": "https://fre8tmsqa.com/uat1/SignalRService"
      }
    },
    {
      "name": "UAT3",
      "rewrite": [
        { "from": "https://fre8tmsqa.com/frqa/", "to": "https://fre8tmsqa.com/uat3/" },
        { "from": "https://fre8tmsqa.com/uat1/", "to": "https://fre8tmsqa.com/uat3/" }
      ],
      "values": {
        "ApplicationName": "FRE8 UAT3",
        "WorkingEnvironment": "UAT",
        "newTabInitialUrl": "/uat3/home",
        "applicationLink": "https://fre8tmsqa.com/uat3/home/",
        "freightLogo": "https://fre8tmsqa.com/uat3/fre8/assets/images/logos/Fre8-removebg-preview.png",
        "SignalrAPI": "https://fre8tmsqa.com/uat3/SignalRService"
      }
    }
  ]
}
""";
}
