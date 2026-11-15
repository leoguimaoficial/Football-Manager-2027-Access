using System.Text.RegularExpressions;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal static class SpriteHelper
{
    internal static bool IsCaptionedRoleArtwork(VisualElement image)
    {
        if (image.GetIl2CppType().Name != "SIImage" || image.name != "RoleIcon"
            || !string.IsNullOrWhiteSpace(image.tooltip)
            || image.parent is not { name: "Content" } content
            || content.parent?.GetIl2CppType().Name != "BindingRoot"
            || content.hierarchy.childCount != 3) return false;
        var icon = content.hierarchy[0]; var text = content.hierarchy[1]; var help = content.hierarchy[2];
        return icon.Pointer == image.Pointer && text.name == "RoleText"
            && text.GetIl2CppType().Name == "SIText" && ElementBridge.IsVisibleInHierarchy(text)
            && !string.IsNullOrWhiteSpace(text.TryCast<TextElement>()?.text)
            && !SITextLink.HasLink(text.TryCast<TextElement>()?.text)
            && help.name == "SIVisible_HasDescription" && help.GetIl2CppType().Name == "SIVisible";
    }

    internal static bool IsCaptionedGenericCompetitionBadge(VisualElement element)
    {
        if (element?.GetIl2CppType().Name != "SIImage"
            || !element.name.StartsWith("badges-generic-competition-", StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(element.tooltip) || element.parent == null) return false;
        if (new ImageAdapter(element).HasContextMenu) return false;
        int captions = 0;
        for (int i = 0; i < element.parent.hierarchy.childCount; i++)
        {
            var sibling = element.parent.hierarchy[i];
            if (sibling.TryCast<SI.Bindable.SIText>() is { } text
                && ElementBridge.IsVisibleInHierarchy(text) && !string.IsNullOrWhiteSpace(text.text)) captions++;
        }
        return captions == 1;
    }

    private static readonly Regex LevelPattern = new(@"level=(\d+)", RegexOptions.Compiled);
    private static readonly Regex SharpnessPattern = new(@"sharpness=(\d+)", RegexOptions.Compiled);
    private static readonly Regex MoralePattern = new(@"morale=(\d+)", RegexOptions.Compiled);
    private static readonly Regex FatiguePattern = new(@"^fatigue-([a-z-]+?)_(?:small|medium|large)", RegexOptions.Compiled);

    internal static string ReadSpriteName(VisualElement element)
    {
        try
        {
            var bg = element.resolvedStyle.backgroundImage;
            var name = bg.sprite?.name ?? bg.vectorImage?.name ?? bg.texture?.name;
            if (!string.IsNullOrEmpty(name)) return name;
            // FM27 also draws SIImage glyphs through named styles. These have
            // ImageKind=None and no resolved background asset, despite carrying
            // meaningful information (for example the squad support change).
            return element.TryCast<SI.Bindable.SIImage>()?.m_imageStyleName;
        }
        catch
        {
            return null;
        }
    }

    internal static string GetSpriteLabel(VisualElement element)
    {
        var spriteName = ReadSpriteName(element);
        if (IsRankingArrow(element))
            return ImageStyleLabel.RankingArrow(spriteName)
                ?? Degrade.Read($"unmapped standings direction glyph \"{spriteName}\"");
        if (spriteName == null)
            return IsMatchEventType(element) || IsFixturePlayerEvent(element)
                ? Degrade.Read("match event glyph has no readable native asset") : null;

        // FM27's compact weekly training tile draws Rest with an icon alone.
        // This observed asset family carries the same meaning at 2x/3x sizes.
        if (spriteName.StartsWith("training rest_", StringComparison.OrdinalIgnoreCase)) return "Rest";
        // Informative contract-type pictogram in the shared person report tile.
        if (element.name is "ContractIcon" or "ContractImage"
            && spriteName.StartsWith("contract-full-time_", StringComparison.OrdinalIgnoreCase))
            return "Full-time contract";

        // The shared fixture tile's result is an informative native sprite,
        // not its UXML identifier. Reuse the existing asset-word formatter,
        // scoped to the result slot and its win/draw/defeat families.
        if (element.name == "FixtureResultIcon"
            && (spriteName.StartsWith("win_", StringComparison.OrdinalIgnoreCase)
                || spriteName.StartsWith("draw_", StringComparison.OrdinalIgnoreCase)
                || spriteName.StartsWith("defeat_", StringComparison.OrdinalIgnoreCase)))
            return Server.MatchIncidentLabel.FromSprite(spriteName);

        return ImageStyleLabel.For(spriteName)
            ?? TryConditionLabel(spriteName) ?? TrySharpnessLabel(spriteName)
            ?? TryMoraleLabel(spriteName) ?? TryFatigueLabel(spriteName)
            ?? TryHierarchyLabel(spriteName)
            ?? TryIncidentLabel(element, spriteName);
    }

    internal static bool IsCaptionedPreferredFootArtwork(VisualElement image)
    {
        // This binary pair is the preferred-foot drawing, not the numerical
        // foot-ability fields. Keep its own official native caption once.
        if (image?.GetIl2CppType().Name != "SIImage" || image.name is not ("LeftFoot" or "RightFoot")
            || image.parent is not { name: "Frame" } frame || frame.hierarchy.childCount != 2
            || frame.parent is not { } content || content.hierarchy.childCount != 2) return false;
        var left = frame.hierarchy[0]; var right = frame.hierarchy[1];
        if (left.name != "LeftFoot" || right.name != "RightFoot"
            || left.GetIl2CppType().Name != "SIImage" || right.GetIl2CppType().Name != "SIImage"
            || !ElementBridge.IsVisibleInHierarchy(left) || !ElementBridge.IsVisibleInHierarchy(right)
            || !string.IsNullOrWhiteSpace(left.tooltip) || !string.IsNullOrWhiteSpace(right.tooltip)) return false;
        var l = ReadSpriteName(left); var r = ReadSpriteName(right);
        bool Style(string style, string strength) => style != null
            && Regex.IsMatch(style, @"^size=xxl \(128px\), strength=" + strength + @"_\d+x$");
        if (!(Style(l, "weak") && Style(r, "strong") || Style(l, "strong") && Style(r, "weak"))) return false;
        var caption = content.hierarchy[1];
        return content.hierarchy[0].Pointer == frame.Pointer && caption.TryCast<SI.Bindable.SIText>() is { } text
            && ElementBridge.IsVisibleInHierarchy(caption) && !string.IsNullOrWhiteSpace(text.text)
            && !SITextLink.HasLink(text.text);
    }

    internal static bool IsRankingArrow(VisualElement element)
        => element?.GetIl2CppType().Name == "SIStyleSetter"
            && element.parent?.GetIl2CppType().Name == "BindingExpect"
            && element.parent.parent?.name == "ArrowStateIcon";

    internal static string TryHierarchyLabel(string spriteName)
    {
        if (spriteName == null) return null;
        if (spriteName.StartsWith("team leader_", StringComparison.OrdinalIgnoreCase)) return "Team leader";
        if (spriteName.StartsWith("highly influential_", StringComparison.OrdinalIgnoreCase)) return "Highly influential player";
        if (spriteName.StartsWith("influential_", StringComparison.OrdinalIgnoreCase)) return "Influential player";
        if (spriteName.StartsWith("other_", StringComparison.OrdinalIgnoreCase)) return "Other players";
        return null;
    }

    // A match incident icon (the Data Hub player statistics' Time column: "sub on", "sub off"
    // beside the minute). Scoped by element name, because MatchIncidentLabel humanises any readable
    // sprite it does not know, which would turn an unrelated icon into a plausible-sounding event.
    private const string IncidentImageName = "IncidentImage";

    private static string TryIncidentLabel(VisualElement element, string spriteName)
    {
        if (IsMatchEventType(element) || IsFixturePlayerEvent(element))
            return Server.MatchIncidentLabel.FromSprite(spriteName)
                ?? Degrade.Read($"unreadable match event glyph \"{spriteName}\"");
        return element.name is IncidentImageName or "IncidentIcon"
            ? Server.MatchIncidentLabel.FromSprite(spriteName) : null;
    }

    internal static bool IsMatchEventType(VisualElement element)
    {
        if (element.name != "EventType" || element.GetIl2CppType().Name != "SIImage") return false;
        // FM27's selected-fixture summary uses a separate icon-only incident
        // slot. Its own nearest row binds the real event/player/time record.
        // Do not humanise unrelated images merely because their name matches.
        var record = Server.BindingRecords.RecordAbove(element, "Event", 6, 0);
        if (record == null) return false;
        var fields = Server.BindingRecords.Fields(record);
        return fields.ContainsKey("EventType") && fields.ContainsKey("Player") && fields.ContainsKey("TimeString");
    }

    // The rating row draws its numeric Event values with unnamed SIImages.
    // Their own event binding and containing native list identify meaning;
    // unnamed portraits/decorative images must still be ignored.
    internal static bool IsFixturePlayerEvent(VisualElement element)
    {
        if(element?.GetIl2CppType().Name!="SIImage")return false;
        SI.Bindable.IReadOnlyNode node=null;
        var ancestor=element.parent;
        for(int depth=0;ancestor!=null&&depth<6;depth++,ancestor=ancestor.parent)
            if((node=Server.BindingRecords.NodeOf(ancestor))!=null)break;
        if(node==null)return false;
        var value=Server.BindingRecords.FindNode(node,"Event",0);
        if(value==null||Server.BindingRecords.NumberOf(Server.BindingRecords.ValueOf(value))==null)return false;
        bool events=false;
        for(int depth=0;ancestor!=null&&depth<20;depth++,ancestor=ancestor.parent)
        {
            if(ancestor.GetIl2CppType().Name=="StreamedObjectList")
            {
                if(!events&&ancestor.name!="PlayerEvents")return false;
                events=true;
            }
            if(ancestor.name=="PlayerRatings")return events;
        }
        return false;
    }

    internal static string TryConditionLabel(string spriteName)
    {
        // The same condition slot uses words for injury severity in FM27,
        // rather than a numeric fitness level. Never expose the template id.
        if (spriteName.StartsWith("level=injured,", StringComparison.OrdinalIgnoreCase)) return "Injured";
        if (spriteName.StartsWith("level=slightly injured,", StringComparison.OrdinalIgnoreCase)) return "Slightly injured";
        var match = LevelPattern.Match(spriteName);
        if (!match.Success)
            return null;

        var level = int.Parse(match.Groups[1].Value);
        var label = level switch
        {
            1 => "Terrible",
            2 => "Poor",
            3 => "Fair",
            4 => "Good",
            5 => "Excellent",
            var n => $"level {n}"
        };
        return $"{label} condition";
    }

    internal static string TrySharpnessLabel(string spriteName)
    {
        var match = SharpnessPattern.Match(spriteName);
        if (!match.Success)
            return null;

        return $"{match.Groups[1].Value} of 7";
    }

    internal static string TryMoraleLabel(string spriteName)
    {
        if (spriteName.Contains("happiness=neutral") || spriteName.Contains("happiness=3"))
            return "Neutral morale";
        if (spriteName.Contains("happiness=1"))
            return "Very Negative morale";
        if (spriteName.Contains("happiness=2"))
            return "Negative morale";
        if (spriteName.Contains("happiness=4"))
            return "Positive morale";
        if (spriteName.Contains("happiness=5"))
            return "Very Positive morale";

        var match = MoralePattern.Match(spriteName);
        if (!match.Success)
            return null;

        return $"{match.Groups[1].Value} of 20";
    }

    // The Fatigue column's cell holds nothing but an SIImage, and its sprite names break the
    // "key=value" shape the other status sprites use — they are hyphenated slugs of the level
    // itself ("fatigue-becoming-fatigued_small_2x"), so none of the patterns above matched and
    // the whole column read as blank. The slug between the "fatigue-" prefix and the "_size_2x"
    // suffix IS the game's own vocabulary for the level; "exhuasted" is the game's spelling of
    // the asset, not a typo here. Phrasing carries the word "fatigue" only where the level word
    // is ambiguous alone, so each reads correctly both under the column header and standalone
    // on the speech cursor.
    internal static string TryFatigueLabel(string spriteName)
    {
        var match = FatiguePattern.Match(spriteName);
        if (!match.Success)
            return null;

        return match.Groups[1].Value switch
        {
            "fresh" => "Fresh",
            "low" => "Low fatigue",
            "becoming-fatigued" => "Becoming fatigued",
            "fatigued" => "Fatigued",
            "exhuasted" => "Exhausted",
            var slug => Degrade.Read($"unmapped fatigue sprite \"{spriteName}\" (level slug \"{slug}\")"),
        };
    }
}
