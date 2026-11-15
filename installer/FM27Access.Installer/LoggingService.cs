namespace FM27Access.Installer;

public sealed class LoggingService(string root)
{
    private readonly object sync = new();
    public string Path { get; } = System.IO.Path.Combine(root, "installer.log");
    public void Write(string message)
    {
        lock (sync)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            if (File.Exists(Path) && new FileInfo(Path).Length > 1024 * 1024) File.Move(Path, Path + ".previous", true);
            File.AppendAllText(Path, DateTimeOffset.UtcNow.ToString("o") + " " + message + Environment.NewLine);
        }
    }
}
