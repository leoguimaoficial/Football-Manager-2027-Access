namespace FM27Access.Navigation;

/// <summary>
/// Base class for IAccessibleElement adapters.
/// Provides default GetAnnouncement() composition. Subclasses override for custom formats.
/// </summary>
public abstract class AccessibleElementBase : IAccessibleElement
{
    public abstract string Role { get; }
    public abstract bool CanActivate { get; }

    // Most elements have no right-click menu. PluginAdapterBase overrides to check the live ancestry.
    public virtual bool HasContextMenu => false;
    public abstract string GetLabel();
    public abstract string GetState();
    public abstract bool IsVisible();

    public abstract void Activate();

    public virtual string GetAnnouncement()
    {
        var label = GetLabel();
        var state = GetState();
        var hasLabel = !string.IsNullOrEmpty(label);
        var hasState = !string.IsNullOrEmpty(state);

        if (hasLabel && hasState)
            return $"{label}, {Role}, {state}";
        if (hasLabel)
            return $"{label}, {Role}";
        if (hasState)
            return $"{Role}, {state}";
        return Role;
    }

    // Most elements have a single, indivisible label and no secondary text. Buttons, whose
    // label can be a primary action plus a long descriptive tail, override both.
    public virtual string GetPrimaryLabel() => GetLabel();

    public virtual string GetDescription() => "";
}
