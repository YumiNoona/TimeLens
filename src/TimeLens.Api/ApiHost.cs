using System.Text.Json.Serialization;
using TimeLens.Api.Dtos;
using TimeLens.Api.Services;

namespace TimeLens.Api;

public static class ApiHost
{
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

        if (Directory.Exists(dashboardPath))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(dashboardPath)
            });

            app.MapFallbackToFile("index.html", new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(dashboardPath)
            });
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
                var value = prop.Value.ValueKind == System.Text.Json.JsonValueKind.True
                    ? "true" : prop.Value.ValueKind == System.Text.Json.JsonValueKind.False
                    ? "false" : prop.Value.GetRawText();
                saveSetting?.Invoke(prop.Name switch
                {
                    "trackAudio" => "track_audio",
                    "trackBrowser" => "track_browser",
                    "trackInput" => "track_input",
                    "idleThresholdSeconds" => "idle_threshold_seconds",
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

        app.MapGet("/api/summary", async (HttpContext ctx) =>
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
[JsonSerializable(typeof(AppSettings))]
internal partial class AppJsonContext : JsonSerializerContext { }
