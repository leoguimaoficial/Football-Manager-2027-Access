using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

// FM27's historical timeline selects a passage; the native game applies it
// when playback continues. Keep SISlider's operation/range and read its actual
// localized widget caption and playback display, never infer seconds
// from its internal time-slice numbers or report an engine seek prematurely.
internal static class MatchTimelineInput
{
    internal static string EventCaption(VisualElement element)
    {
        var events=element.TryCast<SI.Bindable.SIButton>()?.Events;
        if(events?.Count!=1)return null;
        var seek=events[0]?.TryCast<FM.Match.TimelineSeekEventContext>();
        var path=seek?.m_SelectedTimeSliceBindingPath.Path;
        if(string.IsNullOrEmpty(path)||path.Split('.').Length!=2)return null;
        SI.Bindable.IReadOnlyNode root=null;
        for(var owner=element.hierarchy.parent;owner!=null;owner=owner.hierarchy.parent)
            if((root=Server.BindingRecords.NodeOf(owner))!=null)break;
        var row=root==null?null:Server.BindingRecords.FindNode(root,path.Split('.')[0],2);
        var record=row==null?null:Server.BindingRecords.RecordOf(Server.BindingRecords.ValueOf(row));
        if(record==null)return null;
        var fields=Server.BindingRecords.Fields(record);
        if(!fields.ContainsKey(path.Split('.')[1]))return null;
        var time=Server.BindingRecords.TextOf(Server.BindingRecords.Field(fields,"TimeString"));
        var icon=Server.ScreenWalker.FindDescendantNamed(element,"IncidentIcon");
        var kind=Server.MatchIncidentLabel.FromSprite(SpriteHelper.ReadSpriteName(icon));
        if(string.IsNullOrWhiteSpace(time)||string.IsNullOrWhiteSpace(kind))return null;
        var people=new System.Collections.Generic.List<string>();
        foreach(var property in new[]{"Player","Player2"})
        {
            var person=Server.BindingRecords.Field(fields,property);
            if(Server.BindingRecords.PersonOf(person)==null)continue;
            people.Add(Server.PlayerNameRequests.NameForEntity(row.Key,person,element)??"Player name loading");
        }
        var names=Server.MatchIncidentLabel.FormatPlayers(kind,people);
        return time+", "+kind+(string.IsNullOrWhiteSpace(names)?"":", "+names);
    }
    internal static bool IsTimeline(VisualElement element)
    {
        var slider=element.TryCast<SI.Bindable.SISlider>();
        var events=slider?.Events;
        if(events?.Count!=1)return false;
        var seek=events[0]?.TryCast<FM.Match.TimelineSeekEventContext>();
        return seek!=null&&!string.IsNullOrEmpty(slider.DataLink.Path)
            &&slider.DataLink.Path==seek.m_SelectedTimeSliceBindingPath.Path;
    }

    internal static string Caption(VisualElement element)
    {
        if(!IsTimeline(element))return null;
        for(var owner=element.hierarchy.parent;owner!=null;owner=owner.hierarchy.parent)
            if(owner.TryCast<SI.Bindable.BindingExpect>()!=null)
                return Server.ServerText.Clean(Server.ScreenWalker.ReadFirstText(owner));
        return null;
    }

    internal static string ValueText(VisualElement element)
    {
        if(!IsTimeline(element))return null;
        var slider=element.Cast<Slider>();
        var clock=Server.MatchWatcher.TryTimelinePlaybackClock();
        if(!float.IsFinite(slider.value)||!float.IsFinite(slider.lowValue)
            ||!float.IsFinite(slider.highValue)||slider.highValue<=slider.lowValue)
            return clock==null?null:"Played "+clock;
        double percentage=System.Math.Clamp(((double)slider.value-slider.lowValue)
            /((double)slider.highValue-slider.lowValue)*100,0,100);
        return percentage.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)
            +"% selected"+(clock==null?"":"; played "+clock);
    }

    internal const string Help="The percentage identifies the selected position. The played clock is the game's displayed playback time, including stoppage time. Playback continues from the selected passage when resumed.";
}
