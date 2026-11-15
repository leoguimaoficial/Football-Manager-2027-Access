using FM27Access.Plugin.Server;

namespace FM27Access.Tests;

public sealed class SettingsFileWriterTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fm27-settings-test-" + Guid.NewGuid().ToString("N"));

    public SettingsFileWriterTests() => Directory.CreateDirectory(root);

    [Fact]
    public void ReplacesCompletedFileAndLeavesNoTemporarySibling()
    {
        var target = Path.Combine(root, "settings.json");
        File.WriteAllText(target, "old");
        SettingsFileWriter.Write(target, "new");
        Assert.Equal("new", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(root));
    }

    [Fact]
    public void CreatesSettingsDirectoryForFirstWrite()
    {
        var target = Path.Combine(root, "nested", "settings.json");
        SettingsFileWriter.Write(target, "first");
        Assert.Equal("first", File.ReadAllText(target));
    }

    [Fact]
    public void RenameFailurePreservesExistingDirectoryAndRemovesTemporaryFile()
    {
        var target = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep"), "original");
        var failure = Record.Exception(() => SettingsFileWriter.Write(target, "replacement"));
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        Assert.Equal("original", File.ReadAllText(Path.Combine(target, "keep")));
        Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public void InvalidParentPreservesOriginalFile()
    {
        var parent = Path.Combine(root, "parent");
        File.WriteAllText(parent, "original");
        Assert.ThrowsAny<IOException>(() => SettingsFileWriter.Write(Path.Combine(parent, "settings.json"), "replacement"));
        Assert.Equal("original", File.ReadAllText(parent));
        Assert.Single(Directory.GetFiles(root));
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(root);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), resolved);
        Assert.StartsWith("fm27-settings-test-", Path.GetFileName(resolved));
        Directory.Delete(resolved, recursive: true);
    }
}
