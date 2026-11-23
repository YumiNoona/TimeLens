using System.Diagnostics;
using Microsoft.Data.Sqlite;
using TimeLens.Api;
using TimeLens.TrayApp.Services;
using Xunit;

[Collection("Tracking integration")]
public sealed class LifecycleSoakTests
{
    [Fact]
    public async Task RepeatedWriterAndApiShutdownDoesNotLeakHandlesOrLoseQueuedWrites()
    {
        var root = Path.Combine(Path.GetTempPath(), "TimeLens-soak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var process = Process.GetCurrentProcess();
        var dbPath = Path.Combine(root, "activity.db");
        DatabaseInitializer.Initialize(dbPath);
        var baselineHandles = 0;
        try
        {
            // The first cycle warms JIT, SQLite and Kestrel. The following twenty
            // cycles measure repeat-lifecycle growth against that stable baseline.
            for (var cycle = 0; cycle <= 20; cycle++)
            {
                using (var writer = new EventWriter(dbPath))
                {
                    Parallel.For(0, 250, index =>
                        writer.InsertInputActivity(1, index % 2, 100 + index, "soak.exe"));
                }

                using (var connection = new SqliteConnection($"Data Source={dbPath}"))
                {
                    connection.Open();
                    using var count = connection.CreateCommand();
                    count.CommandText = "SELECT COUNT(*) FROM input_activity";
                    Assert.Equal(250L * (cycle + 1), (long)count.ExecuteScalar()!);
                }

                var port = FreePort();
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var host = ApiHost.StartAsync(dbPath, stop.Token, port: port, requireAuthentication: false);
                using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(2) };
                await WaitUntilReady(client);
                stop.Cancel();
                await host;
                if (cycle == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    process.Refresh();
                    baselineHandles = process.HandleCount;
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            process.Refresh();
            Assert.True(process.HandleCount <= baselineHandles + 80,
                $"Handle count grew from {baselineHandles} to {process.HandleCount} during lifecycle soak.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitUntilReady(HttpClient client)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                using var response = await client.GetAsync("/api/settings");
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(25);
        }
        throw new TimeoutException("The local API did not become ready during the soak test.");
    }
}
