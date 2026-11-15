using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class MessageButtonAdapter : ButtonAdapter
{
    private static readonly PropertyInfo SIVisibleValueProperty;

    static MessageButtonAdapter()
    {
        var siVisibleType = typeof(SI.Bindable.SIVisible);
        SIVisibleValueProperty = siVisibleType.GetProperty("m_value",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (SIVisibleValueProperty == null)
        {
            Speech.Log.Error("SIVisible.m_value property not found.");
            throw new System.MissingMemberException("SIVisible", "m_value");
        }
    }

    public MessageButtonAdapter(VisualElement element) : base(element) { }

    internal int? MessageId => Server.NativeMessageIdentity.Read(Element);

    // The same native row template also carries read-only processing summaries:
    // enabled geometry alone is not a command. Keep disabled real commands as
    // controls, but do not advertise an inbox action on a row with no native
    // submit operation, serialized events or custom click callback.
    internal static bool HasNativeAction(SI.Bindable.SIButton button)
        => button is null || button.CanSubmit || button.Events?.Count > 0
            || button.m_clickableAction is not null;

    public override bool CanActivate => HasNativeAction(Element.TryCast<SI.Bindable.SIButton>());
    public override string Role => CanActivate ? "button" : "text";

    public override void Activate()
    {
        if (!CanActivate)
            throw new System.InvalidOperationException("This message preview is read-only.");
        base.Activate();
    }

    // The message label (subject, sender, time) is a structured whole; it renders in full
    // rather than as a primary/secondary split. A field-based split is a separate change.
    protected override bool SplitsLabel => false;

    // The curated content shared by both surfaces so they can never drift: status flags lead
    // (so an unread, priority, or response-needed message is skimmable the moment the cursor
    // lands on it), followed by subject, sender, time. Read live each call from the live
    // children — no caching — and each part is included only when present.
    private List<string> BuildContent()
    {
        var parts = new List<string>();

        var status = GetStatusText();
        if (status.Length > 0)
            parts.Add(status);

        var subject = GetSubject() ?? GetPreview();
        if (subject != null)
            parts.Add(subject);

        var sender = GetSender();
        if (sender != null)
            parts.Add(sender);

        var time = GetNamedText("TimeSent");
        if (time != null)
            parts.Add(time);

        return parts;
    }

    // The HTML label. SplitsLabel is false, so DisplayText.Of routes a button's primary label
    // through GetLabel — this is what the companion shows. The role ("button") and disabled
    // state are deliberately omitted: the HTML <button> element conveys both to the screen
    // reader. Falls back to the base label only if a news item somehow carries no content.
    public override string GetLabel()
    {
        var parts = BuildContent();
        return parts.Count > 0 ? string.Join(", ", parts) : base.GetLabel();
    }

    // The speech announcement: the same curated content, then role and (if greyed) disabled,
    // matching every other button's state-last ordering.
    public override string GetAnnouncement()
    {
        var parts = BuildContent();
        if (parts.Count == 0)
            parts.Add(base.GetLabel());

        if (CanActivate)
        {
            parts.Add(Role);
            if (!Element.enabledInHierarchy)
                parts.Add("disabled");
        }

        return string.Join(", ", parts);
    }

    // FM27 also reuses NewsItemButton in the processing summary. That native
    // row binds its actual subject to Headline and its sender to MessageSender,
    // rather than the full/compact inbox names. Resolve the row's own native
    // schema, on both surfaces; do not invent a preview or retry absent fields.
    public string GetSubject() => GetNamedText("MessageHeadline") ?? GetNamedText("Headline");

    // Below a width threshold (the 1366x768 field machines) the game swaps the inbox row to a
    // COMPACT template with no MessageHeadline or PersonSenderName at all: the sender leaf is
    // named "PersonName" and the row shows the message body's first line ("NewsItemText")
    // instead of a subject. Reading only the full-template names made every row on a small
    // screen permanently "couldn't read" — the unreadable-subject field bug. The preview is the
    // game's own row text at that size, so surfaces fall back to it (labelled as a preview,
    // never presented as the subject).
    public string GetPreview() => GetNamedText("NewsItemText");

    // The same parts BuildContent composes, exposed individually so the Messages table can
    // place each in its own column. Raw game text (like every read here): callers on the HTML
    // surface clean via DisplayText.Clean before baking into a cell.
    // PersonName is the compact template's sender leaf (see GetPreview).
    public string GetSender() => GetNamedText("PersonSenderName") ?? GetNamedText("PersonName")
        ?? GetNamedText("MessageSender");

    // The sender-name link element itself, so a surface can carry the SENDER's own context menu on
    // the sender cell — the person actions (Contract, Report, ...) the game arms on this leaf. The
    // subject cell used to inherit it, which read as the subject line opening the sender's menu; the
    // scope rule drops that, and the honest home for the menu is the cell that names the sender.
    // Null for a message with no sender leaf (a pure news round-up). A news-OUTLET sender carries no
    // actions, so the caller's own menu check leaves it plain text.
    public VisualElement GetSenderElement()
        => FindByName(Element, "PersonSenderName") ?? FindByName(Element, "PersonName")
            ?? FindByName(Element, "MessageSender");

    public string GetTime() => GetNamedText("TimeSent");

    public string GetStatusText()
    {
        var parts = new List<string>();
        if (IsUnread()) parts.Add("unread");
        if (IsSIVisibleShown(FindByName(Element, "Priority"))) parts.Add("priority");
        if (IsSIVisibleShown(FindByName(Element, "SIVisible_CanRespond"))) parts.Add("response needed");
        // FM27's visible category chips are part of the inbox row. Preserve
        // the original four-column table and carry their actual translated
        // text with the other status metadata, on both HTML and speech.
        var tags = FindByName(Element, "Tags");
        if (tags != null) CollectVisibleTags(tags, parts);
        return string.Join(", ", parts);
    }

    private static void CollectVisibleTags(VisualElement element, List<string> texts)
    {
        if (!ElementBridge.IsVisible(element)) return;
        var text = element.TryCast<TextElement>()?.text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!texts.Contains(text)) texts.Add(text);
            return;
        }
        for (int i = 0; i < element.hierarchy.childCount; i++)
            CollectVisibleTags(element.hierarchy[i], texts);
    }

    internal bool IsUnread()
    {
        var unread = FindByName(Element, "IsUnRead");
        // FM27's dot beside TimeSent is an unnamed SIVisible. Keep the original
        // status contract, reading that exact native template slot when renamed.
        if (unread == null)
        {
            var timeRow = FindByName(Element, "TimeSent")?.hierarchy.parent?.hierarchy.parent;
            if (timeRow != null)
                for (int i = 0; i < timeRow.hierarchy.childCount; i++)
                {
                    var candidate = timeRow.hierarchy[i];
                    if (candidate.GetIl2CppType().Name == "SIVisible" && FindByName(candidate, "icon") != null)
                    { unread = candidate; break; }
                }
        }
        return IsSIVisibleShown(unread);
    }

    public override string GetState() => "";

    private string GetNamedText(string name)
    {
        var el = FindByName(Element, name);
        if (el == null) return null;
        return ReadTextIn(el, 4);
    }

    // The named element's own text, or the first non-empty TextElement text just below it. The
    // row template puts the name on the SIText itself today, but a template revision that wraps
    // the text in a styling container must read as the text, not as "no subject".
    private static string ReadTextIn(VisualElement el, int depth)
    {
        var textEl = el.TryCast<TextElement>();
        if (textEl != null)
        {
            var text = textEl.text;
            return string.IsNullOrEmpty(text) ? null : text;
        }
        if (depth <= 0) return null;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = ReadTextIn(el.hierarchy[i], depth - 1);
            if (found != null) return found;
        }
        return null;
    }

    private static bool IsSIVisibleShown(VisualElement element)
    {
        if (element == null) return false;
        var siVisible = element.TryCast<SI.Bindable.SIVisible>();
        if (siVisible == null) return false;
        return (bool)SIVisibleValueProperty.GetValue(siVisible);
    }
}

// The subject cell's live-value source. A row whose subject had not bound at compose time renders
// the unreadable marker, and text arrival changes no structural shape, so no re-render fires; the
// bounded marker retry covers only the first few seconds. Tracking the cell here lets the value
// channel push the real subject into the already-rendered button whenever the bind lands, however
// late. Reads ONLY the subject (or the marker, never an empty string — a blank button is a silent
// gap): the cell is subject-only, so the composed status/sender/time label and the tooltip
// description must both stay out of the pushed text.
internal sealed class MessageSubjectValue : MessageButtonAdapter
{
    public MessageSubjectValue(VisualElement element) : base(element) { }

    public override string GetLabel() => GetSubject() ?? GetPreview() ?? Speech.Degrade.Unreadable;

    public override string GetDescription() => "";
}
