namespace FM27Access.Navigation.Profiles;

/// <summary>
/// Structural properties of a navigation stop, extracted by the Plugin layer
/// and matched against by profile rules. All fields are stable across data changes.
/// </summary>
public readonly struct ElementMatchInfo
{
    /// <summary>The element's name attribute from the UI template (e.g. "ClubButton").</summary>
    public readonly string Name;

    /// <summary>The C# type name of the element (e.g. "SIButton", "SIText").</summary>
    public readonly string TypeName;

    /// <summary>The mod's classification (e.g. "Button", "Text", "Checkbox").</summary>
    public readonly string Classification;

    /// <summary>
    /// Ancestor path fragment — a string representation of the element's ancestor chain.
    /// Used when the element itself has no name.
    /// </summary>
    public readonly string AncestorPath;

    /// <summary>The name attribute of the element's immediate parent.</summary>
    public readonly string ParentName;

    /// <summary>
    /// The element's index within its parent's hierarchy children; -1 when unknown.
    /// The last-resort discriminator for template siblings that share name, type, and
    /// path (a row title and its description are often both SIText("text") under the
    /// same frame, differing only by position).
    /// </summary>
    public readonly int ChildIndex;

    public ElementMatchInfo(string name, string typeName, string classification, string ancestorPath, string parentName = "", int childIndex = -1)
    {
        Name = name ?? "";
        TypeName = typeName ?? "";
        Classification = classification ?? "";
        AncestorPath = ancestorPath ?? "";
        ParentName = parentName ?? "";
        ChildIndex = childIndex;
    }
}
