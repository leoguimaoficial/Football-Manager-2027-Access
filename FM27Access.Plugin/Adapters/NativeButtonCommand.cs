using SI.Bindable;
using SI.Bindable.Events;
namespace FM27Access.Plugin.Adapters;
internal static class NativeButtonCommand
{
    internal static bool HasBoundCommand(UnityEngine.UIElements.VisualElement element)
    {
        var button = element.TryCast<SIButton>();
        return button != null && (button.Events?.Count > 0 || button.m_clickableAction != null);
    }

    // A checkbox can own a native panel through a matched open/close event pair.
    // Closing only PanelManager leaves its checked binding set and the next
    // activation merely unchecks it. Resolve the actual owner, not a panel-name
    // allowlist, so Back/Escape follow the same lifecycle as its native toggle.
    internal static SICheckBox PanelOwnerToggle(Panel panel, System.Collections.Generic.IEnumerable<Panel> panels)
    {
        SICheckBox owner = null;
        foreach (var root in panels)
        {
            if (root == null || !root.IsVisible || !root.IsActive) continue;
            FindPanelOwnerToggle(root, panel, ref owner);
        }
        return owner;
    }

    private static void FindPanelOwnerToggle(UnityEngine.UIElements.VisualElement root, Panel panel, ref SICheckBox owner)
    {
        if (!ElementBridge.IsVisible(root)) return;
        if (root.GetIl2CppType().Name == "SICheckBox")
        {
            var toggle = root.Cast<SICheckBox>();
            if (!toggle.value || toggle.CheckedEvents?.Count != 1 || toggle.UncheckedEvents?.Count != 1) return;
            var open = toggle.CheckedEvents[0]?.TryCast<OpenPanelEventContext>();
            var close = toggle.UncheckedEvents[0]?.TryCast<ClosePanelEventContext>();
            if (open?.m_asset == null || close?.m_panelID == null
                || string.IsNullOrEmpty(open.m_asset.AssetGUID)
                || open.m_asset.AssetGUID != close.m_panelID.AssetGUID) return;
            var target = open.m_asset.Asset?.TryCast<PanelID>()
                ?? open.m_asset.LoadAssetAsync().WaitForCompletion();
            if (target?.ID != panel.PanelID?.ID) return;
            if (owner != null && owner.Pointer != toggle.Pointer)
                throw new System.InvalidOperationException("More than one checked toggle owns the foreground panel.");
            owner = toggle;
            return;
        }
        for (int i = 0; i < root.hierarchy.childCount; i++)
            FindPanelOwnerToggle(root.hierarchy[i], panel, ref owner);
    }

    // GenericModalDialog is owned by DialogManager, including its queue. Closing
    // only the Panel leaves that manager waiting and prevents the same prompt
    // from opening again. Resolve the native close response by its slot data,
    // never by translated Yes/No/Cancel captions or an assumed button position.
    internal static SIButton DialogCancelButton(SI.Bindable.Panel panel)
    {
        if (panel?.PanelID?.name != "GenericModalDialog") return null;
        var manager = DialogManager.Instance;
        if (manager == null || !manager.m_isDialogVisible
            || manager.GenericDialogPanel?.Pointer != panel.PanelID.Pointer) return null;
        var data = manager.m_currentDialog?.Value;
        // DialogData overloads equality with a native call that rejects null.
        // Test the managed wrapper without invoking that operator.
        if (data is null) return null;
        var slots = new[] { "si-visible-primary", "si-visible-secondary", "si-visible-tertiary", "si-visible-quaternary" };
        var buttons = new[] { data.PrimaryButton, data.SecondaryButton, data.TertiaryButton, data.QuaternaryButton };
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            if (!button.IsVisible || button.DontCloseOnClick || button.EventID != DialogManager.CloseDialogEventID) continue;
            var slot = FindVisible(panel, slots[i]);
            var hit = slot == null ? null : FindVisible(slot, null)?.TryCast<SIButton>();
            if (hit != null) return hit;
        }
        return null;
    }

    private static UnityEngine.UIElements.VisualElement FindVisible(UnityEngine.UIElements.VisualElement root, string name)
    {
        if (!ElementBridge.IsVisible(root)) return null;
        if (name == null ? root.GetIl2CppType().Name == "SIButton" : root.name == name) return root;
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var found = FindVisible(root.hierarchy[i], name);
            if (found != null) return found;
        }
        return null;
    }

    // Transfer response cells reuse the same tick/clock/cross templates as
    // unrelated controls. Their actual native command has semantic priority.
    // CancelReject also withdraws an outgoing offer; do not call every cross Close.
    internal static string ActionLabel(SIButton button)
    {
        var events = button?.Events;
        if (events == null) return null;
        for (int i = 0; i < events.Count; i++)
        {
            // The bin icon ends a focus and moves it to Completed; it is not
            // permanent deletion. Use the actual confirmation command.
            var dialog = events[i]?.TryCast<TriggerDialogEventContext>();
            if (dialog?.m_type == "RecruitmentFocusRemove"
                && dialog.m_primaryButton?.EventID == "RecruitmentFocusRemove")
                return "Complete recruitment focus";
            var plugin = events[i]?.TryCast<FM.GamePlugin.EventToPluginContext>();
            if (plugin == null) continue;
            switch (plugin.m_id)
            {
                // The native advice acknowledgement is icon-only: no static
                // text, text binding or tooltip. Reuse this command-semantic
                // mod caption path, not its template name or a guessed glyph.
                case FM.GamePlugin.InteropEventIDs.EventFmxAcknowledgeAdvice: return "Acknowledge advice";
                case FM.GamePlugin.InteropEventIDs.EventFmxActionDevelopmentCentreAdvice: return "Apply development advice";
                case FM.GamePlugin.InteropEventIDs.EventFmxTransferCentreResponseAccept: return "Accept offer";
                case FM.GamePlugin.InteropEventIDs.EventFmxTransferCentreResponseStall: return "Delay response to offer";
                case FM.GamePlugin.InteropEventIDs.EventFmxTransferCentreResponseCancelReject: return "Reject or withdraw offer";
                  case FM.GamePlugin.InteropEventIDs.EventFmxRecruitmentFocusRestart: return "Restart recruitment focus";
            }
        }
        return null;
    }

    internal static bool IsCloseControl(UnityEngine.UIElements.VisualElement element)
    {
        var panel = element;
        while (panel != null && panel.GetIl2CppType().Name != "Panel") panel = panel.hierarchy.parent;
        var cancel = DialogCancelButton(panel?.TryCast<SI.Bindable.Panel>());
        if (cancel != null && cancel.Pointer == element.Pointer) return true;
        if (element.name == "EndTutorialButton") return true;
        var events = element.TryCast<SIButton>()?.Events;
        // A content action may close this card AND open another destination.
        // Only a standalone close command is an Escape/dismiss affordance.
        if (events != null && events.Count == 1)
            for (int i = 0; i < events.Count; i++)
                if (events[i]?.TryCast<ClosePanelEventContext>() != null
                    || events[i]?.TryCast<PanelHistoryCloseEventContext>() != null) return true;
        // The shared card-close template sometimes binds the close through
        // its controller rather than the button's own event list.
        for (var parent = element.parent; parent != null; parent = parent.parent)
        {
            if (parent.name is "close-button" or "close_button" or "CloseSearchCardButton"
                or "HideTutorial" or "EndTutorialButton") return true;
            if (parent.GetIl2CppType().Name == "Panel") break;
        }
        return false;
    }

    internal static string Label(SIButton button)
    {
        var events=button?.Events;if(events==null)return null;
        for(int i=0;i<events.Count;i++)
        {
            var context=events[i];
            var plugin=context?.TryCast<FM.GamePlugin.EventToPluginContext>();
            if(plugin?.m_id==FM.GamePlugin.InteropEventIDs.EventFmxRemoveRetainPlayerRules)return "Remove";
            var rebind=context?.TryCast<FM.UI.InputRebindingStateChangedEventContext>();
            if(rebind?.m_state==FM.UI.InputRebindingState.ResetToDefault)return "Reset shortcut";
            if(rebind?.m_state==FM.UI.InputRebindingState.ResetAlternateToDefault)return "Reset alternate shortcut";
            var open=context?.TryCast<OpenPanelEventContext>();
            var reference=open?.m_asset;
            // The same small PanelID metadata load used by BookmarkBar and
            // NavigatorIndex. Icon-only commands can reference an unopened panel.
            // Reading its official title does not open or execute the command.
            var target=reference?.Asset?.TryCast<PanelID>();
            if(target==null&&reference!=null)
                target=reference.LoadAssetAsync().WaitForCompletion();
            if(target?.name=="AddShortcutDialog")return "Add shortcut";
            if(target?.name=="ColourPickerDialog")return new FieldLabelReader(button).GetLabel()??"Colour";
            if(target!=null)
            {
                // Icon-only destination controls carry their name in the
                // native panel command (including FM27's advice shortcuts).
                var title=Server.ServerText.StripUnresolvedEntityTokens(target.Title.TranslatedString);
                if(!string.IsNullOrWhiteSpace(title))return title;
            }
            var set=context?.TryCast<SetDatastoreValueEventContext>();
            if(set?.m_sourceBinding?.path=="filebrowser.CurrentDirectory.ParentDirectory")return "Parent folder";
            if(context?.TryCast<ClosePanelEventContext>()!=null||context?.TryCast<PanelHistoryCloseEventContext>()!=null)return "Close";
        }
        return null;
    }
}
