using System.Collections.Generic;
using System.Text;
using FM27Access.Plugin.Adapters;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only census of the images on the rendered panels: every visible SIImage, tallied by its
/// element name and resolved sprite name, so an icon the surface reads as "image" (or nothing) can be
/// identified and its sprite family mapped. <c>?name=X</c> lists each occurrence of images named X
/// with the texts beside it (its parent's text leaves), to tie a sprite to the row it sits in.
/// GET /spritecensus. Read-only.
/// </summary>
internal static class SpriteCensus
{
    private const int Budget = 60000;

    internal static string Run(string name)
    {
        var tally = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
        var detail = new StringBuilder();
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel is not VisualElement root) continue;
            int budget = 0;
            Walk(root, name, tally, detail, ref budget);
        }

        var sb = new StringBuilder();
        foreach (var kv in tally)
            sb.Append(kv.Value.ToString().PadLeft(5)).Append("  ").Append(kv.Key).Append('\n');
        if (name != null) sb.Append("-- occurrences of \"").Append(name).Append("\":\n").Append(detail);
        return sb.Length > 0 ? sb.ToString() : "no images on the rendered panels\n";
    }

    private static void Walk(VisualElement el, string name, SortedDictionary<string, int> tally, StringBuilder detail, ref int budget)
    {
        if (budget++ > Budget || !ElementBridge.IsVisible(el)) return;
        if (name == "tooltips")
        {
            string tip = el.tooltip;
            if (!string.IsNullOrEmpty(tip))
                detail.Append("  ").Append(el.GetIl2CppType().Name).Append(" \"").Append(el.name).Append("\" tooltip=\"").Append(tip).Append("\"\n");
        }
        if (el.GetIl2CppType().Name == "SIImage")
        {
            string sprite = SpriteHelper.ReadSpriteName(el) ?? "(none)";
            string key = (string.IsNullOrEmpty(el.name) ? "(unnamed)" : el.name) + " -> " + sprite;
            tally[key] = tally.TryGetValue(key, out int n) ? n + 1 : 1;
            if (name != null && el.name == name)
            {
                detail.Append("  ").Append(sprite).Append("  | ");
                var texts = new List<string>();
                CollectTexts(el.hierarchy.parent, texts, 0);
                detail.Append(string.Join(" | ", texts)).Append('\n');
            }
        }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            Walk(el.hierarchy[i], name, tally, detail, ref budget);
    }

    private static void CollectTexts(VisualElement el, List<string> into, int depth)
    {
        if (el == null || depth > 4 || into.Count > 6) return;
        var te = el.TryCast<TextElement>();
        if (te != null && !string.IsNullOrWhiteSpace(te.text)) { into.Add(te.text.Trim()); return; }
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectTexts(el.hierarchy[i], into, depth + 1);
    }
}
