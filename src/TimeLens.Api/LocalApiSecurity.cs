using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TimeLens.Api;

/// <summary>
/// Keeps the dashboard session in memory and persists only a hash of the paired
/// Firefox token. The token grants access solely to the extension endpoints;
/// it is never an administrator credential.
/// </summary>
public sealed class LocalApiSecurity
{
    public const string DashboardCookieName = "TimeLens-Session";
    public const string ExtensionHeaderName = "X-TimeLens-Extension";
    private static readonly TimeSpan PairCodeLifetime = TimeSpan.FromMinutes(2);
    private readonly string _dbPath;
    private readonly string _dashboardToken = Token();
    private readonly object _pairGate = new();
    private string? _pairCodeHash;
    private DateTime _pairCodeExpiresUtc;
    private int _pairFailures;
    private string? _extensionTokenHash;

    public LocalApiSecurity(string dbPath)
    {
        _dbPath = dbPath;
        _extensionTokenHash = ReadSetting("firefox_pair_token_hash");
    }

    public void IssueDashboardCookie(HttpContext context)
    {
        context.Response.Cookies.Append(DashboardCookieName, _dashboardToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = false, // The service intentionally listens on loopback HTTP only.
            IsEssential = true,
            Path = "/",
            MaxAge = TimeSpan.FromHours(12)
        });
    }

    public bool IsDashboard(HttpContext context) =>
        FixedEquals(context.Request.Cookies[DashboardCookieName], _dashboardToken);

    public bool IsExtension(HttpContext context)
    {
        var presented = context.Request.Headers[ExtensionHeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(presented)) return false;
        var expectedHash = Volatile.Read(ref _extensionTokenHash);
        return !string.IsNullOrWhiteSpace(expectedHash) &&
               FixedEquals(Hash(presented), expectedHash);
    }

    public string CreatePairCode()
    {
        var code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8");
        lock (_pairGate)
        {
            _pairCodeHash = Hash(code);
            _pairCodeExpiresUtc = DateTime.UtcNow.Add(PairCodeLifetime);
            _pairFailures = 0;
        }
        return code;
    }

    public string? ExchangePairCode(string code)
    {
        lock (_pairGate)
        {
            if (_pairCodeHash is null || DateTime.UtcNow > _pairCodeExpiresUtc) return null;
            if (!FixedEquals(Hash(code.Trim()), _pairCodeHash))
            {
                if (++_pairFailures >= 5)
                {
                    _pairCodeHash = null;
                    _pairCodeExpiresUtc = DateTime.MinValue;
                }
                return null;
            }
            _pairCodeHash = null;
            _pairCodeExpiresUtc = DateTime.MinValue;
            _pairFailures = 0;
        }

        var token = Token();
        var tokenHash = Hash(token);
        WriteSetting("firefox_pair_token_hash", tokenHash);
        Volatile.Write(ref _extensionTokenHash, tokenHash);
        return token;
    }

    public void RevokeExtension()
    {
        DeleteSetting("firefox_pair_token_hash");
        Volatile.Write(ref _extensionTokenHash, null);
    }

    private string? ReadSetting(string key)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key=$key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    private void WriteSetting(string key, string value)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private void DeleteSetting(string key)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM settings WHERE key=$key";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.ExecuteNonQuery();
    }

    private static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null) return false;
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
