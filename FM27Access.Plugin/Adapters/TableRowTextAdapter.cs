using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace FM27Access.Plugin.Adapters;

internal class TableRowTextAdapter : PluginAdapterBase
{
    public TableRowTextAdapter(VisualElement element) : base(element)
    {
    }

    public override string Role => "button";
    public override bool CanActivate => true;

    public override string GetLabel() => Element.Cast<TextElement>().text;
    public override string GetState() => "";

    public override string GetAnnouncement()
    {
        var label = Speech.TextFilters.ExtractLinkText(GetLabel());
        return $"{label}, button";
    }

    /// <summary>
    /// Whether the cell's link markup is present, i.e. the row's embedded-data records match the
    /// name it is currently displaying. A virtualised row that has just been scrolled back in sets
    /// its text before the engine re-applies the styling that carries the link, and in that window
    /// the element shows the new person while its records still address the previous one. Callers
    /// that force a row to realise (the companion's cell handle) must wait for this before acting,
    /// or they open someone else's screen while announcing the right name.
    /// </summary>
    internal bool LinkReady => SITextLink.HasLink(Element.Cast<TextElement>().text);

    public override void Activate()
    {
        // The clickable text is an SIText embedded <link>, not a button; SITextLink builds the
        // link-tag event pair the engine would otherwise only synthesize from a real glyph hit-test.
        // Priming the row-focus/hover state first registers the link callbacks and marks the link
        // active so the up event's action fires — table-row specific, so it stays here.
        var si = new SI.Bindable.SIText(Element.Pointer);
        if (FindTableRow(Element) != null)
            si.SetHoverLinkOnTableCellFocusedStateChanged(true);

        if (SITextLink.Activate(Element)) return;

        // No markup to fire: the row is mid-rebind (or this text never carried a link). Firing a
        // guessed link id here is what opened the wrong person's screen, so say nothing happened.
        Speech.Degrade.Note($"table row link not bound yet: \"{Speech.TextFilters.ExtractLinkText(GetLabel())}\"");
        Speech.SpeechPipeline.SpeakInterrupt(Speech.Degrade.Unavailable);
    }

    internal static VisualElement FindTableRow(VisualElement element)
    {
        for(var ancestor=element.hierarchy.parent;ancestor!=null;ancestor=ancestor.hierarchy.parent)
        {
            var table=ancestor.TryCast<SI.Bindable.StreamedTable>();
            if(table!=null)return table.GetRowContaining(element);
        }
        var current = element.hierarchy.parent;
        while (current != null)
        {
            if (current.GetIl2CppType().Name == "TableRowNavigatable")
                return current;
            current = current.hierarchy.parent;
        }
        return null;
    }
}
