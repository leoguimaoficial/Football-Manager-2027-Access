using FM27Access.Navigation;

namespace FM27Access.Tests;

public class CursorPositionRestoreTests : IDisposable
{
    private readonly List<string> _spoken = new();
    private readonly List<FakeElement> _elements = new();
    private int _panelIdCounter;
    private readonly TestHostSpec _host;
    private Func<object, string, int, IAccessibleElement> _findByAnnouncement;

    public CursorPositionRestoreTests()
    {
        _findByAnnouncement = (panel, announcement, index) =>
        {
            int count = 0;
            foreach (var el in _elements)
            {
                if (el.GetAnnouncement() == announcement)
                {
                    count++;
                    if (count == index)
                        return el;
                }
            }
            return null;
        };

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
            GetPanelIdentity = panel => (string)panel,
            FindByAnnouncement = (panel, announcement, index) => _findByAnnouncement(panel, announcement, index),
            AnnouncementOccurrenceIndex = (panel, element, announcement) =>
            {
                int count = 0;
                foreach (var el in _elements)
                {
                    if (el.GetAnnouncement() == announcement)
                    {
                        count++;
                        if (el == element)
                            return count;
                    }
                }
                return 1;
            },
        };
        _host.Apply();
    }

    public void Dispose() => NavigationManager.ResetForReload();

    private string NewPanelId() => $"panel_{_panelIdCounter++}";

    [Fact]
    public void RestoresCursorWhenReturningToPanel()
    {
        var panelA = "PanelA";
        _elements.Clear();
        _elements.Add(new FakeElement("First", "button"));
        _elements.Add(new FakeElement("Second", "button"));
        _elements.Add(new FakeElement("Third", "button"));

        NavigationManager.OnPanelReady(panelA, "Panel A", false);
        // Move cursor to Third
        NavigationManager.MoveCursor(NavigationDirection.Down);
        NavigationManager.MoveCursor(NavigationDirection.Down);
        Assert.Equal("Third, button", NavigationManager.GetCurrentElement().GetAnnouncement());

        // Navigate to panel B
        var panelB = "PanelB";
        NavigationManager.OnPanelReady(panelB, "Panel B", false);
        _spoken.Clear();

        // Close panel B — should restore to panel A with cursor at "Third"
        NavigationManager.OnPanelClosed(panelB);

        Assert.Contains(_spoken, s => s.Contains("Third, button"));
    }

    [Fact]
    public void FallsBackToFirstStopWhenSavedElementNotFound()
    {
        var panelA = "PanelA";
        _elements.Clear();
        _elements.Add(new FakeElement("Alpha", "button"));
        _elements.Add(new FakeElement("Beta", "button"));

        NavigationManager.OnPanelReady(panelA, "Panel A", false);
        NavigationManager.MoveCursor(NavigationDirection.Down);

        // Navigate away
        var panelB = "PanelB";
        NavigationManager.OnPanelReady(panelB, "Panel B", false);

        // Change elements so saved announcement won't match
        _elements.Clear();
        _elements.Add(new FakeElement("Gamma", "button"));
        _elements.Add(new FakeElement("Delta", "button"));

        // Make FindStopByAnnouncementFn return null (element not found)
        _findByAnnouncement = (_, _, _) => null;

        _spoken.Clear();
        NavigationManager.OnPanelClosed(panelB);

        // Should fall back to first stop (Gamma)
        Assert.Contains(_spoken, s => s.Contains("Gamma, button"));
    }

    [Fact]
    public void DoesNotSaveWhenCursorIsNull()
    {
        var panelA = "PanelA";
        _elements.Clear(); // No elements — cursor will be null

        NavigationManager.OnPanelReady(panelA, "Panel A", false);

        // Navigate to panel B
        _elements.Add(new FakeElement("Item", "button"));
        var panelB = "PanelB";
        NavigationManager.OnPanelReady(panelB, "Panel B", false);

        _spoken.Clear();
        NavigationManager.OnPanelClosed(panelB);

        // Should go to first stop since nothing was saved for panel A
        Assert.Equal("Item, button", NavigationManager.GetCurrentElement()?.GetAnnouncement());
    }

    [Fact]
    public void EvictsOldestWhenOverFiveEntries()
    {
        // Create 6 panels, navigate to each with cursor at second element
        var panelIds = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            _elements.Clear();
            _elements.Add(new FakeElement($"First{i}", "button"));
            _elements.Add(new FakeElement($"Second{i}", "button"));

            var id = $"Panel{i}";
            panelIds.Add(id);
            NavigationManager.OnPanelReady(id, $"Panel {i}", false);
            NavigationManager.MoveCursor(NavigationDirection.Down);
        }

        // Panel0 should have been evicted (only 5 kept)
        // Go back to Panel0 — should NOT restore
        _elements.Clear();
        _elements.Add(new FakeElement("Fresh", "button"));
        _findByAnnouncement = (_, announcement, _) =>
        {
            // Panel0's saved announcement was "Second0, button"
            if (announcement == "Second0, button") return new FakeElement("Second0", "button");
            return null;
        };

        _spoken.Clear();
        // Simulate returning to Panel0
        NavigationManager.OnPanelReady(panelIds[0], "Panel 0", false);
        // Should get "Fresh" (first stop) since Panel0 was evicted
        Assert.Contains(_spoken, s => s.Contains("Fresh, button"));
    }

    [Fact]
    public void SwitchToPanelSavesCursorPosition()
    {
        _elements.Clear();
        _elements.Add(new FakeElement("Alpha", "button"));
        _elements.Add(new FakeElement("Beta", "button"));

        var panelA = "PanelA";
        NavigationManager.OnPanelReady(panelA, "Panel A", false);
        NavigationManager.MoveCursor(NavigationDirection.Down);

        // Set up panel cycling
        var panelB = "PanelB";
        _host.GetAllOpenPanels = () => new List<OpenPanel>
        {
            new(panelA, 1, "Panel A"),
            new(panelB, 1, "Panel B"),
        };
        _host.NavigableLayers = new HashSet<int> { 1 };
        _host.Apply();

        // Cycle to panel B
        NavigationManager.CyclePanelForward();
        _spoken.Clear();

        // Cycle back to panel A — should restore cursor to Beta
        NavigationManager.CyclePanelBackward();
        Assert.Contains(_spoken, s => s.Contains("Beta, button"));
    }

    [Fact]
    public void DeferredRestoreSucceedsWhenElementAppearsLater()
    {
        var panelA = "PanelA";
        _elements.Clear();
        _elements.Add(new FakeElement("Alpha", "button"));
        _elements.Add(new FakeElement("Beta", "button"));

        NavigationManager.OnPanelReady(panelA, "Panel A", false);
        NavigationManager.MoveCursor(NavigationDirection.Down);

        // Navigate to panel B
        var panelB = "PanelB";
        NavigationManager.OnPanelReady(panelB, "Panel B", false);

        // Return to panel A but make FindStopByAnnouncementFn return null (simulating table not loaded)
        _elements.Clear();
        _elements.Add(new FakeElement("Header", "text"));
        _findByAnnouncement = (_, _, _) => null;

        NavigationManager.OnPanelClosed(panelB);

        // Should have set NeedsCursorRestore
        Assert.True(NavigationManager.NeedsCursorRestore);
        // Cursor should be on first stop as fallback
        Assert.Equal("Header, text", NavigationManager.GetCurrentElement().GetAnnouncement());

        // Now simulate table loading — element becomes findable
        var betaElement = new FakeElement("Beta", "button");
        _findByAnnouncement = (_, announcement, _) =>
            announcement == "Beta, button" ? betaElement : null;

        _spoken.Clear();
        NavigationManager.TryRestoreCursor();

        Assert.False(NavigationManager.NeedsCursorRestore);
        Assert.Equal("Beta, button", NavigationManager.GetCurrentElement().GetAnnouncement());
        Assert.Contains(_spoken, s => s.Contains("Beta, button"));
    }

    [Fact]
    public void DeferredRestoreCancelsOnPanelChange()
    {
        var panelA = "PanelA";
        _elements.Clear();
        _elements.Add(new FakeElement("Alpha", "button"));

        NavigationManager.OnPanelReady(panelA, "Panel A", false);

        // Navigate to panel B
        var panelB = "PanelB";
        NavigationManager.OnPanelReady(panelB, "Panel B", false);

        // Return to panel A with failed restore
        _findByAnnouncement = (_, _, _) => null;
        NavigationManager.OnPanelClosed(panelB);
        Assert.True(NavigationManager.NeedsCursorRestore);

        // Navigate to panel C — pending restore should cancel since active panel changed
        _elements.Clear();
        _elements.Add(new FakeElement("Gamma", "button"));
        _findByAnnouncement = (_, _, _) => _elements[0];
        var panelC = "PanelC";
        NavigationManager.OnPanelReady(panelC, "Panel C", false);

        NavigationManager.TryRestoreCursor();
        Assert.False(NavigationManager.NeedsCursorRestore);
    }

    private sealed record NativePanel(string Key, int Pointer);

    [Theory]
    [InlineData(42, false, true)]
    [InlineData(43, false, false)]
    [InlineData(42, true, false)]
    public void DeferredRestoreUsesNativeInstanceAcrossManagedWrappers(
        int replacementPointer, bool comparisonThrows, bool shouldRestore)
    {
        _host.GetPanelIdentity = panel => ((NativePanel)panel).Key;
        _host.IsSamePanelInstance = (a, b) => ((NativePanel)a).Pointer == ((NativePanel)b).Pointer;
        _host.Apply();
        var panel = new NativePanel("A", 42);
        var other = new NativePanel("B", 99);
        _elements.Add(new FakeElement("Alpha", "button"));
        _elements.Add(new FakeElement("Beta", "button"));
        NavigationManager.OnPanelReady(panel, "A", false);
        NavigationManager.MoveCursor(NavigationDirection.Down);
        NavigationManager.OnPanelReady(other, "B", false);
        _elements.Clear();
        _elements.Add(new FakeElement("Header", "text"));
        _findByAnnouncement = (_, _, _) => null;
        NavigationManager.OnPanelClosed(other);
        Assert.True(NavigationManager.NeedsCursorRestore);

        // The same native panel can arrive in a new managed wrapper while its
        // virtualized content is still binding. A genuinely replaced native
        // instance of the same panel type must instead cancel the old restore.
        NavigationManager.OnPanelReady(new NativePanel("A", replacementPointer), "A", false);
        var beta = new FakeElement("Beta", "button");
        _findByAnnouncement = (_, _, _) => beta;
        if (comparisonThrows)
        {
            _host.IsSamePanelInstance = (_, _) => throw new InvalidOperationException("Native panel expired");
            _host.Apply();
        }
        NavigationManager.TryRestoreCursor();
        Assert.False(NavigationManager.NeedsCursorRestore);
        Assert.Equal(shouldRestore ? "Beta, button" : "Header, text",
            NavigationManager.GetCurrentElement().GetAnnouncement());
    }

    [Fact]
    public void WorksWithoutDelegatesSet()
    {
        _host.GetPanelIdentity = null;
        _host.FindByAnnouncement = null;
        _host.Apply();

        _elements.Clear();
        _elements.Add(new FakeElement("Item", "button"));

        // Should not crash
        NavigationManager.OnPanelReady("Panel1", "Test", false);
        Assert.Equal("Item, button", NavigationManager.GetCurrentElement().GetAnnouncement());
    }

    [Fact]
    public void RestoresCorrectOccurrenceWithDuplicateAnnouncements()
    {
        var panelA = "PanelA";
        _elements.Clear();
        _elements.Add(new FakeElement("Rest", "button"));
        _elements.Add(new FakeElement("Rest", "button"));
        _elements.Add(new FakeElement("Rest", "button"));

        NavigationManager.OnPanelReady(panelA, "Panel A", false);
        // Move to 3rd "Rest"
        NavigationManager.MoveCursor(NavigationDirection.Down);
        NavigationManager.MoveCursor(NavigationDirection.Down);

        // Navigate away
        var panelB = "PanelB";
        NavigationManager.OnPanelReady(panelB, "Panel B", false);
        _spoken.Clear();

        // Return to panel A — should restore to the 3rd instance
        NavigationManager.OnPanelClosed(panelB);

        Assert.Equal(_elements[2], NavigationManager.GetCurrentElement());
    }

    [Fact]
    public void FallsBackToFirstStopWhenOccurrenceIndexExceedsAvailable()
    {
        var panelA = "PanelA";
        _elements.Clear();
        _elements.Add(new FakeElement("Rest", "button"));
        _elements.Add(new FakeElement("Rest", "button"));
        _elements.Add(new FakeElement("Rest", "button"));

        NavigationManager.OnPanelReady(panelA, "Panel A", false);
        // Move to 3rd "Rest"
        NavigationManager.MoveCursor(NavigationDirection.Down);
        NavigationManager.MoveCursor(NavigationDirection.Down);

        // Navigate away
        var panelB = "PanelB";
        NavigationManager.OnPanelReady(panelB, "Panel B", false);

        // Change panel A to only have 2 instances
        _elements.Clear();
        _elements.Add(new FakeElement("Rest", "button"));
        _elements.Add(new FakeElement("Rest", "button"));

        _spoken.Clear();
        NavigationManager.OnPanelClosed(panelB);

        // Index 3 doesn't exist, FindStopByAnnouncementFn returns null, deferred restore kicks in.
        // After max retries it should land on first stop.
        // But since deferred restore also won't find index 3, let's just verify it lands on first stop.
        Assert.Equal(_elements[0], NavigationManager.GetCurrentElement());
    }

    // ---- Detach-rescan restore: the game rebuilds the element under the cursor mid-panel ----
    // (binding-heavy panels like the Tactics Planner toolbar do this constantly; before the
    // restore, every detach reset the cursor to the first stop and the panel was untraversable)

    /// <summary>Replaces the element list with fresh instances carrying the same labels.</summary>
    private void RebuildElementsAs(params string[] labels)
    {
        _elements.Clear();
        foreach (var label in labels)
            _elements.Add(new FakeElement(label, "button"));
    }

    [Fact]
    public void MoveAcrossDetachedElementContinuesFromRestoredPosition()
    {
        RebuildElementsAs("Alpha", "Beta", "Gamma");
        NavigationManager.OnPanelReady("PanelA", "Panel A", false);
        NavigationManager.MoveCursor(NavigationDirection.Down); // Beta

        // Game rebuilds the panel: same content, fresh element instances.
        RebuildElementsAs("Alpha", "Beta", "Gamma");
        _host.IsDetached = el => !_elements.Contains(el);
        _host.Apply();
        _spoken.Clear();

        NavigationManager.MoveCursor(NavigationDirection.Down);

        // Restored to the fresh Beta, then the keypress landed: cursor is on Gamma.
        Assert.Equal(_elements[2], NavigationManager.GetCurrentElement());
        Assert.Contains(_spoken, s => s.Contains("Gamma, button"));
        Assert.DoesNotContain(_spoken, s => s.Contains("Alpha, button"));
    }

    [Fact]
    public void ThrowingWalkAcrossDetachedElementContinuesFromRestoredPosition()
    {
        // Same scenario, but the walk throws on the dead element instead of returning null
        // (the IL2CPP failure mode when touching a destroyed VisualElement).
        _host.FindNext = (current, direction) =>
        {
            int idx = _elements.IndexOf((FakeElement)current);
            if (idx < 0) throw new InvalidOperationException("dead element");
            int next = direction == NavigationDirection.Down ? idx + 1 : idx - 1;
            if (next < 0 || next >= _elements.Count) return null;
            return _elements[next];
        };
        _host.Apply();

        RebuildElementsAs("Alpha", "Beta", "Gamma");
        NavigationManager.OnPanelReady("PanelA", "Panel A", false);
        NavigationManager.MoveCursor(NavigationDirection.Down); // Beta

        RebuildElementsAs("Alpha", "Beta", "Gamma");
        _spoken.Clear();

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Equal(_elements[2], NavigationManager.GetCurrentElement());
        Assert.Contains(_spoken, s => s.Contains("Gamma, button"));
    }

    [Fact]
    public void DetachWithChangedContentResetsToFirstStopAnnounced()
    {
        RebuildElementsAs("Alpha", "Beta", "Gamma");
        NavigationManager.OnPanelReady("PanelA", "Panel A", false);
        NavigationManager.MoveCursor(NavigationDirection.Down); // Beta

        // Rebuild with different content: no announcement match, so the old reset applies.
        RebuildElementsAs("Delta", "Epsilon");
        _host.IsDetached = el => !_elements.Contains(el);
        _host.Apply();
        _spoken.Clear();

        NavigationManager.MoveCursor(NavigationDirection.Down);

        Assert.Equal(_elements[0], NavigationManager.GetCurrentElement());
        Assert.Contains(_spoken, s => s.Contains("Delta, button"));
    }

    [Fact]
    public void DirectRescanWithMatchingContentRestoresSilently()
    {
        RebuildElementsAs("Alpha", "Beta", "Gamma");
        NavigationManager.OnPanelReady("PanelA", "Panel A", false);
        NavigationManager.MoveCursor(NavigationDirection.Down); // Beta

        RebuildElementsAs("Alpha", "Beta", "Gamma");
        _spoken.Clear();

        bool restored = NavigationManager.RescanPanel();

        Assert.True(restored);
        Assert.Equal(_elements[1], NavigationManager.GetCurrentElement());
        Assert.Empty(_spoken); // cursor did not move: nothing to say
    }

    [Fact]
    public void RescanRestoresTheCorrectDuplicateOccurrence()
    {
        RebuildElementsAs("Rest", "Rest", "Rest");
        NavigationManager.OnPanelReady("PanelA", "Panel A", false);
        NavigationManager.MoveCursor(NavigationDirection.Down);
        NavigationManager.MoveCursor(NavigationDirection.Down); // 3rd "Rest"

        RebuildElementsAs("Rest", "Rest", "Rest");

        NavigationManager.RescanPanel();

        Assert.Equal(_elements[2], NavigationManager.GetCurrentElement());
    }

    [Fact]
    public void RepeatedDetachDuringRetryAnnouncesCursorAndTerminates()
    {
        RebuildElementsAs("Alpha", "Beta", "Gamma");
        NavigationManager.OnPanelReady("PanelA", "Panel A", false);
        NavigationManager.MoveCursor(NavigationDirection.Down); // Beta

        // Every element always reads as detached and the walk never finds a next stop:
        // move → rescan restores → retry → detach again → rescan restores → speak, stop.
        _host.FindNext = (_, _) => null;
        _host.IsDetached = _ => true;
        _host.Apply();
        _spoken.Clear();

        NavigationManager.MoveCursor(NavigationDirection.Down);

        // The keypress is not silent and the cursor position survived.
        Assert.Contains(_spoken, s => s.Contains("Beta, button"));
        Assert.Equal("Beta, button", NavigationManager.GetCurrentElement().GetAnnouncement());
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
