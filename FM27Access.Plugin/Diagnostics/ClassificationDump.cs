using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Diagnostics;

/// <summary>
/// Runs the mod's real classification (<see cref="ElementBridge.Describe"/> +
/// <see cref="ElementClassifier.Classify"/>) over a panel and sorts the result into the buckets a
/// reader cares about: navigation stops (with the announcement the player would hear), containers,
/// layout wrappers, unknowns, and hidden subtrees. This is the single most actionable view — "what
/// can the player reach on this screen, and what is being missed."
///
/// Shared by F9's f10_classify.txt file dump (all open panels) and the dev driver's /classify
/// endpoint (the active panel, returned inline over HTTP). The raw structural view is the separate
/// concern of <see cref="TreeDump"/>.
/// </summary>
internal static class ClassificationDump
{
    private const int MaxDepth = 200;

    public sealed class Report
    {
        public readonly List<NavigationStopInfo> NavigationStops = new();
        public readonly List<ContainerInfo> Containers = new();
        public readonly List<UnknownElementInfo> UnknownElements = new();
        public readonly List<ElementInfo> HiddenSubtreeRoots = new();
        public readonly Dictionary<string, int> LayoutWrapperTypes = new();
        public readonly Dictionary<ElementClassification, int> ClassificationCounts = new();
        public int InternalChildCount;
    }

    public struct NavigationStopInfo
    {
        public ElementClassification Classification;
        public string TypeName;
        public string ElementName;
        public string TextContent;
        public string Announcement;
        public string AncestorPath;
    }

    public struct ContainerInfo
    {
        public ElementClassification Classification;
        public string TypeName;
        public string ElementName;
        public int ChildCount;
        public string AncestorPath;
    }

    public struct UnknownElementInfo
    {
        public string TypeName;
        public string ElementName;
        public string[] Interfaces;
        public string[] UssClasses;
        public string AncestorPath;
    }

    public struct ElementInfo
    {
        public string TypeName;
        public string ElementName;
        public int ChildCount;
    }

    /// <summary>Classify every visible element under <paramref name="panel"/> into a report.</summary>
    public static Report Classify(VisualElement panel)
    {
        var report = new Report();
        ClassifyRecursive(panel, 0, null, report);
        return report;
    }

    /// <summary>Render one panel's report into the file/HTTP section format.</summary>
    public static void RenderPanel(StringBuilder sb, Report report)
    {
        sb.Append("---------- NAVIGATION STOPS ----------\n");
        foreach (var stop in report.NavigationStops)
            sb.Append($"  [{stop.Classification}] type={stop.TypeName} name=\"{stop.ElementName}\" text=\"{stop.TextContent}\" announcement=\"{stop.Announcement}\" path={stop.AncestorPath}\n");

        sb.Append("---------- CONTAINERS ----------\n");
        foreach (var c in report.Containers)
            sb.Append($"  [{c.Classification}] type={c.TypeName} name=\"{c.ElementName}\" children={c.ChildCount} path={c.AncestorPath}\n");

        sb.Append("---------- LAYOUT WRAPPERS ----------\n");
        foreach (var kvp in report.LayoutWrapperTypes.OrderByDescending(k => k.Value))
            sb.Append($"  {kvp.Key}: {kvp.Value}\n");

        sb.Append("---------- UNCLASSIFIED / UNKNOWN ----------\n");
        if (report.UnknownElements.Count == 0)
        {
            sb.Append("  (none)\n");
        }
        else
        {
            foreach (var u in report.UnknownElements)
                sb.Append($"  type={u.TypeName} name=\"{u.ElementName}\" interfaces=[{string.Join(", ", u.Interfaces)}] uss=[{string.Join(", ", u.UssClasses)}] path={u.AncestorPath}\n");
        }

        sb.Append("---------- HIDDEN SUBTREES ----------\n");
        foreach (var h in report.HiddenSubtreeRoots.Take(50))
            sb.Append($"  type={h.TypeName} name=\"{h.ElementName}\" children={h.ChildCount}\n");

        sb.Append("--- PANEL SUMMARY ---\n");
        sb.Append($"Navigation stops: {report.NavigationStops.Count}\n");
        sb.Append($"Containers: {report.Containers.Count}\n");
        sb.Append($"Layout wrappers: {report.LayoutWrapperTypes.Values.Sum()}\n");
        sb.Append($"Unknown/unclassified: {report.UnknownElements.Count}\n");
        sb.Append($"Internal children: {report.InternalChildCount}\n");
        sb.Append($"Hidden subtree roots: {report.HiddenSubtreeRoots.Count}\n");
        sb.Append("Classifications by type:\n");
        foreach (var kvp in report.ClassificationCounts.OrderByDescending(k => k.Value))
            sb.Append($"  {kvp.Key}: {kvp.Value}\n");
    }

    private static void ClassifyRecursive(
        VisualElement element, int depth, ElementClassification? parentClassification, Report report)
    {
        if (depth > MaxDepth) return;

        if (!ElementBridge.IsVisible(element))
        {
            if (depth < 10 && element.hierarchy.childCount > 0)
            {
                report.HiddenSubtreeRoots.Add(new ElementInfo
                {
                    TypeName = element.GetIl2CppType().Name,
                    ElementName = element.name ?? "",
                    ChildCount = element.hierarchy.childCount,
                });
            }
            return;
        }

        ElementClassification classification;
        try
        {
            // Classify through the same entry point the live walkers use (ClassificationCache →
            // ElementBridge.ClassifyElement), not the raw ElementClassifier: the Plugin-layer
            // promotions there (SpeakToPanel, attribute rows, context-menu triggers, tactics-pitch
            // suppression) change what IS a stop, and a dump that skips them reports a different
            // screen than the one the player navigates.
            classification = ElementBridge.ClassifyElement(element, parentClassification);

            report.ClassificationCounts.TryGetValue(classification, out int cc);
            report.ClassificationCounts[classification] = cc + 1;

            var ancestorPath = BuildAncestorPath(element, 4);

            if (classification == ElementClassification.Unknown)
            {
                var descriptor = ElementBridge.Describe(element, parentClassification);
                report.UnknownElements.Add(new UnknownElementInfo
                {
                    TypeName = descriptor.TypeName,
                    ElementName = element.name ?? "",
                    Interfaces = descriptor.InterfaceNames.ToArray(),
                    UssClasses = descriptor.UssClasses.ToArray(),
                    AncestorPath = ancestorPath,
                });
                return;
            }

            if (classification == ElementClassification.InternalChild)
            {
                report.InternalChildCount++;
                return;
            }

            if (TreeWalker.NavigationStops.Contains(classification))
            {
                var textContent = TreeDump.NodeText(element) ?? "";
                string announcement = "";
                try
                {
                    var adapter = ElementBridge.CreateAdapter(element, classification);
                    announcement = adapter.GetAnnouncement();
                }
                // A container-style stop (SpeakToPanel) has a context, not an adapter; it lists
                // with no announcement of its own and its inner stops follow from the recursion.
                catch (ArgumentException) { }
                // A single stop whose announcement throws must not abort the whole report; it
                // lists with an empty announcement, which itself flags the broken stop.
                catch (Exception ex) { Log.Warn($"[ClassificationDump] Announcement failed for {element.name}: {ex.Message}"); }

                report.NavigationStops.Add(new NavigationStopInfo
                {
                    Classification = classification,
                    TypeName = element.GetIl2CppType().Name,
                    ElementName = element.name ?? "",
                    TextContent = TreeDump.Truncate(textContent, 60),
                    Announcement = TreeDump.Truncate(announcement, 100),
                    AncestorPath = ancestorPath,
                });
                // Fall through to the child loop: the cursor's walk (TreeWalker) keeps recursing
                // after recording a stop, so stops nested inside another stop (the Quick Chat
                // answer buttons inside the side-panel wrapper SIButton) are reachable in-game and
                // must appear here too. The stop's classification propagates as the parent, so its
                // own texts/images still fold away as InternalChild rather than listing as stops.
            }

            if (TreeWalker.ContainerClassifications.Contains(classification))
            {
                report.Containers.Add(new ContainerInfo
                {
                    Classification = classification,
                    TypeName = element.GetIl2CppType().Name,
                    ElementName = element.name ?? "",
                    ChildCount = element.hierarchy.childCount,
                    AncestorPath = ancestorPath,
                });
            }

            if (classification == ElementClassification.LayoutWrapper)
            {
                var tn = element.GetIl2CppType().Name;
                report.LayoutWrapperTypes.TryGetValue(tn, out int lwc);
                report.LayoutWrapperTypes[tn] = lwc + 1;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[ClassificationDump] Exception classifying element: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        for (int i = 0; i < element.hierarchy.childCount; i++)
            ClassifyRecursive(element.hierarchy[i], depth + 1, classification, report);
    }

    private static string BuildAncestorPath(VisualElement element, int levels)
    {
        var parts = new List<string>();
        var current = element.hierarchy.parent;
        for (int i = 0; i < levels && current != null; i++)
        {
            var tn = current.GetIl2CppType().Name;
            var n = current.name;
            parts.Add(string.IsNullOrEmpty(n) ? tn : $"{tn}(\"{n}\")");
            current = current.hierarchy.parent;
        }
        parts.Reverse();
        return string.Join(" > ", parts);
    }
}
