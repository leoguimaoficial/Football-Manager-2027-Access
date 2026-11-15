namespace FM27Access.Plugin.Contexts;

// A missing body-row binding means its virtualised shell is not populated.
// An optional footer can legitimately contain no binding or content at all.
// Actual pending bindings remain pending in both contexts.
internal readonly record struct NativeBindingReadiness(int ObservedBindings, int PendingBindings)
{
    internal int PendingRowCells => ObservedBindings == 0 ? 1 : PendingBindings;
    internal bool OptionalContentPending => PendingBindings > 0;
}
