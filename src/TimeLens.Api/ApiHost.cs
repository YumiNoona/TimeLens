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
        Action<string>? deleteRule = null)
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
            cmd.CommandText = "SELECT exe_pattern, category FROM custom_rules ORDER BY exe_pattern";
            using var reader = await cmd.ExecuteReaderAsync();
            using var arr = new System.Text.Json.Utf8JsonWriter(ctx.Response.BodyWriter);
            arr.WriteStartArray();
            while (await reader.ReadAsync())
            {
                arr.WriteStartObject();
                arr.WriteString("pattern", reader.GetString(0));
                arr.WriteString("category", reader.GetString(1));
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

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO custom_rules (exe_pattern, category) VALUES ($pattern, $category)";
            cmd.Parameters.AddWithValue("$pattern", pattern);
            cmd.Parameters.AddWithValue("$category", category);
            await cmd.ExecuteNonQueryAsync();

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
                return;
            }

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO browser_events (domain, url, title, start_time, browser)
                VALUES ($domain, $url, $title, $start, $browser)
                """;
            cmd.Parameters.AddWithValue("$domain", evt.Domain);
            cmd.Parameters.AddWithValue("$url", evt.Url);
            cmd.Parameters.AddWithValue("$title", evt.Title);
            cmd.Parameters.AddWithValue("$start", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$browser", evt.Browser);
            await cmd.ExecuteNonQueryAsync();

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
<title>TimeLens · Browser Extensions</title>
<style>
:root {
  --md-surface:#0D0F0A;--md-surface-1:#141810;--md-surface-2:#1C2118;--md-surface-3:#222819;
  --md-outline:rgba(255,255,255,.07);--md-outline-var:rgba(255,255,255,.14);
  --md-primary:#C8E86A;--md-on-primary:#151E00;--md-primary-cont:#1E2E00;--md-on-pri-cont:#D6F572;
  --md-secondary:#E8A23A;--md-on-secondary:#211500;--md-sec-cont:#2C1E00;
  --md-tertiary:#7ECFA8;--md-on-tertiary:#00331F;--md-ter-cont:#00472B;
  --md-on-surf:#E4E8DC;--md-on-surf-var:#8A9283;--md-on-surf-dim:#4A5145;
  --font-display:Inter,system-ui,-apple-system,sans-serif;--font-mono:'JetBrains Mono',monospace;
  --shape-sm:8px;--shape-md:12px;--shape-lg:16px;--shape-full:9999px;
  --sp-1:4px;--sp-2:8px;--sp-3:12px;--sp-4:16px;--sp-5:20px;--sp-6:24px;
}
.theme-ember{--md-surface:#0F0A08;--md-surface-1:#1A1008;--md-surface-2:#23180E;--md-surface-3:#2C1E12;--md-primary:#FF8A65;--md-on-primary:#3B0D00;--md-primary-cont:#4A1504;--md-on-pri-cont:#FFB89A;--md-secondary:#FFAB40;--md-on-secondary:#2E1500;--md-sec-cont:#421F00;--md-tertiary:#F6A0A0;--md-on-tertiary:#401010;--md-ter-cont:#571C1C}
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
  min-height:100vh;padding:24px;
  display:flex;flex-direction:column;align-items:center;
  -webkit-font-smoothing:antialiased
}
.logo{display:flex;align-items:center;gap:10px;margin-bottom:4px}
.logo-icon{
  width:36px;height:36px;border-radius:10px;
  background:linear-gradient(135deg,var(--md-primary),var(--md-tertiary));
  display:flex;align-items:center;justify-content:center;
  font-size:18px;color:#1A1A1A;font-weight:700
}
.logo-text{font-size:20px;font-weight:700;letter-spacing:-.02em}
.subtitle{font-size:13px;color:var(--md-on-surf-var);margin-bottom:8px;max-width:420px;text-align:center;line-height:1.5}
.steps{display:flex;gap:var(--sp-2);margin-bottom:28px}
.step{
  display:flex;align-items:center;gap:6px;
  font-size:11px;color:var(--md-on-surf-dim);
  background:var(--md-surface-1);border:1px solid var(--md-outline);
  border-radius:var(--shape-full);padding:4px 12px
}
.step-num{
  width:18px;height:18px;border-radius:50%;
  background:var(--md-primary-cont);color:var(--md-on-pri-cont);
  display:flex;align-items:center;justify-content:center;
  font-size:10px;font-weight:700
}
.module{
  background:var(--md-surface-1);border:1px solid var(--md-outline);
  border-radius:var(--shape-lg);padding:28px;width:100%;max-width:620px;
  display:flex;flex-direction:column;gap:16px
}
.module-header{
  display:flex;align-items:flex-start;gap:16px;
  padding-bottom:16px;border-bottom:1px solid var(--md-outline)
}
.module-icon{
  width:52px;height:52px;border-radius:14px;flex-shrink:0;
  display:flex;align-items:center;justify-content:center;
  font-size:22px;font-weight:700
}
.ic-chrome{background:#1a2e0a;color:var(--md-primary)}
.ic-firefox{background:#2e1a00;color:var(--md-secondary)}
.module-title{font-size:16px;font-weight:600;letter-spacing:-.01em}
.module-desc{font-size:12px;color:var(--md-on-surf-var);margin-top:2px}
.browser-card{
  background:var(--md-surface-2);border:1px solid rgba(255,255,255,.05);
  border-radius:12px;padding:14px 16px;display:flex;
  align-items:center;gap:14px;transition:all .15s;
  text-decoration:none;color:inherit
}
.browser-card:hover{background:var(--md-surface-3);border-color:var(--md-primary);transform:translateY(-1px)}
.browser-card+.browser-card{margin-top:8px}
.bc-icon{
  width:44px;height:44px;border-radius:12px;flex-shrink:0;
  display:flex;align-items:center;justify-content:center;
  font-size:18px;font-weight:700
}
.bc-chrome{background:linear-gradient(135deg,#4285F4,#34A853);color:#fff}
.bc-edge{background:linear-gradient(135deg,#0078D4,#00BCF2);color:#fff}
.bc-brave{background:linear-gradient(135deg,#FB542B,#FF5500);color:#fff}
.bc-firefox{background:linear-gradient(135deg,#FF7139,#FFA436);color:#fff}
.bc-zen{background:linear-gradient(135deg,#7B68EE,#9370DB);color:#fff}
.bc-info{flex:1;min-width:0}
.bc-name{font-size:13px;font-weight:600}
.bc-detail{font-size:11px;color:var(--md-on-surf-dim);margin-top:1px}
.bc-detail code{
  font-family:var(--font-mono);font-size:10px;
  background:rgba(255,255,255,.04);padding:1px 6px;border-radius:4px
}
.bc-dl{
  display:flex;align-items:center;gap:4px;
  font-size:11px;color:var(--md-on-surf-dim);flex-shrink:0;
  transition:color .15s,transform .15s
}
.bc-dl i{font-size:14px}
.browser-card:hover .bc-dl{color:var(--md-primary);transform:translateX(3px)}
.status-bar{
  display:flex;align-items:center;gap:8px;
  padding-top:16px;border-top:1px solid var(--md-outline);
  font-size:11px;color:var(--md-on-surf-dim)
}
.status-dot{width:8px;height:8px;border-radius:50%;flex-shrink:0}
.status-dot.online{background:var(--md-tertiary);box-shadow:0 0 6px var(--md-tertiary)}
.status-dot.offline{background:#E07070}
.status-dot.checking{background:var(--md-secondary);animation:pulse 1.2s ease-in-out infinite}
@keyframes pulse{0%,100%{opacity:1}50%{opacity:.3}}
.footer{
  margin-top:24px;font-size:11px;color:var(--md-on-surf-dim);
  max-width:620px;width:100%;text-align:center
}
.footer a{color:var(--md-on-surf-var);text-decoration:underline}
.footer a:hover{color:var(--md-primary)}
@media(max-width:500px){
  .module{padding:20px;gap:12px}
  .steps{flex-wrap:wrap}
}
</style>
</head>
<body>

<div class="logo">
  <div class="logo-icon">T</div>
  <span class="logo-text">TimeLens</span>
</div>
<p class="subtitle">Install the browser extension to track tabs, domains, and audible media in your activity timeline.</p>

<div class="steps">
  <div class="step"><span class="step-num">1</span> Download</div>
  <div class="step"><span class="step-num">2</span> Extract</div>
  <div class="step"><span class="step-num">3</span> Load unpacked</div>
</div>

<div class="module">

  <div class="module-header">
    <div class="module-icon ic-chrome">C</div>
    <div>
      <div class="module-title">Chromium Browsers</div>
      <div class="module-desc">Same extension works across all Chromium-based browsers. Download the zip, extract, then load it unpacked in 3 clicks.</div>
    </div>
  </div>

  <a class="browser-card" href="https://github.com/YumiNoona/TimeLens/releases/latest/download/TimeLens-extension-chrome.zip" target="_blank">
    <div class="bc-icon bc-chrome">C</div>
    <div class="bc-info">
      <div class="bc-name">Google Chrome</div>
      <div class="bc-detail">Load at <code>chrome://extensions</code> → Developer mode → Load unpacked</div>
    </div>
    <div class="bc-dl"><i class="ti ti-download"></i> Download</div>
  </a>

  <a class="browser-card" href="https://github.com/YumiNoona/TimeLens/releases/latest/download/TimeLens-extension-chrome.zip" target="_blank">
    <div class="bc-icon bc-edge">E</div>
    <div class="bc-info">
      <div class="bc-name">Microsoft Edge</div>
      <div class="bc-detail">Load at <code>edge://extensions</code> → Developer mode → Load unpacked</div>
    </div>
    <div class="bc-dl"><i class="ti ti-download"></i> Download</div>
  </a>

  <a class="browser-card" href="https://github.com/YumiNoona/TimeLens/releases/latest/download/TimeLens-extension-chrome.zip" target="_blank">
    <div class="bc-icon bc-brave">B</div>
    <div class="bc-info">
      <div class="bc-name">Brave / Arc / Opera / Vivaldi</div>
      <div class="bc-detail">All Chromium-based — same zip, same <code>extensions</code> page flow</div>
    </div>
    <div class="bc-dl"><i class="ti ti-download"></i> Download</div>
  </a>

  <div class="module-header" style="margin-top:8px;padding-top:16px;border-top:1px solid var(--md-outline)">
    <div class="module-icon ic-firefox">FF</div>
    <div>
      <div class="module-title">Firefox Family</div>
      <div class="module-desc">Manifest V2 extension. Firefox requires temporary loading through the debug page or AMO signing.</div>
    </div>
  </div>

  <a class="browser-card" href="https://github.com/YumiNoona/TimeLens/releases/latest/download/TimeLens-extension-firefox.zip" target="_blank">
    <div class="bc-icon bc-firefox">F</div>
    <div class="bc-info">
      <div class="bc-name">Mozilla Firefox</div>
      <div class="bc-detail">Load at <code>about:debugging</code> → This Firefox → Load Temporary Add-on</div>
    </div>
    <div class="bc-dl"><i class="ti ti-download"></i> Download</div>
  </a>

  <a class="browser-card" href="https://github.com/YumiNoona/TimeLens/releases/latest/download/TimeLens-extension-firefox.zip" target="_blank">
    <div class="bc-icon bc-zen">Z</div>
    <div class="bc-info">
      <div class="bc-name">Zen Browser</div>
      <div class="bc-detail">Load at <code>about:debugging</code> → This Firefox → Load Temporary Add-on</div>
    </div>
    <div class="bc-dl"><i class="ti ti-download"></i> Download</div>
  </a>

  <div class="status-bar" id="status-bar">
    <div class="status-dot checking" id="status-dot"></div>
    <span id="status-text">Checking tray app connection...</span>
  </div>

</div>

<div class="footer">
  Extracted the zip? Open your browser's extensions page, enable <strong>Developer mode</strong>, and click <strong>Load unpacked</strong> — select the folder. For full instructions see the <a href="https://github.com/YumiNoona/TimeLens/blob/master/docs/how-to-install-extension.md" target="_blank">installation guide</a>.
</div>

<script>
  // Apply saved theme
  fetch('http://127.0.0.1:47821/api/settings')
    .then(r => r.ok ? r.json() : null)
    .then(s => {
      if (s && s.theme && s.theme !== 'default') {
        document.documentElement.className = 'theme-' + s.theme;
      }
      // Check tray connection
      document.getElementById('status-dot').className = 'status-dot online';
      document.getElementById('status-text').textContent = 'TimeLens tray app is running — extension will connect on install';
    })
    .catch(() => {
      document.getElementById('status-dot').className = 'status-dot offline';
      document.getElementById('status-text').textContent = 'Tray app not detected — start TimeLens.TrayApp.exe first';
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
internal partial class AppJsonContext : JsonSerializerContext { }
