namespace TimeLens.Api;

/// <summary>Shared by tray and HTTP exit so saved passwords cannot lock an empty blocklist.</summary>
public static class BlockExitPolicy
{
    public static bool RequiresUnlock(AppSettings settings) =>
        settings.BlockProtectionEnabled && settings.BlockExitProtection &&
        (BlockEntryHelper.TryParseBlockEntries(settings.FocusBlocklist) ?? [])
            .Any(entry => !entry.IsExpired() && !BlockEntryHelper.IsProtected(entry.I) &&
                !BlockEntryHelper.IsUnsafeShellAction(entry, settings.BlockAction));
}
