using System.Collections.Generic;
using System.Text;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// In-game player cards (match view): the inline-disclosure dropdown feature. Everything
/// player-card lives here — the route, the renderer contribution, and the handle. The card
/// is a button with the live player label; activating it opens the card's child SIDropdown
/// session and injects that dropdown's widget into the panel below (the actual panel HTML is
/// built by the testable DropdownHtml.BuildPlayerPanel and driven by DropdownSessionManager).
/// </summary>
internal static class PlayerCard
{
    public static void RegisterRoutes(Dictionary<string, Route> routes)
    {
        routes["/player/panel"] = new Route
        {
            ContentType = "text/html",
            LoadingBody = () => "The game is still loading.",
            Handler = (q, b) => (DropdownSessionManager.OpenPanel(Query.Str(q, "id")), null),
        };
    }

    // A squad player card: a button with the live card label that, on activate, opens the
    // dropdown session and injects the child dropdown into the panel below it. Only one
    // card's panel is open at a time (enforced client-side and by the single server session).
    public static void Render(StringBuilder sb, Adapters.SquadPlayerButtonAdapter player)
    {
        string id = ServerHandles.Register(new PlayerMenuHandle { Player = player }, player);
        LiveValues.Track(id, player);
        sb.Append("<p><button type=\"button\" id=\"").Append(id)
          .Append("\" aria-expanded=\"false\" onclick=\"fmplayer('").Append(id).Append("')\">")
          .Append(ServerText.Escape(DisplayText.Of(player))).Append("</button></p>");
        sb.Append("<div id=\"").Append(id).Append("-panel\" class=\"fm-player-panel\"></div>");
    }
}

internal sealed class PlayerCardRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el)
        => ServerText.SafeRole(el) == "button" && el is Adapters.SquadPlayerButtonAdapter;

    public void Render(StringBuilder sb, IAccessibleElement el)
        => PlayerCard.Render(sb, (Adapters.SquadPlayerButtonAdapter)el);
}

// A squad player card. Its menu lives in a child SIDropdown, reached only through the
// adapter; the card itself is never activated (the HTML surface opens the menu via the
// dropdown-session widget, like every other dropdown). So Activate stays inert and the
// handle exists only to resolve back to the child dropdown in ResolveDropdownHandle.
internal sealed class PlayerMenuHandle : ServerHandles.Handle
{
    public Adapters.SquadPlayerButtonAdapter Player;

    public override bool TryValidate(out string error)
    {
        error = null;
        var be = Player.BackingElement;
        if (be.panel == null || !ElementBridge.IsVisibleInHierarchy(be))
        {
            error = "That player is no longer on screen. Refresh.";
            return false;
        }
        return true;
    }

    public override bool TryGetDropdownAdapter(out Adapters.DropdownAdapter adapter, out string error)
    {
        adapter = Player.GetChildDropdownAdapter();
        error = null;
        return true;
    }
}
