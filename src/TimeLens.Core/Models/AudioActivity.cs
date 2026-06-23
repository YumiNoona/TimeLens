namespace TimeLens.Core.Models;

public sealed class AudioActivity
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int? Pid { get; set; }
    public string? ExeName { get; set; }
    public int? SessionId { get; set; }
    public bool IsPlaying { get; set; }
}
