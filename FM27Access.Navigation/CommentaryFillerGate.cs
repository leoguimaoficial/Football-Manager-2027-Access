namespace FM27Access.Navigation;

/// <summary>
/// Pure timing gate for the commentary gap filler: the possession line (minus its team clause)
/// is inserted into the match live region only while the game's own play-by-play has gone quiet, so
/// the enrichment can never crowd real commentary. The game-coupled reads live in the plugin (the SSE
/// loop feeds this each tick); this holds the quiet clock and dedupe state so the rules are
/// unit-testable without the game:
///   - a real commentary change resets the quiet clock (and remembers what the region now says);
///   - a filler emits only once the commentary has been quiet for <see cref="QuietMs"/>, at most every
///     <see cref="SpacingMs"/>, and only when it differs from the last text put in the region (real or
///     filler) — new information speaks, the same fact never repeats back-to-back.
/// The live region is aria-live=polite, so an emitted filler queues behind any speech in progress; the
/// gate's job is only to keep fillers out of the way of the real stream, not to sequence speech.
/// </summary>
public struct CommentaryFillerGate
{
    // How long the game commentary must be unchanged before a filler may speak, and the minimum
    // spacing between fillers while the gap continues. Per the user's tuning: 2 seconds each.
    private const long QuietMs = 2000;
    private const long SpacingMs = 2000;

    private long _lastCommentaryMs;
    private long _lastFillerMs;
    private string _lastRegionText;

    /// <summary>
    /// Cheap pre-check for this tick: whether the quiet and spacing clocks would allow a filler, so
    /// the caller only pays for composing a candidate (a full player scan) when one could actually
    /// emit. A commentary change this tick still vetoes in <see cref="Observe"/>.
    /// </summary>
    public readonly bool WantsCandidate(long nowMs)
        => nowMs - _lastCommentaryMs >= QuietMs
        && (_lastFillerMs == 0 || nowMs - _lastFillerMs >= SpacingMs);

    /// <summary>
    /// Fold one tick in and return whether <paramref name="candidate"/> should be inserted into the
    /// live region. <paramref name="commentaryChanged"/> true means the game's own line just updated
    /// (with <paramref name="commentaryText"/> now in the region): the quiet clock resets and nothing
    /// fills this tick. A null candidate (possession unavailable — e.g. between highlights) never
    /// emits and never disturbs the clocks.
    /// </summary>
    public bool Observe(long nowMs, bool commentaryChanged, string commentaryText, string candidate)
    {
        if (commentaryChanged)
        {
            _lastCommentaryMs = nowMs;
            _lastRegionText = commentaryText;
            return false;
        }
        if (candidate == null) return false;
        if (nowMs - _lastCommentaryMs < QuietMs) return false;
        if (_lastFillerMs != 0 && nowMs - _lastFillerMs < SpacingMs) return false;
        if (candidate == _lastRegionText) return false;

        _lastFillerMs = nowMs;
        _lastRegionText = candidate;
        return true;
    }
}
