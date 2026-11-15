using System.IO;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Plugin.Diagnostics;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin;

/// <summary>
/// The F8-F11 in-game debug keys: file dumps of the UI tree, the classification report, and the
/// cursor's context, written to the dumps/ folder for offline reading. The tree walk and the
/// classification walk themselves live in <see cref="TreeDump"/> and <see cref="ClassificationDump"/>
/// (shared with the dev driver's /panels, /gui and /classify endpoints); this class is the file-dump
/// front end over them plus the cursor-context and unclassified-tracker dumps.
/// </summary>
internal static class DebugTools
{
    private const int MaxTreeDepth = 200;

    private static readonly string DumpDir = Path.Combine(
        BepInEx.Paths.PluginPath, "FM27Access", "dumps");

    private static StringBuilder _dumpBuffer;

    public static void DumpFullTree()
    {
        if (!SI.Bindable.PanelManager.Exists())
        {
            Log.Warn("[DebugDump] PanelManager not available.");
            SpeechPipeline.SpeakInterrupt("panel manager not available");
            return;
        }

        var pm = SI.Bindable.PanelManager.Instance;
        var layers = FM27PanelCatalog.GetLayers(pm);

        int totalPanels = 0;
        int totalLayers = 0;
        var aggregate = new TreeDump.TreeStats();

        BeginDump();
        DumpLine("DebugDump", "========== ALL LAYERS TREE DUMP ==========");

        for (int i = 0; i < layers.Count; i++)
        {
            var layerInfo = layers[i];
            var layerEnum = layerInfo.Layer;
            var openPanels = layerInfo.Open;
            int closingCount = layerInfo.Closing?.Count ?? 0;

            if ((openPanels == null || openPanels.Count == 0) && closingCount == 0)
            {
                DumpLine("DebugDump", $"---------- LAYER: {layerEnum} ({i}) ----------");
                DumpLine("DebugDump", "  (no open panels)");
                continue;
            }

            DumpLine("DebugDump", $"---------- LAYER: {layerEnum} ({i}) ----------");

            if (closingCount > 0)
                DumpLine("DebugDump", $"  ({closingCount} panel(s) closing)");

            if (openPanels == null) continue;

            totalLayers++;
            for (int p = 0; p < openPanels.Count; p++)
            {
                var panel = openPanels[p];
                if (panel == null) continue;

                totalPanels++;
                var title = GetPanelTitle(panel);
                DumpLine("DebugDump", $"========== PANEL: \"{title}\" ==========");
                DumpLine("DebugDump", $"Panel type: {panel.GetIl2CppType().Name}, name: \"{panel.name}\", layer: {layerEnum} ({i})");

                var stats = new TreeDump.TreeStats();
                var tree = new StringBuilder();
                bool truncated = TreeDump.Write(panel, tree, new TreeDump.Options
                {
                    MaxDepth = MaxTreeDepth,
                    MaxNodes = 0, // file dump is the complete record; no node cap
                    ShowChildCount = true,
                    ShowEnabled = true,
                    ShowOpacity = true,
                    ShowInterfaces = true,
                    ShowUss = true,
                    Stats = stats,
                });
                DumpRaw(tree.ToString());
                if (truncated) DumpLine("DebugDump", "  (tree truncated)");

                DumpLine("DebugDump", "--- PANEL STATS ---");
                DumpLine("DebugDump", $"Total elements: {stats.TotalElements}");
                DumpLine("DebugDump", $"Visible elements: {stats.VisibleElements}");
                DumpLine("DebugDump", $"Hidden elements: {stats.HiddenElements}");
                DumpLine("DebugDump", $"Max depth reached: {stats.MaxDepthReached}");
                DumpLine("DebugDump", $"Text elements with content: {stats.TextElementsWithContent}");
                DumpLine("DebugDump", "Elements by type:");
                foreach (var kvp in stats.TypeCounts.OrderByDescending(k => k.Value))
                    DumpLine("DebugDump", $"  {kvp.Key}: {kvp.Value}");

                aggregate.TotalElements += stats.TotalElements;
                aggregate.VisibleElements += stats.VisibleElements;
                aggregate.HiddenElements += stats.HiddenElements;
            }
        }

        DumpLine("DebugDump", "========== AGGREGATE STATS ==========");
        DumpLine("DebugDump", $"Total panels: {totalPanels} across {totalLayers} layers");
        DumpLine("DebugDump", $"Total elements: {aggregate.TotalElements}");
        DumpLine("DebugDump", $"Visible elements: {aggregate.VisibleElements}");
        DumpLine("DebugDump", $"Hidden elements: {aggregate.HiddenElements}");
        DumpLine("DebugDump", "========== END ALL LAYERS DUMP ==========");
        FlushDump("f9_tree.txt");

        DumpAllLayersClassification(totalPanels, totalLayers, aggregate);
    }

    private static void DumpAllLayersClassification(int totalPanels, int totalLayers, TreeDump.TreeStats aggregateTreeStats)
    {
        var pm = SI.Bindable.PanelManager.Instance;
        var layers = FM27PanelCatalog.GetLayers(pm);

        int totalStops = 0;
        int totalContainers = 0;
        int totalUnknown = 0;

        BeginDump();
        DumpLine("DebugClassify", "========== ALL LAYERS CLASSIFICATION REPORT ==========");

        for (int i = 0; i < layers.Count; i++)
        {
            var layerInfo = layers[i];
            var openPanels = layerInfo.Open;
            if (openPanels == null || openPanels.Count == 0) continue;

            DumpLine("DebugClassify", $"---------- LAYER: {layerInfo.Layer} ({i}) ----------");

            for (int p = 0; p < openPanels.Count; p++)
            {
                var panel = openPanels[p];
                if (panel == null) continue;

                DumpLine("DebugClassify", $"========== PANEL: \"{GetPanelTitle(panel)}\" ==========");

                var report = ClassificationDump.Classify(panel);
                var section = new StringBuilder();
                ClassificationDump.RenderPanel(section, report);
                DumpRaw(section.ToString());

                totalStops += report.NavigationStops.Count;
                totalContainers += report.Containers.Count;
                totalUnknown += report.UnknownElements.Count;
            }
        }

        DumpLine("DebugClassify", "========== AGGREGATE SUMMARY ==========");
        DumpLine("DebugClassify", $"Total panels: {totalPanels} across {totalLayers} layers");
        DumpLine("DebugClassify", $"Total navigation stops: {totalStops}");
        DumpLine("DebugClassify", $"Total containers: {totalContainers}");
        DumpLine("DebugClassify", $"Total unknown: {totalUnknown}");
        DumpLine("DebugClassify", "========== END ALL LAYERS CLASSIFICATION ==========");
        FlushDump("f10_classify.txt");

        SpeechPipeline.SpeakInterrupt($"{totalPanels} panels across {totalLayers} layers. {aggregateTreeStats.TotalElements} elements. {totalStops} stops, {totalContainers} containers, {totalUnknown} unknown.");
    }

    public static void DumpCursorContext()
    {
        var current = NavigationManager.GetCurrentElement();
        if (current == null)
        {
            Log.Warn("[DebugCursor] No current element.");
            SpeechPipeline.SpeakInterrupt("no cursor");
            return;
        }

        BeginDump();
        DumpLine("DebugCursor", "========== CURSOR CONTEXT ==========");
        DumpLine("DebugCursor", $"Current element: role={current.Role} announcement=\"{current.GetAnnouncement()}\"");

        if (current is Adapters.PluginAdapterBase adapter)
        {
            var el = adapter.BackingElement;
            var typeName = el.GetIl2CppType().Name;
            DumpLine("DebugCursor", $"Backing element: type={typeName} name=\"{el.name}\"");
            DumpLine("DebugCursor", $"Visible: {ElementBridge.IsVisible(el)}");
            DumpLine("DebugCursor", $"Resolved style: display={el.resolvedStyle.display} visibility={el.resolvedStyle.visibility} opacity={el.resolvedStyle.opacity}");
            DumpLine("DebugCursor", $"Layout: x={el.layout.x} y={el.layout.y} w={el.layout.width} h={el.layout.height}");

            DumpLine("DebugCursor", "---------- ANCESTOR CHAIN ----------");
            var ancestor = el.hierarchy.parent;
            int depth = 0;
            while (ancestor != null && depth < MaxTreeDepth)
            {
                var aType = ancestor.GetIl2CppType().Name;
                var aName = ancestor.name ?? "";
                var aVisible = ElementBridge.IsVisible(ancestor);
                DumpLine("DebugCursor", $"  [{depth}] type={aType} name=\"{aName}\" visible={aVisible} childCount={ancestor.hierarchy.childCount}");
                ancestor = ancestor.hierarchy.parent;
                depth++;
            }

            var parent = el.hierarchy.parent;
            if (parent != null)
            {
                DumpLine("DebugCursor", "---------- SIBLINGS ----------");
                int sibCount = parent.hierarchy.childCount;
                for (int i = 0; i < sibCount; i++)
                {
                    var sib = parent.hierarchy[i];
                    var sType = sib.GetIl2CppType().Name;
                    var sVisible = ElementBridge.IsVisible(sib);
                    var marker = sib.Pointer == el.Pointer ? " <<< CURRENT" : "";
                    var sText = TreeDump.NodeText(sib);
                    DumpLine("DebugCursor", $"  [{i}/{sibCount}] type={sType} name=\"{sib.name}\" visible={sVisible}{(sText != null ? $" text=\"{TreeDump.Truncate(sText, 60)}\"" : "")}{marker}");
                }
            }

            if (el.hierarchy.childCount > 0)
            {
                DumpLine("DebugCursor", "---------- CHILDREN OF CURRENT ----------");
                DumpChildrenFlat(el, 0, 3);
            }

            DumpLine("DebugCursor", "---------- NEARBY STOPS ----------");
            try
            {
                var cache = new ClassificationCache();
                var panelObj = NavigationManager.GetActivePanel();
                if (panelObj is VisualElement panelVe)
                {
                    TreeWalker.FindFirstStop(panelVe, cache);
                    var prev = TreeWalker.FindPreviousStop(el, cache);
                    var next = TreeWalker.FindNextStop(el, cache);
                    DumpLine("DebugCursor", $"  Previous: {(prev != null ? $"{prev.Role} \"{TreeDump.Truncate(prev.GetAnnouncement(), 80)}\"" : "(none)")}");
                    DumpLine("DebugCursor", $"  Next: {(next != null ? $"{next.Role} \"{TreeDump.Truncate(next.GetAnnouncement(), 80)}\"" : "(none)")}");
                }
            }
            catch (Exception ex)
            {
                DumpLine("DebugCursor", $"  Could not determine nearby stops: {ex.GetType().Name}: {ex.Message}");
            }
        }

        DumpLine("DebugCursor", "========== END CURSOR CONTEXT ==========");
        FlushDump("f10_cursor.txt");
        SpeechPipeline.SpeakInterrupt("cursor context dumped.");
    }

    public static void DumpUnclassifiedTracker()
    {
        if (UnclassifiedTracker.Entries.Count == 0)
        {
            Log.Info("[DebugUnclassified] No unclassified elements tracked since last clear.");
            SpeechPipeline.SpeakInterrupt("no unclassified elements tracked");
            return;
        }

        BeginDump();
        DumpLine("DebugUnclassified", "========== UNCLASSIFIED ELEMENT TRACKER ==========");
        DumpRaw(UnclassifiedTracker.RenderText());
        DumpLine("DebugUnclassified", "========== END UNCLASSIFIED TRACKER ==========");
        FlushDump("f11_unclassified.txt");

        SpeechPipeline.SpeakInterrupt($"{UnclassifiedTracker.Entries.Count} unclassified types.");
    }

    public static void ClearUnclassifiedTracker()
    {
        UnclassifiedTracker.Entries.Clear();
        Log.Info("[DebugUnclassified] Tracker cleared.");
        SpeechPipeline.SpeakInterrupt("unclassified tracker cleared");
    }

    // Phase 0 of the "remove panel cycling" plan (docs/archive/plans/removing panel cycling plan.md §10):
    // validate the interactive-set gate signal before any rendering work. For every open panel
    // across all layers it prints layer, root visible, Panel.IsVisible/IsActive, and the game's
    // own IsOpenAndInteractive(PanelID), then the computed interactive set. Run across the four
    // §12 fixtures and confirm only the truly-interactive panels qualify (modal -> just dialog;
    // card-over-report -> just card; plain squad -> title bar + squad + report; ghost excluded).
    // Diagnostic only: it reports a thrown gate call rather than crashing, so we can SEE whether
    // the signal is reliable. Nothing here feeds speech.
    public static void DumpAdHoc()
    {
        if (!SI.Bindable.PanelManager.Exists())
        {
            Log.Warn("[DebugInteractive] PanelManager not available.");
            SpeechPipeline.SpeakInterrupt("panel manager not available");
            return;
        }

        var pm = SI.Bindable.PanelManager.Instance;
        var layers = FM27PanelCatalog.GetLayers(pm);

        var interactiveSet = new List<string>();
        int totalOpen = 0;

        BeginDump();
        DumpLine("DebugInteractive", "========== INTERACTIVE PANEL GATE READOUT ==========");

        for (int i = 0; i < layers.Count; i++)
        {
            var layerInfo = layers[i];
            var layerEnum = layerInfo.Layer;
            var openPanels = layerInfo.Open;
            if (openPanels == null || openPanels.Count == 0) continue;

            DumpLine("DebugInteractive", $"---------- LAYER: {layerEnum} ({i}) ----------");

            for (int p = 0; p < openPanels.Count; p++)
            {
                var panel = openPanels[p];
                if (panel == null) continue;

                totalOpen++;
                var title = GetPanelTitle(panel);

                bool rootVisible;
                try { rootVisible = ElementBridge.IsVisible(panel); }
                catch (Exception ex) { rootVisible = false; DumpLine("DebugInteractive", $"  IsVisible(root) threw: {ex.Message}"); }

                bool panelIsVisible, panelIsActive;
                try { panelIsVisible = panel.IsVisible; } catch { panelIsVisible = false; }
                try { panelIsActive = panel.IsActive; } catch { panelIsActive = false; }

                // TakesFocusOnOpen drives the game's HighestLayerIndex: IsOpenAndInteractive is
                // false for any panel below the highest layer holding a focus-taking panel. This
                // column explains why the chrome below a Report/Card reads as non-interactive.
                bool takesFocus;
                try { takesFocus = SI.UI.NavigationManager.Instance.GetVisualElementNavigationSortOrder(panel) == SI.UI.NavigationManager.Instance.HighestNavigationSortOrder; } catch { takesFocus = false; }

                string interactive;
                bool isInteractive = false;
                try
                {
                    isInteractive = FM27PanelCatalog.IsOpenAndInteractive(pm,panel.PanelID);
                    interactive = isInteractive.ToString();
                }
                catch (Exception ex) { interactive = $"THREW: {ex.GetType().Name}: {ex.Message}"; }

                DumpLine("DebugInteractive",
                    $"  PANEL \"{title}\" name=\"{panel.name}\" rootVisible={rootVisible} "
                    + $"Panel.IsVisible={panelIsVisible} Panel.IsActive={panelIsActive} "
                    + $"TakesFocusOnOpen={takesFocus} IsOpenAndInteractive={interactive}");

                // The proposed gate (§6.1): on a content layer, root visible, and interactive.
                if (isInteractive && rootVisible)
                    interactiveSet.Add($"{title} ({layerEnum})");
            }
        }

        DumpLine("DebugInteractive", "---------- COMPUTED INTERACTIVE SET (visible && IsOpenAndInteractive) ----------");
        if (interactiveSet.Count == 0)
            DumpLine("DebugInteractive", "  (empty)");
        else
            foreach (var entry in interactiveSet)
                DumpLine("DebugInteractive", $"  {entry}");

        DumpLine("DebugInteractive", $"========== {totalOpen} open panels, {interactiveSet.Count} interactive ==========");
        FlushDump("f12_interactive.txt");

        SpeechPipeline.SpeakInterrupt($"{totalOpen} open panels, {interactiveSet.Count} interactive.");
    }

    // ---- File output helpers ----

    private static void BeginDump() => _dumpBuffer = new StringBuilder();

    // Headers and summaries: echoed to Player.log so the log shows the shape of a dump, and
    // buffered into the file. The bulk tree/classification bodies go through DumpRaw (file only) —
    // logging thousands of element lines per F9 floods Player.log without adding anything the file
    // does not already hold.
    private static void DumpLine(string tag, string line)
    {
        Log.Info($"[{tag}] {line}");
        _dumpBuffer.Append(line).Append('\n');
    }

    private static void DumpRaw(string block) => _dumpBuffer.Append(block);

    private static void FlushDump(string fileName)
    {
        Directory.CreateDirectory(DumpDir);
        File.WriteAllText(Path.Combine(DumpDir, fileName), _dumpBuffer.ToString());
        _dumpBuffer = null;
    }

    // ---- Private helpers ----

    private static void DumpChildrenFlat(VisualElement parent, int currentDepth, int maxDepth)
    {
        if (currentDepth > maxDepth) return;
        var indent = new string(' ', currentDepth * 2);
        for (int i = 0; i < parent.hierarchy.childCount; i++)
        {
            var child = parent.hierarchy[i];
            var typeName = child.GetIl2CppType().Name;
            var visible = ElementBridge.IsVisible(child);
            var text = TreeDump.NodeText(child);
            var textFrag = text != null ? $" text=\"{TreeDump.Truncate(text, 60)}\"" : "";
            DumpLine("DebugCursor", $"    {indent}[{i}] {typeName} name=\"{child.name}\" visible={visible} children={child.hierarchy.childCount}{textFrag}");
            DumpChildrenFlat(child, currentDepth + 1, maxDepth);
        }
    }

    private static string GetPanelTitle(SI.Bindable.Panel panel)
    {
        try
        {
            return panel.PanelID.Title.TranslatedString ?? panel.PanelID.Title.Text ?? "(untitled)";
        }
        catch
        {
            Log.Warn($"[Debug] Could not read title for panel \"{panel.name}\"");
            return "(error reading title)";
        }
    }
}
