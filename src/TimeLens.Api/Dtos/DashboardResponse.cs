namespace TimeLens.Api.Dtos;

public sealed record DashboardResponse(
    SummaryDto Summary,
    TimelineBlockDto[] Timeline,
    TopAppDto[] TopApps,
    HeatmapEntryDto[] Heatmap,
    CategoryEntryDto[] Categories,
    LiveStatusDto Live,
    BrowserEntryDto[] BrowserSites,
    AudioSessionDto[] AudioSessions
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
    string LastVisit,
    double TotalSeconds = 0,
    int? Keystrokes = null,
    int? Clicks = null,
    BrowserPageDto[]? Pages = null
);

public sealed record BrowserPageDto(string Url, string Title, string Browser, double TotalSeconds,
    int Sessions, string LastSeen, int? Keystrokes, int? Clicks);

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
    double Minutes,
    int Keystrokes = 0,
    int Clicks = 0
);

public sealed record HeatmapEntryDto(
    string Date,
    double Value
);

public sealed record CategoryEntryDto(
    string Name,
    double Percentage,
    double Minutes
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
