using System.Text.Json.Serialization;

namespace TimeLens.Api;

public record AppSettings
{
    [JsonPropertyName("offlineWorkEnabled")]
    public bool OfflineWorkEnabled { get; init; } = false;
    [JsonPropertyName("collectionSince")]
    public string CollectionSince { get; init; } = "";
    [JsonPropertyName("ignoredApps")]
    public string IgnoredApps { get; init; } = "";
    [JsonPropertyName("hiddenTitleApps")]
    public string HiddenTitleApps { get; init; } = "";
    [JsonPropertyName("ignoredDomains")]
    public string IgnoredDomains { get; init; } = "";
    [JsonPropertyName("titleRedactionPatterns")]
    public string TitleRedactionPatterns { get; init; } = "";
    [JsonPropertyName("pauseUntil")]
    public string PauseUntil { get; init; } = "";
    [JsonPropertyName("trackAudio")]
    public bool TrackAudio { get; init; } = true;

    [JsonPropertyName("trackBrowser")]
    public bool TrackBrowser { get; init; } = true;

    [JsonPropertyName("trackInput")]
    public bool TrackInput { get; init; } = true;

    [JsonPropertyName("browserUrlMode")]
    public string BrowserUrlMode { get; init; } = "full";

    [JsonPropertyName("browserStoreTitles")]
    public bool BrowserStoreTitles { get; init; } = true;

    [JsonPropertyName("idleThresholdSeconds")]
    public int IdleThresholdSeconds { get; init; } = 180;

    [JsonPropertyName("theme")]
    public string Theme { get; init; } = "default";

    [JsonPropertyName("timelineGrouped")]
    public bool TimelineGrouped { get; init; } = true;

    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; init; } = false;

    [JsonPropertyName("retentionDays")]
    public int RetentionDays { get; init; } = 90;

    [JsonPropertyName("showTitles")]
    public bool ShowTitles { get; init; } = false;

    [JsonPropertyName("breakReminder")]
    public bool BreakReminder { get; init; } = false;

    [JsonPropertyName("breakIntervalMinutes")]
    public int BreakIntervalMinutes { get; init; } = 50;

    [JsonPropertyName("focusMode")]
    public bool FocusMode { get; init; } = false;

    [JsonPropertyName("focusBlocklist")]
    public string FocusBlocklist { get; init; } = "[]";

    [JsonPropertyName("blockAction")]
    public string BlockAction { get; init; } = "hide";

    [JsonPropertyName("blockTitle")]
    public string BlockTitle { get; init; } = BlockNotification.DefaultTitle;

    [JsonPropertyName("blockMessage")]
    public string BlockMessage { get; init; } = BlockNotification.DefaultMessage;

    [JsonPropertyName("blockImageVersion")]
    public string BlockImageVersion { get; init; } = "";

    [JsonPropertyName("blockMediaType")]
    public string BlockMediaType { get; init; } = "";

    [JsonPropertyName("blockNotifyIntervalSeconds")]
    public int BlockNotifyIntervalSeconds { get; init; } = 300;

    [JsonPropertyName("blockNotifyPosition")]
    public string BlockNotifyPosition { get; init; } = "bottom-left";

    [JsonPropertyName("blockMediaLayout")]
    public string BlockMediaLayout { get; init; } = "large";

    [JsonPropertyName("timeFormat")]
    public string TimeFormat { get; init; } = "12h";

    [JsonPropertyName("showSeconds")]
    public bool ShowSeconds { get; init; } = false;

    [JsonPropertyName("pollIntervalSeconds")]
    public int PollIntervalSeconds { get; init; } = 30;

    [JsonPropertyName("defaultView")]
    public string DefaultView { get; init; } = "today";

    [JsonPropertyName("density")]
    public string Density { get; init; } = "comfortable";

    [JsonPropertyName("motionEnabled")]
    public bool MotionEnabled { get; init; } = true;

    [JsonPropertyName("timelineMinSegmentSeconds")]
    public int TimelineMinSegmentSeconds { get; init; } = 60;

    [JsonPropertyName("heatmapDays")]
    public int HeatmapDays { get; init; } = 365;

    [JsonPropertyName("blockProtectionEnabled")]
    public bool BlockProtectionEnabled { get; init; } = false;

    [JsonPropertyName("blockProtectionScope")]
    public string BlockProtectionScope { get; init; } = "strict";

    [JsonPropertyName("blockExitProtection")]
    public bool BlockExitProtection { get; init; } = true;
}
