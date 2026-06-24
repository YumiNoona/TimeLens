namespace TimeLens.Api.Dtos;

public sealed record BrowserEventDto(
    string Domain,
    string Url,
    string Title,
    string Browser,
    bool Audible,
    int TabId = 0
);
