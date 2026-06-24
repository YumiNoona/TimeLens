namespace TimeLens.Api.Dtos;

public sealed record DashboardResponse(
    SummaryDto Summary,
    TimelineBlockDto[] Timeline,
    TopAppDto[] TopApps,
    HeatmapEntryDto[] Heatmap,
    CategoryEntryDto[] Categories,
    LiveStatusDto Live
);

public sealed record SummaryDto(
    string ActiveTime,
    int ActiveSeconds,
    string IdleTime,
    int IdleSeconds,
    int FocusScore,
    string TopCategory,
    string TopCategoryTime,
    int? VsYesterday
);

public sealed record TimelineBlockDto(
    double StartHour,
    double EndHour,
    string Type
);

public sealed record TopAppDto(
    string Name,
    int Minutes
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
