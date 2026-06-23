using TimeLens.Core.Interfaces;

namespace TimeLens.TrayApp.Services;

public sealed class CategoryClassifier : ICategoryClassifier
{
    private static readonly Dictionary<string, string> ExeRules = new(StringComparer.OrdinalIgnoreCase)
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

    public string Classify(string exeName, string? windowTitle = null, string? domain = null)
    {
        if (domain is not null && DomainRules.TryGetValue(domain, out var domainCat))
            return domainCat;

        if (ExeRules.TryGetValue(exeName, out var exeCat))
            return exeCat;

        return "other";
    }
}
