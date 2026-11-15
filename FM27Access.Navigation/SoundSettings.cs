namespace FM27Access.Navigation;

/// <summary>
/// The player's match sound-cue preferences: one level per audio group plus the two bed switches.
/// Pure and game-independent so the defaults and the clamping stay unit-testable — the plugin owns
/// the JSON file and the HTTP endpoint around this, and the companion owns the audio graph these
/// numbers drive.
///
/// Levels are the slider position, 0..100, NOT a gain. The companion converts position to amplitude
/// through a perceptual taper; keeping the stored value as the slider position means the taper can be
/// re-shaped later without rewriting everyone's saved settings, and means a level reads the same in
/// the file as it does on screen.
///
/// Both switches — <see cref="WingDronesEnabled"/> and <see cref="PressureEnabled"/> — default to FALSE,
/// unlike every level, and that asymmetry is the point: each drives a sustained bed rather than an
/// occasional cue, and a bed is the kind of sound a player has to choose rather than have chosen for
/// them. Someone installing the mod for the first time gets the cues that mark events and nothing
/// humming underneath. Everything else defaults to <see cref="DefaultLevel"/>, which the companion maps
/// to exactly today's loudness — so a fresh install with both beds switched on sounds bit-identical to
/// the version before any of this existed.
/// </summary>
public sealed class SoundSettings
{
    /// <summary>Slider position meaning "as authored" — the level every group starts at.</summary>
    public const int DefaultLevel = 100;

    public const int MinLevel = 0;
    public const int MaxLevel = 100;

    /// <summary>The group keys, as they appear in the file and on the wire.</summary>
    public const string MasterKey = "master";
    public const string PossessionKey = "possession";
    public const string PlaybackKey = "playback";
    public const string EventsKey = "events";
    public const string DronesKey = "drones";
    public const string PressureKey = "pressure";

    /// <summary>The wing-drone on/off key, the one setting that is not a level.</summary>
    public const string WingDronesKey = "wingdrones";

    /// <summary>The carrier-pressure on/off key.</summary>
    public const string PressureEnabledKey = "pressureon";

    /// <summary>Everything, including the groups that bypass the match cue gate.</summary>
    public int Master { get; set; } = DefaultLevel;

    /// <summary>Possession ticks, the contested loop, and the ball-in-air loop.</summary>
    public int Possession { get; set; } = DefaultLevel;

    /// <summary>Fast forwarding and the replay start / end markers.</summary>
    public int Playback { get; set; } = DefaultLevel;

    /// <summary>The one-shot event sounds: restarts, attempts, goals, defensive actions.</summary>
    public int Events { get; set; } = DefaultLevel;

    /// <summary>The near and far wing drones, when they are switched on at all.</summary>
    public int Drones { get; set; } = DefaultLevel;

    /// <summary>The three carrier-pressure stems, when they are switched on at all.</summary>
    public int Pressure { get; set; } = DefaultLevel;

    /// <summary>Off until the player asks for it — see the type remarks for why this one differs.</summary>
    public bool WingDronesEnabled { get; set; }

    /// <summary>Off until the player asks for it, for the same reason as the wing drones.</summary>
    public bool PressureEnabled { get; set; }

    /// <summary>Force a level into range. Out-of-range input is clamped rather than rejected: it
    /// arrives from a slider that cannot produce it and from a file a player may have hand-edited,
    /// and a silently ignored write would leave the page showing a value the audio is not using.</summary>
    public static int ClampLevel(int level)
    {
        if (level < MinLevel) return MinLevel;
        if (level > MaxLevel) return MaxLevel;
        return level;
    }

    /// <summary>Apply a level by key, clamping it. False for an unknown key, so the caller can
    /// report the bad request instead of silently accepting a setting that changes nothing.</summary>
    public bool TrySetLevel(string key, int level)
    {
        int value = ClampLevel(level);
        switch (key)
        {
            case MasterKey: Master = value; return true;
            case PossessionKey: Possession = value; return true;
            case PlaybackKey: Playback = value; return true;
            case EventsKey: Events = value; return true;
            case DronesKey: Drones = value; return true;
            case PressureKey: Pressure = value; return true;
            default: return false;
        }
    }

    /// <summary>Read a level back by key; -1 for an unknown key (never a valid level).</summary>
    public int GetLevel(string key)
    {
        switch (key)
        {
            case MasterKey: return Master;
            case PossessionKey: return Possession;
            case PlaybackKey: return Playback;
            case EventsKey: return Events;
            case DronesKey: return Drones;
            case PressureKey: return Pressure;
            default: return -1;
        }
    }

    /// <summary>Apply the wing-drone switch by key. False for an unknown key.</summary>
    public bool TrySetToggle(string key, bool on)
    {
        switch (key)
        {
            case WingDronesKey: WingDronesEnabled = on; return true;
            case PressureEnabledKey: PressureEnabled = on; return true;
            default: return false;
        }
    }

    /// <summary>Bring a set of values loaded from disk back into range. A file written by an older
    /// build, or edited by hand, must not be able to push a gain out of bounds.</summary>
    public void Normalise()
    {
        Master = ClampLevel(Master);
        Possession = ClampLevel(Possession);
        Playback = ClampLevel(Playback);
        Events = ClampLevel(Events);
        Drones = ClampLevel(Drones);
        Pressure = ClampLevel(Pressure);
    }

    public SoundSettings Copy() => new()
    {
        Master = Master,
        Possession = Possession,
        Playback = Playback,
        Events = Events,
        Drones = Drones,
        Pressure = Pressure,
        WingDronesEnabled = WingDronesEnabled,
        PressureEnabled = PressureEnabled,
    };
}
