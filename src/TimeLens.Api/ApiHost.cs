using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
    private static readonly ConcurrentDictionary<string, long> OpenBrowserEvents = new(StringComparer.OrdinalIgnoreCase);

    private static string TabKey(string browser, int tabId) => $"{browser}:{tabId}";
    private static readonly ConcurrentDictionary<string, byte[]> IconCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> InfrastructureExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost", "TextInputHost", "SystemSettings", "RuntimeBroker",
        "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost", "ctfmon",
        "conhost", "fontdrvhost",
        "svchost", "dwm", "csrss", "smss", "wininit", "winlogon", "services",
        "lsass", "spoolsv", "taskhostw", "sihost",
        "TimeLens.TrayApp", "TimeLens", "NVDisplay.Container", "NVIDIA Share", "nvsphelper64",
        "explorer", "CalculatorApp",
        "steamwebhelper", "SteamWebHelper", "SteamService", "SteamClientBootstrapper",
    };
    public static DateTime LastActivityUtc { get; private set; } = DateTime.MinValue;

    public static async Task StartAsync(string dbPath, CancellationToken ct = default,
        Action<string, string>? saveSetting = null,
        Action<bool>? setTrackAudio = null,
        Action<bool>? setTrackInput = null,
        Action<string, string, string, string, int>? upsertRule = null,
        Action<string>? deleteRule = null,
        Action<string>? enforceBlock = null)
    {
        var dashboardPath = Path.Combine(
            AppContext.BaseDirectory, "dashboard");

        var analytics = new AnalyticsService(dbPath);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{DefaultPort}");

        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
        });

        var app = builder.Build();

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
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeLens", "api_error.log"),
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
                staticOpts = new StaticFileOptions { FileProvider = embedded };
            }
        }

        if (staticOpts is null && Directory.Exists(dashboardPath))
        {
            staticOpts = new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(dashboardPath)
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
                    "pollIntervalSeconds" => "poll_interval_seconds",
                    "timeFormat" => "time_format",
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
                    case "timeFormat":
                        LiveStatusStore.Settings = LiveStatusStore.Settings with { TimeFormat = value };
                        break;
                    case "pollIntervalSeconds":
                        if (int.TryParse(value, out var pis))
                            LiveStatusStore.Settings = LiveStatusStore.Settings with { PollIntervalSeconds = pis };
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
                           (julianday(COALESCE(end_time, $now)) - julianday(start_time)) * 86400
                       ), 0) AS secs
                FROM app_events
                WHERE (category = 'other' OR category IS NULL)
                  AND session_state = 'active'
                  AND start_time >= $today
                GROUP BY exe_name
                HAVING secs > 60
                ORDER BY secs DESC
                LIMIT 30
                """;
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$today", DateTime.UtcNow.Date.ToString("o"));

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
            var domainBlocked = false;
            if (LiveStatusStore.Settings.FocusMode && !string.IsNullOrEmpty(evt.Domain))
            {
                var blocklist = LiveStatusStore.Settings.FocusBlocklist;
                if (!string.IsNullOrEmpty(blocklist) && blocklist != "[]")
                {
                    try
                    {
                        var items = BlockEntryHelper.TryParseBlockEntries(blocklist);
                        if (items is not null)
                        {
                            var host = evt.Domain.ToLowerInvariant();
                            var url = (evt.Url ?? "").ToLowerInvariant();
                            foreach (var be in items)
                            {
                                if (string.IsNullOrEmpty(be.I)) continue;
                                if (be.M == "t" && be.E is not null &&
                                    DateTime.TryParse(be.E, null, System.Globalization.DateTimeStyles.RoundtripKind, out var exp) &&
                                    DateTime.UtcNow >= exp) continue;
                                var pat = be.I.ToLowerInvariant();
                                if (host.Contains(pat) || url.Contains(pat))
                                {
                                    domainBlocked = true;
                                    LiveStatusStore.PendingFocusBlock = evt.Domain;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            if (!LiveStatusStore.Settings.TrackBrowser)
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(domainBlocked ? "{\"blocked\":true}" : "{\"ok\":true}");
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
            await ctx.Response.WriteAsync(domainBlocked ? "{\"ok\":true,\"blocked\":true}" : "{\"ok\":true}");
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
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
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
            var eodUtc = TimeZoneInfo.ConvertTimeToUtc(localDate.AddDays(1));

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT domain, start_time,
                       COALESCE(end_time, LEAD(start_time, 1, $eod) OVER (ORDER BY start_time)) AS next_time
                FROM browser_events
                WHERE local_date = $date
                ORDER BY start_time
                """;
            cmd.Parameters.AddWithValue("$date", dateStr);
            cmd.Parameters.AddWithValue("$eod", eodUtc.ToString("o"));

            var domainSecs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var domain = r.GetString(0);
                var start = DateTime.Parse(r.GetString(1), null, DateTimeStyles.RoundtripKind);
                var next = DateTime.Parse(r.GetString(2), null, DateTimeStyles.RoundtripKind);
                var secs = (next - start).TotalSeconds;
                if (secs > 0)
                {
                    var capped = Math.Min(secs, 3600); // cap at 1 hour per event to avoid outliers
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
            var localNow = DateTime.Now;
            var localDate = localNow.Date;
            var offsetHours = (int)Math.Round((localNow - DateTime.UtcNow).TotalHours);
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT CAST(strftime('%H', start_time) AS INTEGER) AS h, COUNT(*) AS cnt
                FROM browser_events
                WHERE local_date = $date
                GROUP BY h ORDER BY h
                """;
            cmd.Parameters.AddWithValue("$date", localDate.ToString("yyyy-MM-dd"));
            var counts = new int[24];
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var utcHour = r.GetInt32(0);
                var localHour = (utcHour + offsetHours + 24) % 24;
                counts[localHour] += r.GetInt32(1);
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
            var procs = System.Diagnostics.Process.GetProcesses()
                .Where(p =>
                    p.MainWindowHandle != IntPtr.Zero &&
                    !string.IsNullOrEmpty(p.ProcessName) &&
                    IsWindowVisible(p.MainWindowHandle) &&
                    !InfrastructureExes.Contains(p.ProcessName))
                .Select(p => p.ProcessName + ".exe")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
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
                System.IO.File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeLens", "query_error.log"), $"{DateTime.UtcNow:o} summary: {ex}{Environment.NewLine}");
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
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsync("""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>TimeLens · Extensions</title>
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&family=JetBrains+Mono:wght@400;500&display=swap" rel="stylesheet">
<link href="https://cdn.jsdelivr.net/npm/@tabler/icons-webfont@3/dist/tabler-icons.min.css" rel="stylesheet">
<style>
:root{
  --md-surface:#0D0F0A;--md-surface-1:#141810;--md-surface-2:#1C2118;--md-surface-3:#222819;
  --md-outline:rgba(255,255,255,.07);--md-outline-var:rgba(255,255,255,.14);
  --md-primary:#C8E86A;--md-on-primary:#151E00;--md-primary-cont:#1E2E00;--md-on-pri-cont:#D6F572;
  --md-secondary:#E8A23A;--md-sec-cont:#2C1E00;
  --md-tertiary:#7ECFA8;--md-ter-cont:#00472B;
  --md-error:#E07070;--md-err-cont:#3B1212;
  --md-on-surf:#E4E8DC;--md-on-surf-var:#8A9283;--md-on-surf-dim:#4A5145;
  --font-display:'Inter',sans-serif;--font-mono:'JetBrains Mono',monospace;
  --shape-sm:8px;--shape-md:12px;--shape-lg:16px;--shape-xl:28px;
  --sp-1:4px;--sp-2:8px;--sp-3:12px;--sp-4:16px;--sp-5:20px;--sp-6:24px
}
.theme-ember{--md-surface:#0F0A08;--md-surface-1:#1A1008;--md-surface-2:#23180E;--md-surface-3:#2C1E12;--md-primary:#FF8A65;--md-on-primary:#3B0D00;--md-primary-cont:#4A1504;--md-on-pri-cont:#FFB89A;--md-secondary:#FFAB40;--md-sec-cont:#421F00;--md-tertiary:#F6A0A0;--md-ter-cont:#571C1C}
.theme-rose{--md-surface:#0F080C;--md-surface-1:#1A0D14;--md-surface-2:#26141C;--md-surface-3:#301A24;--md-primary:#F48FB1;--md-on-primary:#3B0020;--md-primary-cont:#4E0A2D;--md-on-pri-cont:#FDB4CF}
.theme-moss{--md-surface:#0A0F0A;--md-surface-1:#101810;--md-surface-2:#162116;--md-surface-3:#1C291C;--md-primary:#81C784;--md-on-primary:#002202;--md-primary-cont:#0A320B;--md-on-pri-cont:#A5D6A7}
.theme-clay{--md-surface:#0E0C0B;--md-surface-1:#19140F;--md-surface-2:#221C15;--md-surface-3:#2B241B;--md-primary:#BCAAA4;--md-on-primary:#2E1510;--md-primary-cont:#3C221B;--md-on-pri-cont:#D7CCC8}
.theme-sunset{--md-surface:#0F0C08;--md-surface-1:#1A1508;--md-surface-2:#241E0E;--md-surface-3:#2E2714;--md-primary:#FFD54F;--md-on-primary:#2E2000;--md-primary-cont:#402C00;--md-on-pri-cont:#FFE082}
.theme-terminal{--md-surface:#080F08;--md-surface-1:#0C140C;--md-surface-2:#111A11;--md-surface-3:#162016;--md-primary:#39FF14;--md-on-primary:#003300;--md-primary-cont:#0A3300;--md-on-pri-cont:#66FF44}
.theme-copper{--md-surface:#0F0B08;--md-surface-1:#1A1208;--md-surface-2:#231A0E;--md-surface-3:#2C2214;--md-primary:#B87333;--md-on-primary:#1A0A00;--md-primary-cont:#2E1504;--md-on-pri-cont:#D4945A}
.theme-arctic{--md-surface:#090C0E;--md-surface-1:#10161A;--md-surface-2:#161E22;--md-surface-3:#1C262A;--md-primary:#7EC8C8;--md-on-primary:#002222;--md-primary-cont:#0A2E2E;--md-on-pri-cont:#A0DFDF}
.theme-crimson{--md-surface:#0F0808;--md-surface-1:#1A0D0D;--md-surface-2:#231414;--md-surface-3:#2C1A1A;--md-primary:#DC143C;--md-on-primary:#1A0004;--md-primary-cont:#2E000A;--md-on-pri-cont:#F06070}
.theme-gold{--md-surface:#0E0C07;--md-surface-1:#171208;--md-surface-2:#201A0D;--md-surface-3:#292212;--md-primary:#FFB000;--md-on-primary:#1A0F00;--md-primary-cont:#2E1A00;--md-on-pri-cont:#FFCC44}
*,*::before,*::after{box-sizing:border-box;margin:0;padding:0}
body{
  background:var(--md-surface);color:var(--md-on-surf);
  font-family:var(--font-display);font-size:14px;line-height:1.5;
  min-height:100vh;display:flex;
  -webkit-font-smoothing:antialiased
}
.rail{
  width:80px;background:var(--md-surface-1);
  border-right:1px solid var(--md-outline);
  display:flex;flex-direction:column;align-items:center;
  padding:var(--sp-3) 0;gap:var(--sp-1);flex-shrink:0
}
.rail-logo{
  width:40px;height:40px;background:var(--md-primary-cont);
  border-radius:var(--shape-md);display:flex;align-items:center;justify-content:center;
  margin-bottom:var(--sp-4)
}
.rail-logo i{color:var(--md-primary);font-size:20px}
.main{
  flex:1;overflow-y:auto;padding:var(--sp-6);
  display:flex;flex-direction:column;gap:var(--sp-4)
}
.page-header{margin-bottom:4px}
.page-header h1{font-size:24px;font-weight:600;letter-spacing:-.01em;line-height:1.2}
.page-header p{font-size:13px;color:var(--md-on-surf-var);margin-top:2px}
.card{
  background:var(--md-surface-1);border:1px solid var(--md-outline);
  border-radius:var(--shape-lg);padding:var(--sp-5)
}
.card-title{
  font-size:14px;font-weight:500;color:var(--md-on-surf);
  margin-bottom:var(--sp-4);display:flex;align-items:center;gap:var(--sp-2)
}
.card-title i{color:var(--md-on-surf-var);font-size:16px}
.steps-row{
  display:flex;gap:var(--sp-3);margin-bottom:var(--sp-4);flex-wrap:wrap
}
.step-chip{
  display:flex;align-items:center;gap:var(--sp-2);
  background:var(--md-surface-2);border:1px solid var(--md-outline);
  border-radius:var(--shape-xl);padding:var(--sp-2) var(--sp-4);
  font-size:13px;font-weight:500;color:var(--md-on-surf-var)
}
.step-num{
  width:24px;height:24px;border-radius:50%;
  background:var(--md-primary-cont);color:var(--md-on-pri-cont);
  display:flex;align-items:center;justify-content:center;
  font-size:12px;font-weight:700;flex-shrink:0
}
.browser-row{
  display:flex;align-items:center;gap:var(--sp-3);
  padding:var(--sp-3);border-radius:var(--shape-md);
  background:var(--md-surface-2);border:1px solid transparent;
  text-decoration:none;color:inherit;
  transition:all .15s;margin-bottom:var(--sp-2)
}
.browser-row:last-child{margin-bottom:0}
.browser-row:hover{
  background:var(--md-surface-3);border-color:var(--md-primary);
  transform:translateY(-1px)
}
.br-icon{
  width:42px;height:42px;border-radius:var(--shape-md);flex-shrink:0;
  display:flex;align-items:center;justify-content:center;
  font-size:16px;font-weight:700;color:#fff
}
.br-chrome{background:linear-gradient(135deg,#4285F4,#34A853)}
.br-edge{background:linear-gradient(135deg,#0078D4,#00BCF2)}
.br-brave{background:linear-gradient(135deg,#FB542B,#FF5500)}
.br-firefox{background:linear-gradient(135deg,#FF7139,#FFA436)}
.br-zen{background:linear-gradient(135deg,#7B68EE,#9370DB)}
.br-info{flex:1;min-width:0}
.br-name{font-size:13px;font-weight:600}
.br-hint{
  font-size:11px;color:var(--md-on-surf-dim);margin-top:2px;
  font-family:var(--font-mono)
}
.br-dl{
  display:flex;align-items:center;gap:6px;
  font-size:12px;font-weight:500;color:var(--md-primary);
  flex-shrink:0;opacity:0;transition:opacity .15s
}
.browser-row:hover .br-dl{opacity:1}
.section-divider{
  display:flex;align-items:center;gap:var(--sp-3);
  margin:var(--sp-4) 0 var(--sp-3)
}
.section-divider::after{
  content:'';flex:1;height:1px;background:var(--md-outline)
}
.section-label{
  font-size:12px;font-weight:500;color:var(--md-on-surf-var);
  text-transform:uppercase;letter-spacing:.05em;flex-shrink:0
}
.status-row{
  display:flex;align-items:center;gap:var(--sp-2);
  padding-top:var(--sp-4);border-top:1px solid var(--md-outline);
  font-size:12px;color:var(--md-on-surf-dim)
}
.status-dot{width:8px;height:8px;border-radius:50%;flex-shrink:0}
.status-dot.online{background:var(--md-tertiary);box-shadow:0 0 6px var(--md-tertiary)}
.status-dot.offline{background:var(--md-error)}
.status-dot.checking{background:var(--md-secondary);animation:pulse 1.2s ease-in-out infinite}
@keyframes pulse{0%,100%{opacity:1}50%{opacity:.3}}
.footer-note{margin-top:var(--sp-4);font-size:11px;color:var(--md-on-surf-dim);line-height:1.6}
.footer-note a{color:var(--md-on-surf-var)}
@media(max-width:600px){body{flex-direction:column}.rail{width:100%;flex-direction:row;padding:var(--sp-2);gap:var(--sp-2)}.main{padding:var(--sp-3)}.rail-logo{margin-bottom:0;margin-right:auto}}
</style>
</head>
<body>

<div class="rail">
  <div class="rail-logo">
    <i class="ti ti-clock-hour-4"></i>
  </div>
</div>

<main class="main">

  <div class="page-header">
    <h1>Browser Extensions</h1>
    <p>Install the extension to track tabs, domains, and audible media in your activity timeline.</p>
  </div>

  <div class="steps-row">
    <div class="step-chip"><span class="step-num">1</span> Download the zip</div>
    <div class="step-chip"><span class="step-num">2</span> Extract to a folder</div>
    <div class="step-chip"><span class="step-num">3</span> Load unpacked in browser</div>
  </div>

  <div class="card">
    <div class="card-title">
      <i class="ti ti-brand-chrome"></i>
      Chromium Browsers
    </div>

    <a class="browser-row" href="https://github.com/YumiNoona/TimeLens/releases/latest/download/TimeLens-extension-chrome.zip" target="_blank">
      <div class="br-icon br-chrome">C</div>
      <div class="br-info">
        <div class="br-name">Google Chrome</div>
        <div class="br-hint">chrome://extensions</div>
      </div>
      <div class="br-dl"><i class="ti ti-download"></i> Download</div>
    </a>

    <a class="browser-row" href="https://github.com/YumiNoona/TimeLens/releases/latest/download/TimeLens-extension-chrome.zip" target="_blank">
      <div class="br-icon br-edge">E</div>
      <div class="br-info">
        <div class="br-name">Microsoft Edge</div>
        <div class="br-hint">edge://extensions</div>
      </div>
      <div class="br-dl"><i class="ti ti-download"></i> Download</div>
    </a>

    <a class="browser-row" href="https://github.com/YumiNoona/TimeLens/releases/latest/download/TimeLens-extension-chrome.zip" target="_blank">
      <div class="br-icon br-brave">B</div>
      <div class="br-info">
        <div class="br-name">Brave / Arc / Opera / Vivaldi</div>
        <div class="br-hint">Any Chromium extensions page</div>
      </div>
      <div class="br-dl"><i class="ti ti-download"></i> Download</div>
    </a>

    <div class="section-divider">
      <span class="section-label">Firefox Family</span>
    </div>

    <a class="browser-row" href="https://github.com/YumiNoona/TimeLens/releases/latest/download/TimeLens-extension-firefox.zip" target="_blank">
      <div class="br-icon br-firefox">F</div>
      <div class="br-info">
        <div class="br-name">Mozilla Firefox</div>
        <div class="br-hint">about:debugging</div>
      </div>
      <div class="br-dl"><i class="ti ti-download"></i> Download</div>
    </a>

    <a class="browser-row" href="https://github.com/YumiNoona/TimeLens/releases/latest/download/TimeLens-extension-firefox.zip" target="_blank">
      <div class="br-icon br-zen">Z</div>
      <div class="br-info">
        <div class="br-name">Zen Browser</div>
        <div class="br-hint">about:debugging</div>
      </div>
      <div class="br-dl"><i class="ti ti-download"></i> Download</div>
    </a>

    <div class="status-row">
      <div class="status-dot checking" id="sd"></div>
      <span id="st">Checking tray app...</span>
    </div>
  </div>

  <div class="footer-note">
    After extracting the zip, go to your browser's extensions page, enable <strong>Developer mode</strong>, and click <strong>Load unpacked</strong> — select the extracted folder.
    <a href="https://github.com/YumiNoona/TimeLens/blob/master/docs/how-to-install-extension.md" target="_blank">Full installation guide</a>.
  </div>

</main>

<script>
  fetch('/api/settings')
    .then(function(r){ return r.ok ? r.json() : null })
    .then(function(s){
      if(s && s.theme && s.theme !== 'default')
        document.documentElement.className = 'theme-' + s.theme;
      document.getElementById('sd').className = 'status-dot online';
      document.getElementById('st').textContent = 'Tray app running — extension will connect on install';
    })
    .catch(function(){
      document.getElementById('sd').className = 'status-dot offline';
      document.getElementById('st').textContent = 'Tray app not detected — start TimeLens.TrayApp.exe first';
    });
</script>

</body>
</html>
""");
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
            if (range == "30days")
            {
                cmd.CommandText = """
                    SELECT start_time, exe_name, window_title, category, session_state,
                           COALESCE((julianday(COALESCE(end_time, datetime('now'))) - julianday(start_time)) * 86400, 0) AS duration_secs
                    FROM app_events
                    WHERE start_time >= date('now', '-30 days')
                    ORDER BY start_time
                    """;
            }
            else if (range != "today")
            {
                cmd.CommandText = """
                    SELECT start_time, exe_name, window_title, category, session_state,
                           COALESCE((julianday(COALESCE(end_time, datetime('now'))) - julianday(start_time)) * 86400, 0) AS duration_secs
                    FROM app_events
                    WHERE start_time >= $t0 AND start_time < $t1
                    ORDER BY start_time
                    """;
                if (DateTime.TryParse(range, out var d))
                {
                    var d0 = TimeZoneInfo.ConvertTimeToUtc(d.Date);
                    var d1 = d0.AddDays(1);
                    cmd.Parameters.AddWithValue("$t0", d0.ToString("o"));
                    cmd.Parameters.AddWithValue("$t1", d1.ToString("o"));
                }
                else
                {
                    cmd.CommandText = """
                        SELECT start_time, exe_name, window_title, category, session_state,
                               COALESCE((julianday(COALESCE(end_time, datetime('now'))) - julianday(start_time)) * 86400, 0) AS duration_secs
                        FROM app_events
                        WHERE start_time >= date('now', 'start of day')
                        ORDER BY start_time
                        """;
                }
            }
            else
            {
                cmd.CommandText = """
                    SELECT start_time, exe_name, window_title, category, session_state,
                           COALESCE((julianday(COALESCE(end_time, datetime('now'))) - julianday(start_time)) * 86400, 0) AS duration_secs
                    FROM app_events
                    WHERE start_time >= date('now', 'start of day')
                    ORDER BY start_time
                    """;
            }

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
            using var sr = new System.IO.StreamReader(ctx.Request.Body);
            var body = await sr.ReadToEndAsync();
            var doc = System.Text.Json.JsonDocument.Parse(body);
            var exe = doc.RootElement.GetProperty("exe").GetString();
            if (!string.IsNullOrEmpty(exe))
                enforceBlock?.Invoke(exe);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapGet("/api/block/stats", async (HttpContext ctx) =>
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT blocked_exe, blocked_action, COUNT(*) as cnt
                FROM block_log
                WHERE timestamp >= date('now', 'start of day')
                GROUP BY blocked_exe, blocked_action
                ORDER BY cnt DESC
                """;
            using var arr = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
            arr.WriteStartArray();
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                arr.WriteStartObject();
                arr.WriteString("exe", r.GetString(0));
                arr.WriteString("action", r.GetString(1));
                arr.WriteNumber("count", r.GetInt32(2));
                arr.WriteEndObject();
            }
            arr.WriteEndArray();
            await arr.FlushAsync();
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
        });

        await app.RunAsync(ct);
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
internal partial class AppJsonContext : JsonSerializerContext { }

internal sealed record BlockEntry(string I, string M, string? E);

internal static class BlockEntryHelper
{
    public static BlockEntry[]? TryParseBlockEntries(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return null;
        try
        {
            var entries = System.Text.Json.JsonSerializer.Deserialize<BlockEntry[]>(json);
            if (entries is not null) return entries;
        }
        catch { }
        // Fallback: legacy string[] format
        try
        {
            var legacy = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
            if (legacy is null) return null;
            return legacy.Select(s => new BlockEntry(s, "u", null)).ToArray();
        }
        catch { }
        return null;
    }
}
