using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class SquadPlayerButtonAdapter : ButtonAdapter
{
    public SquadPlayerButtonAdapter(VisualElement element) : base(element) { }

    // The detailed label includes condition and body language on both surfaces: speech
    // reads GetAnnouncement, the HTML surface reads GetLabel (via DisplayText), and the
    // live-values channel re-reads GetLabel each tick so condition/morale refresh in place.
    public override string GetLabel() => base.GetLabel() + DetailSuffix();

    public override string GetAnnouncement() => base.GetAnnouncement() + DetailSuffix();

    // The player card is a custom HTML feature that deliberately shows its whole label
    // (name plus condition/morale detail), so it does not split into primary/secondary.
    protected override bool SplitsLabel => false;

    // The generic text collectors join every SIText descendant, which otherwise wedges the
    // shirt number and a bare goal/assist count mid-label. Suppress those nodes here so the
    // shirt number is dropped entirely and the goal/assist counts are read separately, with
    // labels, in DetailSuffix.
    private static readonly HashSet<string> ExcludedNames = new(StringComparer.Ordinal)
    {
        "fm-tactics-shirt-number", "goalSpacer", "assistSpacer",
    };

    protected override ISet<string> ExcludedTextNodeNames => ExcludedNames;

    // Composes ", {morale}, {condition} condition" from the live sprites. The body-language
    // word stands alone ("Calm", "Overwhelmed by the feedback"): a "body language" suffix
    // after the game's own descriptor is redundant per user.
    // Empty when the data frame or its icons are absent. base.GetAnnouncement builds from
    // SIText only (never the condition/morale sprites), so appending here adds the detail
    // exactly once; a real player card always has name text, so its empty-texts fallback to
    // GetLabel never fires.
    private string DetailSuffix()
    {
        var dataFrame = FindByName(Element, "Data frame");
        if (dataFrame == null)
            return "";

        var conditionLabel = GetConditionLabel(dataFrame);
        var moraleLabel = GetMoraleLabel(dataFrame);

        var suffix = "";
        if (moraleLabel != null)
            suffix += $", {moraleLabel}";
        if (conditionLabel != null)
            suffix += $", {conditionLabel} condition";

        return suffix + StatusSuffix();
    }

    // Goals, assists, cards and injury, from the row's binding node in a single read. The
    // rendered spacer text is the fallback for goals/assists only — it is all the DOM can tell
    // us — and an existing-but-unreadable row says so rather than implying a clean sheet.
    private string StatusSuffix()
    {
        var read = Server.MatchSquadReadout.TryReadRowStatus(Element, out var status);
        if (read != Server.MatchSquadReadout.RowStatusRead.Ok)
        {
            var fallback = SpacerCountSuffix();
            return read == Server.MatchSquadReadout.RowStatusRead.Unreadable
                ? fallback + $", match status {Speech.Degrade.Unavailable}"
                : fallback;
        }

        var suffix = "";
        if (status.Goals > 0)
            suffix += $", {status.Goals} goal{(status.Goals == 1 ? "" : "s")}";
        if (status.Assists > 0)
            suffix += $", {status.Assists} assist{(status.Assists == 1 ? "" : "s")}";
        if (status.Yellow)
            suffix += ", booked";
        if (status.Red)
            suffix += ", sent off";
        if (status.InjuryType != 0)
            suffix += $", injured, injury code {status.InjuryType}";

        NoteInjuryCode(status.InjuryType);
        return suffix;
    }

    // The injury field is a numeric enum whose non-zero meanings are not in the decompiled
    // source, so the code is spoken raw rather than guessed at. Log each distinct code once so a
    // live sighting can be mapped to the game's own wording later.
    private static readonly HashSet<long> SeenInjuryCodes = new();

    private static void NoteInjuryCode(long code)
    {
        if (code == 0 || !SeenInjuryCodes.Add(code)) return;
        Speech.Log.Info($"[SquadPlayerButton] Unmapped MatchInjuryType code {code} seen live");
    }

    private string SpacerCountSuffix()
    {
        var suffix = "";
        var goals = ReadSpacerCount(Element, "goalSpacer");
        if (goals != null)
            suffix += $", {goals} goal{(goals == "1" ? "" : "s")}";
        var assists = ReadSpacerCount(Element, "assistSpacer");
        if (assists != null)
            suffix += $", {assists} assist{(assists == "1" ? "" : "s")}";
        return suffix;
    }

    // Reads the goal/assist count from the named spacer. The count's SIText only exists when
    // the player has scored/assisted; the spacer's inner SIVisible is collapsed otherwise, so
    // a missing or invisible text node means none, and we omit that part of the suffix.
    private static string ReadSpacerCount(VisualElement element, string spacerName)
    {
        var spacer = FindByName(element, spacerName);
        if (spacer == null)
            return null;

        var count = FindVisibleText(spacer);
        return string.IsNullOrEmpty(count) ? null : count;
    }

    private static string FindVisibleText(VisualElement root)
    {
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var child = root.hierarchy[i];
            if (!ElementBridge.IsVisible(child))
                continue;

            var textEl = child.TryCast<UnityEngine.UIElements.TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                return textEl.text;

            var deeper = FindVisibleText(child);
            if (deeper != null)
                return deeper;
        }
        return null;
    }

    private string GetConditionLabel(VisualElement dataFrame)
    {
        var conditionIcon = FindByName(dataFrame, "Conditionicon");
        if (conditionIcon == null)
            return null;

        // Prefer the live percentage from the row's binding node (per user): the sprite bands
        // are coarse — 91–100 all render the same "Excellent" heart — while the number moves
        // continuously. The sprite word is the fallback when the binding is unreadable: the
        // game's own truth, just coarser.
        if (Server.MatchSquadReadout.TryReadRowCondition(Element, out var percent))
            return Math.Round(percent).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

        var spriteName = SpriteHelper.ReadSpriteName(conditionIcon);
        if (spriteName == null)
            return null;

        var label = SpriteHelper.TryConditionLabel(spriteName);
        if (label != null)
            return label.Replace(" condition", "");

        Speech.Log.Warn($"[SquadPlayerButton] Condition sprite has no level: \"{spriteName}\"");
        return null;
    }

    private string GetMoraleLabel(VisualElement dataFrame)
    {
        var moraleImage = FindMoraleImage(dataFrame);
        if (moraleImage == null)
            return null;

        // Prefer the game's own detailed word ("Calm", "Composed", "Complacent", ...) from the
        // row's BodyLanguage binding (per user): the icon collapses it to positive/neutral/
        // negative, which is what the sprite mapping below yields — kept as the fallback when
        // the binding is unreadable (truthful, just vaguer).
        if (Server.MatchSquadReadout.TryReadRowBodyLanguage(Element, out var detailed))
            return detailed;

        string spriteName;
        try
        {
            var bg = moraleImage.resolvedStyle.backgroundImage;
            spriteName = bg.sprite?.name ?? bg.vectorImage?.name ?? bg.texture?.name;
        }
        catch
        {
            return null;
        }

        if (spriteName == null)
            return null;

        var label = SpriteHelper.TryMoraleLabel(spriteName);
        if (label != null)
            return label.Replace(" morale", "");

        Speech.Log.Warn($"[SquadPlayerButton] Unknown morale sprite: \"{spriteName}\"");
        return null;
    }

    private VisualElement FindChildDropdownElement()
        => FindChildDropdownElement(Element);

    private static VisualElement FindChildDropdownElement(VisualElement element)
    {
        for (int i = 0; i < element.hierarchy.childCount; i++)
        {
            var child = element.hierarchy[i];
            if (child.GetIl2CppType().Name == "SIDropdown")
                return child;
            // FM27's compact row wraps its own dropdown in one template.
            // Require the actual open-descendant command and its optional
            // native target, so unrelated nested card controls stay separate.
            if (child.GetIl2CppType().Name != "TemplateContainer" || child.hierarchy.childCount != 1
                || child.hierarchy[0].GetIl2CppType().Name != "SIDropdown") continue;
            var dropdown = child.hierarchy[0];
            var events = element.TryCast<SI.Bindable.SIButton>()?.Events;
            if (events == null) continue;
            for (int e = 0; e < events.Count; e++)
            {
                var open = events[e]?.TryCast<FM.UI.OpenDescendantDropdownEvent.OpenDescendantDropdownEventContext>();
                if (open != null && (string.IsNullOrEmpty(open.m_optionalDropdownName)
                    || open.m_optionalDropdownName == dropdown.name)) return dropdown;
            }
        }
        return null;
    }

    private static VisualElement FindMoraleImage(VisualElement dataFrame)
    {
        for (int i = 0; i < dataFrame.hierarchy.childCount; i++)
        {
            var child = dataFrame.hierarchy[i];
            if (child.GetIl2CppType().Name == "SIVisible")
            {
                var img = FindByName(child, "image");
                if (img != null) return img;
            }
        }
        return null;
    }

    public DropdownAdapter GetChildDropdownAdapter()
    {
        var child = FindChildDropdownElement();
        if (child != null)
            return new DropdownAdapter(child);
        Speech.Log.Error("[SquadPlayerButton] No child SIDropdown found on element that was identified as squad player button.");
        throw new System.InvalidOperationException("SquadPlayerButton missing child SIDropdown");
    }

    public static bool IsSquadPlayerButton(VisualElement element)
        => FindChildDropdownElement(element) != null;
}
