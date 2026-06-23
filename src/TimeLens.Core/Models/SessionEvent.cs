namespace TimeLens.Core.Models;

public sealed class SessionEvent
{
    public long Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
