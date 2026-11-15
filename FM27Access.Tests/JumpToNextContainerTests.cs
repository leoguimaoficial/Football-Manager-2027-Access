using FM27Access.Navigation;

namespace FM27Access.Tests;

public class JumpToNextContainerTests : IDisposable
{
    private readonly List<string> _spoken = new();
    private readonly List<FakeElement> _elements = new();
    private readonly TestHostSpec _host;

    public JumpToNextContainerTests()
    {
        _host = new TestHostSpec
        {
            Speak = text => _spoken.Add(text),
            FindFirst = _ => _elements.Count > 0 ? _elements[0] : null,
            FindNext = (current, direction) =>
            {
                int idx = _elements.IndexOf((FakeElement)current);
                if (idx < 0) return null;
                int next = direction == NavigationDirection.Down ? idx + 1 : idx - 1;
                if (next < 0 || next >= _elements.Count) return null;
                return _elements[next];
            },
            EnsureContext = _ => { },
        };
        _host.Apply();
    }

    public void Dispose() => NavigationManager.ResetForReload();

    private void SetupAndMoveTo(int cursorIndex, params FakeElement[] elements)
    {
        _elements.Clear();
        _elements.AddRange(elements);

        NavigationManager.OnPanelReady(new object(), "Test", false);
        // Move cursor to desired position
        for (int i = 0; i < cursorIndex; i++)
            NavigationManager.MoveCursor(NavigationDirection.Down);
        _spoken.Clear();
    }

    private static Func<IAccessibleElement, IContainerContext> MakeTableFinder(
        Dictionary<FakeElement, object> tableScopes)
    {
        var contexts = new Dictionary<object, FakeTableContext>();
        return element =>
        {
            if (tableScopes.TryGetValue((FakeElement)element, out var scope))
            {
                if (!contexts.TryGetValue(scope, out var ctx))
                {
                    ctx = new FakeTableContext(scope);
                    contexts[scope] = ctx;
                }
                return ctx;
            }
            return null;
        };
    }

    [Fact]
    public void Forward_FromOutsideTable_LandsOnFirstTableElement()
    {
        var table1Scope = new object();
        var e0 = new FakeElement("header", "text");
        var e1 = new FakeElement("Name", "column header");
        var e2 = new FakeElement("Player 1", "cell");

        var finder = MakeTableFinder(new Dictionary<FakeElement, object>
        {
            { e1, table1Scope },
            { e2, table1Scope },
        });

        SetupAndMoveTo(0, e0, e1, e2);

        NavigationManager.JumpToNextContainer(finder, true, "no more tables");

        Assert.Single(_spoken);
        Assert.Contains("Name", _spoken[0]);
    }

    [Fact]
    public void Forward_FromInsideTable_SkipsToNextTable()
    {
        var table1 = new object();
        var table2 = new object();
        var e0 = new FakeElement("Col A", "column header");
        var e1 = new FakeElement("Row 1", "cell");
        var e2 = new FakeElement("between", "text");
        var e3 = new FakeElement("Col B", "column header");
        var e4 = new FakeElement("Row 2", "cell");

        var finder = MakeTableFinder(new Dictionary<FakeElement, object>
        {
            { e0, table1 }, { e1, table1 },
            { e3, table2 }, { e4, table2 },
        });

        SetupAndMoveTo(0, e0, e1, e2, e3, e4);

        NavigationManager.JumpToNextContainer(finder, true, "no more tables");

        Assert.Single(_spoken);
        Assert.Contains("Col B", _spoken[0]);
    }

    [Fact]
    public void Forward_NoMoreTables_SpeaksNotFound()
    {
        var table1 = new object();
        var e0 = new FakeElement("Col A", "column header");
        var e1 = new FakeElement("Row 1", "cell");
        var e2 = new FakeElement("footer", "text");

        var finder = MakeTableFinder(new Dictionary<FakeElement, object>
        {
            { e0, table1 }, { e1, table1 },
        });

        SetupAndMoveTo(0, e0, e1, e2);

        NavigationManager.JumpToNextContainer(finder, true, "no more tables");

        Assert.Single(_spoken);
        Assert.Equal("no more tables", _spoken[0]);
    }

    [Fact]
    public void Forward_NoTablesOnScreen_SpeaksNotFound()
    {
        var e0 = new FakeElement("header", "text");
        var e1 = new FakeElement("save", "button");

        var finder = MakeTableFinder(new Dictionary<FakeElement, object>());

        SetupAndMoveTo(0, e0, e1);

        NavigationManager.JumpToNextContainer(finder, true, "no more tables");

        Assert.Single(_spoken);
        Assert.Equal("no more tables", _spoken[0]);
    }

    [Fact]
    public void Backward_FromInsideTable_LandsOnFirstElementOfPreviousTable()
    {
        var table1 = new object();
        var table2 = new object();
        var e0 = new FakeElement("Col A", "column header");
        var e1 = new FakeElement("Row 1", "cell");
        var e2 = new FakeElement("between", "text");
        var e3 = new FakeElement("Col B", "column header");
        var e4 = new FakeElement("Row 2", "cell");

        var finder = MakeTableFinder(new Dictionary<FakeElement, object>
        {
            { e0, table1 }, { e1, table1 },
            { e3, table2 }, { e4, table2 },
        });

        // Cursor on Row 2 (last element of table 2)
        SetupAndMoveTo(4, e0, e1, e2, e3, e4);

        NavigationManager.JumpToNextContainer(finder, false, "no more tables");

        Assert.Single(_spoken);
        // Should land on Row 1 (last element of table 1 when walking backward)
        Assert.Contains("Row 1", _spoken[0]);
    }

    [Fact]
    public void Backward_FromOutsideTable_LandsOnLastElementOfPreviousTable()
    {
        var table1 = new object();
        var e0 = new FakeElement("Col A", "column header");
        var e1 = new FakeElement("Row 1", "cell");
        var e2 = new FakeElement("footer", "text");

        var finder = MakeTableFinder(new Dictionary<FakeElement, object>
        {
            { e0, table1 }, { e1, table1 },
        });

        // Cursor on footer
        SetupAndMoveTo(2, e0, e1, e2);

        NavigationManager.JumpToNextContainer(finder, false, "no more tables");

        Assert.Single(_spoken);
        Assert.Contains("Row 1", _spoken[0]);
    }

    [Fact]
    public void NoCursor_IsNoOp()
    {
        _host.FindFirst = _ => null;
        _host.Apply();
        NavigationManager.OnPanelReady(new object(), "Empty", false);
        _spoken.Clear();

        var finder = MakeTableFinder(new Dictionary<FakeElement, object>());
        NavigationManager.JumpToNextContainer(finder, true, "no more tables");

        Assert.Empty(_spoken);
    }

    [Fact]
    public void AdjacentTables_Forward_LandsOnSecondTable()
    {
        var table1 = new object();
        var table2 = new object();
        var e0 = new FakeElement("Col A", "column header");
        var e1 = new FakeElement("Col B", "column header");

        var finder = MakeTableFinder(new Dictionary<FakeElement, object>
        {
            { e0, table1 },
            { e1, table2 },
        });

        SetupAndMoveTo(0, e0, e1);

        NavigationManager.JumpToNextContainer(finder, true, "no more tables");

        Assert.Single(_spoken);
        Assert.Contains("Col B", _spoken[0]);
    }

    internal class FakeElement : IAccessibleElement
    {
        private readonly string _label;
        public string Role { get; }
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public FakeElement(string label, string role)
        {
            _label = label;
            Role = role;
        }
        public string GetLabel() => _label;
        public string GetState() => "";
        public string GetAnnouncement() => $"{_label}, {Role}";
        public string GetPrimaryLabel() => _label;
        public string GetDescription() => "";
        public void Activate() { }
        public bool IsVisible() => true;
    }

    private class FakeTableContext : IContainerContext
    {
        public object Scope { get; }
        public FakeTableContext(object scope) => Scope = scope;
        public IAccessibleElement GetFirstChild() => null;
        public bool HandlesDirection(NavigationDirection direction) => true;
        public bool ExitsOnBoundary(NavigationDirection direction) => true;
        public IAccessibleElement Navigate(IAccessibleElement current, NavigationDirection direction) => null;
    }
}
