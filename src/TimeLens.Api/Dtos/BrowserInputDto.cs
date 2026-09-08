namespace TimeLens.Api.Dtos;

public sealed record BrowserInputDto(string BatchId, string Url, string Title, string Browser,
    DateTimeOffset ObservedAt, int Keystrokes, int Clicks);
