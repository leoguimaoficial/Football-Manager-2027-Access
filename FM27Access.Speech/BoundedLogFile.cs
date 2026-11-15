using System.Text;

namespace FM27Access.Speech;

/// <summary>Short-lived managed writes, bounded throughout a long game session.</summary>
public static class BoundedLogFile
{
    private static readonly object Gate = new();

    public static void Append(string path, string message, int maxBytes = 8 * 1024 * 1024,
        int maxEntryBytes = 64 * 1024)
    {
        if (maxBytes < 128 || maxEntryBytes < 8 || maxEntryBytes > maxBytes)
            throw new ArgumentOutOfRangeException(nameof(maxEntryBytes));
        var bytes = new byte[Math.Min(maxEntryBytes - 1, Encoding.UTF8.GetMaxByteCount(message.Length))];
        Encoding.UTF8.GetEncoder().Convert(message.AsSpan(), bytes.AsSpan(), true,
            out _, out int length, out _);
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var info = new FileInfo(path);
            if (info.Exists && info.Length + length + 1 > maxBytes)
                File.Move(path, path + ".bak", overwrite: true);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes, 0, length);
            stream.WriteByte((byte)'\n');
        }
    }
}
