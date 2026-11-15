using System.Text;
using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The game parks summary statistics in a card's tab row (the staff responsibilities card
/// keeps "27 / Under control / 31 / Delegated to Staff" there) as value+label SIText pairs,
/// which the generic walk renders as four separate lines. This renderer consolidates every
/// pair in that strip into ONE line — "27 Under control, 31 Delegated to Staff" — rendered
/// at the first encountered pair text; the remaining pair texts render nothing. Every native
/// value/caption stays tracked, including a value that bound after the initial walk.
///
/// Recognition is structural: an SIText inside a two-SIText frame under the tab bar's
/// ExtraTabTemplate slot. HTML surface only; the speech cursor still visits each text.
/// </summary>
internal sealed class TabRowStatsRenderer : IElementRenderer
{
    private const string TabExtraSlot = "ExtraTabTemplate";
    private const int MaxAncestorDepth = 8;
    private static readonly HashSet<IntPtr> EmittedStrips = new();

    // Main-thread render-pass state, reset alongside the existing HTML/value pools.
    internal static void BeginBody() => EmittedStrips.Clear();

    public bool CanRender(IAccessibleElement el)
    {
        if (ServerText.SafeRole(el) != "text") return false;
        if (el is not Adapters.PluginAdapterBase a) return false;
        var backing = a.BackingElement;
        try
        {
            var pair = backing.hierarchy.parent;
            return pair != null && IsPairFrame(pair) && UnderTabExtraSlot(pair);
        }
        catch (Exception ex)
        {
            Log.Warn($"[TabRowStats] Structure read failed, using generic rendering: {ex.Message}");
            return false;
        }
    }

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        var backing = ((Adapters.PluginAdapterBase)el).BackingElement;
        try
        {
            var pair = backing.hierarchy.parent;
            var strip = pair.hierarchy.parent;

            // The first value can be absent from the walked stops while FM27
            // is binding, even though it exists by this later render. Emit at
            // whichever real pair text survived, once per native strip. Managed
            // wrapper identity must never decide whether all statistics vanish.
            if (!EmittedStrips.Add(strip.Pointer)) return;

            sb.Append("<p>");
            bool first = true;
            int n = strip.hierarchy.childCount;
            for (int i = 0; i < n; i++)
            {
                var f = strip.hierarchy[i];
                if (!IsPairFrame(f)) continue;
                var value = f.hierarchy[0];
                var label = f.hierarchy[1];
                // Both halves can finish binding after the walk/render. Keep
                // their native text live instead of baking a blank caption.
                var adapter = ElementBridge.CreateAdapter(value, ElementClassification.Text);
                string vid = LiveValues.RegisterReadOnly(adapter);
                string lid = LiveValues.RegisterReadOnly(ElementBridge.CreateAdapter(label, ElementClassification.Text));
                if (!first) sb.Append(", ");
                sb.Append("<span id=\"").Append(vid).Append("\">")
                  .Append(ServerText.EscapeText(ReadText(value))).Append("</span> ")
                  .Append("<span id=\"").Append(lid).Append("\">")
                  .Append(ServerText.EscapeText(ReadText(label))).Append("</span>");
                first = false;
            }
            sb.Append("</p>");
        }
        catch (Exception ex)
        {
            // Degrade to the pre-merge presentation for this one text (its pair siblings
            // suppress independently, so the worst case is a plain value or label line);
            // the next body refresh re-reads everything.
            Log.Warn($"[TabRowStats] Combined render failed, falling back to plain text: {ex.Message}");
            sb.Append("<p>").Append(ServerText.Escape(DisplayText.Of(el))).Append("</p>");
        }
    }

    // A frame holding exactly one value+label SIText pair.
    private static bool IsPairFrame(VisualElement f)
    {
        if (f.hierarchy.childCount != 2) return false;
        return f.hierarchy[0].GetIl2CppType().Name == "SIText"
            && f.hierarchy[1].GetIl2CppType().Name == "SIText";
    }

    private static bool UnderTabExtraSlot(VisualElement el)
    {
        var p = el.hierarchy.parent;
        for (int up = 0; p != null && up < MaxAncestorDepth; up++)
        {
            if (p.name == TabExtraSlot) return true;
            p = p.hierarchy.parent;
        }
        return false;
    }

    private static string ReadText(VisualElement el) => el.Cast<SI.Bindable.SIText>().text ?? "";
}
