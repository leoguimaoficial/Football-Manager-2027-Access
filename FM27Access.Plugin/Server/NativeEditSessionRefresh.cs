using FM.GamePlugin;
using FM.UI;
using SI.Bindable;
using SI.Core;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

// FM27 can change edit history without updating existing form-value channels.
// Read those same properties again through their native subscriptions.
// Never reopen the edit-session/provider channels or write a business value.
internal static class NativeEditSessionRefresh
{
    private sealed record Request(SI.Bindable.Panel Panel, ulong Scope, SIButton Button,
        EventToPluginContext Command, int Frame, long Until);
    private static Request pending;

    internal static void Clear() => pending = null;

    internal static void BeforeActivate(SIButton button)
    {
        var events = button.Events;
        if (events?.Count != 1) return;
        var command = events[0]?.TryCast<EventToPluginContext>();
        if (command is null || command.m_id is not (InteropEventIDs.EventFmxRevert
            or InteropEventIDs.EventFmxUndo or InteropEventIDs.EventFmxRedo)) return;
        var panel = FM27PanelCatalog.GetForegroundPanel();
        if (panel is null || !InPanel(button, panel)) return;
        pending = new(panel, panel.Key.m_key, button, command, UnityEngine.Time.frameCount,
            Environment.TickCount64 + 20000);
    }

    internal static void DrainPending()
    {
        var request = pending;
        if (request is null || UnityEngine.Time.frameCount <= request.Frame) return;
        try
        {
            if (Environment.TickCount64 > request.Until || request.Panel.panel is null
                || request.Panel.Key.m_key != request.Scope)
            { Clear(); return; }
            if (FM27PanelCatalog.GetForegroundPanel()?.Pointer != request.Panel.Pointer
                || request.Command.m_targetReference is null) return;
            if (request.Command.m_id == InteropEventIDs.EventFmxRevert
                && request.Button.enabledInHierarchy) return;
            // Revert has an observable clean-state transition. Undo/Redo can
            // leave both history buttons enabled, so do not wait for a Boolean
            // edge. Queue read-only value requests after the native command on
            // the following Unity frame, through the same ordered FMX pipeline.
            // Do not replay the command or recreate its edit-session provider.
            Clear();
            RenewValues(request.Panel, request.Command.m_id);
        }
        catch (Exception ex)
        {
            Clear();
            Speech.Log.Error($"[NativeEditSessionRefresh] Native form-value refresh failed: {ex}");
        }
    }

    private static bool InPanel(VisualElement element, SI.Bindable.Panel panel)
    {
        for (var current = element; current != null; current = current.hierarchy.parent)
            if (current.Pointer == panel.Pointer) return true;
        return false;
    }

    private static void RenewValues(VisualElement root, InteropEventIDs operation)
    {
        var bindings = GameSubsystems.Get<BindingSubsystem>();
        var keys = new HashSet<ulong>();
        var stack = new Stack<VisualElement>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var element = stack.Pop();
            if (!ElementBridge.IsVisible(element)) continue;
            // The existing typed form controllers own their value requests.
            // Do not resubscribe arbitrary reference/list/provider nodes: those
            // may own the native edit session and its Undo/Redo history.
            switch (element.GetIl2CppType().Name)
            {
                case "SIDropdown":
                    keys.Add(element.Cast<SIDropdown>().m_selectionValueBindingKey.m_key); break;
                case "SICheckBox":
                    keys.Add(element.Cast<SICheckBox>().m_valueKey.m_key); break;
                case "BindableTextEditBox":
                    keys.Add(element.Cast<BindableTextEditBox>().m_bindingKey.m_key); break;
                case "SISlider":
                    keys.Add(element.Cast<SISlider>().m_bindingKey.m_key); break;
                case "SIRadioGroup":
                    var radios = element.Cast<SIRadioGroup>();
                    keys.Add(radios.m_selectionKey.m_key);
                    keys.Add(radios.m_indexKey.m_key); break;
            }
            for (int i = 0; i < element.hierarchy.childCount; i++) stack.Push(element.hierarchy[i]);
        }

        int renewed = 0;
        foreach (var number in keys)
        {
            if (number == 0 || !bindings.m_nodes.TryGetValue(number, out var node)) continue;
            var handler = bindings.GetContextInfo(node).handler?.TryCast<InteropDataHandler>();
            if (handler is null || !HasOpenChannel(handler, number)) continue;
            var source = node.Parent?.Value;
            if (source?.Get()?.TryCast<SI.Interop.InteropReference>() is null) continue;
            var property = new Bindings.Property(node.Name, node.PropID);
            var key = node.Key;
            handler.CloseChannel(key);
            handler.OpenChannel(source, property, key);
            renewed++;
        }
        Speech.Log.Info($"[NativeEditSessionRefresh] Renewed {renewed} existing native form-value subscriptions after {operation}.");
    }

    private static bool HasOpenChannel(InteropDataHandler handler, ulong number)
    {
        // 0.9.2409 exposes the dictionary directly. 0.9.2435 wraps it in
        // InteropChannels.m_open alongside native reply-generation routing.
        // Read only its actual open-key membership, without changing routing,
        // constructing a replacement channel or binding this assembly to the
        // newer return type (which does not exist in the earlier interop).
        var channelProperty = typeof(InteropDataHandler).GetProperty("m_channels")
            ?? throw new MissingMemberException(typeof(InteropDataHandler).FullName, "m_channels");
        var channels = channelProperty.GetValue(handler);
        if (channels is null) return false;
        var openProperty = channels.GetType().GetProperty("m_open");
        var open = openProperty is null ? channels : openProperty.GetValue(channels);
        if (open is null) return false;
        var contains = open.GetType().GetMethod("ContainsKey", new[] { typeof(ulong) });
        if (contains is null || contains.ReturnType != typeof(bool))
            throw new InvalidOperationException($"Unsupported native open-channel collection: {open.GetType().FullName}");
        return (bool)contains.Invoke(open, new object[] { number });
    }
}
