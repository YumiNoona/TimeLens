namespace TimeLens.Api.Dtos;

public sealed record TodaySummaryDto(
    string ActiveTime,
    int ActiveSeconds,
    string IdleTime,
    int IdleSeconds,
    int FocusScore,
    string TopCategory,
    string TopCategoryTime,
    int VsYesterday
);
