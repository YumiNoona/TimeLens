using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TimeLens.Core.Interfaces;

namespace TimeLens.TrayApp.Services;

public sealed record CustomRule(string Pattern, string Category, string RuleType, string Target, int Priority);

public sealed class CategoryClassifier : ICategoryClassifier
{
    private static readonly Dictionary<string, string> BuiltInExeRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code.exe"] = "development",
        ["devenv.exe"] = "development",
        ["cursor.exe"] = "development",
        ["windsurf.exe"] = "development",
        ["notepad++.exe"] = "development",
        ["git-bash.exe"] = "development",
        ["powershell.exe"] = "development",
        ["cmd.exe"] = "development",
        ["windowsTerminal.exe"] = "development",
        ["slack.exe"] = "communication",
        ["discord.exe"] = "communication",
        ["teams.exe"] = "communication",
        ["zoom.exe"] = "communication",
        ["outlook.exe"] = "communication",
        ["wpp.exe"] = "communication",
        ["chrome.exe"] = "browsing",
        ["msedge.exe"] = "browsing",
        ["firefox.exe"] = "browsing",
        ["zen.exe"] = "browsing",
        ["brave.exe"] = "browsing",
        ["winword.exe"] = "documents",
        ["excel.exe"] = "documents",
        ["powerpnt.exe"] = "documents",
        ["notion.exe"] = "documents",
        ["obsidian.exe"] = "documents",
        ["spotify.exe"] = "media",
        ["vlc.exe"] = "media",
        ["mpc-hc.exe"] = "media",
        ["wmplayer.exe"] = "media",
        ["TimeLens.TrayApp.exe"] = "system",
        ["ShellExperienceHost.exe"] = "system",
        ["explorer.exe"] = "system",
        ["OpenCode.exe"] = "development",
    };

    private static readonly Dictionary<string, string> DomainRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["github.com"] = "development",
        ["gitlab.com"] = "development",
        ["stackoverflow.com"] = "development",
        ["docs.microsoft.com"] = "development",
        ["learn.microsoft.com"] = "development",
        ["developer.mozilla.org"] = "development",
        ["youtube.com"] = "media",
        ["netflix.com"] = "media",
        ["spotify.com"] = "media",
        ["twitch.tv"] = "media",
        ["slack.com"] = "communication",
        ["discord.com"] = "communication",
        ["teams.microsoft.com"] = "communication",
        ["zoom.us"] = "communication",
        ["google.com"] = "browsing",
        ["reddit.com"] = "social",
        ["twitter.com"] = "social",
        ["x.com"] = "social",
        ["linkedin.com"] = "social",
        ["instagram.com"] = "social",
        ["facebook.com"] = "social",
    };

    public List<CustomRule> CustomRules { get; } = new();

    public void AddCustomRule(string pattern, string category, string ruleType = "substring", string target = "exe", int priority = 0)
    {
        var existing = CustomRules.FindIndex(r => string.Equals(r.Pattern, pattern, StringComparison.OrdinalIgnoreCase));
        var rule = new CustomRule(pattern, category, ruleType, target, priority);
        if (existing >= 0)
            CustomRules[existing] = rule;
        else
            CustomRules.Add(rule);
    }

    public bool RemoveCustomRule(string pattern)
    {
        var idx = CustomRules.FindIndex(r => string.Equals(r.Pattern, pattern, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return false;
        CustomRules.RemoveAt(idx);
        return true;
    }

    public void LoadBuiltins(string csvPath)
    {
        if (!File.Exists(csvPath)) return;

        foreach (var line in File.ReadLines(csvPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

            var parts = trimmed.Split(',');
            if (parts.Length < 3) continue;

            var pattern  = parts[0].Trim();
            var target   = parts[1].Trim();
            var category = parts[2].Trim();
            var ruleType = parts.Length > 3 ? parts[3].Trim() : "substring";

            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(category)) continue;

            if (!CustomRules.Any(r => string.Equals(r.Pattern, pattern, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(r.Target, target, StringComparison.OrdinalIgnoreCase)))
            {
                CustomRules.Add(new CustomRule(pattern, category, ruleType, target, Priority: 100));
            }
        }
    }

    public string Classify(string exeName, string? windowTitle = null, string? domain = null)
    {
        // Custom rules first, ordered by priority (lower = higher priority)
        foreach (var rule in CustomRules.OrderBy(r => r.Priority))
        {
            var text = rule.Target switch
            {
                "title" => windowTitle ?? "",
                "domain" => domain ?? "",
                _ => exeName
            };
            if (string.IsNullOrEmpty(text)) continue;

            bool match = rule.RuleType switch
            {
                "glob" => GlobMatch(rule.Pattern, text),
                "regex" => RegexMatch(rule.Pattern, text),
                _ => text.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase)
            };
            if (match) return rule.Category.ToLowerInvariant();
        }

        if (domain is not null && DomainRules.TryGetValue(domain, out var domainCat))
            return domainCat.ToLowerInvariant();

        if (BuiltInExeRules.TryGetValue(exeName, out var exeCat))
            return exeCat.ToLowerInvariant();

        return "other";
    }

    private static bool GlobMatch(string pattern, string text)
    {
        // Convert glob pattern to regex — * matches any, ? matches single char
        var escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return Regex.IsMatch(text, $"^{escaped}$", RegexOptions.IgnoreCase);
    }

    private static bool RegexMatch(string pattern, string text)
    {
        try { return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase); }
        catch { return false; }
    }

    public static string? ExtractProject(string exeName, string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return null;
        var exe = Path.GetFileName(exeName).ToLowerInvariant();
        switch (exe)
        {
            case "code.exe":
            case "code - insiders.exe":
            case "cursor.exe":
            case "windsurf.exe":
                var parts = windowTitle.Split(" - ");
                return parts.Length >= 3 ? parts[^2].Trim() : null;
            case "rider64.exe":
            case "rider.exe":
                var riderParts = windowTitle.Split(" - ");
                return riderParts.Length >= 2 ? riderParts.Last().Trim() : null;
            case "devenv.exe":
                var devParts = windowTitle.Split(" - ");
                return devParts.Length >= 2 ? devParts[0].Trim() : null;
            default:
                return null;
        }
    }
}
