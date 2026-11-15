using System.Text;
using FM27Access.Speech;

namespace FM27Access.Tests;

public sealed class BoundedLogFileTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "fm27-log-" + Guid.NewGuid().ToString("N"));
    private string LogPath => Path.Combine(folder, "test.log");

    [Fact]
    public void RotatesDuringTheSameSessionAndRetainsOnlyOnePreviousFile()
    {
        for (int i = 0; i < 20; i++) BoundedLogFile.Append(LogPath, i + new string('x', 80), 256, 128);
        Assert.InRange(new FileInfo(LogPath).Length, 1, 256);
        Assert.InRange(new FileInfo(LogPath + ".bak").Length, 1, 256);
        Assert.Contains("19", File.ReadAllText(LogPath));
        Assert.Equal(2, Directory.GetFiles(folder).Length);
    }

    [Fact]
    public void OversizedUnicodeEntryRemainsValidUtf8AndWithinTheLimit()
    {
        BoundedLogFile.Append(LogPath, string.Concat(Enumerable.Repeat("⚽🏆é", 200)), 256, 128);
        var bytes = File.ReadAllBytes(LogPath);
        Assert.InRange(bytes.Length, 1, 128);
        Assert.EndsWith("\n", new UTF8Encoding(false, true).GetString(bytes));
    }

    [Fact]
    public void ConcurrentWritersKeepWholeEntriesAndBoundBothFiles()
    {
        Parallel.For(0, 100, i => BoundedLogFile.Append(LogPath, $"entry:{i}:end", 256, 128));
        foreach (string file in Directory.GetFiles(folder))
        {
            Assert.InRange(new FileInfo(file).Length, 1, 256);
            Assert.All(File.ReadAllLines(file), line => Assert.Matches("^entry:[0-9]+:end$", line));
        }
    }

    [Fact]
    public void IoFailureIsReportedToTheCaller()
    {
        BoundedLogFile.Append(LogPath, "first", 256, 128);
        using var exclusive = new FileStream(LogPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => BoundedLogFile.Append(LogPath, "second", 256, 128));
    }

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
}
