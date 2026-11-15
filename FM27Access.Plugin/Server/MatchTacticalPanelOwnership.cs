using FM.Match;
using SI.Bindable;
using SI.Bindable.Events;
using UnityEngine.AddressableAssets;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

// A plain OpenPanel command can expose a tactical editor without entering the
// match controller's edit session. Its MatchCloseTacticalPanel commands then
// cannot close/discard it. Adopt that actual panel through the native tactical
// opener before any edits, retaining native bindings, transactions and rules.
internal static class MatchTacticalPanelOwnership
{
    private sealed record Request(AssetReferenceT<PanelID> Asset, IntPtr Match, int Frame, long Until);
    private static Request pending;

    internal static void Clear() => pending = null;

    internal static void BeforeActivate(SIButton button)
    {
        var events = button.Events;
        if (events?.Count != 1) return;
        var open = events[0]?.TryCast<OpenPanelEventContext>();
        if (open?.m_asset is null) return;
        var match = MatchBallLocation.CurrentMatch();
        var controller = UnityEngine.Object.FindObjectOfType<FMMatchUIComponent>();
        if (match is null || controller is null || HasOwner(controller)) return;
        pending = new(open.m_asset, match.Pointer, UnityEngine.Time.frameCount,
            Environment.TickCount64 + 20000);
    }

    // Observe the resulting native foreground on a later Unity frame, after
    // opened callbacks and resource resolution. Never re-enter panel
    // construction or the original click.
    internal static void DrainPending()
    {
        if (pending is null) return;
        try { ObservePending(); }
        catch (Exception ex)
        {
            Clear();
            Speech.Log.Error($"[MatchTacticalPanelOwnership] Native edit-session handoff failed: {ex}");
        }
    }

    private static void ObservePending()
    {
        var request = pending;
        if (request is null || UnityEngine.Time.frameCount <= request.Frame) return;
        var match = MatchBallLocation.CurrentMatch();
        if (Environment.TickCount64 > request.Until || match is null || match.Pointer != request.Match)
        { Clear(); return; }
        // OpenPanel resolves through the native manager's resource handles;
        // the AssetReference.Asset property can remain null after a real open.
        var manager = PanelManager.Instance;
        if (manager is null || !manager.TryGetPanelID(request.Asset, out var target)
            || target is null) return;
        var panel = FM27PanelCatalog.GetForegroundPanel();
        if (panel is null || panel.PanelID?.ID != target.ID) return;
        Clear();
        var controller = UnityEngine.Object.FindObjectOfType<FMMatchUIComponent>();
        if (controller is null || HasOwner(controller) || !HasTacticalClose(panel)) return;

        // Use the original event's actual asset, the native panel classification
        // and the controller's current possession phase. Do not write owner,
        // pending-change or session fields, invent an ID, or close a bare Panel.
        controller.OpenTacticalChangesPanel(request.Asset,
            controller.IsTacticsPanel(request.Asset), controller.m_inPosession);
        Speech.Log.Info($"[MatchTacticalPanelOwnership] Entered native edit session for {panel.PanelID.name}.");
    }

    private static bool HasOwner(FMMatchUIComponent controller)
        => controller.IsInTacticalChanges()
            || controller.m_openTacticalChangesPanelID is not null
            || controller.m_sessionTacticReference is not null;

    private static bool HasTacticalClose(VisualElement root)
    {
        var stack = new Stack<VisualElement>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var element = stack.Pop();
            if (!ElementBridge.IsVisible(element)) continue;
            if (element.GetIl2CppType().Name == "SIButton")
            {
                var events = element.Cast<SIButton>().Events;
                if (events != null)
                    for (int i = 0; i < events.Count; i++)
                        if (events[i]?.TryCast<MatchCloseTacticalPanelEventContext>() is not null) return true;
            }
            for (int i = 0; i < element.hierarchy.childCount; i++) stack.Push(element.hierarchy[i]);
        }
        return false;
    }
}
