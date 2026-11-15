using UnityEngine.UIElements;
using FM27Access.Navigation;

namespace FM27Access.Plugin.Adapters;

internal class RadialGaugeAdapter : PluginAdapterBase
{
    public RadialGaugeAdapter(VisualElement element) : base(element)
    {
        PrivateFieldCache.EnsureInitialized();
    }

    public override string Role => "gauge";
    public override bool CanActivate => false;

    // The coach-workload component uses a styled circular glyph beside its
    // authoritative, localised workload word. Its gauge is fixed artwork (all
    // categories can have the same fill while their actual workloads differ).
    // Suppress only that glyph while its own native caption is available.
    internal static bool IsCaptionedWorkloadArtwork(VisualElement element)
    {
        if (element == null || !string.IsNullOrWhiteSpace(element.tooltip)) return false;
        if (TryCountedWorkloadParts(element.parent?.parent?.parent,
                out _, out _, out var countedGlyph) && countedGlyph.Pointer == element.Pointer)
            return true;
        var style = element.parent;
        var frame = style?.parent;
        var component = frame?.parent;
        if (style?.GetIl2CppType().Name != "SIStyleSetter"
            || component?.GetIl2CppType().Name != "ComponentElement"
            || component.hierarchy.childCount != 2) return false;
        var caption = component.hierarchy[1];
        var name = caption.name ?? "";
        return caption.GetIl2CppType().Name == "SIText"
            && name.StartsWith("coach-", StringComparison.Ordinal)
            && name.EndsWith("-workload-text", StringComparison.Ordinal)
            && ElementBridge.IsVisible(caption)
            && !string.IsNullOrWhiteSpace(caption.TryCast<TextElement>()?.text);
    }

    // The native coach-assignment tile's category legend is [count, styled
    // radial glyph, severity caption]. Its fixed fills depict the categories;
    // the actual data is the count paired with the game's own severity word.
    // Match the shared asset and exact leaf schema, not caption translations or
    // a particular screen/slot. Other meters and informative help stay intact.
    internal static bool TryCountedWorkloadParts(VisualElement row,
        out VisualElement count, out VisualElement caption, out VisualElement glyph)
    {
        count = caption = glyph = null;
        if (row?.panel == null || !ElementBridge.IsVisibleInHierarchy(row)
            || row.name?.EndsWith("_chart", StringComparison.Ordinal) != true) return false;
        var children = new List<VisualElement>();
        for (int i = 0; i < row.hierarchy.childCount; i++)
            if (ElementBridge.IsVisible(row.hierarchy[i])) children.Add(row.hierarchy[i]);
        if (children.Count != 3 || children[0].name != "count" || children[2].name != "text"
            || children[0].GetIl2CppType().Name != "SIText"
            || children[2].GetIl2CppType().Name != "SIText"
            || children[1].GetIl2CppType().Name != "VisualElement") return false;
        var style = SingleVisibleChild(children[1]);
        if (style?.GetIl2CppType().Name != "SIStyleSetter") return false;
        var gauge = SingleVisibleChild(style);
        if (gauge?.GetIl2CppType().Name != "SIRadialGauge"
            || !string.IsNullOrWhiteSpace(gauge.tooltip)
            || !string.IsNullOrWhiteSpace(style.tooltip)
            || !string.IsNullOrWhiteSpace(children[1].tooltip)
            || !string.IsNullOrWhiteSpace(children[2].tooltip)
            || SITextLink.HasLink(children[0].TryCast<TextElement>()?.text)
            || SITextLink.HasLink(children[2].TryCast<TextElement>()?.text)) return false;
        int depth = 0;
        for (var owner = row.parent; owner != null && depth++ < 16; owner = owner.parent)
        {
            if (owner.TryCast<TemplateContainer>()?.templateSource?.name != "CoachAssignmentsTile_4x2")
                continue;
            count = children[0]; caption = children[2]; glyph = gauge;
            return true;
        }
        return false;
    }

    private string Caption()
    {
        var caption=PairedCaption;
        if(caption!=null)return Server.ServerText.Clean(caption.TryCast<TextElement>()?.text);
        return FindFieldLabel() ?? FindChildText();
    }

    public override string GetLabel() => Caption() ?? "Gauge";

    // Shared inline meters either pair [radial, SIText caption], or put the
    // radial in a single style wrapper beside [styled caption, explanation].
    // Follow only exact single-child wrappers in that second shape; the
    // explanation remains a separate stop. Never borrow another row's text.
    internal VisualElement PairedCaption
    {
        get
        {
            var parent=Element.parent;if(parent==null)return null;
            VisualElement caption=null;int visible=0;
            for(int i=0;i<parent.hierarchy.childCount;i++)
            {
                var child=parent.hierarchy[i];if(!ElementBridge.IsVisible(child))continue;
                visible++;if(child.Pointer!=Element.Pointer)caption=child;
            }
            if(visible==1&&parent.GetIl2CppType().Name=="SIStyleSetter")
            {
                var owner=parent.parent;if(owner==null)return null;
                VisualElement sibling=null;int peers=0;
                for(int i=0;i<owner.hierarchy.childCount;i++)
                {
                    var child=owner.hierarchy[i];if(!ElementBridge.IsVisible(child))continue;
                    peers++;if(child.Pointer!=parent.Pointer)sibling=child;
                }
                // A field footer can add its own help icon and heading before
                // this same styled gauge/status pair. Keep that exact four-item
                // native structure; never scan another row for a caption.
                if(peers==4)
                {
                    var children=new List<VisualElement>();
                    for(int i=0;i<owner.hierarchy.childCount;i++)
                        if(ElementBridge.IsVisible(owner.hierarchy[i]))children.Add(owner.hierarchy[i]);
                    if(children[0].GetIl2CppType().Name!="TemplateContainer"
                        ||children[1].GetIl2CppType().Name!="SIText"
                        ||children[2].Pointer!=parent.Pointer
                        ||children[3].GetIl2CppType().Name!="SIText")return null;
                    sibling=children[3];
                }
                else if(peers!=2||sibling==null)return null;
                // FM27 also pairs a single styled radial directly with its native
                // SIText value (for example the processing estimate). Keep that
                // exact pair, rather than borrowing the separate field heading.
                if(sibling.GetIl2CppType().Name=="SIText")caption=sibling;
                else
                {
                    var frame=SingleVisibleChild(sibling);
                    if(frame?.GetIl2CppType().Name!="VisualElement"||frame.hierarchy.childCount!=2)return null;
                    var label=frame.hierarchy[0];var explanation=frame.hierarchy[1];
                    if(label.GetIl2CppType().Name!="SIStyleSetter"||!ElementBridge.IsVisible(label)
                        ||!ElementBridge.IsVisible(explanation))return null;
                    caption=SingleText(label);
                    if(SingleText(explanation)==null)return null;
                }
            }
            else if(visible!=2)return null;
            if(caption?.GetIl2CppType().Name!="SIText")return null;
            var text=caption.TryCast<TextElement>()?.text;
            return !string.IsNullOrWhiteSpace(text)&&text.Any(char.IsLetter)&&!SITextLink.HasLink(text)?caption:null;
        }
    }

    private static VisualElement SingleVisibleChild(VisualElement owner)
    {
        VisualElement only=null;
        for(int i=0;i<owner.hierarchy.childCount;i++)
        {
            var child=owner.hierarchy[i];if(!ElementBridge.IsVisible(child))continue;
            if(only!=null)return null;only=child;
        }
        return only;
    }

    private static VisualElement SingleText(VisualElement owner)
    {
        for(int depth=0;owner!=null&&depth<5;depth++)
        {
            var type=owner.GetIl2CppType().Name;
            if(type=="SIText")return SITextLink.HasLink(owner.TryCast<TextElement>()?.text)?null:owner;
            if(type is not ("VisualElement" or "SIStyleSetter"))return null;
            owner=SingleVisibleChild(owner);
        }
        return null;
    }

    internal override IReadOnlyList<string> FoldedTexts()=>PairedCaption is { } caption
        ?new[]{Server.ServerText.Clean(caption.TryCast<TextElement>()?.text)}:Array.Empty<string>();

    internal static void FoldPairedCaptions(List<IAccessibleElement> stops)
    {
        HashSet<IntPtr> folded=null;
        foreach(var stop in stops)
            if(stop is RadialGaugeAdapter gauge&&gauge.PairedCaption is { } caption)
                (folded??=new()).Add(caption.Pointer);
        if(folded!=null)stops.RemoveAll(stop=>!stop.CanActivate&&stop is PluginAdapterBase native
            &&stop.Role=="text"&&folded.Contains(native.BackingElement.Pointer));
    }

    public override string GetState()
    {
        var gauge = Element.Cast<SI.Bindable.SIRadialGauge>();
        var current = (float)PrivateFieldCache.GaugeCurrent.GetValue(gauge);
        var min = (int)PrivateFieldCache.GaugeMinValue.GetValue(gauge);
        var max = (int)PrivateFieldCache.GaugeMaxValue.GetValue(gauge);
        return $"{(int)current} of {min} to {max}";
    }

    // This read-only value is plain text in the inherited HTML. Keep the
    // native caption and full current/min/max value without repeating the
    // implementation role ("Gauge, gauge") on either accessible surface.
    public override string GetAnnouncement() => Caption() is { Length: > 0 } caption
        ? $"{caption}{(caption.EndsWith(":", StringComparison.Ordinal) ? " " : ": ")}{GetState()}" : GetState();

    public override void Activate() =>
        throw new NotSupportedException("Gauges cannot be activated.");
}
