using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FM27Access.Plugin")]
[assembly: InternalsVisibleTo("FM27Access.Host")]
[assembly: InternalsVisibleTo("FM27Access.Tests")]

namespace FM27Access.Speech;

public static class Log
{
    private const string Prefix = "[FM27Access]";

    internal static Action<string> LogFn = msg => Console.WriteLine(msg);
    internal static Action<string> WarnFn = msg => Console.Error.WriteLine(msg);
    internal static Action<string> ErrorFn = msg => Console.Error.WriteLine(msg);

    // Debug has its own backend so the host can route it to the /log ring buffer WITHOUT the
    // Unity write behind LogFn: debug lines fire on hot paths (every spoken utterance, every
    // profile activation during a compose), and each Unity write is a synchronous Player.log
    // file write on the main thread. The ring keeps full fidelity for the dev driver.
    internal static Action<string> DebugFn = msg => Console.WriteLine(msg);

    [System.Diagnostics.Conditional("FM27_DEVELOPER_TOOLS")]
    public static void Debug(string msg)
    {
#if FM27_DEVELOPER_TOOLS
        DebugFn($"{Prefix} [DEBUG] {msg}");
#endif
    }
    public static void Info(string msg)  => LogFn($"{Prefix} {msg}");
    public static void Warn(string msg)  => WarnFn($"{Prefix} [WARN] {msg}");
    public static void Error(string msg) => ErrorFn($"{Prefix} [ERROR] {msg}");

    public static void Fatal(string msg)
    {
        ErrorFn($"{Prefix} [FATAL] {msg}");
    }
}
