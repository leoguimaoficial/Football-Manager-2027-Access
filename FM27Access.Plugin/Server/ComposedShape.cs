namespace FM27Access.Plugin.Server;

// Immutable receipt for the companion's last body, shared with the SSE reader.
// Kept separate from the Unity loop so completeness/reuse can be tested against
// the actual production policy without game fixtures or a second renderer.
internal sealed class ComposedShape
{
    public readonly string Shape;
    public readonly long AtMs;
    public readonly bool HasMarker;
    public ComposedShape(string shape,long atMs,bool hasMarker)
    { Shape=shape; AtMs=atMs; HasMarker=hasMarker; }

    internal bool CanSuppress(string currentShape,long now,long ttl)
        // A streamed compose can note the final native shape after its earlier
        // walk omitted late controls. A provisional body is not delivery proof.
        // Keep the existing retry budget; only complete receipts may suppress
        // the already scheduled native navigation/shape recovery.
        => !HasMarker && MatchesNativeShape(currentShape,now,ttl);

    // Adopting a structural baseline does not certify delivery/completeness.
    // A partial compose may itself change a virtualised table's native window.
    // Let the separate bounded marker retry recover its missing content; do
    // not turn its own harvest-induced shape into an endless refetch loop.
    internal bool MatchesNativeShape(string currentShape,long now,long ttl)
        => Shape==currentShape && now-AtMs<=ttl;
}
