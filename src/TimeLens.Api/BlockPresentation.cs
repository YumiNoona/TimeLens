namespace TimeLens.Api;

public readonly record struct BlockActionPlan(
    string Id,
    bool ShowNotification,
    bool MinimizeWindows,
    bool TerminateProcesses,
    bool RepeatEveryFiveSeconds)
{
    public static BlockActionPlan From(string? action) => action?.Trim().ToLowerInvariant() switch
    {
        "notify" => new("notify", true, false, false, false),
        "kill" => new("kill", true, false, true, false),
        "strict" => new("strict", true, true, true, true),
        _ => new("hide", true, true, false, false),
    };
}

public static class BlockEnforcement
{
    public static bool Apply(
        BlockActionPlan plan,
        bool targetDetected,
        Func<bool> minimizeWindows,
        Func<bool> terminateProcesses)
    {
        var intervened = plan.Id == "notify" && targetDetected;
        if (plan.MinimizeWindows) intervened |= minimizeWindows();
        if (plan.TerminateProcesses) intervened |= terminateProcesses();
        return intervened;
    }
}

public static class BlockTargetAction
{
    public static string? Normalize(string identifier, string? action)
    {
        var normalized = action?.Trim().ToLowerInvariant();
        if (identifier.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return normalized == "notify" ? "hide" :
                normalized is "hide" or "kill" or "strict" ? normalized : null;
        return normalized is "notify" or "strict" ? normalized : null;
    }

    public static string Resolve(string identifier, string? action, string? fallback)
    {
        var explicitAction = Normalize(identifier, action);
        if (explicitAction is not null) return explicitAction;
        var legacy = BlockActionPlan.From(fallback).Id;
        return identifier.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? legacy == "notify" ? "hide" : legacy
            : legacy == "notify" ? "notify" : "strict";
    }

    public static bool IsUnsafeShellAction(string identifier, string? action, string? fallback)
        => string.Equals(identifier, "explorer.exe", StringComparison.OrdinalIgnoreCase) &&
           Resolve(identifier, action, fallback) is "kill" or "strict";
}

public static class BlockNotification
{
    public const string DefaultTitle = "Focus Mode";
    public const string DefaultMessage = "'{target}' is blocked — get back to work!";
    public const int MaxTitleLength = 60;
    public const int MaxMessageLength = 240;

    public static string NormalizeTitle(string? value) =>
        Normalize(value, DefaultTitle, MaxTitleLength);

    public static string NormalizeMessage(string? value) =>
        Normalize(value, DefaultMessage, MaxMessageLength);

    public static string Format(string template, string target, string action)
    {
        var normalizedTemplate = NormalizeMessage(template);
        return ReplacePlaceholders(normalizedTemplate, target, action);
    }

    public static string FormatTitle(string template, string target, string action) =>
        ReplacePlaceholders(NormalizeTitle(template), target, action);

    private static string ReplacePlaceholders(string template, string target, string action) =>
        template
            .Replace("{target}", target, StringComparison.OrdinalIgnoreCase)
            .Replace("{mode}", BlockActionPlan.From(action).Id, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value, string fallback, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        var cleaned = string.Join(" ", value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length <= maxLength) return cleaned;
        var length = maxLength;
        if (length > 0 && char.IsHighSurrogate(cleaned[length - 1])) length--;
        return cleaned[..length];
    }
}
