using System.Text.Json.Serialization;

namespace TimeLens.Api;

public record AppSettings
{
    [JsonPropertyName("trackAudio")]
    public bool TrackAudio { get; init; } = true;

    [JsonPropertyName("trackBrowser")]
    public bool TrackBrowser { get; init; } = true;

    [JsonPropertyName("trackInput")]
    public bool TrackInput { get; init; } = true;

    [JsonPropertyName("idleThresholdSeconds")]
    public int IdleThresholdSeconds { get; init; } = 180;

    [JsonPropertyName("theme")]
    public string Theme { get; init; } = "moss";

    [JsonPropertyName("timelineGrouped")]
    public bool TimelineGrouped { get; init; } = false;

    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; init; } = false;

    [JsonPropertyName("retentionDays")]
    public int RetentionDays { get; init; } = 90;
}
