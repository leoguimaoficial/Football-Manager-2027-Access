namespace FM27Access.Navigation.Profiles;

/// <summary>
/// Matches a navigation stop by its structural properties.
/// All specified fields must match (AND logic). Null/empty fields are ignored.
/// </summary>
public class StopMatcher
{
    /// <summary>Match by element name (exact match).</summary>
    public string Name { get; set; }

    /// <summary>Match by C# type name (exact match).</summary>
    public string TypeName { get; set; }

    /// <summary>Match by classification (exact match, case-insensitive).</summary>
    public string Classification { get; set; }

    /// <summary>Match by ancestor path (substring containment).</summary>
    public string PathContains { get; set; }

    /// <summary>Match by immediate parent element name (exact match).</summary>
    public string ParentName { get; set; }

    /// <summary>Match by ancestor path prefix (the path must start with this string).</summary>
    public string PathStartsWith { get; set; }

    /// <summary>
    /// Match by the element's index within its parent (exact match). The last-resort
    /// discriminator for template siblings identical in every other field; always pair
    /// it with a narrowing field. Never matches an info whose index is unknown (-1).
    /// </summary>
    public int? ChildIndex { get; set; }

    public bool Matches(ElementMatchInfo info)
    {
        if (!string.IsNullOrEmpty(Name) && !string.Equals(Name, info.Name, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrEmpty(TypeName) && !string.Equals(TypeName, info.TypeName, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrEmpty(Classification) && !string.Equals(Classification, info.Classification, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(PathContains) && info.AncestorPath.IndexOf(PathContains, StringComparison.Ordinal) < 0)
            return false;

        if (!string.IsNullOrEmpty(ParentName) && !string.Equals(ParentName, info.ParentName, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrEmpty(PathStartsWith) && !info.AncestorPath.StartsWith(PathStartsWith, StringComparison.Ordinal))
            return false;

        if (ChildIndex.HasValue && (info.ChildIndex < 0 || info.ChildIndex != ChildIndex.Value))
            return false;

        return true;
    }
}
