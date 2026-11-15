namespace FM27Access.Plugin.Server;

// Native reads are serialized on the Unity thread. Order the read itself,
// not delivery of its HTTP/SSE response. The boot clock survives Plugin reload;
// this integer remains exact in JavaScript for over 278 years of uptime.
internal static class NativeValueRevision
{
    private static long last;
    internal static long Next() => last = Math.Max(last + 1, Environment.TickCount64 * 1024L);
}
