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
        input.InputActivityTick += (_, _, _, _, _) => throw new IOException("Injected input failure");
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
    TimeLens.Api.LiveStatusStore.AudibleTab = "firefox";
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

    // Preserve all applications and seconds, including short shell interactions.
    var shortPath = Path.Combine(root, "short.db");
    DatabaseInitializer.Initialize(shortPath);
    clock.Now = start;
    using (var writer = new EventWriter(shortPath, clock))
    {
        for (var i = 0; i < 12; i++)
        {
            writer.OpenAppEvent($"app{i}.exe", $"title{i}", i + 1, "active", i == 0 ? "system" : "work");
            clock.Now = clock.Now.AddSeconds(2);
        }
    }
    var shortData = await new AnalyticsService(shortPath).GetDashboardAsync(start.ToLocalTime().Date);
    Check(shortData.TopApps.Length == 12, "Apps page must not be limited to eight apps");
    Check(shortData.Summary.ActiveSeconds == 24 && shortData.Timeline.Length == 12, "Short switches and foreground shell time must survive");
    Check(Math.Abs(shortData.TopApps.Sum(x => x.Minutes) * 60 - 24) < .001, "Per-app minutes must retain seconds");
    Check(Math.Abs(shortData.Heatmap.Last().Value * 60 - 24) < .001, "Heatmap must preserve activity shorter than a minute");
    var deletePath = Path.Combine(root, "privacy-delete.db");
    DatabaseInitializer.Initialize(deletePath);
    using (var deleteWriter = new EventWriter(deletePath, clock))
    {
        deleteWriter.OpenAppEvent("private.exe", "Private", 99, "active", "other");
        deleteWriter.InsertInputActivity(3, 2, 99, "private.exe", clock.Now);
        deleteWriter.InsertBlockLog("private.exe", "hide");
        deleteWriter.ClearAllActivity();
        Check(Scalar(deletePath, "SELECT (SELECT count(*) FROM app_events) + (SELECT count(*) FROM input_activity) + (SELECT count(*) FROM block_log)") == 0,
            "Privacy deletion must drain queued writes and remove every activity class");
    }
    var retentionPath = Path.Combine(root, "retention-boundary.db");
    DatabaseInitializer.Initialize(retentionPath);
    var retentionCutoff = DateTime.UtcNow.AddDays(-30);
    using (var retentionConn = new SqliteConnection($"Data Source={retentionPath}"))
    {
        retentionConn.Open();
        using var retentionInsert = retentionConn.CreateCommand();
        retentionInsert.CommandText = """
            INSERT INTO app_events(exe_name,start_time,end_time) VALUES('boundary.exe',$start,$end);
            INSERT INTO block_log(blocked_exe,blocked_action,timestamp) VALUES('old.exe','hide',$old);
            """;
        retentionInsert.Parameters.AddWithValue("$start", retentionCutoff.AddHours(-1).ToString("o"));
        retentionInsert.Parameters.AddWithValue("$end", retentionCutoff.AddHours(1).ToString("o"));
        retentionInsert.Parameters.AddWithValue("$old", retentionCutoff.AddSeconds(-1).ToString("o"));
        retentionInsert.ExecuteNonQuery();
    }
    DataRetentionService.Purge(retentionPath, 30);
    var retainedSeconds = Scalar(retentionPath, "SELECT (julianday(end_time)-julianday(start_time))*86400 FROM app_events WHERE exe_name='boundary.exe'");
    Check(retainedSeconds is > 3500 and < 3700 && Scalar(retentionPath, "SELECT count(*) FROM block_log") == 0,
        "Retention must clip boundary-spanning intervals and purge discrete privacy records");
    using (var capture = new InputMonitor())
    using (var inputWriter = new EventWriter(shortPath, clock))
    {
        capture.InputActivityTick += (keys, clicks, pid, exe, observedAt) => inputWriter.InsertInputActivity(keys, clicks, pid, exe, observedAt);
        var midnight = start.ToLocalTime().Date.AddDays(1).ToUniversalTime();
        capture.RecordInput(1, "midnight.exe", 2, 1, midnight.AddSeconds(-1));
        capture.RecordInput(1, "midnight.exe", 3, 2, midnight.AddSeconds(1));
        capture.Flush();
    }
    Check(Scalar(shortPath, "SELECT COUNT(*) FROM input_activity WHERE exe_name='midnight.exe'") == 2, "Input batches must not merge across midnight");
    var inputBeforeMidnight = await new AnalyticsService(shortPath).GetDashboardAsync(start.ToLocalTime().Date);
    Check(inputBeforeMidnight.Summary.TotalKeystrokes == 2, "Delayed flush must retain the input capture day");
    using (var input = new InputMonitor())
    {
        var counts = new Dictionary<string, int>();
        input.InputActivityTick += (keys, clicks, pid, exe, observedAt) => counts[exe!] = keys;
        input.RecordInput(1, "editor.exe", 10, 0);
        input.RecordInput(2, "browser.exe", 3, 1);
        input.Flush();
        Check(counts["editor.exe"] == 10 && counts["browser.exe"] == 3, "Input belongs to the app at capture time");
    }
    var browserPath = Path.Combine(root, "browser.db");
    DatabaseInitializer.Initialize(browserPath);
    clock.Now = start;
    TimeLens.Api.LiveStatusStore.CurrentApp = "firefox.exe";
    TimeLens.Api.LiveStatusStore.SystemState = "active";
    TimeLens.Api.LiveStatusStore.IsIdle = false;
    var tracker = new BrowserTrackingService(browserPath, clock);
    var tabA = new TimeLens.Api.Dtos.BrowserEventDto("wrong.example", "https://example.com/a", "A", "firefox", false, 1);
    for (var i = 0; i <= 3600; i++)
    {
        clock.Now = start.AddSeconds(i);
        if (i % 5 == 0) tracker.Observe(tabA);
        tracker.Tick();
    }
    tracker.Observe(tabA with { Title = "Updated page title" });
    Check(Scalar(browserPath, "SELECT COUNT(*) FROM browser_events") == 1, "Heartbeats and title refreshes must not inflate visits");
    Check(Math.Abs(Scalar(browserPath, "SELECT (julianday(end_time)-julianday(start_time))*86400 FROM browser_events") - 3600) < .01, "Long website visits must remain durable without a two-minute cap");
    clock.Now = clock.Now.AddSeconds(1);
    tracker.Observe(tabA with { TabId = 2, Url = "https://second.example/", Title = "B" });
    clock.Now = clock.Now.AddSeconds(2);
    tracker.Leave("firefox", 1); // Closing a background tab must not close B.
    tracker.Tick();
    TimeLens.Api.LiveStatusStore.CurrentApp = "editor.exe";
    clock.Now = clock.Now.AddSeconds(1);
    tracker.Tick();
    var duration = Scalar(browserPath, "SELECT SUM((julianday(end_time)-julianday(start_time))*86400) FROM browser_events");
    Check(Math.Abs(duration - 3604) < .01, "Tab switches must partition, not overlap, website time");
    clock.Now = clock.Now.AddMinutes(10);
    tracker.Tick();
    tracker.Observe(tabA);
    Check(Scalar(browserPath, "SELECT COUNT(*) FROM browser_events") == 2, "Background browser must not create activity");
    TimeLens.Api.LiveStatusStore.CurrentApp = "firefox.exe";
    tracker.Observe(tabA with { ObservedAt = new DateTimeOffset(start) });
    Check(Scalar(browserPath, "SELECT COUNT(*) FROM browser_events") == 2, "Stale delivery cannot become current activity");
    TimeLens.Api.LiveStatusStore.IsIdle = true;
    tracker.Observe(tabA);
    Check(Scalar(browserPath, "SELECT COUNT(*) FROM browser_events") == 2, "Idle browser cannot open activity");
    TimeLens.Api.LiveStatusStore.IsIdle = false;
    tracker.Observe(tabA);
    clock.Now = clock.Now.AddHours(2);
    tracker.Tick();
    DatabaseInitializer.Initialize(browserPath);
    Check(Math.Abs(Scalar(browserPath, "SELECT SUM((julianday(end_time)-julianday(start_time))*86400) FROM browser_events") - duration) < .01, "Suspend and restart cannot invent browser time");
    Check(!BrowserTrackingService.MatchesForeground("chrome", "msedge.exe"), "Chrome cannot claim Edge activity");


    // Website durations must be corroborated by foreground desktop intervals.
    using (var conn = new SqliteConnection($"Data Source={browserPath}"))
    {
        conn.Open(); using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO app_events(exe_name,start_time,end_time,category) VALUES('firefox.exe',$start,$end,'browsing')";
        cmd.Parameters.AddWithValue("$start", start.ToString("o"));
        cmd.Parameters.AddWithValue("$end", start.AddSeconds(3604).ToString("o"));
        cmd.ExecuteNonQuery();
    }
    var accuracyPath = Path.Combine(root, "accuracy.db");
    DatabaseInitializer.Initialize(accuracyPath);
    using (var conn = new SqliteConnection($"Data Source={accuracyPath}"))
    {
        conn.Open();
        void Insert(string sql, int from, int to)
        {
            using var cmd = conn.CreateCommand(); cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$start", start.AddSeconds(from).ToString("o"));
            cmd.Parameters.AddWithValue("$end", start.AddSeconds(to).ToString("o")); cmd.ExecuteNonQuery();
        }
        Insert("INSERT INTO app_events(exe_name,start_time,end_time) VALUES('chrome.exe',$start,$end)", 0, 60);
        Insert("INSERT INTO app_events(exe_name,start_time,end_time) VALUES('editor.exe',$start,$end)", 60, 100);
        Insert("INSERT INTO browser_events(domain,url,title,browser,start_time,end_time) VALUES('a.example','https://a.example/','A','chrome',$start,$end)", 0, 80);
        Insert("INSERT INTO browser_events(domain,url,title,browser,start_time,end_time) VALUES('b.example','https://b.example/','B','chrome',$start,$end)", 50, 100);
        Insert("INSERT INTO idle_spans(start_time,end_time) VALUES($start,$end)", 20, 30);
        Insert("INSERT INTO idle_spans(start_time,end_time) VALUES($start,$end)", 25, 35);
        var corrected = await BrowserAnalyticsService.ReadAsync(conn, start, start.AddSeconds(100));
        Check(corrected.Single(x => x.Domain == "a.example").TotalSeconds == 35 && corrected.Single(x => x.Domain == "b.example").TotalSeconds == 10,
            "Legacy overlapping tabs, overlapping idle spans and background app intervals must not inflate website time");
        Check(corrected.All(x => x.Keystrokes is null && x.Clicks is null), "Missing historical website input must remain unknown");
    }
    clock.Now = start.AddSeconds(55);
    var inputTracker = new BrowserTrackingService(accuracyPath, clock);
    var inputBatch = new TimeLens.Api.Dtos.BrowserInputDto(Guid.NewGuid().ToString(), "https://b.example/", "B", "firefox", new DateTimeOffset(clock.Now), 7, 3);
    Check(inputTracker.RecordInput(inputBatch) && inputTracker.RecordInput(inputBatch), "Input delivery retries must be accepted idempotently");
    Check(Scalar(accuracyPath, "SELECT SUM(keystrokes) FROM browser_input_batches") == 7, "A retry must not double input counts");
    TimeLens.Api.LiveStatusStore.Settings = TimeLens.Api.LiveStatusStore.Settings with { TrackInput = false };
    Check(!inputTracker.RecordInput(inputBatch with { BatchId = Guid.NewGuid().ToString() }), "Disabled input must not be stored");
    TimeLens.Api.LiveStatusStore.Settings = TimeLens.Api.LiveStatusStore.Settings with { TrackInput = true };
    using (var conn = new SqliteConnection($"Data Source={accuracyPath}"))
    {
        conn.Open();
        var detail = await BrowserAnalyticsService.ReadAsync(conn, start, start.AddSeconds(100));
        Check(detail.Single(x => x.Domain == "b.example").Pages!.Single(x => x.Browser == "firefox").Keystrokes == 7, "Website page details must expose recorded input");
        TimeLens.Api.LiveStatusStore.Settings = TimeLens.Api.LiveStatusStore.Settings with { BrowserUrlMode = "domain", BrowserStoreTitles = false };
        var privateBatch = inputBatch with { BatchId = Guid.NewGuid().ToString(), Url = "https://private.example/account?secret=value", Title = "Private title" };
        Check(inputTracker.RecordInput(privateBatch), "Privacy-filtered Firefox input remains trackable");
        using var privacyCmd = conn.CreateCommand();
        privacyCmd.CommandText = "SELECT url || '|' || title FROM browser_input_batches WHERE domain='private.example'";
        Check((privacyCmd.ExecuteScalar()?.ToString()) == "https://private.example/|", "URL detail and titles must be removed before database storage");
        TimeLens.Api.LiveStatusStore.Settings = TimeLens.Api.LiveStatusStore.Settings with { BrowserUrlMode = "full", BrowserStoreTitles = true };
    }
    Console.WriteLine("PASS: corroborated website time, legacy overlap repair at query time, idle union, input deduplication and missing-data semantics.");
    // Run real HTTP routes on a free loopback port, without touching an installed tracker.
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    using var stopApi = new CancellationTokenSource();
    var host = TimeLens.Api.ApiHost.StartAsync(browserPath, stopApi.Token, port: port, requireAuthentication: false);
    using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
    try
    {
        for (var i = 0; ; i++)
        {
            try { (await client.GetAsync("/api/settings")).EnsureSuccessStatusCode(); break; }
            catch when (i < 30) { await Task.Delay(100); }
        }
        async Task<int> SiteSeconds(DateTime date)
        {
            using var json = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync($"/api/browser-time-summary?date={date:yyyy-MM-dd}"));
            return json.RootElement.EnumerateArray().Sum(x => x.GetProperty("totalSeconds").GetInt32());
        }
        Check(await SiteSeconds(start.ToLocalTime().Date) == 3600, "HTTP site summary must not cap a durable one-hour visit");
        Check(await SiteSeconds(start.ToLocalTime().Date.AddDays(1)) == 4, "HTTP site summary must split visits at local midnight");
        using (var hourly = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync($"/api/browser-hourly?date={start.ToLocalTime():yyyy-MM-dd}")))
            Check(Math.Abs(hourly.RootElement.EnumerateArray().Sum(x => x.GetProperty("totalSeconds").GetDouble()) - 3600) < .01,
                "Hourly website time must match corroborated daily time");
        var batchJson = System.Text.Json.JsonSerializer.Serialize(new {
            batchId = Guid.NewGuid().ToString(), url = "https://example.com/", title = "Input test", browser = "firefox",
            observedAt = DateTimeOffset.UtcNow, keystrokes = 4, clicks = 2 });
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var inputBody = new StringContent(batchJson, System.Text.Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("/api/browser-input", inputBody);
            response.EnsureSuccessStatusCode();
            Check((await response.Content.ReadAsStringAsync()).Contains("true"), "HTTP input accepts a valid batch");
        }
        Check(Scalar(browserPath, "SELECT SUM(keystrokes) FROM browser_input_batches") == 4, "HTTP retries must not double input");
        using var observation = new StringContent("{\"domain\":\"example.com\",\"url\":\"https://example.com/\",\"title\":\"live\",\"browser\":\"firefox\",\"audible\":false,\"tabId\":20}", System.Text.Encoding.UTF8, "application/json");
        (await client.PostAsync("/api/browser-event", observation)).EnsureSuccessStatusCode();
        await Task.Delay(100);
        using var pulse = new StringContent("{\"domain\":\"example.com\",\"url\":\"https://example.com/\",\"title\":\"live\",\"browser\":\"firefox\",\"tabId\":20}", System.Text.Encoding.UTF8, "application/json");
        (await client.PostAsync("/api/browser-heartbeat", pulse)).EnsureSuccessStatusCode();
        using var leave = new StringContent("{\"browser\":\"firefox\",\"tabId\":20}", System.Text.Encoding.UTF8, "application/json");
        (await client.PostAsync("/api/browser-leave", leave)).EnsureSuccessStatusCode();
        Check(Scalar(browserPath, "SELECT COUNT(*) FROM browser_events WHERE tab_id=20") == 1, "HTTP heartbeat shares the event writer and does not inflate visits");
    }
    finally { stopApi.Cancel(); await host; }
    Console.WriteLine("PASS: isolated HTTP browser ingestion, heartbeat, leave, long-visit totals and midnight boundaries.");

    listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    using var stopSecureApi = new CancellationTokenSource();
    var secureHost = TimeLens.Api.ApiHost.StartAsync(browserPath, stopSecureApi.Token,
        saveSetting: (key, value) => new SettingsService(browserPath).Save(key, value), port: port);
    using var anonymous = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
    using var dashboardHandler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
    using var dashboard = new HttpClient(dashboardHandler) { BaseAddress = anonymous.BaseAddress, Timeout = TimeSpan.FromSeconds(5) };
    try
    {
        for (var i = 0; ; i++)
        {
            try { await dashboard.GetAsync("/"); break; }
            catch when (i < 30) { await Task.Delay(100); }
        }
        Check((await anonymous.GetAsync("/api/settings")).StatusCode == System.Net.HttpStatusCode.Unauthorized,
            "Unauthenticated localhost callers must not read settings");
        using (var protectedSetting = new StringContent("{\"block_password_hash\":\"attacker\"}", System.Text.Encoding.UTF8, "application/json"))
            Check((await dashboard.PostAsync("/api/settings", protectedSetting)).StatusCode == System.Net.HttpStatusCode.BadRequest,
                "Protected settings must not be writable through the public settings route");
        using (var invalidRetention = new StringContent("{\"retentionDays\":-1}", System.Text.Encoding.UTF8, "application/json"))
            Check((await dashboard.PostAsync("/api/settings", invalidRetention)).StatusCode == System.Net.HttpStatusCode.BadRequest,
                "Invalid retention must be rejected before it can delete history");
        using (var invalidBatch = new StringContent("{\"theme\":\"terminal\",\"retentionDays\":-1}", System.Text.Encoding.UTF8, "application/json"))
            Check((await dashboard.PostAsync("/api/settings", invalidBatch)).StatusCode == System.Net.HttpStatusCode.BadRequest,
                "A rejected settings batch must not partially apply earlier values");
        Check(Scalar(browserPath, "SELECT count(*) FROM settings WHERE key='theme' AND value='default'") == 1,
            "Rejected settings batches must not persist partial changes");
        using (var invalidTrailingSetting = new StringContent("{\"showTitles\":false,\"theme\":\"invalid\"}", System.Text.Encoding.UTF8, "application/json"))
            Check((await dashboard.PostAsync("/api/settings", invalidTrailingSetting)).StatusCode == System.Net.HttpStatusCode.BadRequest,
                "All settings must be validated before any value in a batch is persisted");
        Check(Scalar(browserPath, "SELECT count(*) FROM settings WHERE key='show_titles'") == 0,
            "A later invalid setting must not partially persist an earlier setting");
        using (var secondsPreference = new StringContent("{\"showSeconds\":true}", System.Text.Encoding.UTF8, "application/json"))
            (await dashboard.PostAsync("/api/settings", secondsPreference)).EnsureSuccessStatusCode();
        Check(Scalar(browserPath, "SELECT count(*) FROM settings WHERE key='show_seconds' AND value='true'") == 1 &&
              TimeLens.Api.LiveStatusStore.Settings.ShowSeconds,
            "Duration precision preference was not saved and applied live");
        using var codeResponse = await dashboard.PostAsync("/api/pair/code", null);
        codeResponse.EnsureSuccessStatusCode();
        using var codeDoc = System.Text.Json.JsonDocument.Parse(await codeResponse.Content.ReadAsStringAsync());
        var code = codeDoc.RootElement.GetProperty("code").GetString();
        using var exchange = new HttpRequestMessage(HttpMethod.Post, "/api/pair/exchange")
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { code }), System.Text.Encoding.UTF8, "application/json")
        };
        exchange.Headers.Add("Origin", "moz-extension://timelens-test");
        using var exchangeResponse = await anonymous.SendAsync(exchange);
        exchangeResponse.EnsureSuccessStatusCode();
        using var tokenDoc = System.Text.Json.JsonDocument.Parse(await exchangeResponse.Content.ReadAsStringAsync());
        var token = tokenDoc.RootElement.GetProperty("token").GetString();
        using var extensionSettings = new HttpRequestMessage(HttpMethod.Get, "/api/extension/settings");
        extensionSettings.Headers.Add("Origin", "moz-extension://timelens-test");
        extensionSettings.Headers.Add("X-TimeLens-Extension", token);
        (await anonymous.SendAsync(extensionSettings)).EnsureSuccessStatusCode();
        using var extensionAdmin = new HttpRequestMessage(HttpMethod.Post, "/api/settings")
        {
            Content = new StringContent("{\"trackBrowser\":false}", System.Text.Encoding.UTF8, "application/json")
        };
        extensionAdmin.Headers.Add("Origin", "moz-extension://timelens-test");
        extensionAdmin.Headers.Add("X-TimeLens-Extension", token);
        Check((await anonymous.SendAsync(extensionAdmin)).StatusCode == System.Net.HttpStatusCode.Unauthorized,
            "Firefox pairing must not grant dashboard administration rights");
        using var securityConn = new SqliteConnection($"Data Source={browserPath}");
        securityConn.Open();
        using var securityCmd = securityConn.CreateCommand();
        securityCmd.CommandText = "SELECT value FROM settings WHERE key='firefox_pair_token_hash'";
        var storedToken = securityCmd.ExecuteScalar()?.ToString();
        Check(storedToken?.Length == 64 && storedToken != token,
            "The Firefox bearer token must never be stored in plaintext");
    }
    finally { stopSecureApi.Cancel(); await secureHost; }
    Console.WriteLine("PASS: localhost API session isolation and one-time Firefox pairing.");
    Console.WriteLine("PASS: 12 short apps, precise totals, input attribution, one-hour browser visit, tab partition, focus, idle, stale requests, suspend and restart.");
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
