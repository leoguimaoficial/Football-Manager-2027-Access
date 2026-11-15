using System.Text;
using FM27Access.Plugin.Adapters;
using FM27Access.Plugin.Contexts;
using Il2CppInterop.Runtime;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

// Readonly inspection of already-realized registered links. Never scroll, load
// an asset, activate a control or modify a provider to identify a duplicate.
internal static class EntityLinkProbe
{
    internal static string Describe(string id)
    {
        var handles = (Dictionary<string, ServerHandles.Handle>)typeof(ServerHandles)
            .GetField("_handles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .GetValue(null);
        handles.TryGetValue(id, out var handle);
        if (handle != null && !handle.TryValidate(out _)) return "unavailable";
        VisualElement element = null;
        if (handle is ServerHandles.StopHandle stop) element = (stop.El as PluginAdapterBase)?.BackingElement;
        else if (handle is ServerHandles.CellHandle cell)
        {
            var context = new TableContext(cell.Table, new ClassificationCache());
            if (!context.MatchesRowIdentity(cell.Row, cell.NativeRowIdentity)) return "row changed";
            element = (context.RealizeCellElement(cell.Row, cell.Col, cell.ElementIndex, scroll: false) as PluginAdapterBase)?.BackingElement;
            if (!context.MatchesRowIdentity(cell.Row, cell.NativeRowIdentity)) return "row changed";
        }
        if (element == null && handle == null && LiveValues.TryGetSource(id, out var source))
            element = (source as PluginAdapterBase)?.BackingElement;
        if (element == null) return "not realized";
        var output = new StringBuilder(element.GetIl2CppType().Name + " " + element.name + "\n");
        for (var parent = element.parent; parent != null; parent = parent.parent)
        {
            output.AppendLine("parent " + parent.GetIl2CppType().Name + " " + parent.name);
            if (parent.GetIl2CppType().Name == "Panel") break;
        }
        var button = element.TryCast<SIButton>();
        if (button != null)
        {
            output.AppendLine("pair decision=" + PairedEntityLink.IsRedundantIcon(element));
            var scope = element.parent?.parent?.parent;
            for (int depth = 0; depth < 6 && scope != null; depth++, scope = scope.parent)
            {
                output.AppendLine("scope " + scope.GetIl2CppType().Name + " " + scope.name);
                foreach (var linked in NativeTreeQuery.All<SIText>(scope))
                {
                    if (!ElementBridge.IsVisibleInHierarchy(linked) || !SITextLink.HasLink(linked.text) || linked.EmbeddedData == null) continue;
                    for (int i = 0; i < linked.EmbeddedData.Count; i++)
                    {
                        var reference = BindingRecords.Field(BindingRecords.Fields(linked.EmbeddedData[i]), "UIRecord")?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
                        output.AppendLine("scope link " + Speech.TextFilters.CleanForDisplay(linked.text) + " ref=" + reference?.CombinedIndexAndType);
                    }
                }
                if (scope.GetIl2CppType().Name is "BindingRemapper" or "Cell" or "Panel") break;
            }
            output.AppendLine("custom=" + (button.m_clickableAction != null) + " events=" + button.Events?.Count);
            if (button.Events != null) for (int eventIndex = 0; eventIndex < button.Events.Count; eventIndex++)
            {
                var action = button.Events[eventIndex];
                DescribeAction(action, output);
                var key = action.m_sourceKey;
                var bindings = SI.Core.GameSubsystems.Get<BindingSubsystem>();
                if (bindings != null && bindings.TryGetNode(ref key, out var node))
                {
                    DescribeNode(node, output, 0);
                    DescribeNode(node.Parent, output, 0);
                    DescribeNode(node.Parent?.Parent, output, 0);
                    DescribeNode(node.Parent?.Parent?.Parent, output, 0);
                    foreach (var child in node.Children)
                    {
                        var reference = BindingRecords.ValueOf(child)?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
                        if (reference != null) output.AppendLine("reference " + child.Name + " " + reference.GetIl2CppType().Name + " " + reference.CombinedIndexAndType);
                        if (reference?.TryCast<FM.UI.ClubReference>() is { } club)
                        {
                            var area = element.parent?.parent?.parent?.parent;
                            foreach (var candidateText in NativeTreeQuery.All<SIText>(area))
                            {
                                if (candidateText.EmbeddedData == null) continue;
                                foreach (var data in candidateText.EmbeddedData)
                                {
                                    var team = BindingRecords.Field(BindingRecords.Fields(data), "UIRecord")?.Get()?.TryCast<FM.UI.TeamReference>();
                                    if (team == null) continue;
                                    var method = typeof(PairedEntityLink).GetMethod("IsOwningTeam", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                                    output.AppendLine("pair owning=" + method.Invoke(null, new object[] {child, club, team}) + " club=" + club.CombinedIndexAndType + " team=" + team.CombinedIndexAndType);
                                }
                            }
                        }
                    }
                }
            }
        }
        var text = element.TryCast<SIText>();
        if (text != null)
        {
            var scope = text.parent;
            for (int depth = 0; depth < 12 && scope != null; depth++, scope = scope.parent)
            {
                var type = scope.GetIl2CppType().Name;
                if (type is "Panel" or "DynamicItem" or "Cell" or "BindingRoot") break;
                output.AppendLine("caption scope " + type + " " + scope.name
                    + " texts=" + NativeTreeQuery.All<SIText>(scope).Count
                    + " buttons=" + NativeTreeQuery.All<SIButton>(scope).Count
                    + " images=" + NativeTreeQuery.All<SIImage>(scope).Count);
                foreach (var candidate in NativeTreeQuery.All<SIButton>(scope))
                    output.AppendLine("caption action " + candidate.name + " label=" + new ButtonAdapter(candidate).GetLabel()
                        + " visible=" + ElementBridge.IsVisibleInHierarchy(candidate) + " enabled=" + candidate.enabledInHierarchy
                        + " texts=" + NativeTreeQuery.All<SIText>(candidate).Count + " events=" + candidate.Events?.Count);
                if (type == "BindingRemapper") break;
            }
            var handler = text.m_bindingHandler?.Cast<Il2CppSystem.Object>()?.TryCast<BindingHandler<SIText>>();
            output.AppendLine("text kind=" + text.m_textBinding?.Kind);
            if (text.m_textBinding?.Kind == BindingMethodKind.Direct)
                output.AppendLine("text direct=" + text.m_textBinding.Direct?.Path?.Path);
            if (text.m_textBinding?.Kind == BindingMethodKind.VisualFunction)
                output.AppendLine("text function=" + text.m_textBinding.VisualFunction?.Target);
            var direct = text.m_textBinding?.Kind == BindingMethodKind.Direct
                ? handler?.m_state?.Direct : null;
            if (direct != null)
            {
                var key = direct.Key;
                var subsystem = SI.Core.GameSubsystems.Get<BindingSubsystem>();
                if (key.m_key != 0 && subsystem.TryGetNode(ref key, out var node)) DescribeNode(node, output, 0);
            }
            // Direct and visual-function binding states have different native
            // payloads. Inspect only the state selected by the actual binding.
            var bag = text.m_textBinding?.Kind == BindingMethodKind.VisualFunction
                ? handler?.m_state?.VisualFunction?.Bag : null;
            int remaining = 24;
            for (var request = bag?.m_head; request != null && remaining-- > 0; request = request.Next)
            {
                if (request.m_kind != BindingBag.Request.Kind.Binding) continue;
                var reference = request.Value?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
                output.AppendLine("text input " + request.Value?.Get()?.GetIl2CppType().Name + " ref=" + reference?.CombinedIndexAndType);
                var key = request.Key;
                var subsystem = SI.Core.GameSubsystems.Get<BindingSubsystem>();
                if (key.m_key != 0 && subsystem.TryGetNode(ref key, out var node)) DescribeNode(node, output, 0);
            }
        }
        if (text?.EmbeddedData != null)
            for (int i = 0; i < text.EmbeddedData.Count; i++)
            {
                var fields = BindingRecords.Fields(text.EmbeddedData[i]);
                output.AppendLine("embedded " + i + " fields=" + string.Join(",", fields.Keys));
                var reference = BindingRecords.Field(fields, "UIRecord")?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
                if (reference != null) output.AppendLine("reference UIRecord " + reference.GetIl2CppType().Name + " " + reference.CombinedIndexAndType);
            }
        return output.ToString();
    }

    private static void DescribeNode(IReadOnlyNode node, StringBuilder output, int depth)
    {
        if (node == null || depth > 2) return;
        var reference = BindingRecords.ValueOf(node)?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
        output.AppendLine(new string(' ', depth) + "node " + node.Name + " ref=" + reference?.GetIl2CppType().Name + ":" + reference?.CombinedIndexAndType);
        if (reference != null)
        {
            var alias = node;
            var subsystem = SI.Core.GameSubsystems.Get<BindingSubsystem>();
            for (int i = 0; i < 5 && alias != null; i++)
            {
                var owner = alias.Parent == null ? null : BindingRecords.ValueOf(alias.Parent)?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
                var ownValue = BindingRecords.ValueOf(alias)?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
                output.AppendLine("alias " + alias.Name + " property=" + SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance.GetInfo(alias.PropID)?.Name
                    + " value=" + ownValue?.GetIl2CppType().Name + ":" + ownValue?.CombinedIndexAndType
                    + " clubCast=" + (ownValue?.TryCast<FM.UI.ClubReference>() != null)
                    + " owner=" + owner?.GetIl2CppType().Name + ":" + owner?.CombinedIndexAndType
                    + " teamCast=" + (owner?.TryCast<FM.UI.TeamReference>() != null));
                var aliasKey = alias.Alias;
                if (aliasKey.m_key == 0 || !subsystem.TryGetNode(ref aliasKey, out alias)) break;
            }
        }
        if (node.Name == "ObjectLookupData" && BindingRecords.RecordOf(BindingRecords.ValueOf(node)) is { } lookup)
        {
            var fields = BindingRecords.Fields(lookup);
            output.AppendLine("lookup fields=" + string.Join(",", fields.Keys));
            var target = BindingRecords.Field(fields, "UIRecord")?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
            if (target != null) output.AppendLine("lookup UIRecord " + target.GetIl2CppType().Name + " " + target.CombinedIndexAndType);
        }
        foreach (var child in node.Children) DescribeNode(child, output, depth + 1);
    }

    private static void DescribeAction(Il2CppSystem.Object action, StringBuilder output)
    {
        if (action == null) { output.AppendLine("null action"); return; }
        output.AppendLine("action " + action.GetIl2CppType().Name);
        if (action.TryCast<EmbeddedDataClickedEventContext>() is { } embedded && embedded.m_record != null)
        {
            var fields = BindingRecords.Fields(embedded.m_record);
            output.AppendLine("click fields=" + string.Join(",", fields.Keys));
            var reference = BindingRecords.Field(fields, "UIRecord")?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
            if (reference != null) output.AppendLine("click UIRecord " + reference.GetIl2CppType().Name + " " + reference.CombinedIndexAndType);
        }
        if (action.TryCast<SI.Bindable.Events.OpenPanelEventContext>() is { } open)
        {
            var panel = open.m_asset?.Asset?.TryCast<PanelID>();
            output.AppendLine("panel " + open.m_asset?.AssetGUID + " " + panel?.ID + " " + panel?.name);
            if (open.m_argumentBindings != null)
                foreach (var argument in open.m_argumentBindings)
                {
                    output.AppendLine("argument " + argument.name + " path=" + argument.path.m_path);
                    if (open.m_bindings == null) continue;
                    foreach (var binding in open.m_bindings)
                    {
                        if (binding.path != argument.path.m_path) continue;
                        var key = binding.key;
                        var subsystem = SI.Core.GameSubsystems.Get<BindingSubsystem>();
                        if (key.m_key == 0 || !subsystem.TryGetNode(ref key, out _)) continue;
                        var value = subsystem.Get(ref key)?.Get();
                        var reference = value?.TryCast<FM.UI.DatabaseRecordReference>();
                        output.AppendLine("argument value=" + value?.GetIl2CppType().Name + " ref=" + reference?.CombinedIndexAndType);
                    }
                }
        }
        if (action.TryCast<FM.UI.OpenPanelOrPanelEventContext>() is { } conditional)
        {
            output.AppendLine("conditional value=" + conditional.m_bindingValue);
            DescribeAction(conditional.m_panelWhenTrueEventContext, output);
            DescribeAction(conditional.m_panelWhenFalseEventContext, output);
        }
    }
}
