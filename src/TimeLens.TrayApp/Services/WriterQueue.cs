using Microsoft.Data.Sqlite;

namespace TimeLens.TrayApp.Services;

public sealed class WriterQueue : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly object _syncLock = new();
    private readonly System.Threading.Tasks.Task _drainTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action<SqliteCommand>> _queue = new();
    private Timer? _checkpointTimer;

    public WriterQueue(string dbPath)
    {
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        using var wal = _conn.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-8000;";
        wal.ExecuteNonQuery();
        _drainTask = DrainLoopAsync(_cts.Token);

        _checkpointTimer = new Timer(_ =>
        {
            try
            {
                lock (_syncLock)
                {
                    DrainAll();
                    using var cmd = _conn.CreateCommand();
                    cmd.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }, null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
    }

    public void Enqueue(Action<SqliteCommand> op)
    {
        _queue.Enqueue(op);
    }

    public void ExecuteSync(Action<SqliteConnection> op)
    {
        lock (_syncLock)
        {
            DrainAll();
            op(_conn);
        }
    }

    public long ExecuteSyncWithRowId(Action<SqliteConnection> op)
    {
        lock (_syncLock)
        {
            DrainAll();
            op(_conn);
            using var id = _conn.CreateCommand();
            id.CommandText = "SELECT last_insert_rowid();";
            return (long)id.ExecuteScalar()!;
        }
    }

    private void DrainAll()
    {
        var batch = new List<Action<SqliteCommand>>();
        while (_queue.TryDequeue(out var op))
            batch.Add(op);
        if (batch.Count == 0) return;
        ExecuteBatch(batch);
    }

    private void ExecuteBatch(List<Action<SqliteCommand>> batch)
    {
        using var tx = _conn.BeginTransaction();
        foreach (var op in batch)
        {
            using var cmd = _conn.CreateCommand();
            op(cmd);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private async System.Threading.Tasks.Task DrainLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(5_000, ct).ConfigureAwait(false);

                var batch = new List<Action<SqliteCommand>>();
                while (_queue.TryDequeue(out var op))
                    batch.Add(op);
                if (batch.Count == 0) continue;

                lock (_syncLock)
                {
                    ExecuteBatch(batch);
                }
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // Log and continue
            }
        }
    }

    public void Dispose()
    {
        _checkpointTimer?.Dispose();
        _cts.Cancel();
        try { _drainTask.GetAwaiter().GetResult(); } catch { }
        DrainAll();
        _conn.Close();
        _conn.Dispose();
        _cts.Dispose();
    }
}
