#nullable enable annotations
using SI.Bindable;
using SI.UI;
namespace FM27Access.Plugin;

// FM27 moved layers underneath documents. Keep that API change at this boundary;
// never read Layer.Name (metadata 39) or substitute guessed layer numbers.
internal static class FM27PanelCatalog
{
    internal static Dictionary<string,int> NamedLayers()
    {
        var result=new Dictionary<string,int>();
        foreach(string name in Roles.Keys)
            result.Add(name,(int)PanelLayerSettings.GetLayer(name));
        return result;
    }
    internal sealed record LayerView(int Layer,List<Panel> Open,List<Panel> Closing,bool HasClosablePanels=false);
    internal static List<LayerView> GetNativeLayers(PanelManager manager)
    {
        var handles=new Il2CppSystem.Collections.Generic.List<DocumentHandle>();
        manager.GetDocumentHandles(handles);
        var result=new SortedDictionary<int,LayerView>();
        for(int d=0;d<handles.Count;d++)
        {
            var native=new Il2CppSystem.Collections.Generic.List<PanelManager.LayerInfo>();
            manager.GetLayersForDocument(handles[d],native);
            for(int l=0;l<native.Count;l++)
            {
                var entry=native[l];int rank=(int)entry.Layer;
                if(!result.TryGetValue(rank,out var view)){view=new(rank,new(),new(),entry.Description?.HasClosablePanels==true);result.Add(rank,view);}
                var open=entry.Open;var closing=entry.Closing;
                if(open!=null)for(int i=0;i<open.Count;i++)if(open[i]!=null)view.Open.Add(open[i]);
                if(closing!=null)for(int i=0;i<closing.Count;i++)if(closing[i]!=null)view.Closing.Add(closing[i]);
            }
        }
        return result.Values.ToList();
    }
    private static readonly Dictionary<string,int> Roles=new()
    {
        ["Menu"]=FM27Access.Navigation.InteractivePanelSet.LayerMenu,
        ["MenuTitle"]=FM27Access.Navigation.InteractivePanelSet.LayerMenu,
        ["MenuBody"]=FM27Access.Navigation.InteractivePanelSet.LayerMenu,
        ["Report"]=FM27Access.Navigation.InteractivePanelSet.LayerReport,
        ["ReportBody"]=FM27Access.Navigation.InteractivePanelSet.LayerReport,
        ["Card"]=FM27Access.Navigation.InteractivePanelSet.LayerCard,
        ["CardBody"]=FM27Access.Navigation.InteractivePanelSet.LayerCard,
        ["Overlay"]=FM27Access.Navigation.InteractivePanelSet.LayerOverlay,
        ["Tutorial"]=FM27Access.Navigation.InteractivePanelSet.LayerOverlay,
        ["Tooltip"]=5,["Ribbon"]=6,
        ["ModalDialog"]=FM27Access.Navigation.InteractivePanelSet.LayerModalDialog,
        ["ProfanityLoadingScreen"]=FM27Access.Navigation.InteractivePanelSet.LayerProfanityLoadingScreen,
        ["LoadingScreen"]=FM27Access.Navigation.InteractivePanelSet.LayerLoadingScreen
    };
    private static Dictionary<int,int>? nativeToRole;
    internal static int SemanticLayer(string name)=>Roles[name];
    internal static int SemanticRoleForNativeLayer(int layer)
    {
        nativeToRole??=NamedLayers().ToDictionary(p=>p.Value,p=>Roles[p.Key]);
        return nativeToRole.TryGetValue(layer,out int role)?role:32+layer;
    }
    internal static List<LayerView> GetLayers(PanelManager manager)
    {
        nativeToRole??=NamedLayers().ToDictionary(p=>p.Value,p=>Roles[p.Key]);
        var native=GetNativeLayers(manager);var indexed=new SortedDictionary<int,LayerView>();
        foreach(var view in native)
        {
            int role=nativeToRole.TryGetValue(view.Layer,out int known)?known:32+view.Layer;
            // FM27 splits title/body layers that shared one semantic layer in FM26.
            // Keep every native collection; replacing a role drops its earlier body.
            if(indexed.TryGetValue(role,out var existing))
                indexed[role]=new(role,existing.Open.Concat(view.Open).ToList(),existing.Closing.Concat(view.Closing).ToList(),existing.HasClosablePanels||view.HasClosablePanels);
            else indexed[role]=new(role,view.Open,view.Closing,view.HasClosablePanels);
        }
        // Legacy consumers index by semantic role; missing roles have no panels.
        int maximum=indexed.Count==0?0:indexed.Keys.Max();var result=new List<LayerView>();
        for(int i=0;i<=maximum;i++)result.Add(indexed.TryGetValue(i,out var view)?view:new(i,new(),new()));
        return result;
    }
    internal static bool IsOpenAndInteractive(PanelManager manager,PanelID id)
    {
        var navigation=UnityEngine.Object.FindObjectOfType<FM.UI.FMNavigationManager>();
        if(navigation==null)throw new InvalidOperationException("Native navigation manager is not ready.");
        foreach(var layer in GetNativeLayers(manager))foreach(var panel in layer.Open)
        {
            if(panel.PanelID?.ID!=id.ID||!panel.IsVisible||!panel.IsActive||panel.IsLoading)continue;
            // FM27 registers PanelElement in this index. GetVisualElementNavigationSortOrder
            // returns -1 for that root (it resolves focus leaves), so consult the native panel
            // registry itself, comparing native identity rather than managed wrappers.
            foreach(var pair in navigation.m_openedPanelsToNavigationSortOrder)
                for(int i=0;i<pair.Value.Count;i++)
                    if(!pair.Value[i].Closing&&pair.Value[i].PanelElement.Pointer==panel.Pointer)
                        return pair.Key>=navigation.HighestNavigationSortOrder;
            return false;
        }
        return false;
    }
    internal static Panel? GetForegroundPanel()
    {
        var navigation=UnityEngine.Object.FindObjectOfType<FM.UI.FMNavigationManager>();
        if(navigation==null)return null;
        foreach(var pair in navigation.m_openedPanelsToNavigationSortOrder)
            if(pair.Key==navigation.HighestNavigationSortOrder)
                for(int i=pair.Value.Count-1;i>=0;i--)
                {
                    var entry=pair.Value[i];if(entry.Closing)continue;
                    var panel=entry.PanelElement.TryCast<Panel>();
                    if(panel!=null&&panel.IsActive&&panel.IsVisible&&!panel.IsLoading)return panel;
                }
        return null;
    }
    internal static bool TryCloseForeground(PanelManager manager)
    {
        var panel=GetForegroundPanel();
        if(panel==null||!IsOpenAndInteractive(manager,panel.PanelID))return false;
        var cancel = Adapters.NativeButtonCommand.DialogCancelButton(panel);
        if (cancel != null)
        {
            new Adapters.ButtonAdapter(cancel).Activate();
            return true;
        }
        // A dialog without a native close response must receive an explicit
        // answer. Bare Panel.Close would bypass its DialogManager lifecycle.
        if (panel.PanelID?.name == "GenericModalDialog") return false;
        if (TryCloseToggleOwner(manager, panel)) return true;
        var layer=GetNativeLayerForPanel(manager,panel);
        if(layer?.HasClosablePanels!=true)return false;
        manager.Close(panel);return true;
    }

    internal static bool TryCloseToggleOwnedForeground(PanelManager manager)
    {
        var panel = GetForegroundPanel();
        return panel != null && IsOpenAndInteractive(manager, panel.PanelID)
            && TryCloseToggleOwner(manager, panel);
    }

    private static bool TryCloseToggleOwner(PanelManager manager, Panel panel)
    {
        if (panel.PanelID?.name == "GenericModalDialog") return false;
        if (GetNativeLayerForPanel(manager, panel)?.HasClosablePanels != true) return false;
        var owner = Adapters.NativeButtonCommand.PanelOwnerToggle(panel,
            GetNativeLayers(manager).SelectMany(layer => layer.Open));
        if (owner == null) return false;
        owner.SimulateCheckboxClick();
        return true;
    }
    internal static LayerView? GetNativeLayerForPanel(PanelManager manager,Panel panel)
        =>GetNativeLayers(manager).FirstOrDefault(l=>l.Open.Any(p=>p.Pointer==panel.Pointer));

    internal static bool IsTitleBar(Panel panel)
        =>panel.parent?.name=="Title"
          ||GetNativeLayerForPanel(PanelManager.Instance,panel)?.Layer==(int)PanelLayerSettings.GetLayer("MenuTitle");

    // FM27's full Menu document replaces the split MenuTitle/MenuBody screen.
    // Both remain mounted with zero root bounds and can still answer interactive.
    // Preserve the native distinction before legacy semantic-layer merging.
    internal static bool IsFullMenu(PanelManager manager, Panel panel)
        =>GetNativeLayerForPanel(manager,panel)?.Layer==(int)PanelLayerSettings.GetLayer("Menu");
}
