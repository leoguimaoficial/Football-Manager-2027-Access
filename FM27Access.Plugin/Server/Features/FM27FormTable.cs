using UnityEngine.UIElements;
namespace FM27Access.Plugin.Server;

// FM27's colour preferences are aligned form rows, not StreamedTables. Keep the
// all-or-nothing consolidation pattern used by FM26 SyntheticTables, and walk each
// cell through the original walker so its real buttons/number fields keep working.
internal static class FM27FormTable
{
    private static IEnumerable<VisualElement> Children(VisualElement element)
    {
        for(int i=0;i<element.hierarchy.childCount;i++)
        {var child=element.hierarchy[i];if(ElementBridge.IsVisible(child))yield return child;}
    }
    private static VisualElement Named(VisualElement element,string name)=>Children(element).SingleOrDefault(c=>c.name==name);
    private static List<VisualElement> Find(VisualElement element,Func<VisualElement,bool> match,int depth=0,int maxDepth=12)
    {
        if(depth>maxDepth)return new();if(match(element))return new(){element};
        return Children(element).SelectMany(c=>Find(c,match,depth+1,maxDepth)).ToList();
    }
    internal static ScreenWalker.NativeFormTableItem TryBuild(VisualElement element,Func<VisualElement,List<ScreenWalker.RenderItem>> walk)
    {
        if(element.name=="Frame"&&CareerOwner(element)!=null)return TryCareerSummary(element,walk);
        if(element.name=="LiveTable")return TryLiveTable(element,walk);
        if(element.name=="LineupList")return TryLineup(element,walk);
        if(element.name=="KeyEvents")return TryFixtureEvents(element,walk);
        if(element.name=="PlayerRatings")return TryFixtureRatings(element,walk);
        if(element.name=="StreamedObjectList_Fixtures")return TryFixtures(element,walk);
        if(element.GetIl2CppType().Name=="StreamedObjectList")return TryCareerSeasons(element,walk)??TryFacts(element,walk);
        if(element.name is not("Attributes" or "Positions" or "Other"))return null;
        var header=Named(element,"Header");if(header==null)return null;
        string Text(VisualElement e)=>ServerText.Clean(e.TryCast<TextElement>()?.text??"");
        var headers=Find(header,e=>e.TryCast<TextElement>()!=null).Select(Text).Where(t=>t.Length>0).ToArray();
        bool attributes=element.name=="Attributes";if(headers.Length!=(attributes?5:1))return null;
        var rows=Find(element,e=>e.GetIl2CppType().Name=="ComponentElement"&&Named(e,"LeftFrame")!=null&&Named(e,"RightFrame")!=null);
        if(rows.Count==0||rows.Count>100)return null;
        var plan=new List<List<VisualElement>>();
        foreach(var row in rows)
        {
            var left=Named(row,"LeftFrame");var right=Named(row,"RightFrame");
            if(Find(left,e=>e.name=="title"&&e.TryCast<TextElement>()!=null).Count!=1)return null;
            var fields=Find(right,e=>e.TryCast<SI.Bindable.SIButton>()!=null||e.TryCast<SI.Bindable.BindableIntEditBox>()!=null);
            if(attributes)
            {
                if(fields.Count!=4||fields[0].name!="text-colour-picker"||fields[1].name!="background-colour-picker"||fields[2].name!="border-colour-picker"||fields[3].GetIl2CppType().Name!="BindableIntEditBox")return null;
                plan.Add(new[]{left}.Concat(fields).ToList());
            }
            else{if(fields.Count!=1||fields[0].TryCast<SI.Bindable.SIButton>()==null)return null;plan.Add(new(){left,right});}
        }
        // Colour is mod-owned header text for the two-column variants; every field,
        // value and action in their cells still comes from the native form.
        if(!attributes)headers=new[]{headers[0],"Colour"};
        var rendered=new List<List<List<ScreenWalker.RenderItem>>>();
        foreach(var row in plan)
        {
            string name=Text(Find(row[0],e=>e.name=="title"&&e.TryCast<TextElement>()!=null)[0]);
            var cells=new List<List<ScreenWalker.RenderItem>>();
            for(int column=0;column<row.Count;column++)
            {
                var parts=walk(row[column]);
                if(column>0)foreach(var part in parts)
                    if(part is ScreenWalker.StopItem stop&&stop.Element is Adapters.ButtonAdapter button&&button.GetLabel()==Adapters.ButtonAdapter.UnlabeledIconButton)
                        stop.Element=new FormButton(button.BackingElement,name+", "+headers[column]);
                cells.Add(parts);
            }
            rendered.Add(cells);
        }
        return new(){Headers=headers,Rows=rendered};
    }

    // FM27's selected-fixture summary splits events into home/away lists. The
    // FM26 report table retains Min/Team/Event/Player; use live cells here so
    // the native scorer links and their context menus survive consolidation.
    private static ScreenWalker.NativeFormTableItem TryFixtureEvents(VisualElement element,
        Func<VisualElement,List<ScreenWalker.RenderItem>> walk)
    {
        var groups=Children(element).Where(e=>e.GetIl2CppType().Name=="ComponentElement").ToList();
        if(groups.Count!=2)return null;
        var owner=element.parent;
        while(owner!=null&&owner.name!="CalendarToolFixturesGridCompositionSet")owner=owner.parent;
        if(owner==null)return null;
        var teams=new List<string>();
        foreach(var side in new[]{"HomeTeam","AwayTeam"})
        {
            // Other report tiles can contain similarly named club headers.
            // This composition's score header is the one with a direct Score.
            var headers=Find(owner,e=>e.name==side&&Named(e,"Score")!=null,maxDepth:40);
            if(headers.Count!=1)return null;
            var name=ServerText.Clean(ScreenWalker.ReadFirstText(headers[0]));
            if(string.IsNullOrWhiteSpace(name))return null;
            teams.Add(name);
        }
        var plan=new List<(VisualElement Time,string Team,VisualElement Event,VisualElement Player)>();
        for(int side=0;side<groups.Count;side++)
        {
            var lists=Find(groups[side],e=>e.GetIl2CppType().Name=="StreamedObjectList");
            if(lists.Count!=1||ListWindowSurface.IsUnderRealised(lists[0]))return null;
            var events=Find(lists[0],e=>e.name=="EventType"&&e.GetIl2CppType().Name=="SIImage");
            var nativeList=ListWindowSurface.FindScrollView(lists[0])?.parent?.TryCast<SI.UI.VirtualisedList>();
            if(nativeList==null||ListWindowSurface.ReadItemCount(nativeList)!=events.Count)return null;
            foreach(var icon in events)
            {
                if(!Adapters.SpriteHelper.IsMatchEventType(icon))return null;
                var row=icon.parent;
                while(row!=null&&row.GetIl2CppType().Name!="BindingRoot")row=row.parent;
                if(row==null)return null;
                var texts=Texts(row);var player=texts.Where(e=>e.name=="Name").ToList();
                var time=texts.Where(e=>e.name=="Number").ToList();
                // Never swallow a new native field or another actionable widget.
                if(texts.Count!=2||player.Count!=1||time.Count!=1
                    ||Find(row,e=>e.TryCast<SI.Bindable.SIButton>()!=null).Count!=0)return null;
                plan.Add((time[0],teams[side],icon,player[0]));
            }
        }
        if(plan.Count==0||plan.Count>StreamedTableLoader.PageRows)return null;
        return new(){Headers=new[]{"Min","Team","Event","Player"},RowHeader=3,
            Rows=plan.Select(row=>new List<List<ScreenWalker.RenderItem>>{
                walk(row.Time),new(){new ScreenWalker.NoteItem{Text=row.Team}},walk(row.Event),walk(row.Player)}).ToList()};
    }

    // Preserve the report's real player links and each icon-only player event.
    // The row binding identifies these values as Rating/Player/PlayerEvents;
    // presentation numbers are retained in their official localized form.
    private static ScreenWalker.NativeFormTableItem TryFixtureRatings(VisualElement element,
        Func<VisualElement,List<ScreenWalker.RenderItem>> walk)
    {
        var children=Children(element).ToList();
        if(children.Count!=1||children[0].GetIl2CppType().Name!="StreamedObjectList")return null;
        var list=children[0];if(ListWindowSurface.IsUnderRealised(list))return null;
        var ratings=Find(list,e=>e.name=="Background"&&e.GetIl2CppType().Name=="SIStyleSetter");
        var nativeList=ListWindowSurface.FindScrollView(list)?.parent?.TryCast<SI.UI.VirtualisedList>();
        if(nativeList==null||ratings.Count==0||ratings.Count>StreamedTableLoader.PageRows
            ||ListWindowSurface.ReadItemCount(nativeList)!=ratings.Count)return null;
        var plan=new List<List<VisualElement>>();
        foreach(var rating in ratings)
        {
            var cells=Children(rating.parent).ToList();
            if(cells.Count!=3||cells[0].GetIl2CppType().Name!="BindingRemapper"
                ||cells[1].GetIl2CppType().Name!="StreamedObjectList"||cells[1].name!="PlayerEvents"
                ||cells[2].Pointer!=rating.Pointer)return null;
            var root=rating.parent;
            while(root!=null&&root.GetIl2CppType().Name!="BindingRoot")root=root.parent;
            var node=root==null?null:BindingRecords.NodeOf(root);
            var row=node==null?null:BindingRecords.FindNode(node,"row",0);
            if(row==null||BindingRecords.FindNode(row,"Rating",0)==null
                ||BindingRecords.FindNode(row,"Player",0)==null
                ||BindingRecords.FindNode(row,"PlayerEvents",0)==null)return null;
            plan.Add(cells);
        }
        return new(){Headers=new[]{"Player","Event","Rating"},RowHeader=0,
            Rows=plan.Select(row=>row.Select(walk).ToList()).ToList()};
    }

    // The native career tile separates lifetime totals from season rows. Keep
    // that separation, retaining every real link/transfer control in each cell.
    private static VisualElement CareerOwner(VisualElement element)
    {
        for(var p=element.parent;p!=null;p=p.parent)
            if(p.name=="CareerStats")return p;
        return null;
    }
    private static List<VisualElement> Texts(VisualElement element)=>
        Find(element,e=>e.TryCast<TextElement>()!=null&&!string.IsNullOrWhiteSpace(e.Cast<TextElement>().text));
    private static ScreenWalker.NativeFormTableItem TryCareerSummary(VisualElement element,
        Func<VisualElement,List<ScreenWalker.RenderItem>> walk)
    {
        var children=Children(element).ToList();
        if(children.Count!=2||children.Any(e=>e.name!="Frame")||Find(children[0],e=>e.name=="ClubIcon").Count!=1)return null;
        var texts=Texts(element);if(texts.Count!=6)return null;
        var rows=new List<List<List<ScreenWalker.RenderItem>>>();
        for(int i=0;i<texts.Count;i+=2)rows.Add(new(){walk(texts[i]),walk(texts[i+1])});
        return new(){Headers=new[]{"Stat","Value"},Rows=rows};
    }
    private static ScreenWalker.NativeFormTableItem TryCareerSeasons(VisualElement element,
        Func<VisualElement,List<ScreenWalker.RenderItem>> walk)
    {
        if(CareerOwner(element)==null)return null;
        var anchors=Find(element,e=>e.name=="SeasonYear");
        if(anchors.Count==0||anchors.Count>StreamedTableLoader.PageRows)return null;
        var plan=new List<List<VisualElement>>();
        foreach(var anchor in anchors)
        {
            var cells=Children(anchor.parent).ToList();
            if(!cells.Select(e=>e.name).SequenceEqual(new[]{"SeasonYear","Club","TransferInfo","Apps","Goals"}))return null;
            plan.Add(cells);
        }
        var summary=element.parent==null?null:Children(element.parent).FirstOrDefault(e=>e.name=="Frame");
        if(summary==null)return null;
        var totals=Texts(summary);if(totals.Count!=6)return null;
        string Label(int i)=>ServerText.Clean(totals[i].Cast<TextElement>().text);
        return new(){Headers=new[]{"Season","Club","Transfer",Label(2),Label(4)},
            Rows=plan.Select(row=>row.Select(walk).ToList()).ToList(),RowHeader=0};
    }
    private static ScreenWalker.NativeFormTableItem TryLiveTable(VisualElement element,
        Func<VisualElement,List<ScreenWalker.RenderItem>> walk)
    {
        var header=Named(element,"TableHeader");
        var list=Children(element).SingleOrDefault(e=>e.GetIl2CppType().Name=="StreamedObjectList");
        if(header==null||list==null)return null;
        var headers=Texts(header).Select(e=>ServerText.Clean(e.Cast<TextElement>().text)).ToArray();
        if(headers.Length!=3)return null;
        var rows=Find(list,e=>e.name=="RowContent");
        if(rows.Count==0||rows.Count>StreamedTableLoader.PageRows)return null;
        var plan=new List<List<VisualElement>>();
        foreach(var row in rows)
        {
            var cells=new[]{Named(row,"PositionBlock"),Named(row,"TeamBlock"),Named(row,"CellBlock")};
            if(cells.Any(e=>e==null)||Children(row).Any(e=>e.name is not("PositionBlock" or "TeamBlock" or "CellBlock" or "IndicatorVisible")))return null;
            plan.Add(cells.ToList());
        }
        return new(){Headers=headers,Rows=plan.Select(row=>row.Select(walk).ToList()).ToList(),RowHeader=1};
    }
    // The shared suggested-lineup component draws position, player links (including
    // the replaced player) and a reason as aligned rows. Reuse the existing native
    // form table so all links/optional selection controls retain their real handles.
    private static ScreenWalker.NativeFormTableItem TryLineup(VisualElement element,
        Func<VisualElement,List<ScreenWalker.RenderItem>> walk)
    {
        var rows=Find(element,e=>e.name=="CheckboxContainer");
        if(rows.Count==0||rows.Count>StreamedTableLoader.PageRows)return null;
        var plan=new List<(VisualElement Position,VisualElement Players,VisualElement Reason,VisualElement Selection)>();
        foreach(var anchor in rows)
        {
            var row=anchor.parent;
            if(row==null)return null;
            var position=Find(row,e=>e.name=="RoleName");
            var players=Find(row,e=>e.name=="Players");
            var reason=Find(row,e=>e.name=="SuggestedPlayerReason");
            if(position.Count!=1||players.Count!=1||reason.Count!=1)return null;
            plan.Add((position[0],players[0],reason[0],Named(anchor,"SelectionVisible")));
        }
        var rendered=new List<List<List<ScreenWalker.RenderItem>>>();
        foreach(var row in plan)
        {
            var position=new List<ScreenWalker.RenderItem>();
            if(row.Selection!=null)position.AddRange(walk(row.Selection));
            position.AddRange(walk(row.Position));
            rendered.Add(new(){position,walk(row.Players),walk(row.Reason)});
        }
        return new(){Headers=new[]{"Position","Players","Reason"},Rows=rendered};
    }
    // The compact competition-fixture row draws four aligned cells but keeps
    // club/nation navigation inside them. Reuse live form cells rather than
    // flattening links into the report tile's read-only string grid.
    private static ScreenWalker.NativeFormTableItem TryFixtures(VisualElement element,
        Func<VisualElement,List<ScreenWalker.RenderItem>> walk)
    {
        var rows=Find(element,e=>e.name=="BindingExpect_CompetitionFixturesTile_8x8_Row_Child");
        if(rows.Count==0||rows.Count>StreamedTableLoader.PageRows)return null;
        var plan=new List<VisualElement>();
        foreach(var row in rows)
        {
            var horizontal=Find(row,e=>e.name=="Horizontal");
            if(horizontal.Count!=1)return null;
            var cells=horizontal[0];
            if(cells.hierarchy.childCount!=4||cells.hierarchy[1].name!="HomeTeam"
                ||cells.hierarchy[3].name!="AwayTeam")return null;
            plan.Add(cells);
        }
        var rendered=new List<List<List<ScreenWalker.RenderItem>>>();
        foreach(var row in plan)
        {
            var cells=new List<List<ScreenWalker.RenderItem>>();
            for(int column=0;column<4;column++)cells.Add(walk(row.hierarchy[column]));
            rendered.Add(cells);
        }
        return new(){Headers=new[]{"Time","Home","Score","Away"},Rows=rendered,RowHeader=1};
    }

    // The shared club/division facts layout is a three-column comparison. Its
    // object-list rows are not StreamedTable cells. Retain the original live
    // cell walk and the native entity links, including multiple links per text.
    private static ScreenWalker.NativeFormTableItem TryFacts(VisualElement element,
        Func<VisualElement,List<ScreenWalker.RenderItem>> walk)
    {
        var header=element.parent==null?null:Named(element.parent,"Horizontal1");
        if(header==null||Named(header,"Team")==null||Named(header,"Division")==null)return null;
        string Header(string name)
        {
            var cells=Find(header,e=>e.name==name);
            if(cells.Count!=1)return null;
            var text=Find(cells[0],e=>e.TryCast<TextElement>()!=null);
            return text.Count==1?ServerText.Clean(text[0].Cast<TextElement>().text):null;
        }
        var team=Header("TeamNameCell");var division=Header("DivisionNameCell");
        if(string.IsNullOrWhiteSpace(team)||string.IsNullOrWhiteSpace(division))return null;
        var rows=Find(element,e=>e.name=="Row"&&Find(e,c=>c.name=="FactLabel").Count==1);
        if(rows.Count==0||rows.Count>StreamedTableLoader.PageRows)return null;
        var plan=new List<(VisualElement Label,VisualElement Team,VisualElement Division)>();
        foreach(var row in rows)
        {
            var split=Named(row,"Divided");
            if(Children(row).Count()!=2||Named(row,"Baseline_192")==null||split==null
                ||Children(split).Count()!=2||Named(split,"TeamFacts")==null||Named(split,"DivisionFacts")==null)return null;
            var label=Find(row,e=>e.name=="FactLabel");
            var own=Find(row,e=>e.name=="TeamFacts");var league=Find(row,e=>e.name=="DivisionFacts");
            if(label.Count!=1||own.Count!=1||league.Count!=1)return null;
            plan.Add((label[0],own[0],league[0]));
        }
        bool provisional=false;
        List<ScreenWalker.RenderItem> Cell(VisualElement cell)
        {
            var result=new List<ScreenWalker.RenderItem>();
            foreach(var child in Children(cell))
            {
                var parts=Adapters.SITextLink.ReadParts(child);
                if(parts!=null)result.Add(new ScreenWalker.JoinedTextItem{Parts=parts});
                else
                {
                    if(child.TryCast<SI.Bindable.SIText>() is { } text&&Adapters.SITextLink.HasLink(text.text))provisional=true;
                    result.AddRange(walk(child));
                }
            }
            return result;
        }
        var rendered=new List<List<List<ScreenWalker.RenderItem>>>();
        foreach(var row in plan)rendered.Add(new(){walk(row.Label),Cell(row.Team),Cell(row.Division)});
        return new(){Headers=new[]{"Fact",team,division},Rows=rendered,Provisional=provisional};
    }

    private sealed class FormButton:Adapters.ButtonAdapter
    {
        private readonly string label;
        internal FormButton(VisualElement element,string label):base(element)=>this.label=label;
        protected override bool SplitsLabel=>false;
        public override string GetLabel()=>label;
        public override string GetPrimaryLabel()=>label;
        public override string GetDescription()=>"";
    }
}
