using System.Text;

namespace FM27Access.Speech;

/// <summary>
/// Thread-safe ring buffer of strings the mod has spoken, so the dev driver (which cannot
/// hear the TTS) can read back what was said. Each line has a stable monotonic index;
/// callers poll with a cursor to get only what is new. Fed by
/// <see cref="SpeechPipeline.Observer"/>.
///
/// Lives in the Speech project (not the dev folder) so it builds and is unit-tested without
/// the game, alongside the SpeechPipeline hook it pairs with.
/// </summary>
public sealed class SpeechLog
{
    private const int Capacity = 500;

    private readonly object _lock = new();
    // Fixed circular buffer: line with global index i lives at slot i % Capacity. Add is O(1)
    // (overwrite the slot, slide the window) instead of the old List.RemoveAt(0) that shifted ~499
    // elements under the lock on every call once full — and this is the hottest logging path.
    private readonly string[] _buffer = new string[Capacity];
    private long _base;   // global index of the oldest retained line (at slot _base % Capacity)
    private int _count;   // lines currently retained (0..Capacity)

    public void Add(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_lock)
        {
            long end = _base + _count;                // global index this line takes
            _buffer[(int)(end % Capacity)] = text;
            if (_count < Capacity)
                _count++;
            else
                _base++;                              // full: overwrote the oldest, window slides forward
        }
    }

    /// <summary>
    /// Render lines with global index &gt;= <paramref name="since"/>, one per line as
    /// "index: text". <paramref name="next"/> returns the cursor to pass next time to get
    /// only newer lines.
    /// </summary>
    public string Render(long since, out long next)
    {
        lock (_lock)
        {
            long end = _base + _count; // exclusive
            if (since < _base) since = _base;
            var sb = new StringBuilder();
            for (long i = since; i < end; i++)
                sb.Append(i).Append(": ").Append(_buffer[(int)(i % Capacity)]).Append('\n');
            next = end;
            return sb.ToString();
        }
    }
}
