using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TimeLens.Core.Interfaces;

namespace TimeLens.TrayApp.Services;

public sealed record CustomRule(string Pattern, string Category, string RuleType, string Target, int Priority);

public sealed class CategoryClassifier : ICategoryClassifier
{
    // These processes own the desktop, taskbar, Start menu, lock screen, and other
    // Windows surfaces. They are foreground noise rather than meaningful activity.
    // Keep them out of focus totals even when an older custom rule categorised them.
    private static readonly HashSet<string> SystemForegroundApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "shellexperiencehost.exe", "shellhost.exe", "searchhost.exe",
        "searchapp.exe", "startmenuexperiencehost.exe", "textinputhost.exe",
        "applicationframehost.exe", "lockapp.exe", "dwm.exe", "sihost.exe",
        "taskhostw.exe", "runtimebroker.exe", "widgets.exe", "widgetservice.exe",
        "ctfmon.exe", "audiodg.exe", "fontdrvhost.exe", "timelens.exe",
        "timelens.trayapp.exe"
    };

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
        ["figma.exe"] = "design",
        ["TwinmotionCookedEditor-Win64-Shipping.exe"] = "design",
        ["twinmotion.exe"] = "design",
        ["blender.exe"] = "design",
        ["unrealeditor.exe"] = "development",
        ["godot.exe"] = "development",
        ["unity.exe"] = "development",
        ["chatgpt.exe"] = "development",
        ["claude.exe"] = "development",
        ["r5apex_dx12.exe"] = "gaming",
        ["r5apex.exe"] = "gaming",
        ["valorant.exe"] = "gaming",
        ["gta5.exe"] = "gaming",
        ["steam.exe"] = "gaming",
        ["steamwebhelper.exe"] = "gaming",
        ["armourycrate.exe"] = "utilities",
        ["nvidia overlay.exe"] = "utilities",
        ["onedrive.exe"] = "work",
        ["snippingtool.exe"] = "utilities",
        ["photos.exe"] = "utilities",
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

    private readonly object _rulesGate = new();
    private readonly List<CustomRule> _customRules = new();
    public IReadOnlyList<CustomRule> CustomRules
    {
        get { lock (_rulesGate) return _customRules.ToArray(); }
    }

    public void AddCustomRule(string pattern, string category, string ruleType = "substring", string target = "exe", int priority = 0)
    {
        lock (_rulesGate)
        {
            var existing = _customRules.FindIndex(r => string.Equals(r.Pattern, pattern, StringComparison.OrdinalIgnoreCase));
            var rule = new CustomRule(pattern, category, ruleType, target, priority);
            if (existing >= 0) _customRules[existing] = rule;
            else _customRules.Add(rule);
        }
    }

    public bool RemoveCustomRule(string pattern)
    {
        lock (_rulesGate)
        {
            var idx = _customRules.FindIndex(r => string.Equals(r.Pattern, pattern, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;
            _customRules.RemoveAt(idx);
            return true;
        }
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

            lock (_rulesGate)
            {
                if (!_customRules.Any(r => string.Equals(r.Pattern, pattern, StringComparison.OrdinalIgnoreCase)
                                           && string.Equals(r.Target, target, StringComparison.OrdinalIgnoreCase)))
                    _customRules.Add(new CustomRule(pattern, category, ruleType, target, Priority: 100));
            }
        }
    }

    public string Classify(string exeName, string? windowTitle = null, string? domain = null)
    {
        if (SystemForegroundApps.Contains(Path.GetFileName(exeName)))
            return "system";

        // Custom rules first, ordered by priority (lower = higher priority)
        CustomRule[] rules;
        lock (_rulesGate) rules = _customRules.OrderBy(r => r.Priority).ToArray();
        foreach (var rule in rules)
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
        try { return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50)); }
        catch (ArgumentException) { return false; }
        catch (RegexMatchTimeoutException) { return false; }
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
