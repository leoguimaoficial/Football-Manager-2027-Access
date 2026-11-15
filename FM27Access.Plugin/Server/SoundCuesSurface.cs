using System;
using System.Collections.Generic;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The mod-authored "Sound cues settings" page: a written explanation of the match sound-cue
/// system plus a preview button for every cue, so a blind player can learn what each sound means
/// away from the pressure of live play. Opened from a trigger stop injected at the top of the
/// in-match settings card (MatchPreferencesCard); while open, ComposeBody renders ONLY this
/// section (modal, the bookmark-editor precedent) and the match is paused through the game's own
/// play/pause button, resuming when the page closes. The page is HTML-only (the speech cursor
/// never sees it, like the bookmark editor) and its previews play client-side through the
/// already-loaded Web Audio buffers (FM.soundcues.play), centred, un-panned, and capped at four
/// seconds so the sustained beds (wing drones, fast forwarding) do not run their full loop.
///
/// Unlike the bookmark editor there is no game widget: the open state is mod-owned (a static
/// bool, reset closed by a hot reload — harmless, the page is stateless). Pause and resume go
/// through <see cref="MatchPlayback"/>, which acts only on the observed sprite state of the
/// game's playpause button, never on an assumption, so an unknown state clicks nothing (a wrong
/// click would silently toggle the match the wrong way).
/// </summary>
internal static class SoundCuesSurface
{
    private const string PageTitle = "Sound cues settings";
    private const string TriggerLabel = "Sound cues settings";
    private const string CloseLabel = "Close sound cues settings";

    /// <summary>One previewable cue: its /sound name, the button label, an explanatory note, and
    /// the attenuation (dB below full volume) matching how the live cue actually plays.</summary>
    internal readonly struct Cue
    {
        public readonly string Name;
        public readonly string Label;
        public readonly string Note;
        public readonly int Db;

        public Cue(string name, string label, string note, int db = 0)
        {
            Name = name;
            Label = label;
            Note = note;
            Db = db;
        }
    }

    // Preview order mirrors how a player meets the sounds: the always-on ticks, the sustained
    // ball states, then the one-shot events. Attenuations match the live playback (FM.loop's
    // dbDown for contested/air) so previews teach the real relative loudness.
    private static readonly Cue[] Cues =
    {
        new("medium", "Your team possession tick",
            "Repeats while your team has the ball, speeding up as they close on goal."),
        new("opptick", "Opposition possession tick",
            "The same for the opposition. Speeding up means they are closing on your goal."),
        new("contested", "Contested ball",
            "Loops while both teams fight for the ball, over the top of the possession tick.", db: 2),
        new("dronenear", "Near wing",
            "Rises the nearer the ball is to the touchline the camera stands at.", db: 7),
        new("dronefar", "Far wing",
            "The same for the far side. Only one wing drone plays at a time.", db: 7),
        // The three pressure stems. In a match they layer cumulatively rather than playing alone, so each
        // preview is that stem by itself: what the third one adds, not what high pressure sounds like.
        new("pressurelow", "Press indicator, low",
            "One opponent closing on the player with the ball.", db: 7),
        new("pressuremed", "Press indicator, medium",
            "Added on top of the low layer when a second opponent closes in.", db: 7),
        new("pressurehigh", "Press indicator, high",
            "Added on top of both when a third opponent closes in.", db: 7),
        // Previewed at the middle of its ramp: in play this cue has no single level, so the midpoint is
        // the honest representative of it.
        new("air", "Ball in the air",
            "Plays while the ball is high with no one on it, louder the higher it goes.", db: 21),
        new("fastforwarding", "Fast forwarding",
            "Loops while the game skips between highlights, with every other cue silent."),
        new("replaystart", "Replay starting",
            "The game is rewinding to show a replay."),
        new("replayend", "Replay over",
            "The replay has finished and live play is back."),
        new("touchline", "Throw-in",
            "The ball went out over a touchline."),
        new("goalkick", "Goal kick",
            "The ball went out over a goal line for a goal kick."),
        new("cornerfor", "Corner for your team",
            "Your team has won a corner."),
        new("corneragainst", "Corner against your team",
            "The opposition has won a corner."),
        new("putinplay", "Ball put in play",
            "A kick-off or set piece being taken. A penalty sounds as an attempt on goal instead."),
        new("shotattempt", "Attempt on goal",
            "Either team has struck the ball at goal, before the outcome is known.", db: 3),
        new("headerattempt", "Headed attempt on goal",
            "The same, for an attempt headed at goal."),
        new("goalfor", "You score",
            "Your team puts the ball in the net. Check the score or commentary for a disallowed goal."),
        new("goalagainst", "Opposition scores",
            "The opposition puts the ball in the net. Check the score or commentary for a disallowed goal."),
        new("gooddefence", "Your team good defence",
            "An interception, tackle, save or block by your team."),
        new("oppgooddefence", "Opposition good defence",
            "The same for the opposition."),
    };

    private static readonly string[] Description =
    {
        "Two tick sounds tell you who has the ball: one tick for your team and a different tick for the opposition. "
        + "The tick repeats steadily while that team is clearly in control.",
        "Every sound is laid out the way a televised match is: one fixed camera at the side of the pitch, looking across it. The "
        + "left-right stereo position of a sound is the ball's position along the length of the pitch, goal line to goal line. The "
        + "home team attacks from left to right in the first half, the way a scoreboard puts the home side first, so when you are "
        + "away from home your team attacks right to left.",
        "The teams change ends at half time and the sound follows them, but the camera does not move. That is the whole idea: a "
        + "given patch of grass keeps the same place in the stereo field from the first whistle to the last, and it is the play "
        + "that is heard turning round at the break, not the pitch. So in the second half your team attacks toward the other side "
        + "of the stereo field from the one it attacked toward in the first.",
        "Extra time is read, not assumed. The ends for extra time are settled by a fresh toss rather than going back to how the "
        + "match started, so the mod asks which way each side is actually playing instead of counting periods and guessing. "
        + "Whichever way the toss sends them, the camera and the stereo field stay exactly where they were.",
        "The possession tick speeds up as the team on the ball closes on the goal it is attacking. It starts quickening at the "
        + "halfway line, about 52 metres out, climbs at a steady rate the whole way in, and reaches over five times the normal "
        + "rate at the goal mouth. How fast the tick is running is how near the ball is to a goal.",
        "Event sounds play once when something happens: the ball going out of play, the ball being put back into play, attempts "
        + "on goal, goals, and good defensive actions. They are placed at the ball's position, and the possession ticks briefly "
        + "drop in volume so the event stands clear.",
        "An attempt on goal sounds the instant the ball is struck, with a separate sound for a header, and says nothing about "
        + "where it went. What you hear next is the answer: a goal, a good defence for a save or a block, or a restart if it "
        + "went out.",
        "The stereo tells you where the ball is from goal to goal, and that uses up left and right entirely. Across the pitch there "
        + "is nothing left in the stereo field to say it with, so two low drones carry that direction instead: a near wing drone and "
        + "a far wing drone. The near wing is the touchline the camera stands at, the one along the bottom of a television picture; "
        + "the far wing is the one along the top. Between the stereo position and the wing drone you have the ball's place on the "
        + "pitch in both directions at once.",
        "A wing drone rises the nearer the ball gets to that touchline and is silent only while the ball is down the middle of the "
        + "pitch. Only one plays at a time: a ball carried across the pitch fades one drone away to nothing before the other begins, "
        + "so you hear it leave one wing and arrive at the other rather than the two overlapping.",
        "Near and far belong to the camera, never to a team, and they never swap over. This is the part worth getting straight, "
        + "because a team's own left and right do swap. Take a home side attacking down its right wing in the first half: it is "
        + "attacking left to right, and its right wing is the touchline nearest the camera, so you hear the near wing drone. In the "
        + "second half that same side is attacking the other way, so its right wing is now the far touchline, and the very same run "
        + "down the very same wing of that team sounds on the far wing drone instead.",
        "So a wing drone answers which side of the pitch the ball is on, not which side of a team it is on. It is a fact about the "
        + "ground, like the stereo position: the same patch of grass makes the same drone in the ninetieth minute as it did in the "
        + "first.",
        "Two sustained sounds describe the ball itself: the contested sound loops while both teams fight for the ball, and the "
        + "in-the-air sound plays while the ball is high with no one on it.",
        "Below full match detail, the game fast forwards between highlights. The fast forwarding sound loops in stereo the "
        + "whole time it does, every other cue is silent, and the loop finishing means the next highlight is starting.",
        "A replay of a big moment plays the replay starting sound as the game rewinds and the replay over sound as live play "
        + "returns, both in the centre of the stereo field. Goal sounds accompany the goal shown in a replay. Other "
        + "one-shot event sounds do not repeat for action you have already heard.",
        "Silence means there is no clear possession, the ball is dead, play is at a break, or the match is paused.",
    };

    private const string PreviewIntro =
        "Each button plays its sound once, centred, at the level it was built at rather than at your slider setting. "
        + "Looping sounds fade out after four seconds. Starting a sound stops the one before it.";

    /// <summary>One volume slider: the wire key, its label, and what it covers.</summary>
    internal readonly struct Group
    {
        public readonly string Key;
        public readonly string Label;
        public readonly string Note;

        public Group(string key, string label, string note)
        {
            Key = key;
            Label = label;
            Note = note;
        }
    }

    // Slider order runs from the widest scope inward: master first, then each group under it. The keys
    // match SoundSettings' wire keys, and the grouping matches the companion's audio buses one for one —
    // a slider that did not map to a single bus could not be applied without touching sounds it does not
    // name.
    private static readonly Group[] Groups =
    {
        new(SoundSettings.MasterKey, "Master volume",
            "Every match sound. Not the screen reader."),
        new(SoundSettings.PossessionKey, "Possession, contested and ball in air", ""),
        new(SoundSettings.PlaybackKey, "Fast forward and replay", ""),
        new(SoundSettings.EventsKey, "Event sounds",
            "Restarts, attempts, goals and defensive actions."),
        new(SoundSettings.DronesKey, "Wing drones", ""),
        new(SoundSettings.PressureKey, "Press indicator", ""),
    };

    private const string WingDroneToggleLabel = "Wing drone sounds";
    private const string PressureToggleLabel = "Press indicator sounds";

    private const string SettingsIntro =
        "Full is how each sound was built to play, so a slider left alone changes nothing.";

    // Mod-owned open state (no game widget behind this page). A hot reload resets it to closed,
    // which is safe: the page is stateless and the next token poll simply renders the base screen.
    private static bool _open;

    internal static bool IsOpen() => _open;

    /// <summary>Folded into the screen token so the SSE poll re-renders on open and close.</summary>
    internal static string Signature() => _open ? "soundcues:open" : "";

    private static void Open()
    {
        _open = true;
        MatchPlayback.PauseIfPlaying();
        SpeechPipeline.SpeakInterrupt("sound cues settings opened");
    }

    private static void Close()
    {
        _open = false;
        MatchPlayback.ResumeIfPaused();
        SpeechPipeline.SpeakInterrupt("sound cues settings closed");
    }

    /// <summary>Close if open; a no-op otherwise. Navigation hygiene (nav/bookmark/history), so
    /// navigating away never leaves the match silently paused behind a page that is gone.</summary>
    internal static void CloseIfOpen()
    {
        if (!_open) return;
        _open = false;
        MatchPlayback.ResumeIfPaused();
    }

    /// <summary>
    /// The page for ComposeBody, or null when closed. Static mod-authored content: the close
    /// stop first (context-menu precedent), the written description, then one preview stop per
    /// cue (rendered by <see cref="SoundCuePreviewRenderer"/> as a client-side play button).
    /// </summary>
    internal static (string Title, List<ScreenWalker.RenderItem> Items)? TryBuildSection()
    {
        if (!_open) return null;

        var items = new List<ScreenWalker.RenderItem>
        {
            new ScreenWalker.StopItem { Element = new CloseAdapter() },
            new ScreenWalker.HeadingItem { Text = "How the sound cues work", Level = 3 },
        };
        foreach (var paragraph in Description)
            items.Add(new ScreenWalker.NoteItem { Text = paragraph });

        items.Add(new ScreenWalker.HeadingItem { Text = "Customise sound settings", Level = 3 });
        items.Add(new ScreenWalker.NoteItem { Text = SettingsIntro });

        var settings = SoundSettingsStore.Current();
        if (SoundSettingsStore.PersistenceNotice is { } notice)
            items.Add(new ScreenWalker.NoteItem { Id = "fmsnd_save_notice", Text = notice });
        foreach (var group in Groups)
            items.Add(new ScreenWalker.StopItem
            {
                Element = new LevelAdapter(group, settings.GetLevel(group.Key)),
            });

        items.Add(new ScreenWalker.StopItem
        {
            Element = new ToggleAdapter(SoundSettings.WingDronesKey, WingDroneToggleLabel,
                                        settings.WingDronesEnabled),
        });

        items.Add(new ScreenWalker.StopItem
        {
            Element = new ToggleAdapter(SoundSettings.PressureEnabledKey, PressureToggleLabel,
                                        settings.PressureEnabled),
        });

        items.Add(new ScreenWalker.HeadingItem { Text = "Sound previews", Level = 3 });
        items.Add(new ScreenWalker.NoteItem { Text = PreviewIntro });
        foreach (var cue in Cues)
            items.Add(new ScreenWalker.StopItem { Element = new PreviewAdapter(cue) });

        return (PageTitle, items);
    }

    // --- adapters --------------------------------------------------------------------------------

    /// <summary>
    /// The "Sound cues settings" trigger stop, injected at the top of MatchPreferencesCard.
    /// Synthetic (no backing element); announces "expanded" while the page is open.
    /// </summary>
    internal sealed class TriggerAdapter : IAccessibleElement
    {
        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => TriggerLabel;
        public string GetPrimaryLabel() => TriggerLabel;
        public string GetDescription() => "";
        public bool IsVisible() => true;
        public string GetState() => IsOpen() ? "expanded" : "";
        public string GetAnnouncement()
        {
            var state = GetState();
            return TriggerLabel + ", " + Role + (state.Length > 0 ? ", " + state : "");
        }
        public void Activate()
        {
            if (IsOpen()) Close();
            else Open();
        }
    }

    /// <summary>The page's dismiss stop, first in the section.</summary>
    internal sealed class CloseAdapter : IAccessibleElement
    {
        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => CloseLabel;
        public string GetPrimaryLabel() => CloseLabel;
        public string GetDescription() => "";
        public bool IsVisible() => true;
        public string GetState() => "";
        public string GetAnnouncement() => CloseLabel + ", " + Role;
        public void Activate() => Close();
    }

    /// <summary>
    /// One preview row. The play action is client-side (FM.soundcues.play through the shared Web
    /// Audio buffers), owned by <see cref="SoundCuePreviewRenderer"/>; a server-side activation
    /// should never happen and degrades honestly if it somehow does.
    /// </summary>
    internal sealed class PreviewAdapter : IAccessibleElement
    {
        private readonly Cue _cue;
        internal PreviewAdapter(Cue cue) => _cue = cue;

        internal string SoundName => _cue.Name;
        internal string Note => _cue.Note;
        internal int Db => _cue.Db;
        private string Label => "Play: " + _cue.Label;

        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => Label;
        public string GetPrimaryLabel() => Label;
        public string GetDescription() => _cue.Note;
        public bool IsVisible() => true;
        public string GetState() => "";
        public string GetAnnouncement() => Label + ", " + Role;
        public void Activate()
            => Degrade.Note("[SoundCuesSurface] preview activated server-side; previews play in the companion only.");
    }

    /// <summary>
    /// One volume slider. The value rendered is the stored setting, so reopening the page shows what
    /// the player actually has rather than a default. Dragging applies client-side and persists through
    /// /soundsetting; there is no server-side activation path, matching the preview stops.
    /// </summary>
    internal sealed class LevelAdapter : IAccessibleElement
    {
        private readonly Group _group;
        private readonly int _level;

        internal LevelAdapter(Group group, int level)
        {
            _group = group;
            _level = level;
        }

        internal string Key => _group.Key;
        internal string Note => _group.Note;
        internal int Level => _level;

        public string Role => "slider";
        public bool CanActivate => false;
        public bool HasContextMenu => false;
        public string GetLabel() => _group.Label;
        public string GetPrimaryLabel() => _group.Label;
        public string GetDescription() => _group.Note;
        public bool IsVisible() => true;
        public string GetState() => _level + "%";
        public string GetAnnouncement() => _group.Label + ", " + Role + ", " + GetState();
        public void Activate()
            => Degrade.Note("[SoundCuesSurface] level activated server-side; sliders apply in the companion only.");
    }

    /// <summary>The wing-drone on/off switch — the one setting that is not a level.</summary>
    internal sealed class ToggleAdapter : IAccessibleElement
    {
        private readonly string _key;
        private readonly string _label;
        private readonly bool _on;

        internal ToggleAdapter(string key, string label, bool on)
        {
            _key = key;
            _label = label;
            _on = on;
        }

        internal string Key => _key;
        internal bool On => _on;

        public string Role => "checkbox";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => _label;
        public string GetPrimaryLabel() => _label;
        public string GetDescription() => "";
        public bool IsVisible() => true;
        public string GetState() => _on ? "checked" : "not checked";
        public string GetAnnouncement() => _label + ", " + Role + ", " + GetState();
        public void Activate()
            => Degrade.Note("[SoundCuesSurface] toggle activated server-side; it applies in the companion only.");
    }
}

/// <summary>
/// Renders a preview stop as a plain client-side play button: no server handle, the click plays
/// the sound in the companion's Web Audio (centred, un-panned, at the cue's real relative volume)
/// with the note following as text in the same paragraph.
/// </summary>
internal sealed class SoundCuePreviewRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el) => el is SoundCuesSurface.PreviewAdapter;

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        var preview = (SoundCuesSurface.PreviewAdapter)el;
        sb.Append("<p><button type=\"button\" onclick=\"FM.soundcues.play('")
          .Append(preview.SoundName).Append("',").Append(preview.Db).Append(")\">")
          .Append(ServerText.Escape(preview.GetLabel()))
          .Append("</button> ")
          .Append(ServerText.Escape(preview.Note))
          .Append("</p>");
    }
}

/// <summary>
/// Renders a volume slider as a native range input with an explicit label — native rather than an ARIA
/// widget so arrow keys, Home/End and the screen reader's own slider handling all come for free. The
/// input applies the level to the live audio graph and persists it; there is no server handle, so a
/// drag never waits on a round trip to be heard.
/// </summary>
internal sealed class SoundSettingLevelRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el) => el is SoundCuesSurface.LevelAdapter;

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        var level = (SoundCuesSurface.LevelAdapter)el;
        string id = "fmsnd_" + level.Key;
        sb.Append("<p><label for=\"").Append(id).Append("\">")
          .Append(ServerText.Escape(level.GetLabel()))
          .Append("</label> <input type=\"range\" id=\"").Append(id)
          .Append("\" min=\"0\" max=\"100\" step=\"5\" value=\"").Append(level.Level)
          .Append("\" aria-valuetext=\"").Append(level.Level).Append(" percent\"")
          .Append(" oninput=\"FM.soundsettings.setLevel('").Append(level.Key).Append("', this)\">");
        // Most sliders say everything in their label; only the ones that do not carry a note.
        if (level.Note.Length > 0) sb.Append(' ').Append(ServerText.Escape(level.Note));
        sb.Append("</p>");
    }
}

/// <summary>Renders the wing-drone switch as a native checkbox wrapped in its label.</summary>
internal sealed class SoundSettingToggleRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el) => el is SoundCuesSurface.ToggleAdapter;

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        var toggle = (SoundCuesSurface.ToggleAdapter)el;
        string id = "fmsnd_" + toggle.Key;
        sb.Append("<p><label for=\"").Append(id).Append("\">")
          .Append("<input type=\"checkbox\" id=\"").Append(id).Append('"');
        if (toggle.On) sb.Append(" checked");
        sb.Append(" onchange=\"FM.soundsettings.setToggle('").Append(toggle.Key).Append("', this)\"> ")
          .Append(ServerText.Escape(toggle.GetLabel()))
          .Append("</label></p>");
    }
}
