using System.Collections.Generic;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

// Native history tiles scatter each manager's linked name and tenure across
// two visual branches. Extend the original SyntheticTable/RowActions pattern,
// retaining the SIText link and original list-window paging rather than a new UI.
internal static class ManagerHistoryRows
{
    internal static ScreenWalker.SyntheticTableItem TryRead(VisualElement list)
    {
        if(list.name!="ManagersInfoStreamedList")return null;
        try{return Read(list);}
        catch(System.Exception error)
        {
            Speech.Log.Warn("[ManagerHistoryRows] Native rows not ready; retaining original list: "+error.Message);
            return null;
        }
    }

    private static ScreenWalker.SyntheticTableItem Read(VisualElement list)
    {
        var view=ScreenWalker.FindDescendantNamed(list,"View");
        if(view==null)return null;
        var rows=new List<List<string>>();var actions=new List<IAccessibleElement>();
        for(int i=0;i<view.hierarchy.childCount;i++)
        {
            var root=view.hierarchy[i];if(!ElementBridge.IsVisible(root))continue;
            if(root.GetIl2CppType().Name!="BindingRoot")return null;
            var texts=new List<VisualElement>();
            if(!Collect(root,texts)||texts.Count!=4)return null;
            var name=ServerText.Clean(texts[0].TryCast<TextElement>()?.text);
            var from=ServerText.Clean(texts[1].TryCast<TextElement>()?.text);
            var separator=ServerText.Clean(texts[2].TryCast<TextElement>()?.text);
            var until=ServerText.Clean(texts[3].TryCast<TextElement>()?.text);
            if(string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(from)
                ||separator!="-"||string.IsNullOrWhiteSpace(until))return null;
            var parts=Adapters.SITextLink.ReadParts(texts[0]);
            IAccessibleElement action=null;
            if(parts!=null)
                foreach(var part in parts)
                    if(part.CanActivate)
                    {
                        if(action!=null||ServerText.Clean(part.GetLabel())!=name)return null;
                        action=part;
                    }
            // A temporarily unbound rich link must remain in the ordinary
            // walk; do not convert it into permanent noninteractive text.
            if(texts[0].TryCast<TextElement>()?.text?.Contains("<link=")==true&&action==null)return null;
            rows.Add(new(){name,from,until});actions.Add(action);
        }
        return rows.Count==0?null:new(){Headers=new[]{"Manager","From","Until"},Rows=rows,RowHeader=0,RowActions=actions};
    }

    private static bool Collect(VisualElement element,List<VisualElement> texts)
    {
        if(!ElementBridge.IsVisible(element))return true;
        if(element.GetIl2CppType().Name=="SIText")
        {
            if(!string.IsNullOrWhiteSpace(element.TryCast<TextElement>()?.text))texts.Add(element);
            return true;
        }
        var kind=ElementClassifier.Classify(ElementBridge.Describe(element,null));
        if(TreeWalker.NavigationStops.Contains(kind)&&kind!=ElementClassification.Image)return false;
        for(int i=0;i<element.hierarchy.childCount;i++)
            if(!Collect(element.hierarchy[i],texts))return false;
        return true;
    }
}
