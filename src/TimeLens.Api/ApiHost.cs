using System.Collections.Concurrent;
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

    private static readonly ConcurrentDictionary<int, long> OpenBrowserEvents = new();

    private static readonly HashSet<string> InfrastructureExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost", "TextInputHost", "SystemSettings", "RuntimeBroker",
        "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost", "ctfmon",
        "conhost", "fontdrvhost",
        "svchost", "dwm", "csrss", "smss", "wininit", "winlogon", "services",
        "lsass", "spoolsv", "taskhostw", "sihost",
        "TimeLens.TrayApp", "NVDisplay.Container", "NVIDIA Share", "nvsphelper64",
        "explorer", "CalculatorApp",
    };
    public static DateTime LastActivityUtc { get; private set; } = DateTime.MinValue;

    public static async Task StartAsync(string dbPath, CancellationToken ct = default,
        Action<string, string>? saveSetting = null,
        Action<bool>? setTrackAudio = null,
        Action<bool>? setTrackInput = null,
        Action<string, string>? upsertRule = null,
        Action<string>? deleteRule = null,
        Action<string>? enforceBlock = null)
    {
        var dashboardPath = Path.Combine(
            AppContext.BaseDirectory, "dashboard");

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:47821");

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

            upsertRule?.Invoke(pattern, category);

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
            if (!LiveStatusStore.Settings.TrackBrowser)
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"ok\":true}");
                return;
            }

            var evt = await ctx.Request.ReadFromJsonAsync<BrowserEventDto>(AppJsonContext.Default.BrowserEventDto);
            if (evt is null)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.ContentType = "application/json";
                return;
            }

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            // Close previous event for this tab (if any)
            if (evt.TabId > 0 && OpenBrowserEvents.TryRemove(evt.TabId, out var prevEventId))
            {
                using var closeCmd = conn.CreateCommand();
                closeCmd.CommandText = "UPDATE browser_events SET end_time = $now WHERE id = $id AND end_time IS NULL";
                closeCmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                closeCmd.Parameters.AddWithValue("$id", prevEventId);
                await closeCmd.ExecuteNonQueryAsync();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO browser_events (domain, url, title, start_time, end_time, browser, tab_id)
                VALUES ($domain, $url, $title, $start, NULL, $browser, $tabId)
                """;
            cmd.Parameters.AddWithValue("$domain", evt.Domain);
            cmd.Parameters.AddWithValue("$url", evt.Url);
            cmd.Parameters.AddWithValue("$title", evt.Title);
            cmd.Parameters.AddWithValue("$start", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$browser", evt.Browser);
            cmd.Parameters.AddWithValue("$tabId", evt.TabId);
            await cmd.ExecuteNonQueryAsync();

            // Track the new event ID for this tab for duration tracking
            if (evt.TabId > 0)
            {
                using var getIdCmd = conn.CreateCommand();
                getIdCmd.CommandText = "SELECT last_insert_rowid()";
                var newEventId = (long)(await getIdCmd.ExecuteScalarAsync())!;
                OpenBrowserEvents[evt.TabId] = newEventId;
            }

            // Focus mode check — blocklist match against domain or URL
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
                                if (be.M == "t" && be.E is not null &&
                                    DateTime.TryParse(be.E, null, System.Globalization.DateTimeStyles.RoundtripKind, out var exp) &&
                                    DateTime.UtcNow >= exp) continue;
                                var pattern = be.I.ToLowerInvariant();
                                if (host.Contains(pattern) || host.EndsWith("." + pattern) || url.Contains(pattern))
                                {
                                    LiveStatusStore.PendingFocusBlock = evt.Domain;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"ok\":true}");
        });

        app.MapPost("/api/browser-leave", async (HttpContext ctx) =>
        {
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
            var root = doc.RootElement;
            if (root.TryGetProperty("tabId", out var tabProp) && tabProp.TryGetInt32(out var tabId) && tabId > 0)
            {
                if (OpenBrowserEvents.TryRemove(tabId, out var eventId))
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
            var today = TimeZoneInfo.ConvertTimeToUtc(localDate);
            var tomorrow = TimeZoneInfo.ConvertTimeToUtc(localDate.AddDays(1));
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT domain, COUNT(*), MAX(start_time) FROM browser_events WHERE start_time>=$t0 AND start_time<$t1 GROUP BY domain ORDER BY 2 DESC LIMIT 20";
            cmd.Parameters.AddWithValue("$t0", today.ToString("o"));
            cmd.Parameters.AddWithValue("$t1", tomorrow.ToString("o"));
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
            var today = TimeZoneInfo.ConvertTimeToUtc(localDate);
            var tomorrow = TimeZoneInfo.ConvertTimeToUtc(localDate.AddDays(1));

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            // Estimate time per domain by assuming each event covers until the next event (or end of day)
            cmd.CommandText = """
                SELECT domain, start_time,
                       COALESCE(end_time, LEAD(start_time, 1, $eod) OVER (PARTITION BY DATE(start_time) ORDER BY start_time)) AS next_time
                FROM browser_events
                WHERE start_time >= $t0 AND start_time < $t1
                ORDER BY start_time
                """;
            cmd.Parameters.AddWithValue("$t0", today.ToString("o"));
            cmd.Parameters.AddWithValue("$t1", tomorrow.ToString("o"));
            cmd.Parameters.AddWithValue("$eod", today.AddDays(1).ToString("o"));

            var domainSecs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var domain = r.GetString(0);
                var start = DateTime.Parse(r.GetString(1), null, DateTimeStyles.RoundtripKind);
                var next = DateTime.Parse(r.GetString(2), null, DateTimeStyles.RoundtripKind);
                var secs = (next - start).TotalSeconds;
                if (secs > 0 && secs < 3600) // cap at 1 hour per event to avoid outliers
                {
                    domainSecs.TryGetValue(domain, out var cur);
                    domainSecs[domain] = cur + secs;
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
            var svc = new AnalyticsService(dbPath);
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
  fetch('http://127.0.0.1:47821/api/settings')
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

        app.MapGet("/api/db-size", (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "application/json";
            ctx.Response.StatusCode = 200;
            var size = System.IO.File.Exists(dbPath) ? new System.IO.FileInfo(dbPath).Length : 0;
            return ctx.Response.WriteAsync($"{{\"sizeBytes\":{size}}}");
        });

        app.MapGet("/api/export", async (HttpContext ctx) =>
        {
            var format = ctx.Request.Query["format"].FirstOrDefault() ?? "csv";
            ctx.Response.ContentType = format == "json" ? "application/json" : "text/csv";
            ctx.Response.Headers.Append("Content-Disposition", $"attachment; filename=timelens-export.{format}");

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT start_time, exe_name, window_title, category, session_state,
                       COALESCE((julianday(COALESCE(end_time, datetime('now'))) - julianday(start_time)) * 86400, 0) AS duration_secs
                FROM app_events
                WHERE start_time >= date('now', 'start of day')
                ORDER BY start_time
                """;

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
