using System;
using System.Collections.Generic;
using FM27Access.Navigation;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// One entry of a cascading context menu, backed by the menu's data model
/// (<c>SI.UI.MenuDescription.Entry</c>) rather than a live VisualElement. The whole menu tree is read
/// from <see cref="Server.ContextMenuSurface"/>, so the companion can browse every folder and item
/// without the game visually opening submenus (which only happens on hover and crashes when
/// synthesised). A folder entry drills the surface into its children; a leaf item invokes the entry's
/// action; the synthetic "Back" entry steps out a level. The action is supplied as a closure so this
/// type stays free of the menu plumbing.
///
/// Not a PluginAdapterBase: it has no backing element, so the handle layer keys it by role+label and
/// skips the on-screen validation, exactly as intended for a synthesised stop.
/// </summary>
internal sealed class MenuDataEntryAdapter : IAccessibleElement
{
    private readonly string _label;
    private readonly bool _enabled;
    private readonly bool _isFolder;
    private readonly Action _action;

    /// <summary>
    /// The menu's dismiss stop ("Close menu") — the consistent first stop of every menu view. The
    /// HTML renderer marks it (data-fm-menu-first) so the companion lands screen-reader focus on it
    /// when a menu opens, giving every open the same starting point.
    /// </summary>
    public bool IsMenuDismiss { get; init; }

    /// <summary>
    /// The synthetic "Back" stop present in a submenu — the step-out-one-level entry. The HTML
    /// renderer marks it (data-fm-menu-back) so the companion's Left-arrow can drive it, and so the
    /// arrow-navigation controller can tell it apart from a real menu action.
    /// </summary>
    public bool IsMenuBack { get; init; }

    /// <summary>True when this entry opens a submenu (a folder) rather than invoking an action.</summary>
    public bool IsSubmenu => _isFolder;

    /// <summary>
    /// True when the game has ticked this entry (<c>MenuDescription.EntryFlags.Selected</c>, rendered
    /// as a tick by <c>CascadingMenu.EntryElement.SetSelected</c>).
    ///
    /// The flag only ever says "checked" — there is no companion "is checkable" flag, so an UNticked
    /// checkable entry is indistinguishable from a plain command. The surface therefore announces
    /// checked and stays silent otherwise: claiming "unchecked" on an entry that is simply a command
    /// would be inventing a state the game never expressed.
    /// </summary>
    public bool IsChecked { get; init; }

    public MenuDataEntryAdapter(string label, bool enabled, bool isFolder, Action action)
    {
        _label = label;
        _enabled = enabled;
        _isFolder = isFolder;
        _action = action;
    }

    public string Role => "button";
    public bool CanActivate => _enabled;
    // A menu entry is itself inside the open menu; it has no nested right-click menu of its own.
    public bool HasContextMenu => false;
    public string GetLabel() => _label;
    public string GetPrimaryLabel() => _label;
    public string GetDescription() => "";
    public bool IsVisible() => true;

    // A disabled entry reports it; a folder reports that it opens a submenu; a ticked entry reports
    // checked. Plain items report nothing extra.
    public string GetState()
    {
        if (!_enabled) return "disabled";
        if (_isFolder) return "submenu";
        return IsChecked ? "checked" : "";
    }

    public string GetAnnouncement()
    {
        var parts = new List<string> { _label, Role };
        var state = GetState();
        if (!string.IsNullOrEmpty(state)) parts.Add(state);
        return string.Join(", ", parts);
    }

    public void Activate()
    {
        if (!_enabled)
            throw new NotSupportedException("Disabled menu entries cannot be activated.");
        _action();
    }
}
