using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class ProgressBarAdapter : PluginAdapterBase
{
    public ProgressBarAdapter(VisualElement element) : base(element) { }

    public override string Role => "progress bar";
    public override bool CanActivate => false;

    // FM27 can print the actual formatted percentage beside its drawn bar.
    // Drawing dimensions include layout/minimum widths and are not that value.
    // Reuse the same own-caption pairing principle as RadialGaugeAdapter.
    private VisualElement PairedPercentage
    {
        get
        {
            var parent=Element.parent;if(parent==null)return null;
            VisualElement caption=null;int visible=0;
            for(int i=0;i<parent.hierarchy.childCount;i++)
            {
                var child=parent.hierarchy[i];if(!ElementBridge.IsVisible(child))continue;
                visible++;if(child.Pointer==Element.Pointer)continue;
                if(child.GetIl2CppType().Name=="SIText")
                {
                    var text=child.TryCast<TextElement>()?.text;
                    if(caption!=null||SITextLink.HasLink(text)
                        ||!ProgressCaptionValue.IsPercentage(Server.ServerText.Clean(text)))return null;
                    caption=child;
                }
                else if(child.GetIl2CppType().Name!="VisualElement"||child.hierarchy.childCount!=0
                    ||!string.IsNullOrWhiteSpace(child.tooltip))return null;
            }
            return visible is 2 or 3 ? caption : null;
        }
    }

    private string PercentageText(VisualElement caption)=>
        Server.ServerText.Clean(caption?.TryCast<TextElement>()?.text);

    private string PercentageFieldLabel()=>new FieldLabelReader(Element.parent).GetLabel();

    public override string GetLabel()
    {
        var budget=Server.BudgetTileReadout.IndicatorCaption(Element);
        if(budget!=null)return budget;
        if(PairedPercentage!=null)
        {
            var field=PercentageFieldLabel();
            if(!string.IsNullOrWhiteSpace(field)&&!ProgressCaptionValue.IsPercentage(Server.ServerText.Clean(field)))return field.TrimEnd().TrimEnd(':').TrimEnd();
        }
        // A meter sitting in a settings/form row takes that row's field label as its name — the
        // Preferences "Your device's Graphics Capability" label that precedes the capability bar —
        // ahead of its own child text or the (template-id) element name.
        var label = FindFieldLabel() ?? FindChildText();
        if (!string.IsNullOrEmpty(label)) return label;
        var name = Element.name;
        return !string.IsNullOrEmpty(name) && !name.Contains('-') && !LooksLikeTemplateId(name)
            ? name : "Progress";
    }

    internal override System.Collections.Generic.IReadOnlyList<string> FoldedTexts()
    {
        if(Server.BudgetTileReadout.IndicatorCaption(Element)!=null)return System.Array.Empty<string>();
        if(PairedPercentage is { } percentage)
        {
            var field=PercentageFieldLabel();var value=PercentageText(percentage);
            return string.IsNullOrWhiteSpace(field)||ProgressCaptionValue.IsPercentage(Server.ServerText.Clean(field))
                ? new[]{value}:new[]{field,value};
        }
        // When the bar adopts a preceding field label as its name, that label is also walked as a
        // standalone Text stop just before it; fold it so the walker drops the now-duplicate line.
        var fl = FindFieldLabel();
        return string.IsNullOrEmpty(fl) ? System.Array.Empty<string>() : new[] { fl };
    }

    public override string GetState()
    {
        if(PairedPercentage is { } percentage)return PercentageText(percentage);
        // SIProgressBar has no public value getter. Read the inner bar's width percentage.
        var progressBar = Element.Cast<SI.Bindable.SIProgressBar>();
        var bar = progressBar.ProgressBar;
        var barWidth = bar.resolvedStyle.width;
        var parentWidth = Element.resolvedStyle.width;

        if (parentWidth > 0)
        {
            var percent = (int)(barWidth / parentWidth * 100);
            return $"{percent}%";
        }

        return FM27Access.Speech.Degrade.Read("native progress indicator has no measurable fill or percentage caption");
    }

    public override string GetAnnouncement()
    {
        // Speak the field/child label alongside the bar's value so the speech surface matches the
        // HTML ("Your device's Graphics Capability, progress bar, 80%"). A bare bar whose only name
        // is a template-id element name announces its value alone rather than leaking the dev id.
        var label = GetLabel();
        var state = GetState();
        return string.IsNullOrEmpty(label) ? $"{Role}, {state}" : $"{label}, {Role}, {state}";
    }

    public override string GetDescription()=>Server.BudgetTileReadout.IndicatorCaption(Element)!=null
        ? "Fill of the game's budget indicator. Read the labelled monetary amounts for available budget and spending."
        : base.GetDescription();

    public override void Activate() =>
        throw new NotSupportedException("Progress bars cannot be activated.");
}
