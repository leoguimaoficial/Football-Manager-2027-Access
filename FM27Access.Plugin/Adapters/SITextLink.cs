using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// Activates an SIText embedded hyperlink (rich-text <c>&lt;link="id"&gt;</c>). The clickable thing
/// inside the element is the link, not the element itself; its navigation runs off the text engine's
/// PointerDown/UpLinkTagEvents, which the engine only synthesises from a real pointer that hit-tests
/// onto the link glyphs — something injected pointer events never satisfy. So build those link-tag
/// events directly, carrying the id parsed from the markup, the same pair SIText.HandleEventBubbleUp
/// turns into the link's action. The id comes from the data (the markup), not from a screen position,
/// so this stays independent of visibility/occlusion. Shared by the table player-name links
/// (<see cref="TableRowTextAdapter"/>) and the calendar event-result links
/// (<see cref="CalendarEventAdapter"/>).
/// </summary>
internal static class SITextLink
{
    private static readonly System.Text.RegularExpressions.Regex Links = new(
        "<link=\"(?<id>[^\"]+)\">(?<text>.*?)</link>",
        System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // A rich-text cell can carry several independent embedded records. Preserve
    // its surrounding words and each actual link instead of making the whole
    // sentence activate its first record. Uses the existing joined-text/button
    // renderer; ordinary table-name links keep their original adapter.
    internal static System.Collections.Generic.List<FM27Access.Navigation.IAccessibleElement> ReadParts(VisualElement element)
    {
        var native = element.TryCast<SI.Bindable.SIText>();
        if (native == null) return null;
        string source = native.text;
        if (string.IsNullOrEmpty(source)) return null;
        var matches = Links.Matches(source);
        if (matches.Count == 0) return null;
        var parts = new System.Collections.Generic.List<FM27Access.Navigation.IAccessibleElement>();
        int end = 0, ordinal = 0;
        void Text(string raw)
        {
            var caption = Speech.TextFilters.CleanForDisplay(raw);
            if (!string.IsNullOrWhiteSpace(caption))
                parts.Add(new Part(element, source, caption, null, ordinal++, System.IntPtr.Zero));
        }
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (!int.TryParse(match.Groups["id"].Value, out int index) || index < 0
                || native.EmbeddedData == null || index >= native.EmbeddedData.Count || !native.IsValidLink(index))
                return null; // still binding: retain the complete original text
            Text(source.Substring(end, match.Index - end));
            string caption = Speech.TextFilters.CleanForDisplay(match.Groups["text"].Value);
            if (string.IsNullOrWhiteSpace(caption)) return null;
            if (native.LinkHasActions(index))
            {
                var embedded = native.EmbeddedData[index];
                var reference = Server.BindingRecords.Field(Server.BindingRecords.Fields(embedded), "UIRecord")
                    ?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
                string identity = reference != null
                    ? "entity:" + reference.CombinedIndexAndType.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "record:" + embedded.Pointer.ToInt64().ToString("x");
                parts.Add(new Part(element, source, caption, match.Groups["id"].Value,
                    ordinal++, embedded.Pointer, identity));
            }
            else Text(match.Groups["text"].Value);
            end = match.Index + match.Length;
        }
        Text(source.Substring(end));
        var captions=new System.Collections.Generic.List<string>();
        foreach(var part in parts)captions.Add(part.GetLabel());
        if(!Server.InlineTextSpacing.TryRead(Speech.TextFilters.CleanForDisplay(source),captions,out var separators))
        {
            Speech.Log.Warn($"[SITextLink] Native inline text '{element.name}' no longer matches its fragments; keeping complete source.");
            return null;
        }
        for(int i=0;i<parts.Count;i++)((Part)parts[i]).SeparatorBefore=separators[i];
        return parts;
    }

    internal sealed class Part : PluginAdapterBase, Server.IStableKeyElement
    {
        internal string SeparatorBefore { get; set; }
        private readonly string source, caption, linkId;
        private readonly int ordinal;
        private readonly System.IntPtr record;
        private readonly string targetIdentity;
        private readonly bool wasActivatable;
        private readonly bool embeddedEnabled;
        internal Part(VisualElement element, string source, string caption, string linkId, int ordinal, System.IntPtr record, string targetIdentity = null) : base(element)
        { this.source = source; this.caption = caption; this.linkId = linkId; this.ordinal = ordinal; this.record = record; this.targetIdentity = targetIdentity; embeddedEnabled = element.Cast<SI.Bindable.SIText>().EnableEmbeddedLinks; wasActivatable = CanActivate; }
        public string StableKey => Server.ServerHandles.IdentityPath(Element) + "#part:" + ordinal + ":" + (linkId ?? "text") + (targetIdentity == null ? "" : ":" + targetIdentity);
        // A whole native text widget can explicitly disable embedded links
        // (official pre-game lineup previews). Its styled names are then text,
        // while individually disabled links in an interactive widget keep state.
        public override string Role => linkId == null || !embeddedEnabled ? "text" : "button";
        public override string GetLabel() => caption;
        public override string GetAnnouncement() => caption + (Role == "button" ? ", button" : "");
        public override bool CanActivate
        {
            get
            {
                if (linkId == null || !int.TryParse(linkId, out int index)) return false;
                var native = Element.Cast<SI.Bindable.SIText>();
                return native.text == source && native.EnableEmbeddedLinks
                    && native.EmbeddedData != null && index >= 0 && index < native.EmbeddedData.Count && native.IsValidLink(index)
                    && native.EmbeddedData[index].Pointer == record
                    && native.IsLinkEnabled(index) && native.LinkHasActions(index);
            }
        }
        public override string GetState() => Role == "button" && !CanActivate ? "disabled" : "";
        internal bool IsCurrent
        {
            get
            {
                try
                {
                    var native = Element.Cast<SI.Bindable.SIText>();
                    if (Element.panel == null || native.text != source) return false;
                    if (linkId == null) return true;
                    int index = int.Parse(linkId);
                    return native.EnableEmbeddedLinks == embeddedEnabled
                        && native.EmbeddedData != null && index < native.EmbeddedData.Count
                        && native.EmbeddedData[index].Pointer == record && CanActivate == wasActivatable;
                }
                catch (System.Exception ex)
                {
                    Speech.Log.Debug("[SITextLink] Captured text link left its source: " + ex.GetType().Name);
                    return false;
                }
            }
        }
        internal int? LinkIndex => int.TryParse(linkId, out int index) ? index : null;
        public override bool HasContextMenu => CanActivate && !Element.Cast<SI.Bindable.SIText>().DisableContextMenu
            && ContextMenuOpener.Resolve(Element).Target?.Pointer == Element.Pointer;
        public override void Activate()
        {
            if (!CanActivate) throw new System.InvalidOperationException("That text link changed. Refresh the screen.");
            ActivateLink(Element, linkId, caption);
        }
    }

    // While embedded records bind, retain the original complete text and its
    // live-value behavior. The existing stale-source recompose promotes it to
    // independently guarded parts when those same native records are ready.
    internal sealed class PendingText : TextAdapter
    {
        internal PendingText(VisualElement element) : base(element) { }
        internal bool IsReady
        {
            get
            {
                try { return Element.panel == null || ReadParts(Element) != null; }
                catch (System.Exception ex)
                {
                    Speech.Log.Debug("[SITextLink] Pending text source changed: " + ex.GetType().Name);
                    return true;
                }
            }
        }
    }

    public static bool HasLink(string text) => text != null && text.Contains("<link=");

    // The link id inside the rich-text markup (e.g. <link="0">), or null when the text carries no
    // link markup at all. Null is the only honest answer there: the id indexes the element's own
    // embedded-data records, and an SIText that has not (yet) been given link markup either has no
    // link or is a recycled table row still mid-rebind — its records still hold the PREVIOUS
    // occupant. Guessing "0" fired that stale record, which is how a name cell announced the right
    // person and opened someone else's screen. The element is one link, so the first id is the one
    // to fire.
    public static string TryExtractLinkId(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        const string marker = "link=\"";
        int i = text.IndexOf(marker, System.StringComparison.Ordinal);
        if (i < 0) return null;
        i += marker.Length;
        int j = text.IndexOf('"', i);
        return j > i ? text.Substring(i, j - i) : null;
    }

    /// <summary>
    /// Fire the link inside <paramref name="siText"/> (an SIText / TextElement). False when the
    /// element carries no link markup to fire — the caller degrades rather than firing a guess.
    /// </summary>
    public static bool Activate(VisualElement siText)
    {
        var text = siText.Cast<TextElement>().text;
        string linkId = TryExtractLinkId(text);
        if (linkId == null) return false;
        string linkText = Speech.TextFilters.ExtractLinkText(text);

        return ActivateLink(siText, linkId, linkText);
    }

    private static bool ActivateLink(VisualElement siText, string linkId, string linkText)
    {

        var rect = siText.worldBound;
        var worldCenter = new Vector3(rect.x + rect.width / 2f, rect.y + rect.height / 2f, 0f);
        var content = siText.contentRect;
        var localCenter = new Vector3(content.width / 2f, content.height / 2f, 0f);

        Speech.Log.Info($"[SITextLink] Activating link id=\"{linkId}\" on \"{siText.name}\"");

        var down = PointerDownEvent.GetPooled();
        down.position = worldCenter; down.localPosition = localCenter; down.button = 0;
        var linkDown = PointerDownLinkTagEvent.GetPooled(down.Cast<IPointerEvent>(), linkId, linkText);
        // Unity 6.3 dispatches link-tag events to an assigned target. SendEvent
        // no longer fills it implicitly; without this, every ordinary text
        // link throws "Event target not set" before the game's handler runs.
        linkDown.target = siText.Cast<IEventHandler>();
        try { siText.SendEvent(linkDown); }
        finally { linkDown.Dispose(); down.Dispose(); }

        var up = PointerUpEvent.GetPooled();
        up.position = worldCenter; up.localPosition = localCenter; up.button = 0;
        var linkUp = PointerUpLinkTagEvent.GetPooled(up.Cast<IPointerEvent>(), linkId, linkText);
        linkUp.target = siText.Cast<IEventHandler>();
        try { siText.SendEvent(linkUp); }
        finally { linkUp.Dispose(); up.Dispose(); }
        return true;
    }
}
