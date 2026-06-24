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
            if (dateParam is not null && DateTime.TryParse(dateParam, out var parsed)) queryDate = parsed;
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
            if (dateParam is not null && DateTime.TryParse(dateParam, out var parsed)) queryDate = parsed;
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
            if (dateParam is not null && DateTime.TryParse(dateParam, out var parsed)) queryDate = parsed;
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
                queryDate = parsed;
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
  *,*::before,*::after{box-sizing:border-box;margin:0;padding:0}
  body{
    background:#0D0F0A;color:#E4E8DC;
    font-family:Inter,system-ui,-apple-system,sans-serif;
    font-size:14px;line-height:1.5;
    min-height:100vh;padding:24px;
    display:flex;flex-direction:column;align-items:center;
    -webkit-font-smoothing:antialiased
  }
  .logo{
    display:flex;align-items:center;gap:10px;
    margin-bottom:8px
  }
  .logo-icon{
    width:36px;height:36px;border-radius:10px;
    background:linear-gradient(135deg,#C8E86A,#81C784);
    display:flex;align-items:center;justify-content:center;
    font-size:18px;color:#1A1A1A;font-weight:700
  }
  .logo-text{font-size:20px;font-weight:700;letter-spacing:-.02em}
  .subtitle{
    font-size:13px;color:#8A9283;margin-bottom:32px;
    max-width:400px;text-align:center;line-height:1.5
  }
  .module{
    background:#141810;border:1px solid rgba(255,255,255,.07);
    border-radius:16px;padding:28px;width:100%;max-width:620px;
    display:flex;flex-direction:column;gap:16px
  }
  .module-header{
    display:flex;align-items:flex-start;gap:16px;
    padding-bottom:16px;
    border-bottom:1px solid rgba(255,255,255,.06)
  }
  .module-icon{
    width:52px;height:52px;border-radius:14px;flex-shrink:0;
    display:flex;align-items:center;justify-content:center;
    font-size:26px
  }
  .module-icon.ic-chrome{background:#1a2e0a;color:#C8E86A}
  .module-icon.ic-firefox{background:#2e1a00;color:#FFB74D}
  .module-title{
    font-size:16px;font-weight:600;letter-spacing:-.01em
  }
  .module-desc{
    font-size:12px;color:#8A9283;margin-top:2px
  }
  .browser-card{
    background:#1C2118;border:1px solid rgba(255,255,255,.05);
    border-radius:12px;padding:16px;display:flex;
    align-items:center;gap:14px;transition:all .15s;
    text-decoration:none;color:inherit
  }
  .browser-card:hover{
    background:#222819;border-color:#C8E86A;
    transform:translateY(-1px)
  }
  .browser-card+.browser-card{margin-top:8px}
  .bc-icon{
    width:44px;height:44px;border-radius:12px;flex-shrink:0;
    display:flex;align-items:center;justify-content:center;
    font-size:22px;font-weight:700
  }
  .bc-chrome{background:linear-gradient(135deg,#4285F4,#34A853);color:#fff}
  .bc-edge{background:linear-gradient(135deg,#0078D4,#00BCF2);color:#fff}
  .bc-brave{background:linear-gradient(135deg,#FB542B,#FF5500);color:#fff}
  .bc-firefox{background:linear-gradient(135deg,#FF7139,#FFA436);color:#fff}
  .bc-zen{background:linear-gradient(135deg,#7B68EE,#9370DB);color:#fff}
  .bc-info{flex:1;min-width:0}
  .bc-name{font-size:13px;font-weight:600}
  .bc-detail{font-size:11px;color:#4A5145;margin-top:1px}
  .bc-detail code{
    font-family:'JetBrains Mono','Fira Code',monospace;
    font-size:10px;background:rgba(255,255,255,.04);
    padding:1px 6px;border-radius:4px
  }
  .bc-arrow{
    font-size:11px;color:#4A5145;flex-shrink:0;
    transition:color .15s,transform .15s
  }
  .browser-card:hover .bc-arrow{color:#C8E86A;transform:translateX(3px)}
  .status-bar{
    display:flex;align-items:center;gap:8px;
    padding-top:16px;border-top:1px solid rgba(255,255,255,.06);
    font-size:11px;color:#4A5145
  }
  .status-dot{width:8px;height:8px;border-radius:50%;flex-shrink:0}
  .status-dot.online{background:#81C784;box-shadow:0 0 6px #81C784}
  .status-dot.offline{background:#E07070}
  .footer{
    margin-top:24px;font-size:11px;color:#4A5145;
    max-width:620px;width:100%;text-align:center
  }
  .footer a{color:#8A9283;text-decoration:underline}
  .footer a:hover{color:#C8E86A}
  @media(max-width:500px){
    .module{padding:20px;gap:12px}
    .browser-card{padding:14px}
  }
</style>
</head>
<body>

<div class="logo">
  <div class="logo-icon">T</div>
  <span class="logo-text">TimeLens</span>
</div>
<p class="subtitle">
  Install the browser extension to track tabs, domains, and audible media in your activity timeline.
</p>

<div class="module">

  <!-- Chromium section -->
  <div class="module-header">
    <div class="module-icon ic-chrome">&#x25D0;</div>
    <div>
      <div class="module-title">Chromium Browsers</div>
      <div class="module-desc">Same extension works across all Chromium-based browsers. Download the zip, then load it unpacked in 3 clicks.</div>
    </div>
  </div>

  <a class="browser-card" href="https://github.com/anomalyco/TimeLens/releases/latest/download/TimeLens-extension-chrome.zip" target="_blank">
    <div class="bc-icon bc-chrome">C</div>
    <div class="bc-info">
      <div class="bc-name">Google Chrome</div>
      <div class="bc-detail">Load at <code>chrome://extensions</code> &rarr; Developer mode &rarr; Load unpacked</div>
    </div>
    <div class="bc-arrow">&rarr; Download</div>
  </a>

  <a class="browser-card" href="https://github.com/anomalyco/TimeLens/releases/latest/download/TimeLens-extension-chrome.zip" target="_blank">
    <div class="bc-icon bc-edge">E</div>
    <div class="bc-info">
      <div class="bc-name">Microsoft Edge</div>
      <div class="bc-detail">Load at <code>edge://extensions</code> &rarr; Developer mode &rarr; Load unpacked</div>
    </div>
    <div class="bc-arrow">&rarr; Download</div>
  </a>

  <a class="browser-card" href="https://github.com/anomalyco/TimeLens/releases/latest/download/TimeLens-extension-chrome.zip" target="_blank">
    <div class="bc-icon bc-brave">B</div>
    <div class="bc-info">
      <div class="bc-name">Brave / Arc / Opera / Vivaldi</div>
      <div class="bc-detail">All Chromium-based — same zip, same <code>extensions</code> page flow</div>
    </div>
    <div class="bc-arrow">&rarr; Download</div>
  </a>

  <!-- Firefox section -->
  <div class="module-header" style="margin-top:8px;padding-top:16px;border-top:1px solid rgba(255,255,255,.06)">
    <div class="module-icon ic-firefox">&#x1F98A;</div>
    <div>
      <div class="module-title">Firefox Family</div>
      <div class="module-desc">Manifest V2 extension. Firefox requires temporary loading through the debug page or AMO signing.</div>
    </div>
  </div>

  <a class="browser-card" href="https://github.com/anomalyco/TimeLens/releases/latest/download/TimeLens-extension-firefox.zip" target="_blank">
    <div class="bc-icon bc-firefox">F</div>
    <div class="bc-info">
      <div class="bc-name">Mozilla Firefox</div>
      <div class="bc-detail">Load at <code>about:debugging</code> &rarr; This Firefox &rarr; Load Temporary Add-on</div>
    </div>
    <div class="bc-arrow">&rarr; Download</div>
  </a>

  <a class="browser-card" href="https://github.com/anomalyco/TimeLens/releases/latest/download/TimeLens-extension-firefox.zip" target="_blank">
    <div class="bc-icon bc-zen">Z</div>
    <div class="bc-info">
      <div class="bc-name">Zen Browser</div>
      <div class="bc-detail">Load at <code>about:debugging</code> &rarr; This Firefox &rarr; Load Temporary Add-on</div>
    </div>
    <div class="bc-arrow">&rarr; Download</div>
  </a>

  <!-- Status -->
  <div class="status-bar" id="status-bar">
    <div class="status-dot" id="status-dot"></div>
    <span id="status-text">Checking tray app connection...</span>
  </div>

</div>

<div class="footer">
  Extracted the zip? Open your browser's extensions page, enable <strong>Developer mode</strong>, and click <strong>Load unpacked</strong> — select the folder. For full instructions see the <a href="https://github.com/anomalyco/TimeLens/blob/master/docs/how-to-install-extension.md" target="_blank">installation guide</a>.
</div>

<script>
  fetch('http://127.0.0.1:47821/api/settings')
    .then(r => {
      if (r.ok) {
        document.getElementById('status-dot').className = 'status-dot online';
        document.getElementById('status-text').textContent = 'TimeLens tray app is running — extension will connect on install';
      } else { throw new Error(); }
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
