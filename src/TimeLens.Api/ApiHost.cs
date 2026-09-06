using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json.Serialization;
using TimeLens.Api.Dtos;
using TimeLens.Api.Services;

namespace TimeLens.Api;

public static class ApiHost
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public const int DefaultPort = 47821;
    private const int MaxBlockImageBytes = 4 * 1024 * 1024;
    private const int MaxBlockVideoBytes = 8 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, long> OpenBrowserEvents = new(StringComparer.OrdinalIgnoreCase);

    private static string TabKey(string browser, int tabId) => $"{browser}:{tabId}";
    private static readonly ConcurrentDictionary<string, byte[]> IconCache = new(StringComparer.OrdinalIgnoreCase);

    private static bool IsTrackedBrowserForeground(string? browser)
    {
        var exe = Path.GetFileName(LiveStatusStore.CurrentApp ?? string.Empty).ToLowerInvariant();
        if (string.Equals(browser, "firefox", StringComparison.OrdinalIgnoreCase))
            return exe is "firefox.exe" or "zen.exe" or "floorp.exe" or "waterfox.exe" or "librewolf.exe";
        return exe is "chrome.exe" or "msedge.exe" or "microsoftedge.exe" or "brave.exe" or
            "opera.exe" or "vivaldi.exe" or "arc.exe" or "thorium.exe";
    }

    private static string NormalizeNotifyPosition(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "top-left" => "top-left",
        "top-right" => "top-right",
        "bottom-right" or "right" => "bottom-right",
        _ => "bottom-left"
    };

    private static string NormalizeMediaLayout(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "thumbnail" => "thumbnail",
        "banner" => "banner",
        _ => "large"
    };

    private static bool HasBlockUnlock(HttpContext ctx) =>
        BlockProtectionService.IsAuthorized(ctx.Request.Headers["X-TimeLens-Unlock"].FirstOrDefault());

    private static int BlockActionStrength(string action) => action switch
    {
        "strict" => 3,
        "kill" => 2,
        "hide" => 1,
        _ => 0
    };

    private static BrowserBlockResponseDto BrowserBlockResponse(string? host)
    {
        if (!LiveStatusStore.Settings.FocusMode || string.IsNullOrWhiteSpace(host))
            return new(true, false, "none", null);

        var entry = (BlockEntryHelper.TryParseBlockEntries(LiveStatusStore.Settings.FocusBlocklist) ?? [])
            .FirstOrDefault(item => !item.IsExpired() && BlockEntryHelper.MatchesDomain(item, host));
        if (entry is null) return new(true, false, "none", null);

        var action = BlockEntryHelper.ActionFor(entry, LiveStatusStore.Settings.BlockAction);
        if (action != "notify") action = "strict";
        var target = entry.I;
        var settings = LiveStatusStore.Settings;
        var mediaUrl = string.IsNullOrEmpty(settings.BlockImageVersion)
            ? null
            : $"http://127.0.0.1:{DefaultPort}/api/block/media?v={Uri.EscapeDataString(settings.BlockImageVersion)}";
        var presentation = new BrowserBlockPresentationDto(
            target,
            BlockNotification.FormatTitle(settings.BlockTitle, target, action),
            BlockNotification.Format(settings.BlockMessage, target, action),
            mediaUrl,
            mediaUrl,
            settings.BlockMediaType,
            Math.Clamp(settings.BlockNotifyIntervalSeconds, 5, 86400),
            NormalizeNotifyPosition(settings.BlockNotifyPosition),
            NormalizeMediaLayout(settings.BlockMediaLayout),
            action == "notify",
            "browser");
        return new(true, action == "strict", action, presentation);
    }

    private static bool IsPasswordProtected(BlockEntry entry, string fallback, AppSettings settings) =>
        settings.BlockProtectionScope == "all" ||
        BlockEntryHelper.ActionFor(entry, fallback) == "strict";

    private static bool HasPasswordProtectedTarget(AppSettings settings) =>
        (BlockEntryHelper.TryParseBlockEntries(settings.FocusBlocklist) ?? [])
            .Any(entry => !entry.IsExpired() && IsPasswordProtected(entry, settings.BlockAction, settings));

    private static bool WeakensBlocklist(string currentJson, string nextJson)
    {
        var current = BlockEntryHelper.TryParseBlockEntries(currentJson) ?? [];
        var next = BlockEntryHelper.TryParseBlockEntries(nextJson) ?? [];
        var nextById = next.ToDictionary(entry => entry.I, StringComparer.OrdinalIgnoreCase);
        var settings = LiveStatusStore.Settings;

        foreach (var existing in current.Where(entry => !entry.IsExpired() && IsPasswordProtected(entry, settings.BlockAction, settings)))
        {
            if (!nextById.TryGetValue(existing.I, out var replacement)) return true;
            if (existing.M == "u" && replacement.M != "u") return true;
            if (existing.M == "t" && replacement.M == "t" &&
                DateTime.TryParse(existing.E, null, DateTimeStyles.RoundtripKind, out var existingExpiry) &&
                DateTime.TryParse(replacement.E, null, DateTimeStyles.RoundtripKind, out var replacementExpiry) &&
                replacementExpiry < existingExpiry) return true;
            var fallback = settings.BlockAction;
            if (BlockActionStrength(BlockEntryHelper.ActionFor(replacement, fallback)) <
                BlockActionStrength(BlockEntryHelper.ActionFor(existing, fallback))) return true;
        }
        return false;
    }

    private static bool RequiresBlockUnlock(string name, string value) => name switch
    {
        "focusMode" => LiveStatusStore.Settings.FocusMode && value == "false" && HasPasswordProtectedTarget(LiveStatusStore.Settings),
        "blockAction" => BlockActionStrength(value) < BlockActionStrength(LiveStatusStore.Settings.BlockAction) && HasPasswordProtectedTarget(LiveStatusStore.Settings),
        "focusBlocklist" => WeakensBlocklist(LiveStatusStore.Settings.FocusBlocklist, value),
        _ => false
    };

    private static readonly HashSet<string> InfrastructureExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost", "TextInputHost", "SystemSettings", "RuntimeBroker",
        "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost", "ctfmon",
        "conhost", "fontdrvhost",
        "svchost", "dwm", "csrss", "smss", "wininit", "winlogon", "services",
        "lsass", "spoolsv", "taskhostw", "sihost",
        "TimeLens.TrayApp", "TimeLens", "NVDisplay.Container", "NVIDIA Share", "nvsphelper64",
        "steamwebhelper", "SteamWebHelper", "SteamService", "SteamClientBootstrapper",
    };
    public static DateTime LastActivityUtc { get; private set; } = DateTime.MinValue;

    [SupportedOSPlatform("windows6.1")]
    public static async Task StartAsync(string dbPath, CancellationToken ct = default,
        Action<string, string>? saveSetting = null,
        Action<bool>? setTrackAudio = null,
        Action<bool>? setTrackInput = null,
        Action<string, string, string, string, int>? upsertRule = null,
        Action<string>? deleteRule = null,
        Func<string, bool>? enforceBlock = null,
        Action<string>? showBlockPreview = null,
        Action<string, string>? recordBlockAttempt = null,
        UpdateService? updateService = null,
        Action? requestShutdown = null)
    {
        var dashboardPath = Path.Combine(
            AppContext.BaseDirectory, "dashboard");

        var analytics = new AnalyticsService(dbPath);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            // Shortcuts and Windows startup need not use the installation folder as CWD.
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseUrls($"http://127.0.0.1:{DefaultPort}");

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
        });

        var app = builder.Build();

        // The SPA entry point must never be reused across application upgrades. Embedded
        // files otherwise share a stable timestamp, so a browser can receive a 304 for an
        // old index.html and keep loading the previous hashed JavaScript bundle.
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path;
            var isDashboardEntry = path == "/" || path == "/index.html" ||
                (!Path.HasExtension(path.Value) &&
                 !path.StartsWithSegments("/api") &&
                 !path.StartsWithSegments("/extension"));
            var mustBeFresh = isDashboardEntry;

            if (mustBeFresh)
            {
                ctx.Request.Headers.Remove("If-None-Match");
                ctx.Request.Headers.Remove("If-Modified-Since");
                ctx.Response.OnStarting(() =>
                {
                    ctx.Response.Headers.CacheControl = "no-store, no-cache, max-age=0, must-revalidate";
                    ctx.Response.Headers.Pragma = "no-cache";
                    ctx.Response.Headers.Expires = "0";
                    ctx.Response.Headers.Remove("ETag");
                    ctx.Response.Headers.Remove("Last-Modified");
                    return Task.CompletedTask;
                });
            }

            await next();
        });

        app.Use(async (ctx, next) =>
        {
            try
            {
                LastActivityUtc = DateTime.UtcNow;
                var origin = ctx.Request.Headers.Origin.ToString();
                if (origin.StartsWith("chrome-extension://") ||
                    origin.StartsWith("moz-extension://"))
                {
                    ctx.Response.Headers.Append("Access-Control-Allow-Origin", origin);
                    ctx.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type");
                    ctx.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                }
                if (ctx.Request.Method == "OPTIONS")
                {
                    ctx.Response.StatusCode = 204;
                    return;
                }
                await next();
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(
                    Path.Combine(Path.GetDirectoryName(dbPath)!, "api_error.log"),
                    $"{DateTime.UtcNow:o} {ctx.Request.Method} {ctx.Request.Path}: {ex}{Environment.NewLine}");
                ctx.Response.StatusCode = 500;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync($"{{\"error\":\"{ex.Message.Replace("\"", "'")}\"}}");
            }
        });

        // Try embedded dashboard first (single-file deployment), fall back to physical folder
        var entryAsm = Assembly.GetEntryAssembly();
        StaticFileOptions? staticOpts = null;

        if (entryAsm is not null)
        {
            var embedded = new EmbeddedDashboardProvider(entryAsm);
            if (embedded.GetFileInfo("index.html").Exists)
            {
                staticOpts = new StaticFileOptions
                {
                    FileProvider = embedded,
                    OnPrepareResponse = context => SetStaticAssetCacheHeaders(context.Context)
                };
            }
        }

        if (staticOpts is null && Directory.Exists(dashboardPath))
        {
            staticOpts = new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(dashboardPath),
                OnPrepareResponse = context => SetStaticAssetCacheHeaders(context.Context)
            };
        }

        if (staticOpts is not null)
        {
            app.UseStaticFiles(staticOpts);
            app.MapFallbackToFile("index.html", staticOpts);
        }

        app.MapGet("/api/settings", async (HttpContext ctx) =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(
                LiveStatusStore.Settings, AppJsonContext.Default.AppSettings);
        });

        app.MapGet("/api/update/status", async (HttpContext ctx) =>
        {
            if (updateService is null)
            {
                ctx.Response.StatusCode = 503;
                return;
            }
            var status = await updateService.CheckAsync(ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(status, AppJsonContext.Default.UpdateStatusDto);
        });

        app.MapPost("/api/update/install", async (HttpContext ctx) =>
        {
            if (updateService is null)
            {
                ctx.Response.StatusCode = 503;
                return;
            }
            if (!string.Equals(ctx.Request.Headers["X-TimeLens-Update"].FirstOrDefault(), "install", StringComparison.Ordinal))
            {
                ctx.Response.StatusCode = 403;
                return;
            }

            var status = await updateService.DownloadAndStageAsync(ctx.RequestAborted);
            if (status.Error is not null) ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
            await ctx.Response.WriteAsJsonAsync(status, AppJsonContext.Default.UpdateStatusDto);
            if (status.Restarting && requestShutdown is not null)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(750);
                    requestShutdown();
                });
            }
        });

        app.MapPost("/api/app/exit", async (HttpContext ctx) =>
        {
            if (BlockProtectionService.IsEnabled(dbPath) && LiveStatusStore.Settings.BlockExitProtection && !HasBlockUnlock(ctx))
            {
                ctx.Response.StatusCode = StatusCodes.Status423Locked;
                await ctx.Response.WriteAsync("{\"error\":\"Password required to exit while blocks are protected\",\"code\":\"block_locked\"}");
                return;
            }

            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
            if (requestShutdown is not null)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(250);
                    requestShutdown();
                });
            }
        });

        app.MapPost("/api/block/protection/setup", async (HttpContext ctx) =>
        {
            if (BlockProtectionService.IsEnabled(dbPath))
            {
                ctx.Response.StatusCode = 409;
                await ctx.Response.WriteAsync("{\"error\":\"Block protection is already enabled\"}");
                return;
            }
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
            var password = doc.RootElement.TryGetProperty("password", out var prop) ? prop.GetString() ?? "" : "";
            try
            {
                BlockProtectionService.SetPassword(dbPath, password);
                LiveStatusStore.Settings = LiveStatusStore.Settings with { BlockProtectionEnabled = true };
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"ok\":true}");
            }
            catch (ArgumentException ex)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"{{\"error\":\"{ex.Message}\"}}");
            }
        });

        app.MapPost("/api/block/protection/unlock", async (HttpContext ctx) =>
        {
            if (!BlockProtectionService.IsEnabled(dbPath))
            {
                ctx.Response.StatusCode = 409;
                await ctx.Response.WriteAsync("{\"error\":\"Block protection is not enabled\"}");
                return;
            }
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
            var password = doc.RootElement.TryGetProperty("password", out var prop) ? prop.GetString() ?? "" : "";
            var token = BlockProtectionService.TryUnlock(dbPath, password, out var retryAfter);
            if (token is null)
            {
                ctx.Response.StatusCode = retryAfter > 0 ? 429 : 401;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(retryAfter > 0
                    ? $"{{\"error\":\"Too many attempts. Try again in {retryAfter} seconds\",\"retryAfterSeconds\":{retryAfter}}}"
                    : "{\"error\":\"Incorrect password\"}");
                return;
            }
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync($"{{\"token\":\"{token}\",\"expiresInSeconds\":300}}");
        });

        app.MapPost("/api/block/protection/change", async (HttpContext ctx) =>
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
            var currentPassword = doc.RootElement.TryGetProperty("currentPassword", out var currentProp) ? currentProp.GetString() ?? "" : "";
            var newPassword = doc.RootElement.TryGetProperty("newPassword", out var newProp) ? newProp.GetString() ?? "" : "";
            var verified = BlockProtectionService.TryUnlock(dbPath, currentPassword, out var retryAfter);
            if (verified is null)
            {
                ctx.Response.StatusCode = retryAfter > 0 ? 429 : 401;
                await ctx.Response.WriteAsync("{\"error\":\"Current password is incorrect\"}");
                return;
            }
            try
            {
                BlockProtectionService.SetPassword(dbPath, newPassword);
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"ok\":true}");
            }
            catch (ArgumentException ex)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync($"{{\"error\":\"{ex.Message}\"}}");
            }
        });

        app.MapPost("/api/block/protection/disable", async (HttpContext ctx) =>
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
            var password = doc.RootElement.TryGetProperty("password", out var prop) ? prop.GetString() ?? "" : "";
            var verified = BlockProtectionService.TryUnlock(dbPath, password, out var retryAfter);
            if (verified is null)
            {
                ctx.Response.StatusCode = retryAfter > 0 ? 429 : 401;
                await ctx.Response.WriteAsync("{\"error\":\"Password is incorrect\"}");
                return;
            }
            BlockProtectionService.Disable(dbPath);
            LiveStatusStore.Settings = LiveStatusStore.Settings with { BlockProtectionEnabled = false };
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapPost("/api/settings", async (HttpContext ctx) =>
        {
            using var sr = new System.IO.StreamReader(ctx.Request.Body);
            var body = await sr.ReadToEndAsync();
            var doc = System.Text.Json.JsonDocument.Parse(body);

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var value = prop.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.True => "true",
                    System.Text.Json.JsonValueKind.False => "false",
                    System.Text.Json.JsonValueKind.String => prop.Value.GetString() ?? "",
                    _ => prop.Value.GetRawText()
                };

                if (prop.Name == "blockAction" && value is not ("notify" or "hide" or "kill" or "strict"))
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("{\"error\":\"Invalid block action\"}");
                    return;
                }
                if (prop.Name == "focusMode" && prop.Value.ValueKind is not
                    (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("{\"error\":\"focusMode must be a boolean\"}");
                    return;
                }
                if (prop.Name is "blockTitle" or "blockMessage")
                {
                    if (prop.Value.ValueKind != System.Text.Json.JsonValueKind.String)
                    {
                        ctx.Response.StatusCode = 400;
                        await ctx.Response.WriteAsync("{\"error\":\"Block notification text must be a string\"}");
                        return;
                    }
                    var limit = prop.Name == "blockTitle"
                        ? BlockNotification.MaxTitleLength
                        : BlockNotification.MaxMessageLength;
                    if (value.Length > limit)
                    {
                        ctx.Response.StatusCode = 400;
                        await ctx.Response.WriteAsync($"{{\"error\":\"{prop.Name} must be {limit} characters or fewer\"}}");
                        return;
                    }
                    value = prop.Name == "blockTitle"
                        ? BlockNotification.NormalizeTitle(value)
                        : BlockNotification.NormalizeMessage(value);
                }
                if (prop.Name == "blockNotifyIntervalSeconds" &&
                    (!int.TryParse(value, out var notifyInterval) || notifyInterval < 5 || notifyInterval > 86400))
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("{\"error\":\"Reminder interval must be between 5 seconds and 24 hours\"}");
                    return;
                }
                if (prop.Name == "blockNotifyPosition")
                {
                    value = value switch { "left" => "bottom-left", "right" => "bottom-right", _ => value };
                    if (value is not ("top-left" or "top-right" or "bottom-left" or "bottom-right"))
                    {
                        ctx.Response.StatusCode = 400;
                        await ctx.Response.WriteAsync("{\"error\":\"Invalid reminder position\"}");
                        return;
                    }
                }
                if (prop.Name == "blockMediaLayout" && value is not ("thumbnail" or "large" or "banner"))
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("{\"error\":\"Invalid reminder media size\"}");
                    return;
                }
                if (prop.Name == "autoStart" && prop.Value.ValueKind is not
                    (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("{\"error\":\"autoStart must be a boolean\"}");
                    return;
                }
                if (prop.Name == "defaultView" && value is not ("today" or "history" or "apps" or "browser" or "timeline" or "block" or "rules" or "settings"))
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("{\"error\":\"Invalid default view\"}");
                    return;
                }
                if (prop.Name == "density" && value is not ("comfortable" or "compact"))
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("{\"error\":\"Invalid interface density\"}");
                    return;
                }
                if (prop.Name == "motionEnabled" && prop.Value.ValueKind is not
                    (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("{\"error\":\"motionEnabled must be a boolean\"}");
                    return;
                }
                if (prop.Name == "timelineMinSegmentSeconds" && value is not ("30" or "60" or "120" or "300"))
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("{\"error\":\"Invalid timeline threshold\"}");
                    return;
                }
                if (prop.Name == "heatmapDays" && value is not ("28" or "91" or "273" or "365"))
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("{\"error\":\"Invalid heatmap range\"}");
                    return;
                }
                if (prop.Name == "focusBlocklist")
                {
                    var entries = BlockEntryHelper.TryParseBlockEntries(value) ?? [];
                    if (value != "[]" && entries.Length == 0)
                    {
                        ctx.Response.StatusCode = 400;
                        await ctx.Response.WriteAsync("{\"error\":\"Invalid blocklist\"}");
                        return;
                    }
                    if (entries.Any(entry => BlockEntryHelper.IsProtected(entry.I) ||
                        BlockEntryHelper.IsUnsafeShellAction(entry, LiveStatusStore.Settings.BlockAction)))
                    {
                        ctx.Response.StatusCode = 400;
                        await ctx.Response.WriteAsync("{\"error\":\"Critical Windows targets cannot be blocked; File Explorer supports Hide only\"}");
                        return;
                    }
                    value = BlockEntryHelper.Serialize(entries);
                }
                if (prop.Name == "blockProtectionEnabled")
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsync("{\"error\":\"Use the block protection endpoints\"}");
                    return;
                }
                if (LiveStatusStore.Settings.BlockProtectionEnabled && RequiresBlockUnlock(prop.Name, value) && !HasBlockUnlock(ctx))
                {
                    ctx.Response.StatusCode = 423;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"error\":\"Password required to weaken protected blocks\",\"code\":\"block_locked\"}");
                    return;
                }
                saveSetting?.Invoke(prop.Name switch
                {
                    "trackAudio" => "track_audio",
                    "trackBrowser" => "track_browser",
                    "trackInput" => "track_input",
                    "idleThresholdSeconds" => "idle_threshold_seconds",
                    "theme" => "theme",
                    "timelineGrouped" => "timeline_grouped",
                    "autoStart" => "auto_start",
                    "retentionDays" => "retention_days",
                    "showTitles" => "show_titles",
                    "breakReminder" => "break_reminder",
                    "breakIntervalMinutes" => "break_interval_minutes",
                    "focusMode" => "focus_mode",
                    "focusBlocklist" => "focus_blocklist",
                    "blockAction" => "block_action",
                    "blockTitle" => "block_title",
                    "blockMessage" => "block_message",
                    "blockNotifyIntervalSeconds" => "block_notify_interval_seconds",
                    "blockNotifyPosition" => "block_notify_position",
                    "blockMediaLayout" => "block_media_layout",
                    "pollIntervalSeconds" => "poll_interval_seconds",
                    "timeFormat" => "time_format",
                    "defaultView" => "default_view",
                    "density" => "density",
                    "motionEnabled" => "motion_enabled",
                    "timelineMinSegmentSeconds" => "timeline_min_segment_seconds",
                    "heatmapDays" => "heatmap_days",
                    _ => prop.Name
                }, value);

                // Apply live toggles
                switch (prop.Name)
                {
                    case "trackAudio":
                        setTrackAudio?.Invoke(value == "true");
                        break;
                    case "trackInput":
                        setTrackInput?.Invoke(value == "true");
                        break;
                    case "trackBrowser":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with
                        {
                            TrackBrowser = value == "true"
                        };
                        break;
                    case "idleThresholdSeconds":
                        if (int.TryParse(value, out var secs))
                            LiveStatusStore.Settings = LiveStatusStore.Settings with
                            {
                                IdleThresholdSeconds = secs
                            };
                        break;
                    case "theme":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with
                        {
                            Theme = value
                        };
                        break;
                    case "timelineGrouped":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with
                        {
                            TimelineGrouped = value == "true"
                        };
                        break;
                    case "autoStart":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with { AutoStart = value == "true" };
                        break;
                    case "retentionDays":
                        if (int.TryParse(value, out var days))
                            LiveStatusStore.Settings = LiveStatusStore.Settings with
                            {
                                RetentionDays = days
                            };
                        break;
                    case "showTitles":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with { ShowTitles = value == "true" };
                        break;
                    case "breakReminder":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with { BreakReminder = value == "true" };
                        break;
                    case "breakIntervalMinutes":
                        if (int.TryParse(value, out var bim))
                            LiveStatusStore.Settings = LiveStatusStore.Settings with { BreakIntervalMinutes = bim };
                        break;
                    case "focusMode":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with { FocusMode = value == "true" };
                        break;
                    case "focusBlocklist":
                        // Handled by Program.cs saveSetting callback
                        break;
                    case "blockAction":
                        // Handled by Program.cs saveSetting callback
                        break;
                    case "blockNotifyIntervalSeconds":
                        if (int.TryParse(value, out var blockNotifyIntervalSeconds))
                            LiveStatusStore.Settings = LiveStatusStore.Settings with { BlockNotifyIntervalSeconds = blockNotifyIntervalSeconds };
                        break;
                    case "blockNotifyPosition":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with { BlockNotifyPosition = value };
                        break;
                    case "blockMediaLayout":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with { BlockMediaLayout = value };
                        break;
                    case "timeFormat":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with { TimeFormat = value };
                        break;
                    case "pollIntervalSeconds":
                        if (int.TryParse(value, out var pis))
                            LiveStatusStore.Settings = LiveStatusStore.Settings with { PollIntervalSeconds = pis };
                        break;
                    case "defaultView":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with { DefaultView = value };
                        break;
                    case "density":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with { Density = value };
                        break;
                    case "motionEnabled":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with { MotionEnabled = value == "true" };
                        break;
                    case "timelineMinSegmentSeconds":
                        if (int.TryParse(value, out var tmss))
                            LiveStatusStore.Settings = LiveStatusStore.Settings with { TimelineMinSegmentSeconds = tmss };
                        break;
                    case "heatmapDays":
                        if (int.TryParse(value, out var hd))
                            LiveStatusStore.Settings = LiveStatusStore.Settings with { HeatmapDays = hd };
                        break;
                }
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapGet("/api/rules", async (HttpContext ctx) =>
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT exe_pattern, category, rule_type, target, priority, id FROM custom_rules ORDER BY priority, id";
            using var reader = await cmd.ExecuteReaderAsync();
            using var arr = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
            arr.WriteStartArray();
            while (await reader.ReadAsync())
            {
                arr.WriteStartObject();
                arr.WriteString("pattern", reader.GetString(0));
                arr.WriteString("category", reader.GetString(1));
                arr.WriteString("ruleType", reader.IsDBNull(2) ? "substring" : reader.GetString(2));
                arr.WriteString("target", reader.IsDBNull(3) ? "exe" : reader.GetString(3));
                arr.WriteNumber("priority", reader.IsDBNull(4) ? 0 : reader.GetInt32(4));
                arr.WriteNumber("id", reader.GetInt32(5));
                arr.WriteEndObject();
            }
            arr.WriteEndArray();
            await arr.FlushAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
        });

        app.MapPost("/api/rules", async (HttpContext ctx) =>
        {
            using var sr = new System.IO.StreamReader(ctx.Request.Body);
            var body = await sr.ReadToEndAsync();
            var doc = System.Text.Json.JsonDocument.Parse(body);
            var pattern = doc.RootElement.GetProperty("pattern").GetString() ?? "";
            var category = doc.RootElement.GetProperty("category").GetString() ?? "other";
            var ruleType = doc.RootElement.TryGetProperty("ruleType", out var rt) ? rt.GetString() ?? "substring" : "substring";
            var target = doc.RootElement.TryGetProperty("target", out var tg) ? tg.GetString() ?? "exe" : "exe";
            var priority = doc.RootElement.TryGetProperty("priority", out var pr) && pr.TryGetInt32(out var pv) ? pv : 0;

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            // If updating an existing rule, preserve its id
            var existingId = 0;
            using (var findCmd = conn.CreateCommand())
            {
                findCmd.CommandText = "SELECT id FROM custom_rules WHERE exe_pattern = $pattern";
                findCmd.Parameters.AddWithValue("$pattern", pattern);
                var res = await findCmd.ExecuteScalarAsync();
                if (res is not null) existingId = (int)(long)res;
            }

            if (existingId > 0)
            {
                using var updCmd = conn.CreateCommand();
                updCmd.CommandText = "UPDATE custom_rules SET category=$cat, rule_type=$rt, target=$tg, priority=$pri WHERE id=$id";
                updCmd.Parameters.AddWithValue("$cat", category);
                updCmd.Parameters.AddWithValue("$rt", ruleType);
                updCmd.Parameters.AddWithValue("$tg", target);
                updCmd.Parameters.AddWithValue("$pri", priority);
                updCmd.Parameters.AddWithValue("$id", existingId);
                await updCmd.ExecuteNonQueryAsync();
            }
            else
            {
                using var insCmd = conn.CreateCommand();
                insCmd.CommandText = "INSERT INTO custom_rules (exe_pattern, category, rule_type, target, priority) VALUES ($pattern, $cat, $rt, $tg, $pri)";
                insCmd.Parameters.AddWithValue("$pattern", pattern);
                insCmd.Parameters.AddWithValue("$cat", category);
                insCmd.Parameters.AddWithValue("$rt", ruleType);
                insCmd.Parameters.AddWithValue("$tg", target);
                insCmd.Parameters.AddWithValue("$pri", priority);
                await insCmd.ExecuteNonQueryAsync();
            }

            upsertRule?.Invoke(pattern, category, ruleType, target, priority);

            // Backfill existing uncategorized events matching this pattern
            using (var bfCmd = conn.CreateCommand())
            {
                bfCmd.CommandText = """
                    UPDATE app_events SET category = $cat
                    WHERE (category = 'other' OR category IS NULL)
                      AND session_state = 'active'
                      AND exe_name = $pattern
                    """;
                bfCmd.Parameters.AddWithValue("$cat", category);
                bfCmd.Parameters.AddWithValue("$pattern", pattern);
                await bfCmd.ExecuteNonQueryAsync();
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapDelete("/api/rules/{pattern}", async (HttpContext ctx) =>
        {
            var pattern = ctx.Request.RouteValues["pattern"] as string ?? "";

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM custom_rules WHERE exe_pattern = $pattern";
            cmd.Parameters.AddWithValue("$pattern", pattern);
            await cmd.ExecuteNonQueryAsync();

            deleteRule?.Invoke(pattern);

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapPut("/api/rules/reorder", async (HttpContext ctx) =>
        {
            using var sr = new System.IO.StreamReader(ctx.Request.Body);
            var body = await sr.ReadToEndAsync();
            var doc = System.Text.Json.JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("ids", out var arr))
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
                await conn.OpenAsync();
                var idx = 0;
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.TryGetInt32(out var id))
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "UPDATE custom_rules SET priority = $pri WHERE id = $id";
                        cmd.Parameters.AddWithValue("$pri", idx++);
                        cmd.Parameters.AddWithValue("$id", id);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        }        );

        app.MapGet("/api/uncategorized", async (HttpContext ctx) =>
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT exe_name,
                       COALESCE(SUM(
                           MAX(0, MIN(julianday(COALESCE(end_time, $now)), julianday($now)) - MAX(julianday(start_time), julianday($today))) * 86400
                       ), 0) AS secs
                FROM app_events
                WHERE (category = 'other' OR category IS NULL)
                  AND session_state = 'active'
                  AND julianday(start_time) < julianday($now) AND julianday(COALESCE(end_time, $now)) > julianday($today)
                GROUP BY exe_name
                HAVING secs > 60
                ORDER BY secs DESC
                LIMIT 30
                """;
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$today", DateTime.Today.ToUniversalTime().ToString("o"));

            ctx.Response.ContentType = "application/json";
            using var w = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
            w.WriteStartArray();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                w.WriteStartObject();
                w.WriteString("exe", r.GetString(0));
                w.WriteNumber("seconds", Convert.ToInt32(r["secs"]));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            await w.FlushAsync();
            ctx.Response.StatusCode = 200;
        });

        app.MapGet("/api/goals", async (HttpContext ctx) =>
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, goal_type, target, threshold_minutes, notify_at, enabled, COALESCE(last_notified,'') FROM goals ORDER BY id";
            using var r = await cmd.ExecuteReaderAsync();
            using var arr = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
            arr.WriteStartArray();
            while (await r.ReadAsync())
            {
                arr.WriteStartObject();
                arr.WriteNumber("id", r.GetInt32(0));
                arr.WriteString("goalType", r.GetString(1));
                arr.WriteString("target", r.GetString(2));
                arr.WriteNumber("thresholdMinutes", r.GetInt32(3));
                arr.WriteNumber("notifyAt", r.GetInt32(4));
                arr.WriteBoolean("enabled", r.GetInt32(5) != 0);
                arr.WriteString("lastNotified", r.GetString(6));
                arr.WriteEndObject();
            }
            arr.WriteEndArray();
            await arr.FlushAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
        });

        app.MapPost("/api/goals", async (HttpContext ctx) =>
        {
            using var sr = new System.IO.StreamReader(ctx.Request.Body);
            var body = await sr.ReadToEndAsync();
            var doc = System.Text.Json.JsonDocument.Parse(body);
            var r = doc.RootElement;
            var goalType = r.TryGetProperty("goalType", out var gt) ? gt.GetString() ?? "max_time" : "max_time";
            var target = r.TryGetProperty("target", out var tg) ? tg.GetString() ?? "" : "";
            var minutes = r.TryGetProperty("thresholdMinutes", out var tm) && tm.TryGetInt32(out var m) ? m : 60;
            var notifyAt = r.TryGetProperty("notifyAt", out var na) && na.TryGetInt32(out var n) ? n : 80;

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO goals (goal_type, target, threshold_minutes, notify_at) VALUES ($gt, $tgt, $min, $na)";
            cmd.Parameters.AddWithValue("$gt", goalType);
            cmd.Parameters.AddWithValue("$tgt", target);
            cmd.Parameters.AddWithValue("$min", minutes);
            cmd.Parameters.AddWithValue("$na", notifyAt);
            await cmd.ExecuteNonQueryAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapDelete("/api/goals/{id}", async (HttpContext ctx) =>
        {
            if (!int.TryParse(ctx.Request.RouteValues["id"] as string, out var id))
            { ctx.Response.StatusCode = 400; return; }
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM goals WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapGet("/api/builtin-rules", async (HttpContext ctx) =>
        {
            using var arr = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
            arr.WriteStartObject();
            arr.WritePropertyName("exeRules");
            arr.WriteStartObject();
            arr.WriteString("code.exe", "development"); arr.WriteString("devenv.exe", "development"); arr.WriteString("cursor.exe", "development");
            arr.WriteString("windsurf.exe", "development"); arr.WriteString("notepad++.exe", "development"); arr.WriteString("git-bash.exe", "development");
            arr.WriteString("powershell.exe", "development"); arr.WriteString("cmd.exe", "development"); arr.WriteString("windowsTerminal.exe", "development");
            arr.WriteString("slack.exe", "communication"); arr.WriteString("discord.exe", "communication"); arr.WriteString("teams.exe", "communication");
            arr.WriteString("zoom.exe", "communication"); arr.WriteString("outlook.exe", "communication");
            arr.WriteString("chrome.exe", "browsing"); arr.WriteString("msedge.exe", "browsing"); arr.WriteString("firefox.exe", "browsing");
            arr.WriteString("zen.exe", "browsing"); arr.WriteString("brave.exe", "browsing");
            arr.WriteString("winword.exe", "documents"); arr.WriteString("excel.exe", "documents"); arr.WriteString("powerpnt.exe", "documents");
            arr.WriteString("notion.exe", "documents"); arr.WriteString("obsidian.exe", "documents");
            arr.WriteString("spotify.exe", "media"); arr.WriteString("vlc.exe", "media"); arr.WriteString("mpc-hc.exe", "media"); arr.WriteString("wmplayer.exe", "media");
            arr.WriteString("TimeLens.TrayApp.exe", "system"); arr.WriteString("ShellExperienceHost.exe", "system"); arr.WriteString("explorer.exe", "system");
            arr.WriteString("OpenCode.exe", "development");
            arr.WriteString("figma.exe", "design"); arr.WriteString("unrealeditor.exe", "development"); arr.WriteString("godot.exe", "development"); arr.WriteString("unity.exe", "development");
            arr.WriteString("chatgpt.exe", "development"); arr.WriteString("claude.exe", "development");
            arr.WriteString("r5apex_dx12.exe", "gaming"); arr.WriteString("r5apex.exe", "gaming"); arr.WriteString("valorant.exe", "gaming"); arr.WriteString("gta5.exe", "gaming");
            arr.WriteString("steam.exe", "gaming"); arr.WriteString("steamwebhelper.exe", "gaming"); arr.WriteString("epicgameslauncher.exe", "gaming"); arr.WriteString("ubisoftconnect.exe", "gaming");
            arr.WriteString("armourycrate.exe", "utilities"); arr.WriteString("nvidia overlay.exe", "utilities"); arr.WriteString("nvidia app.exe", "utilities");
            arr.WriteString("onedrive.exe", "work"); arr.WriteString("dropbox.exe", "work"); arr.WriteString("snippingtool.exe", "utilities"); arr.WriteString("photos.exe", "utilities");
            arr.WriteEndObject();
            arr.WritePropertyName("domainRules");
            arr.WriteStartObject();
            arr.WriteString("github.com", "development"); arr.WriteString("gitlab.com", "development");
            arr.WriteString("stackoverflow.com", "development"); arr.WriteString("youtube.com", "media");
            arr.WriteString("netflix.com", "media"); arr.WriteString("spotify.com", "media"); arr.WriteString("twitch.tv", "media");
            arr.WriteString("slack.com", "communication"); arr.WriteString("discord.com", "communication");
            arr.WriteString("teams.microsoft.com", "communication"); arr.WriteString("zoom.us", "communication");
            arr.WriteString("reddit.com", "social"); arr.WriteString("twitter.com", "social"); arr.WriteString("x.com", "social");
            arr.WriteString("linkedin.com", "social"); arr.WriteString("instagram.com", "social"); arr.WriteString("facebook.com", "social");
            arr.WriteEndObject();
            arr.WriteEndObject();
            await arr.FlushAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
        });

        app.MapPost("/api/browser-event", async (HttpContext ctx) =>
        {
            var evt = await ctx.Request.ReadFromJsonAsync<BrowserEventDto>(AppJsonContext.Default.BrowserEventDto);
            if (evt is null)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.ContentType = "application/json";
                return;
            }

            // Focus mode block check — runs regardless of TrackBrowser setting
            var browserBlock = BrowserBlockResponse(evt.Domain);
            if (browserBlock.Action != "none" && browserBlock.Presentation is not null)
            {
                if (recordBlockAttempt is not null)
                    recordBlockAttempt(browserBlock.Presentation.Target, browserBlock.Action);
                else
                    LiveStatusStore.PendingBrowserBlock = new(browserBlock.Presentation.Target, browserBlock.Action);
            }

            if (!LiveStatusStore.Settings.TrackBrowser)
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsJsonAsync(browserBlock, AppJsonContext.Default.BrowserBlockResponseDto);
                return;
            }

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            // Close previous event for this tab (if any)
            if (evt.TabId > 0 && OpenBrowserEvents.TryRemove(TabKey(evt.Browser, evt.TabId), out var prevEventId))
            {
                using var closeCmd = conn.CreateCommand();
                closeCmd.CommandText = "UPDATE browser_events SET end_time = $now WHERE id = $id AND end_time IS NULL";
                closeCmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                closeCmd.Parameters.AddWithValue("$id", prevEventId);
                await closeCmd.ExecuteNonQueryAsync();
            }

            // Extensions can keep an active tab alive while their browser is behind
            // another app. Record only when that browser owns the foreground window.
            if (!IsTrackedBrowserForeground(evt.Browser))
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsJsonAsync(browserBlock, AppJsonContext.Default.BrowserBlockResponseDto);
                return;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO browser_events (domain, url, title, start_time, end_time, browser, tab_id, local_date)
                VALUES ($domain, $url, $title, $start, NULL, $browser, $tabId, $localDate)
                """;
            cmd.Parameters.AddWithValue("$domain", evt.Domain);
            cmd.Parameters.AddWithValue("$url", evt.Url);
            cmd.Parameters.AddWithValue("$title", evt.Title);
            cmd.Parameters.AddWithValue("$start", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$browser", evt.Browser);
            cmd.Parameters.AddWithValue("$tabId", evt.TabId);
            cmd.Parameters.AddWithValue("$localDate", DateTime.Now.ToString("yyyy-MM-dd"));
            await cmd.ExecuteNonQueryAsync();

            // Track the new event ID for this tab for duration tracking
            if (evt.TabId > 0)
            {
                using var getIdCmd = conn.CreateCommand();
                getIdCmd.CommandText = "SELECT last_insert_rowid()";
                var newEventId = (long)(await getIdCmd.ExecuteScalarAsync())!;
                OpenBrowserEvents[TabKey(evt.Browser, evt.TabId)] = newEventId;
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(browserBlock, AppJsonContext.Default.BrowserBlockResponseDto);
        });

        app.MapPost("/api/block/protection/options", async (HttpContext ctx) =>
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
            var password = doc.RootElement.TryGetProperty("password", out var passwordProp) ? passwordProp.GetString() ?? "" : "";
            var scope = doc.RootElement.TryGetProperty("scope", out var scopeProp) ? scopeProp.GetString() ?? "" : "";
            var protectExit = doc.RootElement.TryGetProperty("protectExit", out var exitProp) && exitProp.ValueKind == System.Text.Json.JsonValueKind.True;
            if (scope is not ("strict" or "all"))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("{\"error\":\"Invalid protection scope\"}");
                return;
            }
            if (BlockProtectionService.TryUnlock(dbPath, password, out var retryAfter) is null)
            {
                ctx.Response.StatusCode = retryAfter > 0 ? 429 : 401;
                await ctx.Response.WriteAsync(retryAfter > 0 ? $"{{\"error\":\"Too many attempts. Try again in {retryAfter} seconds\"}}" : "{\"error\":\"Current password is incorrect\"}");
                return;
            }
            saveSetting?.Invoke("block_protection_scope", scope);
            saveSetting?.Invoke("block_exit_protection", protectExit ? "true" : "false");
            LiveStatusStore.Settings = LiveStatusStore.Settings with { BlockProtectionScope = scope, BlockExitProtection = protectExit };
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapGet("/api/browser-block-state", async (HttpContext ctx) =>
        {
            var domain = ctx.Request.Query["domain"].FirstOrDefault();
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(BrowserBlockResponse(domain), AppJsonContext.Default.BrowserBlockResponseDto);
        });

        app.MapPost("/api/browser-leave", async (HttpContext ctx) =>
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
            var root = doc.RootElement;
            if (root.TryGetProperty("tabId", out var tabProp) && tabProp.TryGetInt32(out var tabId) && tabId > 0)
            {
                var browser = root.TryGetProperty("browser", out var b) ? b.GetString() ?? "browser" : "browser";
                if (OpenBrowserEvents.TryRemove(TabKey(browser, tabId), out var eventId))
                {
                    using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
                    await conn.OpenAsync();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE browser_events SET end_time = $now WHERE id = $id AND end_time IS NULL";
                    cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                    cmd.Parameters.AddWithValue("$id", eventId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapPost("/api/browser-heartbeat", async (HttpContext ctx) =>
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tabId", out var tabProp) || !tabProp.TryGetInt32(out var tabId) || tabId <= 0)
            {
                ctx.Response.StatusCode = 400;
                return;
            }

            var domain = root.TryGetProperty("domain", out var d) ? d.GetString() ?? "" : "";
            var url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            var browser = root.TryGetProperty("browser", out var b) ? b.GetString() ?? "browser" : "browser";

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            // Close the current open row for this tab
            if (OpenBrowserEvents.TryRemove(TabKey(browser, tabId), out var prevEventId))
            {
                using var closeCmd = conn.CreateCommand();
                closeCmd.CommandText = "UPDATE browser_events SET end_time = $now WHERE id = $id AND end_time IS NULL";
                closeCmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                closeCmd.Parameters.AddWithValue("$id", prevEventId);
                await closeCmd.ExecuteNonQueryAsync();
            }

            if (!IsTrackedBrowserForeground(browser))
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"ok\":true}");
                return;
            }

            // Open a new row — bounds max miscalculation to heartbeat interval
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO browser_events (domain, url, title, start_time, end_time, browser, tab_id, local_date)
                VALUES ($domain, $url, $title, $start, NULL, $browser, $tabId, $localDate)
                """;
            cmd.Parameters.AddWithValue("$domain", domain);
            cmd.Parameters.AddWithValue("$url", url ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$title", title ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$start", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$browser", browser);
            cmd.Parameters.AddWithValue("$tabId", tabId);
            cmd.Parameters.AddWithValue("$localDate", DateTime.Now.ToString("yyyy-MM-dd"));
            await cmd.ExecuteNonQueryAsync();

            using var getIdCmd = conn.CreateCommand();
            getIdCmd.CommandText = "SELECT last_insert_rowid()";
            OpenBrowserEvents[TabKey(browser, tabId)] = (long)(await getIdCmd.ExecuteScalarAsync())!;

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapPost("/api/audible-status", async (HttpContext ctx) =>
        {
            using var sr = new System.IO.StreamReader(ctx.Request.Body);
            var body = await sr.ReadToEndAsync();
            var doc = System.Text.Json.JsonDocument.Parse(body);
            var audible = doc.RootElement.GetProperty("audible").GetBoolean();
            var browser = doc.RootElement.GetProperty("browser").GetString() ?? "browser";
            LiveStatusStore.AudibleTab = audible ? browser : null;
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapPost("/api/extension-heartbeat", (HttpContext ctx) =>
        {
            LiveStatusStore.LastExtensionHeartbeat = DateTime.UtcNow;
            LiveStatusStore.LastExtensionBrowser = ctx.Request.Query["browser"].FirstOrDefault() ?? "unknown";
            LiveStatusStore.LastExtensionVersion = ctx.Request.Query["version"].FirstOrDefault() ?? "unknown";
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        app.MapGet("/api/extension-status", async (HttpContext ctx) =>
        {
            var ageSeconds = LiveStatusStore.LastExtensionHeartbeat == DateTime.MinValue
                ? -1
                : Math.Max(0, (int)(DateTime.UtcNow - LiveStatusStore.LastExtensionHeartbeat).TotalSeconds);
            ctx.Response.ContentType = "application/json";
            await using var json = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
            json.WriteStartObject();
            json.WriteBoolean("connected", ageSeconds >= 0 && ageSeconds <= 75);
            json.WriteNumber("ageSeconds", ageSeconds);
            json.WriteString("browser", LiveStatusStore.LastExtensionBrowser);
            json.WriteString("version", LiveStatusStore.LastExtensionVersion);
            json.WriteEndObject();
            await json.FlushAsync();
        });

        app.MapGet("/api/input-summary", async (HttpContext ctx) =>
        {
            var dateParam = ctx.Request.Query["date"].FirstOrDefault();
            DateTime queryDate = DateTime.Now;
            if (dateParam is not null && DateTime.TryParse(dateParam, out var parsed)) queryDate = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            var localDate = queryDate.Date;
            var today = TimeZoneInfo.ConvertTimeToUtc(localDate);
            var tomorrow = TimeZoneInfo.ConvertTimeToUtc(localDate.AddDays(1));
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT exe_name, COALESCE(SUM(keystroke_count),0), COALESCE(SUM(click_count),0) FROM input_activity WHERE timestamp >= $t0 AND timestamp < $t1 GROUP BY exe_name ORDER BY 2 DESC";
            cmd.Parameters.AddWithValue("$t0", today.ToString("o"));
            cmd.Parameters.AddWithValue("$t1", tomorrow.ToString("o"));
            using var arr = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
            arr.WriteStartArray();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) { arr.WriteStartObject(); arr.WriteString("exeName", r.IsDBNull(0) ? "" : r.GetString(0)); arr.WriteNumber("keystrokes", r.IsDBNull(1) ? 0 : r.GetInt32(1)); arr.WriteNumber("clicks", r.IsDBNull(2) ? 0 : r.GetInt32(2)); arr.WriteEndObject(); }
            arr.WriteEndArray();
            await arr.FlushAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
        });

        app.MapGet("/api/audio-summary", async (HttpContext ctx) =>
        {
            var dateParam = ctx.Request.Query["date"].FirstOrDefault();
            DateTime queryDate = DateTime.Now;
            if (dateParam is not null && DateTime.TryParse(dateParam, out var parsed)) queryDate = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            var localDate = queryDate.Date;
            var today = TimeZoneInfo.ConvertTimeToUtc(localDate);
            var tomorrow = TimeZoneInfo.ConvertTimeToUtc(localDate.AddDays(1));
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT exe_name, COUNT(*), MIN(timestamp) FROM audio_activity WHERE is_playing=1 AND timestamp>=$t0 AND timestamp<$t1 GROUP BY exe_name ORDER BY 2 DESC";
            cmd.Parameters.AddWithValue("$t0", today.ToString("o"));
            cmd.Parameters.AddWithValue("$t1", tomorrow.ToString("o"));
            using var arr = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
            arr.WriteStartArray();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) { arr.WriteStartObject(); arr.WriteString("exeName", r.IsDBNull(0) ? "" : r.GetString(0)); arr.WriteNumber("sessions", r.IsDBNull(1) ? 0 : r.GetInt32(1)); arr.WriteString("firstSeen", r.IsDBNull(2) ? "" : r.GetString(2)); arr.WriteEndObject(); }
            arr.WriteEndArray();
            await arr.FlushAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
        });

        app.MapGet("/api/browser-summary", async (HttpContext ctx) =>
        {
            var dateParam = ctx.Request.Query["date"].FirstOrDefault();
            DateTime queryDate = DateTime.Now;
            if (dateParam is not null && DateTime.TryParse(dateParam, out var parsed)) queryDate = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            var localDate = queryDate.Date;
            var dateStr = localDate.ToString("yyyy-MM-dd");
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT domain, COUNT(*), MAX(start_time) FROM browser_events WHERE local_date = $date GROUP BY domain ORDER BY 2 DESC LIMIT 20";
            cmd.Parameters.AddWithValue("$date", dateStr);
            using var arr = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
            arr.WriteStartArray();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) { arr.WriteStartObject(); arr.WriteString("domain", r.IsDBNull(0) ? "" : r.GetString(0)); arr.WriteNumber("visits", r.IsDBNull(1) ? 0 : r.GetInt32(1)); arr.WriteString("lastVisit", r.IsDBNull(2) ? "" : r.GetString(2)); arr.WriteEndObject(); }
            arr.WriteEndArray();
            await arr.FlushAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
        });

        app.MapGet("/api/browser-time-summary", async (HttpContext ctx) =>
        {
            var dateParam = ctx.Request.Query["date"].FirstOrDefault();
            DateTime queryDate = DateTime.Now;
            if (dateParam is not null && DateTime.TryParse(dateParam, out var parsed)) queryDate = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            var localDate = queryDate.Date;
            var dateStr = localDate.ToString("yyyy-MM-dd");
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            // Browser heartbeats identify the visible tab, but they keep arriving while
            // the PC is unattended. Subtract OS idle spans so a background YouTube,
            // WhatsApp, or dashboard tab does not turn into hours of claimed use.
            var idleRanges = new List<(DateTime Start, DateTime End)>();
            using (var idleCmd = conn.CreateCommand())
            {
                idleCmd.CommandText = """
                    SELECT start_time, COALESCE(end_time, $now)
                    FROM idle_spans
                    WHERE start_time < $now AND COALESCE(end_time, $now) > $dayStart
                    ORDER BY start_time
                    """;
                idleCmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                idleCmd.Parameters.AddWithValue("$dayStart", TimeZoneInfo.ConvertTimeToUtc(localDate).ToString("o"));
                using var idleReader = await idleCmd.ExecuteReaderAsync();
                while (await idleReader.ReadAsync())
                {
                    idleRanges.Add((
                        DateTime.Parse(idleReader.GetString(0), null, DateTimeStyles.RoundtripKind),
                        DateTime.Parse(idleReader.GetString(1), null, DateTimeStyles.RoundtripKind)));
                }
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT domain, start_time, COALESCE(end_time, $now) AS end_time
                FROM browser_events
                WHERE local_date = $date
                ORDER BY start_time
                """;
            cmd.Parameters.AddWithValue("$date", dateStr);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));

            var domainSecs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var domain = r.GetString(0);
                var start = DateTime.Parse(r.GetString(1), null, DateTimeStyles.RoundtripKind);
                var next = DateTime.Parse(r.GetString(2), null, DateTimeStyles.RoundtripKind);
                var secs = (next - start).TotalSeconds;
                foreach (var idle in idleRanges)
                {
                    var overlapStart = start > idle.Start ? start : idle.Start;
                    var overlapEnd = next < idle.End ? next : idle.End;
                    if (overlapEnd > overlapStart)
                        secs -= (overlapEnd - overlapStart).TotalSeconds;
                }
                if (secs > 0)
                {
                    // The extension rotates the visible-tab event every 45 seconds.
                    // A 2-minute ceiling still tolerates delayed service workers while
                    // preventing a crashed or sleeping tab from becoming a multi-hour visit.
                    var capped = Math.Min(secs, 120);
                    domainSecs.TryGetValue(domain, out var cur);
                    domainSecs[domain] = cur + capped;
                }
            }

            using var arr = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
            arr.WriteStartArray();
            foreach (var kv in domainSecs.OrderByDescending(kv => kv.Value).Take(20))
            {
                arr.WriteStartObject();
                arr.WriteString("domain", kv.Key);
                arr.WriteNumber("totalSeconds", (int)kv.Value);
                arr.WriteNumber("totalMinutes", (int)Math.Round(kv.Value / 60));
                arr.WriteEndObject();
            }
            arr.WriteEndArray();
            await arr.FlushAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
        });

        app.MapGet("/api/browser-hourly", async (HttpContext ctx) =>
        {
            var dateParam = ctx.Request.Query["date"].FirstOrDefault();
            DateTime queryDate = DateTime.Now;
            if (dateParam is not null && DateTime.TryParse(dateParam, out var parsed))
                queryDate = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            var localDate = queryDate.Date;
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT CAST(strftime('%H', start_time, 'localtime') AS INTEGER) AS h, COUNT(*) AS cnt
                FROM browser_events
                WHERE local_date = $date
                GROUP BY h ORDER BY h
                """;
            cmd.Parameters.AddWithValue("$date", localDate.ToString("yyyy-MM-dd"));
            var counts = new int[24];
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                counts[r.GetInt32(0)] += r.GetInt32(1);
            }
            using var arr = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
            arr.WriteStartArray();
            for (int i = 0; i < 24; i++)
            {
                arr.WriteStartObject();
                arr.WriteNumber("hour", i);
                arr.WriteNumber("visits", counts[i]);
                arr.WriteEndObject();
            }
            arr.WriteEndArray();
            await arr.FlushAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
        });

        app.MapGet("/api/app-icon", async (HttpContext ctx) =>
        {
            var name = ctx.Request.Query["name"].FirstOrDefault();
            if (string.IsNullOrEmpty(name))
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"error\":\"missing name\"}");
                return;
            }
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name += ".exe";

            var cacheKey = name.ToLowerInvariant();
            if (IconCache.TryGetValue(cacheKey, out var cached))
            {
                ctx.Response.ContentType = "image/png";
                ctx.Response.ContentLength = cached.Length;
                await ctx.Response.Body.WriteAsync(cached);
                return;
            }

            var exePath = FindExePath(name);
            if (exePath is null)
            {
                ctx.Response.StatusCode = 404;
                return;
            }

            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon is null) { ctx.Response.StatusCode = 404; return; }
                using var ms = new MemoryStream();
                using var bmp = icon.ToBitmap();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                var png = ms.ToArray();
                IconCache[cacheKey] = png;
                ctx.Response.ContentType = "image/png";
                ctx.Response.ContentLength = png.Length;
                await ctx.Response.Body.WriteAsync(png);
            }
            catch
            {
                ctx.Response.StatusCode = 404;
            }
        });

        static string? FindExePath(string name)
        {
            var pathExts = new[] { "", ".exe", ".com", ".bat" };
            foreach (var ext in pathExts)
            {
                var withExt = name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? name : name + ext;
                if (Path.IsPathRooted(withExt) && File.Exists(withExt)) return withExt;
            }

            // 1) Check running processes — most reliable for currently-running apps
            try
            {
                var procName = Path.GetFileNameWithoutExtension(name);
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(procName))
                {
                    try
                    {
                        var path = p.MainModule?.FileName;
                        if (path is not null && File.Exists(path))
                        {
                            p.Dispose();
                            return path;
                        }
                    }
                    catch { }
                    p.Dispose();
                }
            }
            catch { }

            // 2) Search PATH
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var ext in pathExts)
                {
                    var withExt = name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? name : name + ext;
                    var path = Path.Combine(dir, withExt);
                    if (File.Exists(path)) return path;
                }
            }

            // 3) Search common install dirs (top-level + one sub-level)
            var searchDirs = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Programs"),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            };

            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var ext in pathExts)
                {
                    var withExt = name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? name : name + ext;
                    var path = Path.Combine(dir, withExt);
                    if (File.Exists(path)) return path;
                }
                // One level deep
                foreach (var sub in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
                {
                    foreach (var ext in pathExts)
                    {
                        var withExt = name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? name : name + ext;
                        var path = Path.Combine(sub, withExt);
                        if (File.Exists(path)) return path;
                    }
                    // Two levels deep for common patterns like %ProgramFiles%\Microsoft VS Code\Code.exe
                    try
                    {
                        foreach (var sub2 in Directory.EnumerateDirectories(sub, "*", SearchOption.TopDirectoryOnly))
                        {
                            foreach (var ext in pathExts)
                            {
                                var withExt = name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? name : name + ext;
                                var path = Path.Combine(sub2, withExt);
                                if (File.Exists(path)) return path;
                            }
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        app.MapGet("/api/running-processes", (HttpContext ctx) =>
        {
            var visibleApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in System.Diagnostics.Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.MainWindowHandle == IntPtr.Zero ||
                            string.IsNullOrWhiteSpace(process.ProcessName) ||
                            !IsWindowVisible(process.MainWindowHandle) ||
                            InfrastructureExes.Contains(process.ProcessName))
                            continue;
                        visibleApps.Add(process.ProcessName + ".exe");
                    }
                    catch
                    {
                        // Processes can exit or become inaccessible while enumerating.
                    }
                }
            }
            var procs = visibleApps.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            return ctx.Response.WriteAsJsonAsync(procs, AppJsonContext.Default.StringArray);
        });

        app.MapGet("/api/summary", async (HttpContext ctx) =>
        {
            try
            {
            var dateParam = ctx.Request.Query["date"].FirstOrDefault();
            DateTime? queryDate = null;
            if (dateParam is not null && DateTime.TryParse(dateParam, out var parsed))
                queryDate = DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            var svc = analytics;
            var result = await svc.GetDashboardAsync(queryDate);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(result, AppJsonContext.Default.DashboardResponse);
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(Path.Combine(Path.GetDirectoryName(dbPath)!, "query_error.log"), $"{DateTime.UtcNow:o} summary: {ex}{Environment.NewLine}");
                ctx.Response.StatusCode = 500;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"error\":\"" + ex.Message.Replace("\"", "'") + "\"}");
            }
        });

        app.MapPost("/api/idle-reason", async (HttpContext ctx) =>
        {
            using var sr = new System.IO.StreamReader(ctx.Request.Body);
            var body = await sr.ReadToEndAsync();
            var doc = System.Text.Json.JsonDocument.Parse(body);
            var reason = doc.RootElement.GetProperty("reason").GetString() ?? "";
            var startTime = doc.RootElement.GetProperty("startTime").GetString();

            if (startTime is not null)
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE app_events SET idle_reason = $reason WHERE start_time = $start AND session_state = 'idle'";
                cmd.Parameters.AddWithValue("$reason", reason);
                cmd.Parameters.AddWithValue("$start", startTime);
                await cmd.ExecuteNonQueryAsync();
            }

            LiveStatusStore.PendingIdleReturn = false;
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapGet("/extension-setup", (HttpContext ctx) =>
        {
            ctx.Response.Redirect("https://addons.mozilla.org/en-US/firefox/addon/timelens-tracker/", permanent: false);
            return Task.CompletedTask;
        });

        app.MapGet("/api/db-size", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "application/json";
            ctx.Response.StatusCode = 200;
            var size = System.IO.File.Exists(dbPath) ? new System.IO.FileInfo(dbPath).Length : 0;
            await ctx.Response.WriteAsync($"{{\"sizeBytes\":{size}}}");
        });

        app.MapGet("/api/export", async (HttpContext ctx) =>
        {
            var format = ctx.Request.Query["format"].FirstOrDefault() ?? "csv";
            var range = ctx.Request.Query["range"].FirstOrDefault() ?? "today";
            ctx.Response.ContentType = format == "json" ? "application/json" : "text/csv";
            var label = range == "30days" ? "30days" : range == "today" ? "today" : range;
            ctx.Response.Headers.Append("Content-Disposition", $"attachment; filename=timelens-{label}.{format}");

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            var exportDay = DateTime.Today;
            if (range != "today" && range != "30days" && DateTime.TryParse(range, out var selectedDay))
                exportDay = DateTime.SpecifyKind(selectedDay.Date, DateTimeKind.Local);
            var rangeStart = (range == "30days" ? exportDay.AddDays(-29) : exportDay).ToUniversalTime();
            var rangeEnd = exportDay.AddDays(1).ToUniversalTime();
            if (rangeEnd > DateTime.UtcNow) rangeEnd = DateTime.UtcNow;
            cmd.CommandText = """
                SELECT start_time, exe_name, window_title, category, session_state,
                    MAX(0, MIN(julianday(COALESCE(end_time, $end)), julianday($end)) -
                        MAX(julianday(start_time), julianday($start))) * 86400 AS duration_secs
                FROM app_events
                WHERE julianday(start_time) < julianday($end)
                    AND julianday(COALESCE(end_time, $end)) > julianday($start)
                ORDER BY start_time
                """;
            cmd.Parameters.AddWithValue("$start", rangeStart.ToString("o"));
            cmd.Parameters.AddWithValue("$end", rangeEnd.ToString("o"));

            using var r = await cmd.ExecuteReaderAsync();
            if (format == "json")
            {
                await using var w = new System.IO.StreamWriter(ctx.Response.Body);
                w.Write("[");
                var first = true;
                while (await r.ReadAsync())
                {
                    if (!first) w.Write(",");
                    first = false;
                    w.Write($$"""{"start":"{{r.GetString(0)}}","exe":"{{r.GetString(1)}}","title":"{{(r.IsDBNull(2) ? "" : r.GetString(2)).Replace("\"", "'")}}","category":"{{(r.IsDBNull(3) ? "" : r.GetString(3))}}","state":"{{(r.IsDBNull(4) ? "" : r.GetString(4))}}","secs":{{r.GetInt32(5)}}}""");
                }
                w.Write("]");
            }
            else
            {
                await using var w = new System.IO.StreamWriter(ctx.Response.Body);
                await w.WriteLineAsync("start_time,exe_name,window_title,category,session_state,duration_secs");
                while (await r.ReadAsync())
                {
                    var title = r.IsDBNull(2) ? "" : r.GetString(2).Replace("\"", "\"\"");
                    await w.WriteLineAsync(
                        $"{r.GetString(0)},{r.GetString(1)},\"{title}\",{(r.IsDBNull(3) ? "" : r.GetString(3))},{(r.IsDBNull(4) ? "" : r.GetString(4))},{r.GetInt32(5)}");
                }
            }
        });

        app.MapPost("/api/block/enforce", async (HttpContext ctx) =>
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
            var exe = doc.RootElement.TryGetProperty("exe", out var exeProp)
                ? BlockEntryHelper.NormalizeIdentifier(exeProp.GetString())
                : null;
            if (exe is null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.WriteAsync("{\"error\":\"A valid executable is required\"}");
                return;
            }
            if (enforceBlock?.Invoke(exe) != true)
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("{\"error\":\"Target is not in the active blocklist\"}");
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapPost("/api/block/preview", async (HttpContext ctx) =>
        {
            if (showBlockPreview is null)
            {
                ctx.Response.StatusCode = 503;
                await ctx.Response.WriteAsync("{\"error\":\"Notification preview is unavailable\"}");
                return;
            }
            var target = "example.exe";
            if (ctx.Request.ContentLength is > 0)
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
                if (doc.RootElement.TryGetProperty("target", out var targetProp) && targetProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    target = BlockEntryHelper.NormalizeIdentifier(targetProp.GetString()) ?? target;
            }
            showBlockPreview(target);
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        var blockMediaDirectory = Path.GetDirectoryName(dbPath)!;
        string? CurrentBlockMediaPath()
        {
            var configured = BlockMediaPath(blockMediaDirectory, LiveStatusStore.Settings.BlockMediaType);
            if (File.Exists(configured)) return configured;
            var legacy = Path.Combine(blockMediaDirectory, "block-notification.png");
            return File.Exists(legacy) ? legacy : null;
        }

        async Task SendBlockMedia(HttpContext ctx)
        {
            var path = CurrentBlockMediaPath();
            if (path is null)
            {
                ctx.Response.StatusCode = 404;
                return;
            }
            ctx.Response.ContentType = LiveStatusStore.Settings.BlockMediaType switch
            {
                "image/jpeg" => "image/jpeg",
                "image/gif" => "image/gif",
                "video/mp4" => "video/mp4",
                "video/webm" => "video/webm",
                _ => "image/png"
            };
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            await ctx.Response.SendFileAsync(path, ctx.RequestAborted);
        }
        app.MapGet("/api/block/media", SendBlockMedia);
        app.MapGet("/api/block/image", SendBlockMedia); // v4.2.1 extension compatibility

        async Task UploadBlockMedia(HttpContext ctx)
        {
            if (ctx.Request.ContentLength is > (MaxBlockVideoBytes * 2L + MaxBlockImageBytes * 2L))
            {
                ctx.Response.StatusCode = 413;
                await ctx.Response.WriteAsync("{\"error\":\"Media file is too large\"}");
                return;
            }

            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
                var dataUrl = doc.RootElement.TryGetProperty("dataUrl", out var dataProp) ? dataProp.GetString() : null;
                var mediaType = MediaTypeFromDataUrl(dataUrl);
                if (mediaType is null)
                    throw new ArgumentException("Choose a PNG, JPEG, GIF, MP4, or WebM file");

                var bytes = DecodeDataUrl(dataUrl!);
                var isVideo = mediaType.StartsWith("video/", StringComparison.Ordinal);
                var limit = isVideo ? MaxBlockVideoBytes : MaxBlockImageBytes;
                if (bytes.Length == 0 || bytes.Length > limit)
                    throw new ArgumentException(isVideo ? "Video must be 8 MB or smaller" : "Image or GIF must be 4 MB or smaller");

                byte[]? posterBytes = null;
                if (isVideo)
                {
                    var posterDataUrl = doc.RootElement.TryGetProperty("posterDataUrl", out var posterProp) ? posterProp.GetString() : null;
                    if (MediaTypeFromDataUrl(posterDataUrl) is not ("image/png" or "image/jpeg"))
                        throw new ArgumentException("Could not create a preview frame for this video");
                    posterBytes = DecodeDataUrl(posterDataUrl!);
                }

                SaveBlockMedia(bytes, mediaType, blockMediaDirectory, posterBytes);
                var version = DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture);
                saveSetting?.Invoke("block_media_type", mediaType);
                saveSetting?.Invoke("block_image_version", version);
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync($"{{\"ok\":true,\"version\":\"{version}\",\"mediaType\":\"{mediaType}\"}}");
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OutOfMemoryException)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.ContentType = "application/json";
                await using var json = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
                json.WriteStartObject();
                json.WriteString("error", ex is FormatException or OutOfMemoryException
                    ? "The selected media file is invalid"
                    : ex.Message);
                json.WriteEndObject();
                await json.FlushAsync(ctx.RequestAborted);
            }
        }
        app.MapPost("/api/block/media", UploadBlockMedia);
        app.MapPost("/api/block/image", UploadBlockMedia); // v4.2.1 dashboard compatibility

        async Task DeleteBlockMedia(HttpContext ctx)
        {
            DeleteBlockMediaFiles(blockMediaDirectory);
            saveSetting?.Invoke("block_media_type", "");
            saveSetting?.Invoke("block_image_version", "");
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        }
        app.MapDelete("/api/block/media", DeleteBlockMedia);
        app.MapDelete("/api/block/image", DeleteBlockMedia);

        app.MapGet("/api/block/stats", async (HttpContext ctx) =>
        {
            var dateText = ctx.Request.Query["date"].FirstOrDefault();
            var localDay = DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var requestedDay)
                ? requestedDay.Date
                : DateTime.Now.Date;
            var dayStart = DateTime.SpecifyKind(localDay, DateTimeKind.Local).ToUniversalTime();
            var dayEnd = dayStart.AddDays(1);
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT blocked_exe, blocked_action, COUNT(*) as cnt, MAX(timestamp) AS last_attempt
                FROM block_log
                WHERE timestamp >= $dayStart AND timestamp < $dayEnd
                GROUP BY blocked_exe, blocked_action
                ORDER BY last_attempt DESC, cnt DESC
                """;
            cmd.Parameters.AddWithValue("$dayStart", dayStart.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$dayEnd", dayEnd.ToString("o", CultureInfo.InvariantCulture));
            using var arr = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
            arr.WriteStartArray();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                arr.WriteStartObject();
                arr.WriteString("target", r.GetString(0));
                arr.WriteString("exe", r.GetString(0));
                arr.WriteString("action", r.GetString(1));
                arr.WriteNumber("count", r.GetInt32(2));
                arr.WriteString("lastAttempt", r.IsDBNull(3) ? "" : r.GetString(3));
                arr.WriteEndObject();
            }
            arr.WriteEndArray();
            await arr.FlushAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
        });

        await app.RunAsync(ct);
    }

    private static void SetStaticAssetCacheHeaders(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/assets"))
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    }

    [SupportedOSPlatform("windows6.1")]
    private static void SaveBlockImage(byte[] bytes, string destinationPath)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var source = Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: true);
        if (source.Width < 1 || source.Height < 1 || source.Width > 4096 || source.Height > 4096 ||
            (long)source.Width * source.Height > 16_000_000)
            throw new ArgumentException("Image dimensions must be between 1 and 4096 pixels");

        const int size = 192;
        using var output = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            var scale = Math.Min((double)size / source.Width, (double)size / source.Height);
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            graphics.DrawImage(source, (size - width) / 2, (size - height) / 2, width, height);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporaryPath = destinationPath + ".new";
        try
        {
            output.Save(temporaryPath, ImageFormat.Png);
            File.Move(temporaryPath, destinationPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string? MediaTypeFromDataUrl(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl)) return null;
        foreach (var mediaType in new[] { "image/png", "image/jpeg", "image/gif", "video/mp4", "video/webm" })
            if (dataUrl.StartsWith($"data:{mediaType};base64,", StringComparison.OrdinalIgnoreCase)) return mediaType;
        return null;
    }

    private static byte[] DecodeDataUrl(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0) throw new FormatException();
        return Convert.FromBase64String(dataUrl[(comma + 1)..]);
    }

    private static string BlockMediaPath(string directory, string? mediaType) => Path.Combine(directory, mediaType switch
    {
        "image/jpeg" => "block-notification-media.jpg",
        "image/gif" => "block-notification-media.gif",
        "video/mp4" => "block-notification-media.mp4",
        "video/webm" => "block-notification-media.webm",
        _ => "block-notification-media.png"
    });

    [SupportedOSPlatform("windows6.1")]
    private static void SaveBlockMedia(byte[] bytes, string mediaType, string directory, byte[]? posterBytes)
    {
        if (mediaType.StartsWith("image/", StringComparison.Ordinal))
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            if (source.Width < 1 || source.Height < 1 || source.Width > 4096 || source.Height > 4096 ||
                (long)source.Width * source.Height > 16_000_000)
                throw new ArgumentException("Image dimensions must be between 1 and 4096 pixels");
        }
        else if (mediaType == "video/mp4")
        {
            if (bytes.Length < 12 || !System.Text.Encoding.ASCII.GetString(bytes, 4, 4).Equals("ftyp", StringComparison.Ordinal))
                throw new ArgumentException("The selected MP4 file is invalid");
        }
        else if (mediaType == "video/webm")
        {
            if (bytes.Length < 4 || bytes[0] != 0x1A || bytes[1] != 0x45 || bytes[2] != 0xDF || bytes[3] != 0xA3)
                throw new ArgumentException("The selected WebM file is invalid");
        }

        Directory.CreateDirectory(directory);
        var destination = BlockMediaPath(directory, mediaType);
        var temporary = destination + ".new";
        var posterDestination = Path.Combine(directory, "block-notification-poster.png");
        var posterTemporary = Path.Combine(directory, "block-notification-poster.pending.png");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (posterBytes is not null) SaveBlockImage(posterBytes, posterTemporary);
            DeleteBlockMediaFiles(directory);
            File.Move(temporary, destination, true);
            if (posterBytes is not null)
                File.Move(posterTemporary, posterDestination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(posterTemporary)) File.Delete(posterTemporary);
        }
    }

    private static void DeleteBlockMediaFiles(string directory)
    {
        foreach (var name in new[]
        {
            "block-notification.png", "block-notification-media.png", "block-notification-media.jpg",
            "block-notification-media.gif", "block-notification-media.mp4", "block-notification-media.webm",
            "block-notification-poster.png"
        })
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserEventDto))]
[JsonSerializable(typeof(DashboardResponse))]
[JsonSerializable(typeof(SummaryDto))]
[JsonSerializable(typeof(TimelineBlockDto))]
[JsonSerializable(typeof(TopAppDto))]
[JsonSerializable(typeof(HeatmapEntryDto))]
[JsonSerializable(typeof(CategoryEntryDto))]
[JsonSerializable(typeof(LiveStatusDto))]
[JsonSerializable(typeof(InputSummaryDto))]
[JsonSerializable(typeof(BrowserEntryDto))]
[JsonSerializable(typeof(AudioSessionDto))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(BlockEntry[]))]
[JsonSerializable(typeof(BrowserBlockResponseDto))]
[JsonSerializable(typeof(UpdateStatusDto))]
internal partial class AppJsonContext : JsonSerializerContext { }

public sealed record BrowserBlockPresentationDto(
    string Target, string Title, string Message, string? ImageUrl, string? MediaUrl, string MediaType,
    int RepeatIntervalSeconds, string Position, string MediaLayout, bool Continuous, string Surface);

public sealed record BrowserBlockResponseDto(
    bool Ok, bool Blocked, string Action, BrowserBlockPresentationDto? Presentation);

public sealed record BlockEntry(string I, string M, string? E, string? A = null)
{
    public bool IsExpired() => M == "t" && E is not null &&
        DateTime.TryParse(E, null, DateTimeStyles.RoundtripKind, out var expires) &&
        DateTime.UtcNow >= expires;
}

public static class BlockEntryHelper
{
    private static readonly HashSet<string> ProtectedTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "timelens.exe", "dwm.exe", "csrss.exe", "winlogon.exe",
        "services.exe", "lsass.exe", "svchost.exe", "system", "idle",
        "localhost", "127.0.0.1", "::1"
    };

    public static BlockEntry[]? TryParseBlockEntries(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return null;
        BlockEntry[]? parsed = null;
        try
        {
            parsed = System.Text.Json.JsonSerializer.Deserialize(json, AppJsonContext.Default.BlockEntryArray);
        }
        catch { }

        if (parsed is null)
        {
            try
            {
                var legacy = System.Text.Json.JsonSerializer.Deserialize(json, AppJsonContext.Default.StringArray);
                parsed = legacy?.Select(s => new BlockEntry(s, "u", null, null)).ToArray();
            }
            catch { }
        }

        if (parsed is null) return null;

        var normalized = new List<BlockEntry>(Math.Min(parsed.Length, 200));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in parsed)
        {
            var id = NormalizeIdentifier(entry.I);
            if (id is null || !seen.Add(id)) continue;
            var mode = entry.M == "t" ? "t" : "u";
            if (mode == "t" && (entry.E is null ||
                !DateTime.TryParse(entry.E, null, DateTimeStyles.RoundtripKind, out _)))
                continue;
            normalized.Add(new BlockEntry(id, mode, mode == "t" ? entry.E : null, NormalizeAction(id, entry.A)));
            if (normalized.Count == 200) break;
        }
        return normalized.Count == 0 ? null : normalized.ToArray();
    }

    public static string Serialize(IEnumerable<BlockEntry> entries) =>
        System.Text.Json.JsonSerializer.Serialize(entries.ToArray(), AppJsonContext.Default.BlockEntryArray);

    public static string? NormalizeIdentifier(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim().Trim('"', '\'').ToLowerInvariant();
        if (value.Length > 512) return null;

        if (value.Contains('\\'))
            value = Path.GetFileName(value);

        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/')) return null;
            return value;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value.TrimStart('*', '.');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            return null;

        var host = uri.IdnHost.Trim('.').ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        return Uri.CheckHostName(host) == UriHostNameType.Unknown ? null : host;
    }

    public static bool IsExecutable(BlockEntry entry) =>
        entry.I.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    public static string? NormalizeAction(string identifier, string? action)
        => BlockTargetAction.Normalize(identifier, action);

    public static string ActionFor(BlockEntry entry, string? fallback)
    {
        return BlockTargetAction.Resolve(entry.I, entry.A, fallback);
    }

    public static bool IsProtected(string identifier) => ProtectedTargets.Contains(identifier);

    public static bool IsUnsafeShellAction(BlockEntry entry, string? fallback)
        => BlockTargetAction.IsUnsafeShellAction(entry.I, entry.A, fallback);

    public static bool MatchesExecutable(BlockEntry entry, string executable) =>
        IsExecutable(entry) && string.Equals(
            Path.GetFileName(entry.I), Path.GetFileName(executable), StringComparison.OrdinalIgnoreCase);

    public static bool MatchesDomain(BlockEntry entry, string host)
    {
        if (IsExecutable(entry)) return false;
        var normalizedHost = NormalizeIdentifier(host);
        return normalizedHost is not null &&
            (string.Equals(normalizedHost, entry.I, StringComparison.OrdinalIgnoreCase) ||
             normalizedHost.EndsWith("." + entry.I, StringComparison.OrdinalIgnoreCase));
    }
}
