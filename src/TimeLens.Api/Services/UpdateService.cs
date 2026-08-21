using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TimeLens.Api.Dtos;

namespace TimeLens.Api.Services;

public sealed class UpdateService : IDisposable
{
    public const string DefaultFeedUrl = "https://timelens.venusapp.in/api/latest-release";
    private const long MaximumDownloadBytes = 250L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly string _feedUrl;
    private readonly SemaphoreSlim _installGate = new(1, 1);
    private bool _disposed;

    private sealed record ReleaseManifest(string Version, Uri DownloadUri, string Sha256, long Size);

    public UpdateService(string? feedUrl = null)
    {
        _feedUrl = feedUrl
            ?? Environment.GetEnvironmentVariable("TIMELENS_UPDATE_FEED_URL")
            ?? DefaultFeedUrl;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TimeLens-Updater/4.0");
    }

    public async Task<UpdateStatusDto> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion();
        try
        {
            var manifest = await GetManifestAsync(cancellationToken);
            var available = ParseVersion(manifest.Version) > ParseVersion(current);
            return new UpdateStatusDto
            {
                CurrentVersion = current,
                LatestVersion = manifest.Version,
                UpdateAvailable = available,
                Message = available
                    ? $"TimeLens {manifest.Version} is ready to install."
                    : "You have the latest production version."
            };
        }
        catch
        {
            return new UpdateStatusDto
            {
                CurrentVersion = current,
                Message = "TimeLens could not check for updates.",
                Error = "The update service is unavailable. Try again later."
            };
        }
    }

    public async Task<UpdateStatusDto> DownloadAndStageAsync(CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion();
        string? temporaryPath = null;
        await _installGate.WaitAsync(cancellationToken);
        try
        {
            var manifest = await GetManifestAsync(cancellationToken);
            if (ParseVersion(manifest.Version) <= ParseVersion(current))
            {
                return new UpdateStatusDto
                {
                    CurrentVersion = current,
                    LatestVersion = manifest.Version,
                    Message = "You have the latest production version."
                };
            }

            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The current executable path is unavailable.");
            var executableName = Path.GetFileName(executablePath);
            if (!executableName.Equals("TimeLens.exe", StringComparison.OrdinalIgnoreCase) &&
                !executableName.Equals("TimeLens.TrayApp.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Updates are available only in the packaged TimeLens app.");
            EnsureInstallDirectoryIsWritable(executablePath);

            var updateDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TimeLens", "updates");
            Directory.CreateDirectory(updateDirectory);
            var stagedPath = Path.Combine(updateDirectory, $"TimeLens-{manifest.Version}.exe");
            temporaryPath = stagedPath + ".new";

            using (var response = await _httpClient.GetAsync(
                manifest.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidDataException("The update download did not use HTTPS.");
                if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
                    throw new InvalidDataException("The update is larger than the supported limit.");

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = new FileStream(
                    temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(destination, cancellationToken);
            }

            var fileInfo = new FileInfo(temporaryPath);
            if (fileInfo.Length == 0 || fileInfo.Length > MaximumDownloadBytes ||
                (manifest.Size > 0 && fileInfo.Length != manifest.Size))
                throw new InvalidDataException("The update download is incomplete.");

            await using (var executable = File.OpenRead(temporaryPath))
            {
                if (executable.ReadByte() != 'M' || executable.ReadByte() != 'Z')
                    throw new InvalidDataException("The update is not a Windows executable.");
                executable.Position = 0;
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(executable, cancellationToken));
                if (!actualHash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update checksum does not match the release.");
            }

            File.Move(temporaryPath, stagedPath, overwrite: true);
            StartReplacementProcess(stagedPath, executablePath);
            return new UpdateStatusDto
            {
                CurrentVersion = current,
                LatestVersion = manifest.Version,
                UpdateAvailable = true,
                Restarting = true,
                Message = $"TimeLens {manifest.Version} is verified. Restarting to finish the update…"
            };
        }
        catch (Exception)
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); } catch { }
            }
            return new UpdateStatusDto
            {
                CurrentVersion = current,
                Message = "The update was not installed.",
                Error = "TimeLens could not verify or replace the executable. Try downloading it from the website."
            };
        }
        finally
        {
            _installGate.Release();
        }
    }

    private async Task<ReleaseManifest> GetManifestAsync(CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(_feedUrl, UriKind.Absolute, out var feedUri) || feedUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The update feed must use HTTPS.");

        using var response = await _httpClient.GetAsync(feedUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var version = root.GetProperty("version").GetString() ?? "";
        var downloadUrl = root.GetProperty("downloadUrl").GetString() ?? "";
        var sha256 = root.GetProperty("sha256").GetString() ?? "";
        var size = root.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0;

        _ = ParseVersion(version);
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("The update download must use HTTPS.");
        if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The update checksum is invalid.");
        if (size < 0 || size > MaximumDownloadBytes)
            throw new InvalidDataException("The update size is invalid.");

        return new ReleaseManifest(version, downloadUri, sha256, size);
    }

    private static string CurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        return $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    private static Version ParseVersion(string value)
    {
        if (!Version.TryParse(value, out var version) || version.Build < 0)
            throw new InvalidDataException("The release version is invalid.");
        return version;
    }

    private static void EnsureInstallDirectoryIsWritable(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath)
            ?? throw new InvalidOperationException("The executable folder is unavailable.");
        var probe = Path.Combine(directory, $".timelens-write-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        finally
        {
            if (File.Exists(probe)) File.Delete(probe);
        }
    }

    private static string PowerShellLiteral(string value) => $"'{value.Replace("'", "''")}'";

    private static void StartReplacementProcess(string stagedPath, string executablePath)
    {
        var updateDirectory = Path.GetDirectoryName(stagedPath)!;
        var scriptPath = Path.Combine(updateDirectory, $"apply-{Guid.NewGuid():N}.ps1");
        var processId = Environment.ProcessId;
        var staged = PowerShellLiteral(stagedPath);
        var target = PowerShellLiteral(executablePath);
        var script = PowerShellLiteral(scriptPath);
        var content = $$"""
            $ErrorActionPreference = 'Stop'
            for ($i = 0; $i -lt 120; $i++) {
              if (-not (Get-Process -Id {{processId}} -ErrorAction SilentlyContinue)) { break }
              Start-Sleep -Milliseconds 250
            }
            if (Get-Process -Id {{processId}} -ErrorAction SilentlyContinue) { exit 2 }
            $copied = $false
            for ($i = 0; $i -lt 40; $i++) {
              try {
                Copy-Item -LiteralPath {{staged}} -Destination {{target}} -Force
                $copied = $true
                break
              } catch {
                Start-Sleep -Milliseconds 250
              }
            }
            if (-not $copied) {
              Start-Process -FilePath {{target}}
              exit 3
            }
            Start-Process -FilePath {{target}}
            Start-Sleep -Milliseconds 500
            Remove-Item -LiteralPath {{staged}} -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath {{script}} -Force -ErrorAction SilentlyContinue
            """;
        File.WriteAllText(scriptPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = updateDirectory
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden",
            "-ExecutionPolicy", "Bypass", "-File", scriptPath
        })
            startInfo.ArgumentList.Add(argument);
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("The update helper could not start.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
        _installGate.Dispose();
    }
}
