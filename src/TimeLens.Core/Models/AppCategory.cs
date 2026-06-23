namespace TimeLens.Core.Models;

public sealed class AppCategoryEntry
{
    public long Id { get; set; }
    public string ExeName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsUserDefined { get; set; }
}
