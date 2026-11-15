using System.Net;

namespace FM27Access.Navigation;

/// <summary>The inherited waiting document also recovers from temporary startup stalls.</summary>
public static class SurfaceStartupHtml
{
    public static string Build(string message = null)
    {
        string heading = message == null ? "Waiting for the game to load" : "FM27 Access";
        string status = WebUtility.HtmlEncode(message ?? "Connected. Waiting for the game to be ready.");
        return "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>FM27 Access</title></head><body>"
            + "<main><h1>" + heading + "</h1><p id=\"status\" role=\"status\" aria-live=\"polite\">" + status + "</p></main>"
            + "<script>" + RecoveryScript + "</script></body></html>";
    }

    // Read-only health polling must not queue Unity work or reload repeatedly on
    // an unavailable /version response. One bounded request at a time, no actions.
    internal const string RecoveryScript = @"
(function waitForGame(){
 var controller=new AbortController(),timeout=setTimeout(function(){controller.abort();},3000),ready=false;
 fetch('/health',{signal:controller.signal,cache:'no-store'})
 .then(function(r){if(!r.ok)throw new Error('Health unavailable');return r.json();})
 .then(function(h){if(h.screen==='Live'){ready=true;location.reload();}})
 .catch(function(){})
 .finally(function(){clearTimeout(timeout);if(!ready)setTimeout(waitForGame,1500);});
})();";
}
