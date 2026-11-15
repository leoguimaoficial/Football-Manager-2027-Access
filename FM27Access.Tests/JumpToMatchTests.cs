using FM27Access.Navigation;

namespace FM27Access.Tests;

public class JumpToMatchTests : IDisposable
{
    private readonly List<string> _spoken = new();
    private readonly List<FakeElement> _elements = new();
    private readonly TestHostSpec _host;

    public JumpToMatchTests()
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

    private void SetupElements(params (string label, string role)[] specs)
    {
        _elements.Clear();
        foreach (var (label, role) in specs)
            _elements.Add(new FakeElement(label, role));

        NavigationManager.OnPanelReady(new object(), "Test", false);
        _spoken.Clear();
    }

    [Fact]
    public void Forward_FindsNextDropdown()
    {
        SetupElements(
            ("Name", "text field"),
            ("Season", "dropdown"),
            ("League", "dropdown"));

        NavigationManager.JumpToMatch(e => e.Role == "dropdown", true, "no more dropdowns");

        Assert.Single(_spoken);
        Assert.Contains("Season", _spoken[0]);
    }

    [Fact]
    public void Forward_SkipsNonMatchingElements()
    {
        SetupElements(
            ("Name", "text field"),
            ("Save", "button"),
            ("League", "dropdown"));

        NavigationManager.JumpToMatch(e => e.Role == "dropdown", true, "no more dropdowns");

        Assert.Single(_spoken);
        Assert.Contains("League", _spoken[0]);
    }

    [Fact]
    public void Backward_FindsPreviousDropdown()
    {
        SetupElements(
            ("Season", "dropdown"),
            ("Save", "button"),
            ("Current", "text field"));

        // Move cursor to last element
        NavigationManager.MoveCursor(NavigationDirection.Down);
        NavigationManager.MoveCursor(NavigationDirection.Down);
        _spoken.Clear();

        NavigationManager.JumpToMatch(e => e.Role == "dropdown", false, "no more dropdowns");

        Assert.Single(_spoken);
        Assert.Contains("Season", _spoken[0]);
    }

    [Fact]
    public void NoMatch_SpeaksNotFoundMessage()
    {
        SetupElements(
            ("Name", "text field"),
            ("Save", "button"));

        NavigationManager.JumpToMatch(e => e.Role == "dropdown", true, "no more dropdowns");

        Assert.Single(_spoken);
        Assert.Equal("no more dropdowns", _spoken[0]);
    }

    [Fact]
    public void NoCursor_IsNoOp()
    {
        _host.FindFirst = _ => null;
        _host.Apply();
        NavigationManager.OnPanelReady(new object(), "Empty", false);
        _spoken.Clear();

        NavigationManager.JumpToMatch(e => e.Role == "dropdown", true, "no more dropdowns");

        Assert.Empty(_spoken);
    }

    [Fact]
    public void AlreadyOnLastDropdown_SpeaksNotFound()
    {
        SetupElements(
            ("Season", "dropdown"),
            ("Save", "button"));

        // Cursor is on Season (first element, which is the dropdown)
        // Jump forward — no more dropdowns after it
        NavigationManager.JumpToMatch(e => e.Role == "dropdown", true, "no more dropdowns");

        Assert.Single(_spoken);
        Assert.Equal("no more dropdowns", _spoken[0]);
    }

    [Fact]
    public void MatchesSearchableDropdown()
    {
        SetupElements(
            ("Name", "text field"),
            ("Country", "searchable dropdown"));

        NavigationManager.JumpToMatch(
            e => e.Role == "dropdown" || e.Role == "searchable dropdown",
            true, "no more dropdowns");

        Assert.Single(_spoken);
        Assert.Contains("Country", _spoken[0]);
    }

    private class FakeElement : IAccessibleElement
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
}
