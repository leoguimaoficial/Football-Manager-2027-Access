using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using SI.Bindable;
using UnityEngine.UIElements;
namespace FM27Access.Plugin.Server;

// The FM27 picker exposes colours as a GridSelector of native hex strings. Reuse
// FM26's RadioGroupItem/HTML, with native indexed selection and native availability.
internal static class FM27ColourSurface
{
    internal sealed class Current:PluginAdapterBase
    {
        private readonly ColourPicker picker;
        internal Current(ColourPicker picker):base(picker)=>this.picker=picker;
        public override string Role=>"text";
        public override bool CanActivate=>false;
        public override string GetLabel()=>"Selected colour: "+Hex(picker);
        public override string GetAnnouncement()=>GetLabel();
        public override string GetState()=>"";
        public override void Activate()=>throw new InvalidOperationException("Read-only colour value.");
    }
    internal static string Hex(ColourPicker picker)
    {
        int Byte(float value)=>(int)Math.Round(Math.Clamp(value,0,1)*255);
        var c=picker.m_colour;return $"#{Byte(c.r):X2}{Byte(c.g):X2}{Byte(c.b):X2}{Byte(c.a):X2}";
    }
    internal static bool TryPalette(VisualElement element,List<ScreenWalker.RenderItem> items)
    {
        var grid=element.TryCast<GridSelector>();var values=grid?.m_items;
        if(values==null||values.Count==0)return false;
        var colours=new List<string>();
        for(int i=0;i<values.Count;i++)
        {
            var raw=values[i]?.AsString();string h=raw?.TrimStart('#');
            if(values[i]?.DataType?.FullName!="System.String"||h?.Length is not(6 or 8)||!h.All(Uri.IsHexDigit))return false;
            colours.Add("#"+h.ToUpperInvariant()+(h.Length==6?"FF":""));
        }
        var radios=new List<IAccessibleElement>();
        for(int i=0;i<colours.Count;i++)radios.Add(new Choice(grid,i,colours[i]));
        items.Add(new ScreenWalker.RadioGroupItem{Group=grid,Radios=radios,Label=new FieldLabelReader(grid).GetLabel()??"Preset colours"});
        return true;
    }
    private sealed class Choice:PluginAdapterBase,IStableKeyElement
    {
        private readonly GridSelector grid;private readonly int index;private readonly string colour;
        internal Choice(GridSelector grid,int index,string colour):base(grid){this.grid=grid;this.index=index;this.colour=colour;}
        public string StableKey=>ServerHandles.IdentityPath(grid)+"|colour:"+index;
        public override string Role=>"radio button";
        public override bool CanActivate=>true;
        public override string GetLabel()=>colour;
        public override string GetState()=>grid.m_selectedIndex==index?"selected":"unselected";
        public override void Activate()
        {
            if(grid.m_items==null||index>=grid.m_items.Count||ColourValue.NormalizeHex(grid.m_items[index]?.AsString())!=colour)throw new InvalidOperationException("Native palette changed.");
            if(grid.m_boundElements?.ContainsKey(index)!=true)throw new InvalidOperationException("Native palette item is not realised yet.");
            var bound=grid.m_boundElements[index];var lists=grid.m_virtualisedLists;
            for(int line=0;lists!=null&&line<lists.Count;line++)
            {
                var list=lists[line];if(list==null)continue;
                for(int i=0;i<list.GetActiveCount();i++)
                {
                    var item=list.GetActiveItem(i);
                    if(item?.Content?.Pointer!=bound.Pointer||grid.CombinedIndexToRealIndex(line,item.Index)!=index)continue;
                    list.SetSelection(item.Index);return;
                }
            }
            throw new InvalidOperationException("Native palette item is no longer realised.");
        }
    }
}
