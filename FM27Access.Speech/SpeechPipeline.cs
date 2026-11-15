namespace FM27Access.Speech;

public static class SpeechPipeline
{
    private static readonly List<Func<string, string>> _filters = new();

    // Dedup state
    private static string _lastText = "";
    private static long _lastTimestamp;
    private const long DedupWindowMs = 50;

    internal static Action<string, bool> SpeakAction = SpeechEngine.Speak;
    internal static Func<long> TimeSource = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // Optional tap on every string the mod actually speaks (post-filter, post-dedup), set by
    // the dev driver's SpeechLog so an agent that cannot hear the TTS can read back what was
    // said. Null in normal play.
    public static Action<string> Observer;

    public static void SpeakInterrupt(string text)
    {
        var filtered = ApplyFilters(text);
        if (string.IsNullOrEmpty(filtered)) return;
        if (IsDuplicate(filtered)) return;
        Log.Debug($"SpeakInterrupt: \"{filtered}\"");
        Observer?.Invoke(filtered);
        SpeakAction(filtered, true);
    }

    public static void SpeakQueued(string text)
    {
        var filtered = ApplyFilters(text);
        if (string.IsNullOrEmpty(filtered)) return;
        if (IsDuplicate(filtered)) return;
        Log.Debug($"SpeakQueued: \"{filtered}\"");
        Observer?.Invoke(filtered);
        SpeakAction(filtered, false);
    }

    public static void RegisterFilter(Func<string, string> filter)
    {
        _filters.Add(filter);
    }

    internal static void ClearFilters()
    {
        _filters.Clear();
    }

    internal static string ApplyFilters(string text)
    {
        foreach (var filter in _filters)
            text = filter(text);
        return text;
    }

    private static bool IsDuplicate(string text)
    {
        var now = TimeSource();
        if (text == _lastText && (now - _lastTimestamp) < DedupWindowMs)
        {
            Log.Debug($"Dedup suppressed: \"{text}\"");
            return true;
        }
        _lastText = text;
        _lastTimestamp = now;
        return false;
    }
}
