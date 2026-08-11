using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace TimeLens.Api;

internal static class BlockProtectionService
{
    private const int Iterations = 210_000;
    private static readonly TimeSpan UnlockLifetime = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, DateTime> UnlockTokens = new(StringComparer.Ordinal);
    private static readonly object AttemptLock = new();
    private static int FailedAttempts;
    private static DateTime RetryAfterUtc = DateTime.MinValue;

    public static bool IsEnabled(string dbPath) =>
        string.Equals(ReadSetting(dbPath, "block_protection_enabled"), "true", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(ReadSetting(dbPath, "block_password_hash")) &&
        !string.IsNullOrWhiteSpace(ReadSetting(dbPath, "block_password_salt"));

    public static void SetPassword(string dbPath, string password)
    {
        ValidatePassword(password);
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);

        using var conn = Open(dbPath);
        using var tx = conn.BeginTransaction();
        WriteSetting(conn, tx, "block_password_salt", Convert.ToBase64String(salt));
        WriteSetting(conn, tx, "block_password_hash", Convert.ToBase64String(hash));
        WriteSetting(conn, tx, "block_password_iterations", Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture));
        WriteSetting(conn, tx, "block_protection_enabled", "true");
        tx.Commit();
        RevokeAllTokens();
    }

    public static void Disable(string dbPath)
    {
        using var conn = Open(dbPath);
        using var tx = conn.BeginTransaction();
        WriteSetting(conn, tx, "block_protection_enabled", "false");
        DeleteSetting(conn, tx, "block_password_salt");
        DeleteSetting(conn, tx, "block_password_hash");
        DeleteSetting(conn, tx, "block_password_iterations");
        tx.Commit();
        RevokeAllTokens();
    }

    public static bool VerifyPassword(string dbPath, string password)
    {
        if (string.IsNullOrEmpty(password) || !IsEnabled(dbPath)) return false;
        try
        {
            var saltText = ReadSetting(dbPath, "block_password_salt");
            var hashText = ReadSetting(dbPath, "block_password_hash");
            var iterationsText = ReadSetting(dbPath, "block_password_iterations");
            if (saltText is null || hashText is null) return false;
            var salt = Convert.FromBase64String(saltText);
            var expected = Convert.FromBase64String(hashText);
            var iterations = int.TryParse(iterationsText, out var parsed) && parsed >= 100_000 ? parsed : Iterations;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string? TryUnlock(string dbPath, string password, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        lock (AttemptLock)
        {
            if (DateTime.UtcNow < RetryAfterUtc)
            {
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((RetryAfterUtc - DateTime.UtcNow).TotalSeconds));
                return null;
            }
        }

        if (!VerifyPassword(dbPath, password))
        {
            lock (AttemptLock)
            {
                FailedAttempts++;
                if (FailedAttempts >= 5)
                {
                    RetryAfterUtc = DateTime.UtcNow.AddSeconds(30);
                    FailedAttempts = 0;
                    retryAfterSeconds = 30;
                }
            }
            return null;
        }

        lock (AttemptLock)
        {
            FailedAttempts = 0;
            RetryAfterUtc = DateTime.MinValue;
        }
        CleanupExpiredTokens();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        UnlockTokens[token] = DateTime.UtcNow.Add(UnlockLifetime);
        return token;
    }

    public static bool IsAuthorized(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !UnlockTokens.TryGetValue(token, out var expires)) return false;
        if (expires <= DateTime.UtcNow)
        {
            UnlockTokens.TryRemove(token, out _);
            return false;
        }
        return true;
    }

    public static void ValidatePassword(string password)
    {
        if (password.Length is < 6 or > 128)
            throw new ArgumentException("Password must be between 6 and 128 characters.", nameof(password));
    }

    private static void CleanupExpiredTokens()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in UnlockTokens)
            if (pair.Value <= now) UnlockTokens.TryRemove(pair.Key, out _);
    }

    private static void RevokeAllTokens() => UnlockTokens.Clear();

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return conn;
    }

    private static string? ReadSetting(string dbPath, string key)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key LIMIT 1";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    private static void WriteSetting(SqliteConnection conn, SqliteTransaction tx, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO settings (key, value) VALUES ($key, $value)";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private static void DeleteSetting(SqliteConnection conn, SqliteTransaction tx, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM settings WHERE key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.ExecuteNonQuery();
    }
}
