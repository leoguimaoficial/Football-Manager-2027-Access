using System;
using FM27Access.Navigation;

namespace FM27Access.Plugin.Server;

/// <summary>
/// One registered HTTP endpoint on the accessibility surface. A route owns its content
/// type, the HTTP method it answers, the body it serves while the game is still loading
/// (before the main-thread dispatcher is Ready), and the handler that produces the response
/// once Ready. The handler receives the query string and the request body (the body is empty
/// for GET routes and the companion's query-string routes; dev POST routes such as /eval read
/// it) and returns the body plus an optional extra header (only /body uses one, for X-FM-Token).
///
/// Routing is data: the request loop looks a path up in the table, with one designated
/// fallback for unknown paths. The only endpoint that is NOT a Route is /events, which keeps
/// its socket open and is handled explicitly.
/// </summary>
internal sealed class Route
{
    public string ContentType;
    public string Method = "GET";
    public Func<string> LoadingBody;
    // Args are (query, body); body is empty for GET and query-string routes, read by dev POSTs.
    public Func<string, string, (string body, string header)> Handler;

    // When true the handler runs even before the dispatcher is Ready (it does not touch live game
    // state through MainThreadDispatcher). /reload uses this so a fix can be reloaded even when a
    // prior reload's Mount failed and the mod never reached Ready. Normal routes leave it false and
    // serve LoadingBody until Ready.
    public bool AlwaysReady;

    /// <summary>
    /// The body served when the surface cannot read the game for a reason that is NOT loading — the
    /// core stopped, its tick is failing, or the game stopped running frames. Receives the sentence the
    /// player should be told and, by default, serves exactly that; a route whose content type needs
    /// more than text (the HTML page builders) overrides this to wrap it.
    /// </summary>
    public Func<string, string> UnavailableBody = message => message;
}
