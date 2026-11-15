using System;
using System.Collections.Generic;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The companion-surface bridge for generic <c>SIFloatingPopupWidget</c> popups — the game's
/// trigger-plus-floating-content widget used for table filter menus (Job Market's "Filter" and
/// its siblings). FM27's native <c>BookmarksPopupWidget</c> also uses this surface,
/// with typed native bookmark rows; the companion Ctrl editor remains separate.
/// ScreenWalker emits a trigger stop in the widget's place (the classifier
/// treats the type as infrastructure, so it never becomes a stop on its own); activating the
/// trigger toggles the game's own popup, and while it is open ComposeBody renders only the
/// popup section, modal (context-menu / bookmark-editor precedent). HTML-only — the floating
/// content attaches under PanelManager-container, outside every panel, where the speech
/// cursor cannot follow.
///
/// The popup content renders structurally, no template names required: SICheckBox rows become
/// native checkbox stops (CheckBoxAdapter reads the sibling label and live checked state, and
/// its activation runs the game's own toggle flow — probed live 2026-07-07 on the Job Market
/// filter, where unchecking a nation also uncheck's the game's own "All Nations" aggregate);
/// an SIText whose parent also holds a streamed list is that list's section heading; an SIText
/// consumed as a checkbox's label is folded into the stop; any other text renders as-is so an
/// unknown popup's content degrades to readable text, never to nothing.
///
/// The game holds all session truth: "open" is the widget's floating element's IsVisible, read
/// live on every call. The only cache is the live widget reference last toggled through a
/// trigger (the allowed kind), so signature polls do not re-walk every panel.
///
/// Main thread only (reads live VisualElements), like all panel-tree access.
/// </summary>
internal static class FilterPopupSurface
{
    // The context-menu Close precedent: the modal's consistent first stop. The game applies
    // each checkbox toggle immediately (verified live), so closing loses nothing.
    private const string CloseLabel = "Close";
    private const string FallbackTitle = "Popup";

    // Held live Unity reference (the allowed kind of cache): the widget the player last
    // toggled through a trigger stop. The popup can only be open on the HTML surface through
    // that trigger, so this is the one widget whose open state matters. A destroyed widget
    // (panel rebuilt underneath the popup) reads as closed via the guarded IsOpen.
    private static SI.Bindable.SIFloatingPopupWidget _widget;
    private static int _session;
    private static (SI.Bindable.SIFloatingPopupWidget Widget, int Session, int Frame)? _pendingClose;

    internal static bool IsOpen()
    {
        var widget = _widget;
        if (widget == null) return false;
        try
        {
            return widget.m_floatingElement is { } fe && fe.IsVisible;
        }
        catch (Exception e)
        {
            // A stale reference after the owning panel was torn down: not an open popup.
            Degrade.Note($"filter popup open-check failed, dropping widget ref: {e.GetType().Name}");
            _widget = null;
            return false;
        }
    }

    /// <summary>
    /// Toggle a widget's popup through its own click entry (the trigger stop's action) and tell
    /// the player what happened through Prism — the modal section replaces the page on the next
    /// refresh, and without a cue an NVDA user has no way to know the press worked. The announced
    /// word is the state read back AFTER the call, never a prediction; the only forward-looking
    /// word is "opening", for a first-ever open whose popup template is still loading.
    /// </summary>
    internal static void Toggle(SI.Bindable.SIFloatingPopupWidget widget, string label)
    {
        _session++;
        _pendingClose = null;
        bool wasOpen = _widget?.Pointer == widget.Pointer && IsOpen();
        _widget = widget;
        widget.HandleFloatingPart();
        bool open = IsOpen();
        if (open && !wasOpen) SpeechPipeline.SpeakInterrupt(label + " opened");
        else if (!open && wasOpen) SpeechPipeline.SpeakInterrupt(label + " closed");
        else if (!open) SpeechPipeline.SpeakInterrupt(label + " opening");
        else
        {
            Degrade.Note($"filter popup toggle: '{label}' still open after a close toggle");
            SpeechPipeline.SpeakInterrupt(label + " still open");
        }
    }

    /// <summary>Close if open; a no-op otherwise. Navigation hygiene (nav/bookmark/history).</summary>
    internal static void CloseIfOpen()
    {
        _pendingClose = null;
        if (IsOpen())
        {
            _widget.HideFloatingPart();
            _session++;
        }
    }

    // Native navigation can leave the floating widget open because a programmatic
    // button operation does not produce mouse focus-loss. Let its native callbacks
    // finish before closing that SAME popup session on the following Unity frame.
    internal static void CloseAfterAction(VisualElement origin)
    {
        if (_widget != null && Owns(origin, _widget.name))
            _pendingClose = (_widget, _session, UnityEngine.Time.frameCount);
    }

    internal static void CancelPendingClose() => _pendingClose = null;

    internal static void DrainPendingClose()
    {
        var request = _pendingClose;
        if (!request.HasValue || UnityEngine.Time.frameCount <= request.Value.Frame) return;
        _pendingClose = null;
        if (_widget?.Pointer == request.Value.Widget.Pointer && _session == request.Value.Session)
            CloseIfOpen();
    }

    internal static bool Owns(VisualElement element, string widgetName)
    {
        if (_widget?.name != widgetName || !IsOpen()) return false;
        var content = _widget.m_floatingPartContent;
        for (var parent = element; parent != null; parent = parent.hierarchy.parent)
            if (parent.Pointer == content?.Pointer) return true;
        return false;
    }

    // Native floating content is attached outside its trigger's panel/tree.
    // Resolve ownership by the actual content Pointer, never its caption/path.
    internal static VisualElement OwnerOf(VisualElement element)
    {
        if (element == null || !IsOpen()) return null;
        var content = _widget.m_floatingPartContent;
        for (var parent = element; parent != null; parent = parent.hierarchy.parent)
            if (parent.Pointer == content?.Pointer) return _widget;
        return null;
    }

    /// <summary>
    /// The popup section for ComposeBody, or null when no popup is open. Title is the trigger's
    /// own caption ("Filter").
    /// </summary>
    internal static (string Title, List<ScreenWalker.RenderItem> Items)? TryBuildSection()
    {
        if (!IsOpen()) return null;

        var items = new List<ScreenWalker.RenderItem>();
        // Close first (context-menu precedent): reachable without arrowing through every row.
        items.Add(new ScreenWalker.StopItem { Element = new CloseAdapter() });

        var content = _widget.m_floatingPartContent;
        if (content == null || content.panel == null)
        {
            // Visible but content-less (first-open template still binding). Render an honest
            // placeholder plus the close stop; the token signature re-renders once rows arrive.
            Degrade.Note("filter popup open but content not bound yet");
            items.Add(new ScreenWalker.NoteItem { Text = "Popup content " + Degrade.Unavailable });
            return (NativeBookmarkSurface.Title(_widget) ?? TriggerCaption(_widget) ?? FallbackTitle, items);
        }

        WalkContent(content, items);
        return (NativeBookmarkSurface.Title(_widget) ?? TriggerCaption(_widget) ?? FallbackTitle, items);
    }

    // Structural render of the floating content. Recognised controls become stops; a text
    // whose parent also holds a streamed list is that list's heading; a text folded into a
    // checkbox's label is skipped; any other text is preserved as a note. Unknowns recurse, so
    // nothing readable is silently dropped.
    private static void WalkContent(VisualElement el, List<ScreenWalker.RenderItem> items)
    {
        if (!ElementBridge.IsVisible(el)) return;
        var typeName = el.GetIl2CppType().Name;
        if (NativeBookmarkSurface.TryBuild(el, items)) return;
        if (SyntheticTables.TryBuild(el, items)) return;

        // FM27 also hosts ordinary tables and tab bars in floating menus.
        // Keep their FM26 renderer/selection/paging contracts instead of
        // flattening the table's cells and dropping its SelectionCell controls.
        if (typeName == "StreamedTable" || el.name is "tweak-header" or "player-header"
            || typeName == "ButtonGroup" && el.parent?.GetIl2CppType().Name == "TabGroup")
        {
            ScreenWalker.WalkFragment(el, items);
            return;
        }

        if (typeName == "SICheckBox")
        {
            items.Add(new ScreenWalker.StopItem
            {
                Element = ElementBridge.CreateAdapter(el, ElementClassification.Checkbox),
            });
            return;
        }
        // A collapsible checkbox group (Choose Visuals' categories): its header row becomes one
        // expander stop driving the game's own caret, then its rows walk as usual — visible only
        // while the game has the group expanded, so the page shows exactly what the game does.
        if (typeName == "SICheckBoxGroup")
        {
            var group = el.Cast<SI.Bindable.SICheckBoxGroup>();
            items.Add(new ScreenWalker.StopItem { Element = new GroupAdapter(group) });
            var list = group.m_parentVisuals?.ListContainer;
            if (list != null) WalkContent(list, items);
            return;
        }
        if (typeName == "SIButton" || typeName == "NavigatableVisualElement"
            && Adapters.GridSelectorSelection.TryNativeListEntry(el, out _, out _))
        {
            items.Add(new ScreenWalker.StopItem
            {
                Element = ElementBridge.CreateAdapter(el, ElementClassification.Button),
            });
            return;
        }
        if (typeName == "SIText")
        {
            var text = ReadText(el);
            if (string.IsNullOrWhiteSpace(text)) return;
            if (HasListChild(el.hierarchy.parent) && !el.name.EndsWith("-description", StringComparison.Ordinal))
                items.Add(new ScreenWalker.HeadingItem { Text = text, Level = 3 });
            else if (!IsFoldedCheckboxText(el.hierarchy.parent, text))
                items.Add(new ScreenWalker.NoteItem { Text = text });
            return;
        }

        if (typeName is "SIRadioGroup" or "SIDropdown" or "SIMenu" or "SISlider"
            or "SIProgressBar" or "SIRadialGauge" or "SIStepper" or "BindableTextEditBox"
            or "BindableIntEditBox" or "BindableFloatEditBox")
        {
            ScreenWalker.WalkFragment(el, items);
            return;
        }

        if (typeName == "StreamedObjectList")
            ListWindowSurface.AppendChrome(items, el, ListWindowSurface.Labels.Generic);
        for (int i = 0; i < el.hierarchy.childCount; i++)
            WalkContent(el.hierarchy[i], items);
    }

    // A section heading's parent holds the section's row list as a direct child; a row label's
    // parent holds only the row's own control template.
    private static bool HasListChild(VisualElement parent)
    {
        if (parent == null) return false;
        for (int i = 0; i < parent.hierarchy.childCount; i++)
        {
            var name = parent.hierarchy[i].GetIl2CppType().Name;
            if (name == "StreamedObjectList" || name == "VirtualisedList") return true;
        }
        return false;
    }

    // Only a checkbox's resolved caption/status belongs inside that control.
    // A parent can also contain unrelated header/help text beside the switch.
    private static bool IsFoldedCheckboxText(VisualElement root, string text)
    {
        if (root == null) return false;
        if (root.GetIl2CppType().Name == "SICheckBox")
            return new Adapters.CheckBoxAdapter(root).FoldedTexts().Any(value =>
                string.Equals(TextFilters.CleanForDisplay(value ?? "").Trim(), text.Trim(),
                    StringComparison.Ordinal));
        for (int i = 0; i < root.hierarchy.childCount; i++)
            if (IsFoldedCheckboxText(root.hierarchy[i], text)) return true;
        return false;
    }

    private static string ReadText(VisualElement el)
    {
        try
        {
            var text = el.Cast<TextElement>().text;
            return string.IsNullOrWhiteSpace(text) ? null : TextFilters.CleanForDisplay(text);
        }
        catch (Exception e)
        {
            Degrade.Note($"filter popup text read failed: {e.GetType().Name}");
            return null;
        }
    }

    /// <summary>
    /// The trigger's visible caption and count. The floating part attaches elsewhere,
    /// so this walk stays tiny. Keep all visible trigger text: a leading count alone
    /// does not name a menu. Hidden state variants and tooltips cannot supply its name.
    /// </summary>
    internal static string TriggerCaption(VisualElement widget)
    {
        try
        {
            var texts = new List<string>();
            CollectTriggerTexts(widget, texts);
            return texts.Count == 0 ? null : string.Join(", ", texts);
        }
        catch (Exception e)
        {
            Degrade.Note($"filter popup trigger caption read failed: {e.GetType().Name}");
            return null;
        }
    }

    private static void CollectTriggerTexts(VisualElement el, List<string> texts)
    {
        if (!ElementBridge.IsVisible(el) || el.GetIl2CppType().Name == "Tooltip") return;
        var text = el.TryCast<TextElement>()?.text;
        if (!string.IsNullOrWhiteSpace(text)) texts.Add(TextFilters.CleanForDisplay(text));
        for (int i = 0; i < el.hierarchy.childCount; i++)
            CollectTriggerTexts(el.hierarchy[i], texts);
    }

    /// <summary>
    /// A signature of the open popup — every checkbox's label and live checked bit, the same
    /// collection the render walks — or "" when closed. Folded into the screen token so the SSE
    /// poll refreshes the page on open, close, and every toggle (a toggle can also flip the
    /// game's own aggregate rows, so the whole set is hashed, not just the clicked one).
    /// </summary>
    internal static string Signature()
    {
        if (!IsOpen()) return "";
        var content = _widget.m_floatingPartContent;
        var sb = new StringBuilder("filterpopup:");
        // Equal-caption menus in different records are distinct interactions.
        // Reuse the trigger's normal native/date-scoped key, including while
        // the new floating content is still loading.
        sb.Append(ServerHandles.KeyFor(new TriggerAdapter(_widget))).Append('|');
        if (content == null || content.panel == null) return sb.Append("open").ToString();
        AppendCheckboxSignature(content, sb);
        return sb.ToString();
    }

    private static void AppendCheckboxSignature(VisualElement el, StringBuilder sb)
    {
        if (!ElementBridge.IsVisible(el)) return;
        NativeBookmarkSurface.AppendSignature(el, sb);
        if (el.GetIl2CppType().Name == "SICheckBox")
        {
            var adapter = ElementBridge.CreateAdapter(el, ElementClassification.Checkbox);
            sb.Append(adapter.GetLabel() ?? "?").Append('=').Append(adapter.GetState()).Append('|');
            return;
        }
        if (el.GetIl2CppType().Name == "ExpandedToggle")
            sb.Append("selected=").Append(el.Cast<Toggle>().value).Append('|');
        if (el.GetIl2CppType().Name == "SIText")
        {
            sb.Append(ReadText(el)).Append('|');
            return;
        }
        for (int i = 0; i < el.hierarchy.childCount; i++)
            AppendCheckboxSignature(el.hierarchy[i], sb);
    }

    /// <summary>
    /// The trigger stop ScreenWalker emits in the widget's place. Synthetic (no PluginAdapterBase
    /// backing): the widget has its own native operation. Its stable native identity keeps
    /// equal-caption triggers in separate weeks distinct across open/close re-renders.
    /// The caption, availability and open state read live at render time.
    /// </summary>
    internal sealed class TriggerAdapter : IAccessibleElement, IStableKeyElement
    {
        private readonly SI.Bindable.SIFloatingPopupWidget _trigger;

        public TriggerAdapter(SI.Bindable.SIFloatingPopupWidget trigger) => _trigger = trigger;

        // The widget this stop stands for, so a panel restructure can place the trigger by its
        // position in the game's tree like any other stop. This adapter is not a PluginAdapterBase,
        // so the usual BackingElement route does not reach it.
        internal UnityEngine.UIElements.VisualElement Widget => _trigger;
        public string StableKey => ServerHandles.IdentityPath(_trigger);

        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => NativeBookmarkSurface.Title(_trigger) ?? TriggerCaption(_trigger) ?? Degrade.UnknownControl;
        public string GetPrimaryLabel() => GetLabel();
        public string GetDescription() => _trigger.name == NativeBookmarkSurface.WidgetName
            ? "Manage the game's bookmark bar. These favorites are separate from the companion's Ctrl shortcuts."
            : "";
        public bool IsVisible() => ElementBridge.IsVisible(_trigger);
        public string GetState() => !_trigger.enabledInHierarchy ? "disabled"
            : _widget?.Pointer == _trigger.Pointer && IsOpen() ? "expanded" : "";
        public string GetAnnouncement()
        {
            var state = GetState();
            return GetLabel() + ", " + Role + (state.Length > 0 ? ", " + state : "");
        }
        public void Activate() => Toggle(_trigger, GetLabel());
    }

    /// <summary>
    /// A checkbox group's header: the category name, whether the game has it expanded, and how many
    /// of its rows are chosen (the game's own tri-state parent status). Activating it presses the
    /// game's caret, which expands or collapses the group. The label is the bare name so the stop
    /// keeps its id across the toggle; the changing parts are state.
    /// </summary>
    internal sealed class GroupAdapter : IAccessibleElement
    {
        private readonly SI.Bindable.SICheckBoxGroup _group;

        public GroupAdapter(SI.Bindable.SICheckBoxGroup group) => _group = group;

        // Mod-authored state words.
        private const string Expanded = "expanded";
        private const string Collapsed = "collapsed";
        private const string NoneChosen = "none chosen";
        private const string SomeChosen = "some chosen";
        private const string AllChosen = "all chosen";

        public string Role => "button";
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel()
        {
            var label = _group.m_parentVisuals?.Label;
            return label == null ? Degrade.UnknownControl : ReadText(label) ?? Degrade.UnknownControl;
        }
        public string GetPrimaryLabel() => GetLabel();
        public string GetDescription() => "";
        public bool IsVisible() => ElementBridge.IsVisible(_group);
        public string GetState()
        {
            string chosen = ChosenStatus();
            string open = _group.m_expanded ? Expanded : Collapsed;
            return chosen == null ? open : open + ", " + chosen;
        }

        // The game's tri-state parent status in words; null for a status this does not know.
        internal string ChosenStatus() => _group.m_parentStatus switch
        {
            SI.Bindable.SICheckBoxGroup.ParentStatus.Unchecked => NoneChosen,
            SI.Bindable.SICheckBoxGroup.ParentStatus.Partial => SomeChosen,
            SI.Bindable.SICheckBoxGroup.ParentStatus.Checked => AllChosen,
            _ => null,
        };
        public string GetAnnouncement() => GetLabel() + ", " + Role + ", " + GetState();
        public void Activate()
        {
            _group.OnCaretClicked();
            SpeechPipeline.SpeakInterrupt(GetLabel() + " " + (_group.m_expanded ? Expanded : Collapsed));
        }
    }

    /// <summary>The popup's dismiss stop, first in the section (context-menu Close precedent).</summary>
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
        public void Activate()
        {
            var label = _widget != null ? TriggerCaption(_widget) ?? FallbackTitle : FallbackTitle;
            CloseIfOpen();
            SpeechPipeline.SpeakInterrupt(label + " closed");
        }
    }
}
