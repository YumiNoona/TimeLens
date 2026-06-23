using System.Text.Json.Serialization;
using TimeLens.Api.Dtos;
using TimeLens.Api.Services;

namespace TimeLens.Api;

public static class ApiHost
{
    public static async Task StartAsync(string dbPath, CancellationToken ct = default)
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
            var origin = ctx.Request.Headers.Origin.ToString();
            if (origin.StartsWith("chrome-extension://") ||
                origin.StartsWith("moz-extension://"))
            {
                ctx.Response.Headers.Append("Access-Control-Allow-Origin", origin);
                ctx.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type");
                ctx.Response.Headers.Append("Access-Control-Allow-Methods", "POST, OPTIONS");
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

        app.MapPost("/api/browser-event", async (HttpContext ctx) =>
        {
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

        app.MapGet("/api/rules", async (HttpContext ctx) =>
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, exe_name, category, is_user_defined FROM app_categories ORDER BY exe_name";
            var rules = new List<RuleDto>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rules.Add(new RuleDto(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3) != 0
                ));
            }
            await ctx.Response.WriteAsJsonAsync(rules, AppJsonContext.Default.ListRuleDto);
        });

        app.MapPost("/api/rules", async (HttpContext ctx) =>
        {
            var dto = await ctx.Request.ReadFromJsonAsync<CreateRuleDto>(AppJsonContext.Default.CreateRuleDto);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Pattern))
            {
                ctx.Response.StatusCode = 400;
                return;
            }
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO app_categories (exe_name, category, is_user_defined)
                VALUES ($exe, $cat, 1)
                ON CONFLICT(exe_name) DO UPDATE SET category = $cat, is_user_defined = 1
                """;
            cmd.Parameters.AddWithValue("$exe", dto.Pattern.Trim().ToLowerInvariant());
            cmd.Parameters.AddWithValue("$cat", dto.Category);
            await cmd.ExecuteNonQueryAsync();
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(new { ok = true });
        });

        app.MapDelete("/api/rules/{id:long}", async (HttpContext ctx) =>
        {
            if (!long.TryParse(ctx.Request.RouteValues["id"]?.ToString(), out var id))
            {
                ctx.Response.StatusCode = 400;
                return;
            }
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM app_categories WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsJsonAsync(new { ok = true });
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
[JsonSerializable(typeof(RuleDto))]
[JsonSerializable(typeof(List<RuleDto>))]
[JsonSerializable(typeof(CreateRuleDto))]
internal partial class AppJsonContext : JsonSerializerContext { }
