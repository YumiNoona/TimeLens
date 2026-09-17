namespace TimeLens.Api.Dtos;

public sealed record MediaStateDto(
    string MediaId,
    string Url,
    string Title,
    string Browser,
    int TabId,
    string Kind,
    bool Playing,
    bool Audible,
    bool Muted,
    bool PictureInPicture,
    string Visibility,
    string Confidence,
    DateTimeOffset ObservedAt
);
