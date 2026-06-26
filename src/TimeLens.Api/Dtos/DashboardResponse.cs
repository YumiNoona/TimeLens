namespace TimeLens.Api.Dtos;

public sealed record DashboardResponse(
    SummaryDto Summary,
    TimelineBlockDto[] Timeline,
    TopAppDto[] TopApps,
    HeatmapEntryDto[] Heatmap,
    CategoryEntryDto[] Categories,
    LiveStatusDto Live,
    BrowserEntryDto[] BrowserSites = null,
    AudioSessionDto[] AudioSessions = null
);

public sealed record SummaryDto(
    string ActiveTime,
    int ActiveSeconds,
    string IdleTime,
    int IdleSeconds,
    int FocusScore,
    string TopCategory,
    string TopCategoryTime,
    int? VsYesterday,
    int TotalKeystrokes,
    int TotalClicks
);

public sealed record InputSummaryDto(
    string ExeName,
    int Keystrokes,
    int Clicks
);

public sealed record BrowserEntryDto(
    string Domain,
    int Visits,
    string LastVisit
);

public sealed record AudioSessionDto(
    string ExeName,
    int Sessions,
    string FirstSeen
);

public sealed record TimelineBlockDto(
    double StartHour,
    double EndHour,
    string Type,
    string ExeName,
    string? WindowTitle,
    int DurationSeconds,
    string? Project = null
);

public sealed record TopAppDto(
    string Name,
    int Minutes,
    int Keystrokes = 0,
    int Clicks = 0
);

public sealed record HeatmapEntryDto(
    string Date,
    int Value
);

public sealed record CategoryEntryDto(
    string Name,
    double Percentage,
    int Minutes
);

public sealed record LiveStatusDto(
    string CurrentApp,
    int IdleMinutes,
    bool IsIdle,
    string? AudibleTab,
    bool AudioActive,
    string SystemState,
    bool PendingIdleReturn
);
