using UnityEngine.UIElements;
namespace FM27Access.Plugin.Server;

// Read the actual career/team tactic reference and selected slot; never use
// its caption or a displayed formation as business-context identity.
internal static class NativeTacticContext
{
    internal static string Read(VisualElement from)
    {
        int depth = 0;
        for (var owner = from; owner != null && depth++ < 100; owner = owner.parent)
        {
            var node = BindingRecords.NodeOf(owner);
            if (node == null) continue;
            var tactic = BindingRecords.FindNode(node, "Tactic", 4);
            if (tactic == null) continue;
            var slotNode = BindingRecords.FindNode(tactic, "CurrentTacticSlot", 0);
            if (slotNode == null) continue;
            var slot = BindingRecords.NumberOf(BindingRecords.ValueOf(slotNode));
            var reference = BindingRecords.ValueOf(tactic)?.Get()?.TryCast<SI.Interop.InteropReference>();
            var identity = BindingRecords.ReferenceIdentity(reference);
            if (identity != null && slot.HasValue && slot.Value >= 0 && slot.Value <= int.MaxValue
                && slot.Value == System.Math.Floor(slot.Value)) return identity + "|slot:" + (int)slot.Value;
        }
        return null;
    }
}
