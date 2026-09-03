namespace TimeLens.Api.Dtos;

public sealed record UpdateStatusDto
{
    public string CurrentVersion { get; init; } = "0.0.0";
    public string? LatestVersion { get; init; }
    public bool UpdateAvailable { get; init; }
    public bool Restarting { get; init; }
    public string Message { get; init; } = "";
    public string? Error { get; init; }
    public string? ReleaseNotes { get; init; }
}
