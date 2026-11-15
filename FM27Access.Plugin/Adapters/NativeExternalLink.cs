using SI.Bindable;
using SI.Bindable.Events;
namespace FM27Access.Plugin.Adapters;
internal static class NativeExternalLink
{
    internal static string Read(SIButton button)
    {
        if(button==null)return null;
        var events=button.Events;string result=null;
        for(int i=0;events!=null&&i<events.Count;i++)
        {
            var context=events[i]?.TryCast<OpenURLEventContext>();if(context==null)continue;
            var evt=OpenURLEvent.GetPooled();string url;
            try{context.FillEvent(evt);url=evt.Url;}finally{evt.Dispose();}
            if(!Uri.TryCreate(url,UriKind.Absolute,out var parsed)||parsed.Scheme is not("http" or "https"))continue;
            if(result!=null&&result!=url)return null;
            result=url;
        }
        if(result==null&&button.name=="ClickableBannerButton")
        {
            // This new FM27 template is driven by the PromoBanner subsystem rather than
            // SIButton.Events. Read the currently displayed asset's own click destination.
            var source=UnityEngine.Object.FindObjectOfType<FM.UI.PromoBanner.PromoBanner>();
            var banner=source?.m_currentBanner;
            string url=banner?.TryCast<FM.UI.PromoBanner.Banner>()?.ClickUrl??banner?.TryCast<FM.UI.PromoBanner.RemoteBanner>()?.ClickUrl;
            if(Uri.TryCreate(url,UriKind.Absolute,out var address)&&address.Scheme is "http" or "https")result=url;
        }
        return result;
    }
}

