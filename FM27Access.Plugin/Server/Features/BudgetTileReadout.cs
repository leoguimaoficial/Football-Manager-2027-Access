using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

// The native budget tile has two monetary rows and a separate visual bar.
// The ordinary form-label heuristic must not borrow SpendingText (or a wage
// amount) to name that bar. Reuse the card title and FM26 label/value lines;
// no financial ratio is inferred from a rendered bar width.
internal static class BudgetTileReadout
{
    internal static VisualElement WageSummaryOf(VisualElement element)
    {
        for(var owner=element;owner!=null;owner=owner.parent)
        {
            if(owner.name=="FinancesWagesTilePercentageSection")return owner;
            if(owner.GetIl2CppType().Name is "Panel" or "BindableVariableSizedGridElement")break;
        }
        return null;
    }
    private static VisualElement CardOf(VisualElement element)
    {
        for(var owner=element;owner!=null;owner=owner.parent)
        {
            var type=owner.GetIl2CppType().Name;
            if(type=="Panel")break;
            if(type=="BindableVariableSizedGridElement")return owner;
        }
        return null;
    }

    internal static bool IsInCard(VisualElement element)=>CardOf(element)!=null;

    // FM27's shared three-budget summary uses generic Frame/text names, unlike
    // the separate AvailableText/SpendingText detail tiles. Each native row has
    // caption + availability in its left branch and one amount on its right.
    // Match the actual shared component and boundaries, never money/caption text.
    internal static bool IsCaptionStatusValueRow(VisualElement caption, VisualElement status, VisualElement value)
    {
        if(caption==null||status==null||value==null)return false;
        var card=CardOf(caption);
        if(card?.name!="RecruitmentBudgets"||CardOf(status)?.Pointer!=card.Pointer||CardOf(value)?.Pointer!=card.Pointer)return false;
        VisualElement Branch(VisualElement leaf,VisualElement parent)
        {
            for(var branch=leaf;branch!=null;branch=branch.parent)
                if(branch.parent?.Pointer==parent.Pointer)return branch;
            return null;
        }
        int TextCount(VisualElement root)
        {
            if(!ElementBridge.IsVisible(root))return 0;
            int count=root.TryCast<TextElement>() is TextElement text&&!string.IsNullOrWhiteSpace(text.text)?1:0;
            for(int i=0;i<root.hierarchy.childCount&&count<=3;i++)count+=TextCount(root.hierarchy[i]);
            return count;
        }
        for(var row=caption.parent;row!=null&&row.Pointer!=card.Pointer;row=row.parent)
        {
            var first=Branch(caption,row);var second=Branch(status,row);var last=Branch(value,row);
            if(first==null||second==null||last==null)continue;
            if(first.Pointer!=second.Pointer||first.Pointer==last.Pointer)return false;
            int visible=0;for(int i=0;i<row.hierarchy.childCount;i++)if(ElementBridge.IsVisible(row.hierarchy[i]))visible++;
            return visible==2&&TextCount(first)==2&&TextCount(last)==1;
        }
        return false;
    }

    private static bool HasBudgetRows(VisualElement card)
        =>card!=null&&ScreenWalker.FindDescendantNamed(card,"AvailableText")!=null
            &&ScreenWalker.FindDescendantNamed(card,"AvailableValueText")!=null
            &&ScreenWalker.FindDescendantNamed(card,"SpendingText")!=null
            &&ScreenWalker.FindDescendantNamed(card,"SpendingValueText")!=null;

    internal static string IndicatorCaption(VisualElement element)
    {
        var card=CardOf(element);
        if(card==null||(!HasBudgetRows(card)
            &&ScreenWalker.FindDescendantNamed(card,"FinancesWagesTilePercentageSection")==null))return null;
        var title=ElementBridge.FindVisibleTileTitle(card)?.TryCast<TextElement>()?.text;
        return string.IsNullOrWhiteSpace(title)?null:ServerText.Clean(title)+", indicator fill";
    }

    internal static bool IsMonetaryPair(VisualElement label,VisualElement value)
    {
        if(label==null||value==null)return false;
        var frameName=label.name switch
        {
            "AvailableText" when value.name=="AvailableValueText"=>"AvailableFrame",
            "SpendingText" when value.name=="SpendingValueText"=>"SpendingFrame",
            _=>null
        };
        if(frameName==null)return false;
        var card=CardOf(label);
        if(!HasBudgetRows(card)||CardOf(value)?.Pointer!=card.Pointer)return false;
        for(var owner=label.parent;owner!=null&&owner.Pointer!=card.Pointer;owner=owner.parent)
            if(owner.name==frameName)
                return ScreenWalker.FindDescendantNamed(owner,value.name)?.Pointer==value.Pointer;
        return false;
    }
}
