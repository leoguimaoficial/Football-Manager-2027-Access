using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

/// <summary>
/// Raised when a bounded read runs out of budget. Carries what it managed to see so the failure
/// names its own size instead of just asserting it was too big.
/// </summary>
internal sealed class WalkBudgetExceededException : Exception
{
    public WalkBudgetExceededException(string what, int visited, string limit)
        : base($"{what} exceeded its walk budget after {visited} elements ({limit})")
    {
        What = what;
        Visited = visited;
    }

    public string What { get; }
    public int Visited { get; }
}

/// <summary>
/// A ceiling on one read of the game's element tree, in elements and in wall-clock time.
///
/// Such a read runs on the Unity main thread, so its cost is not the mod's alone to spend: while it
/// runs, the game renders no frames. An unbounded read is therefore an unbounded freeze, and a read
/// written against a small non-virtualized list becomes one the moment that assumption stops holding.
///
/// The budget is <b>ambient</b> — installed by the caller for a whole operation, charged by every
/// walk underneath it — rather than passed to individual methods. That is a correction, not a
/// preference: budgeting call sites one at a time was tried and the freeze simply moved to the
/// neighbouring line in the same method, three times over. A ceiling that only some reads honour is
/// not a ceiling.
///
/// <b>What this cannot do.</b> Charging happens between steps, so a budget bounds work that is spread
/// across many calls. It cannot interrupt a single call that blocks inside the game — nothing on the
/// main thread can. If a freeze survives this, that is the finding: the time is inside one game call,
/// not in the mod's walking, and the fix has to be to stop making that call at that moment.
/// </summary>
internal sealed class WalkBudget : IDisposable
{
    /// <summary>Reading the clock per element would cost more than the walk; per block is enough.</summary>
    private const int ClockCheckInterval = 256;

    [ThreadStatic] private static WalkBudget _current;

    private readonly WalkBudget _previous;
    private readonly string _what;
    private readonly int _maxElements;
    private readonly int _maxMs;
    private readonly long _deadline;
    private int _visited;

    private WalkBudget(string what, int maxElements, int maxMs)
    {
        _what = what;
        _maxElements = maxElements;
        _maxMs = maxMs;
        _deadline = Environment.TickCount64 + maxMs;
        _previous = _current;
        _current = this;
    }

    public void Dispose() => _current = _previous;

    public int Visited => _visited;

    /// <summary>
    /// Run <paramref name="body"/> with a budget installed for this thread. Main-thread callers only:
    /// the budget is thread-static, so it must be opened inside the dispatched action, not around it.
    /// </summary>
    public static T Run<T>(string what, int maxElements, int maxMs, Func<T> body)
    {
        using var budget = new WalkBudget(what, maxElements, maxMs);
        return body();
    }

    /// <summary>
    /// Charge one element against whatever budget is in force. A no-op when none is — reads outside a
    /// budgeted operation keep their old behaviour rather than failing in a new way.
    /// </summary>
    public static void Charge() => _current?.Visit();

    private void Visit()
    {
        _visited++;
        if (_visited > _maxElements)
            throw new WalkBudgetExceededException(_what, _visited, $"limit {_maxElements} elements");
        if (_visited % ClockCheckInterval == 0 && Environment.TickCount64 > _deadline)
            throw new WalkBudgetExceededException(_what, _visited, $"limit {_maxMs}ms");
    }

    /// <summary>Depth-first name search, charged against the ambient budget.</summary>
    public static VisualElement FindByName(VisualElement parent, string name)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            Charge();
            var child = parent.hierarchy[i];
            if (child.name == name) return child;
            var deeper = FindByName(child, name);
            if (deeper != null) return deeper;
        }
        return null;
    }
}
