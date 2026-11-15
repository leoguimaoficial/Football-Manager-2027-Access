using FM27Access.Navigation;
using SI.Bindable;
using UnityEngine.UIElements;

namespace FM27Access.Plugin;

/// <summary>
/// Locates the game's context-sensitive continue button: the single SIButton named
/// "continue-button" that relabels itself with the current flow action ("Continue", the
/// team-talk and match-flow steps, ...) wherever it currently lives. One live scan shared by
/// the speech surface's Tab/Shift+Tab (via the navigation host capability) and the HTML
/// surface's "\" / Alt+Enter commands (via ContinueSurface).
/// </summary>
internal static class ContinueButtonLocator
{
    internal static ContinueButtonHit? Find()
    {
        var pm = PanelManager.Instance;
        var layers = FM27PanelCatalog.GetLayers(pm);
        for (int i = 0; i < layers.Count; i++)
        {
            var openPanels = layers[i].Open;
            for (int j = 0; j < openPanels.Count; j++)
            {
                var panel = openPanels[j];
                var btn = panel.Q<SI.Bindable.SIButton>("continue-button");
                if (btn != null && ElementBridge.IsVisible(btn))
                {
                    var title = Server.ServerText.StripUnresolvedEntityTokens(panel.PanelID.Title.TranslatedString);
                    if (string.IsNullOrEmpty(title))
                        title = panel.PanelID.name ?? "unknown panel";
                    var classification = ElementBridge.ClassifyElement(btn, null);
                    if (!TreeWalker.NavigationStops.Contains(classification)) return null;
                    var adapter = ElementBridge.CreateAdapter(btn, classification);
                    return new ContinueButtonHit(adapter, panel, title);
                }
            }
        }
        return null;
    }
}
