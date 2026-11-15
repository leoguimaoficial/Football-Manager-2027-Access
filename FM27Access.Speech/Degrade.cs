namespace FM27Access.Speech;

/// <summary>
/// The "degrade in place, never lie" path. When the mod cannot read, classify, or act on one
/// element, value, or widget, it emits an honest, unmistakable gap marker, logs the reason, and
/// keeps the rest of the mod running — instead of halting everything. This is NOT a lie: a marker
/// the player clearly understands as a gap is the opposite of speaking wrong data as if it were
/// real.
///
/// Reserved for *localized* failures. A broken mod foundation (Prism/Speech init, Harmony patch
/// application, input-system init) still halts via <c>AccessibilityMod.Kill()</c>, because nothing
/// the mod says can be trusted after that. See docs/reference/failure-handling.md.
///
/// The markers are centralized here (not inline literals) so every surface degrades with the same
/// words. A full LocString/translation pass for them is deferred — the mod already speaks
/// mod-authored chrome ("cancelled", "Mod reloaded") as plain strings.
/// </summary>
public static class Degrade
{
    /// <summary>An interactive element the mod could not map to a known widget.</summary>
    public const string UnknownControl = "unknown control";

    /// <summary>A value (score, label, field) that was present but could not be read.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>A read that failed where no more specific marker fits.</summary>
    public const string Unreadable = "couldn't read";

    /// <summary>
    /// Record a localized failure so it is diagnosable via /log without tearing the mod down.
    /// Logs at Warn (lands in the LogHistory ring buffer the dev driver serves at /log). Use this
    /// for one-off read/activate failures, NOT for per-frame classification of an unknown widget
    /// type — route those through <c>UnclassifiedTracker</c>, which dedupes by type.
    /// </summary>
    public static void Note(string reason) => Log.Warn($"[degrade] {reason}");

    /// <summary>Log the reason and return the "unknown control" marker.</summary>
    public static string Control(string reason) { Note(reason); return UnknownControl; }

    /// <summary>Log the reason and return the "unavailable" marker.</summary>
    public static string Value(string reason) { Note(reason); return Unavailable; }

    /// <summary>Log the reason and return the "couldn't read" marker.</summary>
    public static string Read(string reason) { Note(reason); return Unreadable; }
}
