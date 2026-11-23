using TimeLens.TrayApp.Services;
using Xunit;

public sealed class CategoryClassifierTests
{
    [Fact]
    public void RuleSnapshotTracksPriorityChangesAndRemovals()
    {
        var classifier = new CategoryClassifier();
        classifier.AddCustomRule("editor", "development", priority: 10);
        classifier.AddCustomRule("editor.exe", "design", priority: 0);

        Assert.Equal("design", classifier.Classify("editor.exe"));

        Assert.True(classifier.RemoveCustomRule("editor.exe"));
        Assert.Equal("development", classifier.Classify("editor.exe"));

        classifier.AddCustomRule("editor", "work", priority: 0);
        Assert.Equal("work", classifier.Classify("editor.exe"));
    }

    [Fact]
    public void LoadingBuiltinsIsIdempotentAndKeepsTargetSpecificRules()
    {
        var classifier = new CategoryClassifier();
        var csv = Path.Combine(Path.GetTempPath(), $"TimeLens-categories-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(csv, "editor,exe,development,substring\neditor,title,design,substring\n");
            classifier.LoadBuiltins(csv);
            classifier.LoadBuiltins(csv);

            Assert.Equal(2, classifier.CustomRules.Count);
            Assert.Equal("development", classifier.Classify("editor.exe"));
            Assert.Equal("design", classifier.Classify("other.exe", "editor document"));
        }
        finally
        {
            File.Delete(csv);
        }
    }
}
