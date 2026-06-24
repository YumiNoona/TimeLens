using Microsoft.Data.Sqlite;

namespace TimeLens.TrayApp.Services;

public sealed class WriterQueue : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly object _syncLock = new();
    private readonly System.Threading.Tasks.Task _drainTask;
    private readonly CancellationTokenSource _cts = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action<SqliteCommand>> _queue = new();

    public WriterQueue(string dbPath)
    {
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        using var wal = _conn.CreateCommand();
        wal.CommandText = "PRAGMA journal_mode=WAL;";
        wal.ExecuteNonQuery();
        _drainTask = DrainLoopAsync(_cts.Token);
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
        _cts.Cancel();
        try { _drainTask.GetAwaiter().GetResult(); } catch { }
        DrainAll();
        _conn.Close();
        _conn.Dispose();
        _cts.Dispose();
    }
}
