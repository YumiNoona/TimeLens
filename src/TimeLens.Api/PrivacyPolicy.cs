using System.Globalization;
using System.Text.RegularExpressions;

namespace TimeLens.Api;

/// <summary>Filters collection before it reaches any durable activity store.</summary>
public static class PrivacyPolicy
{
    // Support the reset line without admitting older pre-privacy companions.
    public static bool SupportsCompanionPrivacy(string? version) =>
        Version.TryParse(version, out var parsed) &&
        (parsed.Major == 2 || parsed >= new Version(8, 3, 0));
    public static bool HideBrowserTitles => Lines(LiveStatusStore.Settings.HiddenTitleApps).Any(app =>
        app.ToLowerInvariant() is "firefox.exe" or "chrome.exe" or "msedge.exe" or "brave.exe" or "zen.exe" or "floorp.exe" or "waterfox.exe" or "librewolf.exe" or "opera.exe" or "vivaldi.exe");

    public static void ValidateSetting(string name, string value)
    {
        if (value.Length > 8192 || Lines(value).Length > 100) throw new ArgumentException("Privacy rules are limited to 100 lines / 8192 characters.");
        if (name == "pauseUntil" && value != "" && (!DateTimeOffset.TryParse(value, out var until) || until > DateTimeOffset.UtcNow.AddDays(1)))
            throw new ArgumentException("Pause must end within 24 hours.");
        if (name == "ignoredDomains" && Lines(value).Any(line => Uri.CheckHostName(line.Trim('.')) == UriHostNameType.Unknown))
            throw new ArgumentException("Use hostnames, not URLs, for excluded sites.");
        if (name == "titleRedactionPatterns")
            foreach (var pattern in Lines(value))
                try { _ = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(10)); }
                catch (ArgumentException) { throw new ArgumentException("A title redaction pattern is not a valid regular expression."); }
    }
    private sealed record Rules(string Apps, string Titles, string Domains, string Patterns,
        HashSet<string> IgnoredApps, HashSet<string> HiddenApps, string[] IgnoredDomains, Regex[] Redactions);
    private static readonly object Gate = new();
    private static Rules? _rules;
    public static string[] Lines(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static bool IsPaused => DateTimeOffset.TryParse(LiveStatusStore.Settings.PauseUntil,
        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var until) && until > DateTimeOffset.UtcNow;
    private static Rules Current
    {
        get
        {
            var settings = LiveStatusStore.Settings;
            lock (Gate)
            {
                if (_rules is { } existing && existing.Apps == settings.IgnoredApps && existing.Titles == settings.HiddenTitleApps &&
                    existing.Domains == settings.IgnoredDomains && existing.Patterns == settings.TitleRedactionPatterns) return existing;
                var regexes = new List<Regex>();
                foreach (var pattern in Lines(settings.TitleRedactionPatterns))
                    try { regexes.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(10))); }
                    catch (ArgumentException) { regexes.Add(new Regex(".*", RegexOptions.None, TimeSpan.FromMilliseconds(10))); }
                return _rules = new(settings.IgnoredApps, settings.HiddenTitleApps, settings.IgnoredDomains, settings.TitleRedactionPatterns,
                    new(Lines(settings.IgnoredApps), StringComparer.OrdinalIgnoreCase),
                    new(Lines(settings.HiddenTitleApps), StringComparer.OrdinalIgnoreCase), Lines(settings.IgnoredDomains), regexes.ToArray());
            }
        }
    }
    public static bool IgnoreApp(string exe) => IsPaused || Current.IgnoredApps.Contains(Path.GetFileName(exe));
    public static bool IgnoreBrowser(string browser) => IsPaused || Current.IgnoredApps.Any(app => Services.BrowserTrackingService.MatchesForeground(browser, app));
    public static string BrowserTitle(string title, string browser) => Current.HiddenApps.Any(app => Services.BrowserTrackingService.MatchesForeground(browser, app)) ? "" : Title(title);
    public static bool AcceptObservation(DateTimeOffset observedAt) =>
        !DateTimeOffset.TryParse(LiveStatusStore.Settings.CollectionSince, out var since) || observedAt >= since;
    public static bool IgnoreDomain(string domain) => IsPaused || Current.IgnoredDomains.Any(item =>
        domain.Equals(item, StringComparison.OrdinalIgnoreCase) || domain.EndsWith("." + item, StringComparison.OrdinalIgnoreCase));
    public static string Title(string title, string? exe = null)
    {
        if (exe is not null && Current.IgnoredDomains.Length > 0 &&
            (exe.EndsWith("firefox.exe", StringComparison.OrdinalIgnoreCase) || exe.EndsWith("chrome.exe", StringComparison.OrdinalIgnoreCase) ||
             Services.BrowserTrackingService.MatchesForeground("firefox", exe) || Services.BrowserTrackingService.MatchesForeground("chrome", exe) ||
             Services.BrowserTrackingService.MatchesForeground("edge", exe) || Services.BrowserTrackingService.MatchesForeground("opera", exe))) return "";
        var rules = Current;
        if (exe is not null && rules.HiddenApps.Contains(Path.GetFileName(exe))) return "";
        foreach (var regex in rules.Redactions)
        {
            try { if (regex.IsMatch(title)) return ""; }
            catch (RegexMatchTimeoutException) { return ""; } // Fail closed for expensive sensitive-title patterns.
        }
        return title;
    }
}
