using UnityEngine;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// Synthesises a real mouse press-release over an element. Some game widgets — the context-menu trigger
/// (PluginContextMenuBindings) and the cascading-menu EntryElements it opens — are driven by a
/// manipulator that only acts on a genuine pointer click and validates the live cursor position, so a
/// bare ClickEvent or NavigationSubmitEvent is ignored. The pooled pointer events must therefore carry
/// the element's real screen rect and the right button. Verified live: <see cref="LeftClick"/> opens the
/// squad Actions menu and selects its entries, where ClickEvent/PointerEvents with a default position do
/// nothing. <see cref="RightClick"/> is the right-button twin used to open per-element right-click menus
/// (see docs/reference/context-menus.md).
/// </summary>
internal static class SyntheticPointer
{
    public static void LeftClick(VisualElement element) => Click(element, button: 0);

    public static void RightClick(VisualElement element) => Click(element, button: 1);

    private static void Click(VisualElement element, int button)
    {
        Press(element, button);
        Release(element, button);
    }

    /// <summary>
    /// Send only the press half of a click. Splitting press from release lets a caller poll between them:
    /// some menus open on the press and capture the pointer, so sending the release too would read as a
    /// click-outside and dismiss the just-opened menu; others open on the release. See
    /// <c>ServerHandles.OpenContextMenu</c>, which presses, checks, and only releases if nothing opened.
    /// </summary>
    public static void Press(VisualElement element, int button)
    {
        var (pos, local) = At(element);
        var down = PointerDownEvent.GetPooled();
        down.pointerId = PointerId.mousePointerId;
        down.button = button;
        down.pressedButtons = 1 << button;   // bit 0 = left held, bit 1 = right held
        down.position = pos;
        down.localPosition = local;
        element.SendEvent(down);
    }

    /// <summary>Send only the release half of a click.</summary>
    public static void Release(VisualElement element, int button)
    {
        var (pos, local) = At(element);
        var up = PointerUpEvent.GetPooled();
        up.pointerId = PointerId.mousePointerId;
        up.button = button;
        up.pressedButtons = 0;               // released
        up.position = pos;
        up.localPosition = local;
        element.SendEvent(up);
    }

    private static (Vector3 pos, Vector3 local) At(VisualElement element)
    {
        var wb = element.worldBound;
        return (new Vector3(wb.center.x, wb.center.y, 0f), new Vector3(wb.width / 2f, wb.height / 2f, 0f));
    }
}
