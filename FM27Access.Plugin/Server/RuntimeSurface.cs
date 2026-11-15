using System.Net;
using System.Text.Json;
namespace FM27Access.Plugin.Server;

// Deployment/session boundary. Screen walking, HTML, handles and actions remain FM26's.
internal static class RuntimeSurface
{
    private static string session;
    private static readonly string Instance=Guid.NewGuid().ToString("N");
    private static string coreMvid="";
    internal static void Configure(string value,string module=""){session=value;coreMvid=module;}
    internal static string Health()=>JsonSerializer.Serialize(new{protocol=1,adapter="fm26-original-core",instance=Instance,screen=MainThreadDispatcher.Health().ToString(),pid=Environment.ProcessId,coreMvid,version=FM27Access.Speech.BuildProfile.Version,developerTools=FM27Access.Speech.BuildProfile.DeveloperTools});
    internal static string Cookie=>"Set-Cookie: fm27session="+session+"; HttpOnly; SameSite=Strict; Path=/";
    internal static bool Authorize(string[] lines,string path)
    {
        var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var line in lines.Skip(1))
        {
            if(line.Length==0)continue;int colon=line.IndexOf(':');
            if(colon<1||!headers.TryAdd(line.Substring(0,colon),line.Substring(colon+1).Trim()))return false;
        }
        const string host="127.0.0.1:8787";
        if(!headers.TryGetValue("Host",out var h)||h!=host)return false;
        if(headers.TryGetValue("Origin",out var origin)&&origin!="http://"+host)return false;
        if(headers.TryGetValue("Sec-Fetch-Site",out var site)&&site=="cross-site")return false;
        return path is "/" or "/health"||headers.TryGetValue("Cookie",out var cookie)&&cookie.Split(';').Any(p=>p.Trim()=="fm27session="+session);
    }
    // AggregateSignature retains FM26's unit separator between panel identities.
    // HTTP field values cannot contain that control character. Encode only at
    // transport and decode in the companion before comparing with SSE tokens.
    internal static string BodyHeader(string token,string title,int? historyIndex=null,string language="und",string historyOrigin=null)=>"X-FM-Token: "+WebUtility.UrlEncode(Clean(token))+"\r\nX-FM-Title: "+WebUtility.UrlEncode(title)
        +(historyIndex.HasValue?"\r\nX-FM-History-Index: "+historyIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture):"")
        +(historyOrigin != null ? "\r\nX-FM-History-Origin: " + WebUtility.UrlEncode(historyOrigin) : "")
        +"\r\nX-FM-Language: "+DocumentLanguage.FromNative(language);
    private static string Clean(string s)=>s.Replace("\r","").Replace("\n"," ");
}
