namespace TimeLens.Core.Interfaces;

public interface ICategoryClassifier
{
    string Classify(string exeName, string? windowTitle = null, string? domain = null);
}
