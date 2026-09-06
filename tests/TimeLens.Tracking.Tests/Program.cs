using Microsoft.Data.Sqlite;
using TimeLens.Api.Services;
using TimeLens.TrayApp.Services;
using TimeLens.TrayApp.Watchers;

var root = Path.Combine(Path.GetTempPath(), "TimeLens-tracking-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    TimeLens.TrayApp.RuntimeDiagnostics.Initialize(root);
    using (var watcher = new WinEventWatcher())
    using (var input = new InputMonitor())
    {
        Action<string, string, int> badForeground = (_, _, _) => throw new IOException("Injected database failure");
        watcher.ForegroundChanged += badForeground;
        Check(!watcher.PublishForeground("test.exe", "", 1), "Foreground failures must be contained at the native boundary");
        watcher.ForegroundChanged -= badForeground;
        var received = false;
        watcher.ForegroundChanged += (_, _, _) => received = true;
        Check(watcher.PublishForeground("test.exe", "", 1) && received, "Tracking must continue after a callback failure");
        input.InputActivityTick += (_, _, _, _) => throw new IOException("Injected input failure");
        Check(!input.PublishInput(1, 1, 1, "test.exe"), "Timer subscriber failure must not terminate the process");
        Check(File.ReadAllText(Path.Combine(root, "runtime.log")).Contains("Injected database failure"), "Callback failures must have a persistent diagnostic");
    }
    var path = Path.Combine(root, "activity.db");
    DatabaseInitializer.Initialize(path);
    var start = DateTime.SpecifyKind(DateTime.Now.Date.AddDays(-3).AddHours(23), DateTimeKind.Local).ToUniversalTime();
    var clock = new FakeClock(start);
    using (var writer = new EventWriter(path, clock))
    {
        for (var i = 0; i <= 2160; i++)
        {
            clock.Now = start.AddSeconds(i * 5);
            writer.OpenAppEvent("Twinmotion.exe", "Project", 42, "active", "design");
        }
        Check(Scalar(path, "SELECT COUNT(*) FROM app_events") == 1, "Unchanged 3-hour session should be one durable row");
        Check(Math.Abs(Scalar(path, "SELECT (julianday(end_time)-julianday(start_time))*86400 FROM app_events") - 10800) < .01, "Three hours survive without foreground changes or shutdown");
        // Reinitialization models crash recovery while the latest checkpoint is durable.
        DatabaseInitializer.Initialize(path);
        Check(Math.Abs(Scalar(path, "SELECT (julianday(end_time)-julianday(start_time))*86400 FROM app_events") - 10800) < .01, "Startup must not truncate a long session");
        clock.Now = start.AddHours(5);
        writer.OpenAppEvent("Twinmotion.exe", "Project", 42, "active", "design");
        Check(Scalar(path, "SELECT COUNT(*) FROM app_events") == 2, "Unobserved gap must start another session");
        writer.CloseCurrentAppEvent();
        writer.StartIdleSpan("Twinmotion.exe", "input_idle");
        clock.Now = clock.Now.AddSeconds(5);
        writer.StartIdleSpan("Twinmotion.exe", "input_idle");
        writer.EndIdleSpan();
    }
    // Legacy SQLite-formatted ends use a space instead of T: valid duration must
    // survive bootstrap rather than being mistaken for a negative interval.
    var legacyPath = Path.Combine(root, "legacy.db");
    DatabaseInitializer.Initialize(legacyPath);
    using (var c = new SqliteConnection($"Data Source={legacyPath}"))
    {
        c.Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO app_events (exe_name,start_time,end_time) VALUES ('legacy.exe',$start,$end)";
        cmd.Parameters.AddWithValue("$start", start.ToString("o"));
        cmd.Parameters.AddWithValue("$end", start.AddMinutes(30).ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }
    DatabaseInitializer.Initialize(legacyPath);
    Check(Math.Abs(Scalar(legacyPath, "SELECT (julianday(end_time)-julianday(start_time))*86400 FROM app_events") - 1800) < .01, "Legacy timestamp format must not erase valid time");
    var api = new AnalyticsService(path);
    var first = await api.GetDashboardAsync(start.ToLocalTime().Date);
    var second = await api.GetDashboardAsync(start.ToLocalTime().Date.AddDays(1));
    Check(first.Summary.ActiveSeconds == 3600 && second.Summary.ActiveSeconds == 7200, "Midnight split must conserve all 3 hours");
    Check(first.TopApps.Single().Minutes == 60 && second.TopApps.Single().Minutes == 120, "Top apps must agree with summary");
    Check(first.Categories.Single().Minutes == 60 && second.Categories.Single().Minutes == 120, "Categories must agree with summary");
    Check(second.Heatmap.Last().Value == 120, "Heatmap must agree with summary");
    Check(second.Timeline.Any(x => x.StartHour == 0 && x.EndHour == 2), "Timeline must clip a session that started yesterday");

    long inputAge = 0;
    var idle = new IdleMonitor(() => inputAge);
    idle.SetSessionState("locked");
    Check(idle.GetState() == "away", "Locked is always away");
    idle.SetSessionState("sleep");
    idle.SetSessionState("wake");
    Check(idle.GetState() == "away", "Wake must not unlock the session");
    var transitions = new List<string>();
    idle.StateChanged += (_, to) => transitions.Add(to);
    idle.SetSessionState("unlocked");
    idle.IdleThresholdSeconds = int.MaxValue / 1000;
    Check(idle.GetState() == "active" && transitions.Contains("active"), "Unlock must emit a return transition to end idle");
    idle.IdleThresholdSeconds = 180;
    inputAge = 179999;
    Check(idle.GetState() == "active", "Reading within the idle timeout stays active");
    inputAge = 180000;
    Check(idle.GetState() == "idle", "Idle begins at the configured threshold");
    TimeLens.Api.LiveStatusStore.CurrentApp = "Twinmotion.exe";
    TimeLens.Api.LiveStatusStore.AudibleTab = "video";
    TimeLens.Api.LiveStatusStore.LastExtensionHeartbeat = DateTime.UtcNow;
    Check(idle.GetState() == "idle", "Background browser audio cannot keep Twinmotion active");
    TimeLens.Api.LiveStatusStore.CurrentApp = "firefox.exe";
    Check(idle.GetState() == "active", "Fresh foreground browser playback sustains activity");
    TimeLens.Api.LiveStatusStore.LastExtensionHeartbeat = DateTime.UtcNow.AddMinutes(-3);
    Check(idle.GetState() == "idle", "Stale browser audio cannot sustain activity");
    TimeLens.Api.LiveStatusStore.LastExtensionHeartbeat = DateTime.UtcNow;
    inputAge = 7200000;
    Check(idle.GetState() == "idle", "Unattended playback is bounded");
    inputAge = 0;
    Check(idle.GetState() == "active", "New input resumes activity");
    Console.WriteLine("PASS: durable 3-hour session, restart, observation gap, midnight summary/apps/categories/heatmap/timeline, lock/sleep/wake/unlock.");
}
finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }

static double Scalar(string path, string sql)
{
    using var c = new SqliteConnection($"Data Source={path}"); c.Open();
    using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToDouble(cmd.ExecuteScalar());
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
sealed class FakeClock(DateTime now) : TimeProvider
{
    public DateTime Now = now;
    public override DateTimeOffset GetUtcNow() => new(Now);
}
