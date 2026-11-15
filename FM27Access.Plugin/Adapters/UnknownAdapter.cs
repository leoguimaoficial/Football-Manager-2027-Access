using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// Landing adapter for an element the classifier could not map to a known widget
/// (<see cref="Navigation.ElementClassification.Unknown"/>). Rather than halt the whole mod, the
/// cursor lands here and announces a best-effort label plus "unknown control" — an honest gap the
/// player understands, and a signal an agent can chase via /log and /unclassified.
///
/// Reads only safe Unity properties (child text, tooltip, name), so describing a widget the mod
/// explicitly does not understand can never itself throw. The element is not activated: we do not
/// know how to drive it correctly, and acting on a guess would be worse than admitting the gap.
/// See docs/reference/failure-handling.md.
/// </summary>
internal sealed class UnknownAdapter : PluginAdapterBase
{
    public UnknownAdapter(VisualElement element) : base(element) { }

    public override string Role => Speech.Degrade.UnknownControl;
    public override bool CanActivate => false;

    public override string GetLabel() => FindBestLabel() ?? "";

    public override string GetState() => "";

    public override void Activate() =>
        throw new NotSupportedException("Unknown control cannot be activated.");
}
