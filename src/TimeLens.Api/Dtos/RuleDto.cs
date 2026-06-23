namespace TimeLens.Api.Dtos;

public sealed record RuleDto(long Id, string Pattern, string Category, bool IsUserDefined);

public sealed record CreateRuleDto(string Pattern, string Category);
