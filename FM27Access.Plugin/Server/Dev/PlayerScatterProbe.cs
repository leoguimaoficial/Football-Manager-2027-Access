using System.Globalization;
using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only: the open player scatter card's points as PlayerScatterPoints reads them (values, ours or
/// not, whether the point's own player button was found). <c>?click=N</c> presses the game's button
/// of our Nth player (0-based), to see what the point opens. GET /playerscatter.
/// </summary>
internal static class PlayerScatterProbe
{
    internal static string Run(int click)
    {
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel is not VisualElement root) continue;
            var container = ScreenWalker.FindDescendantNamed(root, "scatter_chart");
            if (container == null) continue;
            var points = PlayerScatterPoints.Read(container, out var reason);
            if (points == null) return "read failed: " + reason + "\n";
            var sb = new StringBuilder();
            int ours = 0;
            foreach (var p in points)
            {
                sb.Append(p.Ours ? "OURS " : "     ").Append(p.X.ToString("0.00", CultureInfo.InvariantCulture)).Append(", ")
                  .Append(p.Y.ToString("0.00", CultureInfo.InvariantCulture)).Append(p.Button == null ? "  (no button)" : "").Append('\n');
                if (p.Ours && ours++ == click && p.Button != null)
                {
                    p.Button.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
                    sb.Append("  ^ clicked\n");
                }
            }
            sb.Append(points.Count).Append(" points, ").Append(ours).Append(" ours\n");
            return sb.ToString();
        }
        return "no scatter chart on the rendered panels\n";
    }
}
