using Microsoft.Data.Sqlite;
using TimeLens.Api;
using TimeLens.Api.Dtos;
using TimeLens.Api.Services;
using TimeLens.TrayApp.Services;
using Xunit;

[Collection("Tracking integration")]
public sealed class PrivacyAndOfflineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TimeLens-privacy-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _previous = LiveStatusStore.Settings;
    private string Db => Path.Combine(_root, "activity.db");
    public PrivacyAndOfflineTests()
    {
        Directory.CreateDirectory(_root);
        DatabaseInitializer.Initialize(Db);
        LiveStatusStore.Settings = new AppSettings { BrowserStoreTitles = true };
        LiveStatusStore.CurrentApp = "firefox.exe";
        LiveStatusStore.IsIdle = false;
        LiveStatusStore.SystemState = "active";
    }
    private object? Scalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={Db}"); conn.Open();
        using var cmd = conn.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar();
    }
    private void Execute(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={Db}"); conn.Open();
        using var cmd = conn.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }
    [Fact]
    public void ResetCompanionLineDoesNotAdmitPrePrivacyLegacyBuilds()
    {
        Assert.True(PrivacyPolicy.SupportsCompanionPrivacy("2.0.0"));
        Assert.True(PrivacyPolicy.SupportsCompanionPrivacy("8.3.1"));
        Assert.False(PrivacyPolicy.SupportsCompanionPrivacy("8.2.0"));
        Assert.False(PrivacyPolicy.SupportsCompanionPrivacy("1.0.0"));
        Assert.False(PrivacyPolicy.SupportsCompanionPrivacy(null));
    }
    [Fact]
    public void OfflineWorkIsOptInAndSurvivesSettingsReload()
    {
        var settings = new SettingsService(Db);
        Assert.False(new AppSettings().OfflineWorkEnabled);
        Assert.False(settings.Load().OfflineWorkEnabled);
        settings.Save("offline_work_enabled", "true");
        Assert.True(new SettingsService(Db).Load().OfflineWorkEnabled);
        settings.Save("offline_work_enabled", "false");
        Assert.False(new SettingsService(Db).Load().OfflineWorkEnabled);
    }
    [Fact]
    public void ExcludedAppsAndPauseNeverPersistInputAudioOrTitles()
    {
        LiveStatusStore.Settings = LiveStatusStore.Settings with { IgnoredApps = "SECRET.EXE" };
        using (var writer = new EventWriter(Db))
        {
            writer.OpenAppEvent("secret.exe", "private title", 1, "active", "work", "private project");
            writer.InsertInputActivity(10, 1, 1, "secret.exe");
            writer.InsertAudioActivity(1, "secret.exe", true);
            LiveStatusStore.Settings = LiveStatusStore.Settings with { PauseUntil = DateTimeOffset.UtcNow.AddMinutes(1).ToString("o") };
            Assert.True(PrivacyPolicy.IsPaused);
            writer.OpenAppEvent("allowed.exe", "paused title", 2, "active", "work");
            writer.InsertInputActivity(10, 1, 2, "allowed.exe");
            writer.InsertSessionEvent("locked");
            writer.StartIdleSpan("allowed.exe", "idle");
        }
        foreach (var table in new[] { "app_events", "input_activity", "audio_activity", "session_events", "idle_spans" })
            Assert.Equal(0L, Scalar($"SELECT COUNT(*) FROM {table}"));
    }
    [Fact]
    public void TitleRedactionAlsoRemovesDerivedProjectAndPersistsAcrossRestart()
    {
        var settings = new SettingsService(Db);
        settings.Save("hidden_title_apps", "code.exe"); settings.Save("title_redaction_patterns", "confidential");
        settings.Save("ignored_domains", "bank.example"); settings.Save("ignored_apps", "secret.exe");
        settings.Save("pause_until", ""); settings.Save("theme", "paper");
        LiveStatusStore.Settings = settings.Load();
        Assert.Equal("paper", LiveStatusStore.Settings.Theme);
        Assert.True(PrivacyPolicy.IgnoreDomain("login.bank.example"));
        Assert.False(PrivacyPolicy.IgnoreDomain("notbank.example"));
        using (var writer = new EventWriter(Db))
        {
            writer.OpenAppEvent("code.exe", "private file", 1, "active", "development", "private project");
            writer.OpenAppEvent("other.exe", "Confidential data", 2, "active", "work", "derived secret");
        }
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM app_events WHERE window_title <> '' OR project IS NOT NULL"));
    }
    [Fact]
    public void ExcludedDomainsCannotStoreBrowserMediaOrRetriedInput()
    {
        LiveStatusStore.Settings = LiveStatusStore.Settings with { IgnoredDomains = "bank.example" };
        var browser = new BrowserTrackingService(Db);
        browser.Observe(new BrowserEventDto("login.bank.example", "https://login.bank.example/account", "secret", "firefox", false, 1));
        Assert.False(browser.RecordInput(new BrowserInputDto(Guid.NewGuid().ToString(), "https://bank.example/", "secret", "firefox", DateTimeOffset.UtcNow, 1, 1)));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM browser_events"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM browser_input_batches"));
    }
    [Fact]
    public void RepeatedObservationsCheckpointEveryTenSecondsAndFlushOnClose()
    {
        var clock = new FakeClock(DateTime.UtcNow.AddMinutes(-10));
        using var writer = new EventWriter(Db, clock);
        writer.OpenAppEvent("work.exe", "work", 1, "active", "work");
        var initial = (string)Scalar("SELECT end_time FROM app_events")!;
        for (var i = 0; i < 9; i++) { clock.Now = clock.Now.AddSeconds(1); writer.OpenAppEvent("work.exe", "work", 1, "active", "work"); }
        Assert.Equal(initial, Scalar("SELECT end_time FROM app_events"));
        clock.Now = clock.Now.AddSeconds(1); writer.OpenAppEvent("work.exe", "work", 1, "active", "work");
        Assert.Equal(clock.Now.ToString("o"), Scalar("SELECT end_time FROM app_events"));
        clock.Now = clock.Now.AddSeconds(1); writer.CloseCurrentAppEvent();
        Assert.Equal(clock.Now.ToString("o"), Scalar("SELECT end_time FROM app_events"));
    }
    [Fact]
    public void IdleReviewIsDurableAndReconciliationIsIdempotent()
    {
        var clock = new FakeClock(DateTime.UtcNow.AddMinutes(-10));
        using (var writer = new EventWriter(Db, clock))
        {
            writer.StartIdleSpan("work.exe", "idle");
            for (var i = 0; i < 90; i++) { clock.Now = clock.Now.AddSeconds(1); writer.StartIdleSpan("work.exe", "idle"); }
            writer.EndIdleSpan();
        }
        var service = new OfflineTimeService(Db);
        var span = Assert.Single(service.Pending());
        var request = new OfflineRequest(span.Id, null, null, "meeting", "Release", "Discussion");
        service.Save(request); service.Save(request);
        Assert.Empty(service.Pending());
        Assert.Equal(1L, Scalar("SELECT COUNT(*) FROM offline_entries"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM app_events"));
        var saved = Assert.Single(service.Read(DateTime.Parse(span.StartTime).ToLocalTime().Date));
        Assert.Equal("Release", saved.Project);
        service.Delete(saved.Id);
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM offline_entries"));
    }
    [Fact]
    public void ManualTimeRejectsFutureOverlapsAndInvalidLabels()
    {
        var service = new OfflineTimeService(Db);
        var start = DateTime.UtcNow.AddHours(-2).ToString("o"); var end = DateTime.UtcNow.AddHours(-1).ToString("o");
        service.Save(new(null, start, end, "call"));
        Assert.Throws<ArgumentException>(() => service.Save(new(null, start, end, "work")));
        Assert.Throws<ArgumentException>(() => service.Save(new(null, start, DateTime.UtcNow.AddHours(1).ToString("o"), "work")));
        Assert.Throws<ArgumentException>(() => service.Save(new(null, start, end, "random")));
        var activeStart = DateTime.UtcNow.AddMinutes(-30).ToString("o"); var activeEnd = DateTime.UtcNow.AddMinutes(-20).ToString("o");
        Execute($"INSERT INTO app_events(exe_name,start_time,end_time,session_state) VALUES('work.exe','{activeStart}','{activeEnd}','active')");
        Assert.Throws<ArgumentException>(() => service.Save(new(null, activeStart, activeEnd, "work")));
    }
    [Fact]
    public void ClearAllAndRetentionIncludeOfflineEntries()
    {
        var service = new OfflineTimeService(Db);
        service.Save(new(null, DateTime.UtcNow.AddHours(-2).ToString("o"), DateTime.UtcNow.AddHours(-1).ToString("o"), "work"));
        using var writer = new EventWriter(Db); writer.ClearAllActivity();
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM offline_entries"));
        Execute($"INSERT INTO offline_entries(start_time,end_time,kind) VALUES('{DateTime.UtcNow.AddDays(-100):o}','{DateTime.UtcNow.AddDays(-99):o}','work')");
        DataRetentionService.Purge(Db, 30);
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM offline_entries"));
    }
    [Fact]
    public void PrivacyValidationRejectsBadUrlsAndRegex()
    {
        Assert.Throws<ArgumentException>(() => PrivacyPolicy.ValidateSetting("ignoredDomains", "https://bank.example/account"));
        Assert.Throws<ArgumentException>(() => PrivacyPolicy.ValidateSetting("titleRedactionPatterns", "["));
        Assert.Throws<ArgumentException>(() => PrivacyPolicy.ValidateSetting("pauseUntil", DateTimeOffset.UtcNow.AddDays(2).ToString("o")));
    }
    [Fact]
    public void ActivityDeletionBoundaryRejectsOldExtensionAndNativeBatches()
    {
        var observed = DateTimeOffset.UtcNow.AddSeconds(-30);
        LiveStatusStore.Settings = LiveStatusStore.Settings with { CollectionSince = DateTimeOffset.UtcNow.ToString("o") };
        Assert.False(new BrowserTrackingService(Db).RecordInput(new(Guid.NewGuid().ToString(), "https://example.com/", "", "firefox", observed, 1, 1)));
        using (var writer = new EventWriter(Db)) writer.InsertInputActivity(1, 1, 1, "work.exe", observed.UtcDateTime);
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM input_activity"));
        Assert.Equal(0L, Scalar("SELECT COUNT(*) FROM browser_input_batches"));
    }
    [Fact]
    public void BrowserLeaveFlushesTheUncheckpointedTail()
    {
        var clock = new FakeClock(DateTime.UtcNow.AddMinutes(-10));
        var browser = new BrowserTrackingService(Db, clock);
        browser.Observe(new("example.com", "https://example.com/", "Page", "firefox", false, 1));
        clock.Now = clock.Now.AddSeconds(5);
        browser.Tick();
        browser.Leave("firefox", 1);
        Assert.Equal(clock.Now.ToString("o"), Scalar("SELECT end_time FROM browser_events"));
    }
    [Fact]
    public void DailyIntervalQueriesUseTheEndTimeExpressionIndex()
    {
        using var conn = new SqliteConnection($"Data Source={Db}"); conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXPLAIN QUERY PLAN SELECT id FROM app_events WHERE session_state='active' AND julianday(start_time) < julianday($end) AND COALESCE(julianday(end_time), 1e99) > julianday($start)";
        cmd.Parameters.AddWithValue("$start", DateTime.UtcNow.AddDays(-1).ToString("o"));
        cmd.Parameters.AddWithValue("$end", DateTime.UtcNow.ToString("o"));
        using var rows = cmd.ExecuteReader();
        var descriptions = new List<string>(); while (rows.Read()) descriptions.Add(rows.GetString(3));
        Assert.Contains(descriptions, description => description.Contains("idx_app_range_end"));
    }
    public void Dispose()
    {
        LiveStatusStore.Settings = _previous;
        SqliteConnection.ClearAllPools(); Directory.Delete(_root, true);
    }
}
