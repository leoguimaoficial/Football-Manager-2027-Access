using System.Text;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The Tactics Planner slot strip (template "tactics-picker", also hosted by the full-screen
/// Team Selection panel). The game renders it as a segmented control: one numbered chip per
/// inactive tactic slot, an active block showing the current slot's number + tactic name + its
/// options menu, and an Add block. Rendered generically the page reads as disconnected
/// fragments — a button "1", a text "2", a text "4 3 3 root 1 basic" — with no clue they are a
/// slot system. These renderers compose the sighted user's mental model into the labels:
/// "Switch to tactic slot 1, button" and a single "Active tactic, slot 2: 4 3 3 root 1 basic"
/// stop. The adjacent menus keep their profile labels ("Tactic options", "Add tactic").
///
/// HTML surface only: the speech cursor still reads the raw fragments.
/// </summary>
internal static class TacticSlots
{
    // The slot strip's template root; every stop these renderers own sits under it.
    private const string PickerRoot = "tactics-picker";
    // The active slot's block, holding the "number" and "text-icon" children.
    private const string ActiveBlock = "buttons-hero-toggle-buttons-active";

    internal static bool HasAncestorNamed(VisualElement el, string name, int maxDepth)
    {
        var current = el.hierarchy.parent;
        for (int depth = 0; current != null && depth < maxDepth; depth++)
        {
            if (current.name == name) return true;
            current = current.hierarchy.parent;
        }
        return false;
    }

    // First non-empty text in the subtree, cleaned of rich-text markup. Null when the subtree
    // holds no text.
    internal static string FirstText(VisualElement root)
    {
        try
        {
            var textEl = root.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrWhiteSpace(textEl.text))
                return ServerText.Clean(textEl.text);
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[TacticSlots] TryCast<TextElement> failed for '{root.name}': {e.GetType().Name}");
        }
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FirstText(root.hierarchy[i]);
            if (found != null) return found;
        }
        return null;
    }

    // The active block ("buttons-hero-toggle-buttons-active") this element sits under, or null.
    internal static VisualElement ActiveBlockOf(VisualElement el)
    {
        var current = el.hierarchy.parent;
        for (int depth = 0; current != null && depth < 4; depth++)
        {
            if (current.name == ActiveBlock) return current;
            current = current.hierarchy.parent;
        }
        return null;
    }

    // The named direct-or-deep child, bounded to the block's own small subtree.
    internal static VisualElement FindChildNamed(VisualElement root, string name, int maxDepth)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (child.name == name) return child;
            if (maxDepth > 0)
            {
                var deeper = FindChildNamed(child, name, maxDepth - 1);
                if (deeper != null) return deeper;
            }
        }
        return null;
    }
}

/// <summary>
/// An inactive slot chip: an SIButton under the picker strip (outside the active block) whose
/// visible content is just the slot number. Clicking it makes that slot's tactic the active
/// one (the game's SetTacticSlot), so the label says so: "Switch to tactic slot 1".
/// </summary>
internal sealed class TacticSlotChipRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el)
    {
        if (ServerText.SafeRole(el) != "button") return false;
        if (ServerText.IsDisabled(el)) return false;
        if (el is not Adapters.PluginAdapterBase a) return false;
        var backing = a.BackingElement;
        if (!TacticSlots.HasAncestorNamed(backing, "tactics-picker", 6)) return false;
        if (TacticSlots.ActiveBlockOf(backing) != null) return false;
        return TacticSlots.FirstText(backing) != null;
    }

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        var slot = TacticSlots.FirstText(((Adapters.PluginAdapterBase)el).BackingElement);
        string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
        // Not value-tracked: the values channel would push the raw chip text ("1") over this
        // composed label. The strip re-renders on the post-action settle window instead.
        sb.Append("<p><button type=\"button\" id=\"").Append(id)
          .Append("\" onclick=\"act('").Append(id).Append("')\">Switch to tactic slot ")
          .Append(ServerText.EscapeText(slot)).Append("</button></p>");
    }
}

/// <summary>
/// The active slot's tactic-name text. Composed with the sibling slot number into the one stop
/// that explains the strip: "Active tactic, slot 2: 4 3 3 root 1 basic". The bare number stop
/// itself is suppressed by <see cref="TacticSlotNumberRenderer"/>.
/// </summary>
internal sealed class TacticSlotActiveRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el)
    {
        if (ServerText.SafeRole(el) != "text") return false;
        if (el is not Adapters.PluginAdapterBase a) return false;
        var backing = a.BackingElement;
        if (backing.name != "text") return false;
        var parent = backing.hierarchy.parent;
        if (parent == null || parent.name != "text-icon") return false;
        return TacticSlots.ActiveBlockOf(backing) != null;
    }

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        var backing = ((Adapters.PluginAdapterBase)el).BackingElement;
        string name = ServerText.Escape(DisplayText.Of(el));

        string slot = null;
        var block = TacticSlots.ActiveBlockOf(backing);
        var numberEl = block != null ? TacticSlots.FindChildNamed(block, "number", 2) : null;
        if (numberEl != null) slot = TacticSlots.FirstText(numberEl);

        // Not value-tracked (see TacticSlotChipRenderer); a slot switch re-renders via the
        // settle window.
        sb.Append("<p>Active tactic");
        if (slot != null) sb.Append(", slot ").Append(ServerText.EscapeText(slot));
        sb.Append(": ").Append(name).Append("</p>");
    }
}

/// <summary>
/// The active slot's bare number text ("2"): folded into
/// <see cref="TacticSlotActiveRenderer"/>'s label, so its own stop renders nothing — but only
/// while the sibling tactic-name text is actually present to carry it (a missing name would
/// otherwise silently drop the slot number from the page).
/// </summary>
internal sealed class TacticSlotNumberRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el)
    {
        if (ServerText.SafeRole(el) != "text") return false;
        if (el is not Adapters.PluginAdapterBase a) return false;
        var backing = a.BackingElement;
        if (!TacticSlots.HasAncestorNamed(backing, "number", 2)) return false;
        var block = TacticSlots.ActiveBlockOf(backing);
        if (block == null) return false;
        var nameEl = TacticSlots.FindChildNamed(block, "text-icon", 2);
        return nameEl != null && TacticSlots.FirstText(nameEl) != null;
    }

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        // Intentionally empty: the number is spoken inside the active-tactic label.
    }
}
