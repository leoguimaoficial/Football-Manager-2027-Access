using FM27Access.Navigation;

namespace FM27Access.Tests;

/// <summary>
/// The full-screen Message view as a back/forward entry (docs/message-view-history-plan.md). These
/// drive the real decision layer the plugin drives — the same Observe / Note* / Back / Forward calls,
/// with the game reads (stack position, reading pane) supplied as the plugin supplies them.
///
/// The shapes here are the game's own history strings, taken from a live career: the Messages panel
/// sits at index 1 under the portal, and an email's bottom button pushes a card at index 2.
/// </summary>
public class MessageViewHistoryTests
{
    private const string Inbox = "PortalScreen, PortalMessagesTool";
    private const string WithCard = "PortalScreen, PortalMessagesTool, SquadMedicalCentreCard";
    private const string Subject = "US Orléans fitness update";

    // The state after the reported repro's first two steps: an email opened on the Messages panel,
    // then one of its bottom buttons opening a card. The view has self-closed; its entry survives.
    private static MessageViewHistory AfterOpeningAnAttachment()
    {
        var h = new MessageViewHistory();
        h.Observe(1, 2, Inbox);
        h.NoteOpened(1, Inbox, Subject);
        h.Observe(2, 3, WithCard);
        return h;
    }

    [Fact]
    public void OpeningAMessage_RecordsAnEntryAtTheInboxIndex()
    {
        var h = new MessageViewHistory();
        h.Observe(1, 2, Inbox);
        h.NoteOpened(1, Inbox, Subject);

        var slot = h.SlotAt(1);
        Assert.NotNull(slot);
        Assert.Equal(Subject, slot.Value.Subject);
        Assert.True(slot.Value.WasOpen);
        Assert.Null(h.SlotAt(0));
    }

    [Fact]
    public void BackFromTheOpenMessage_ClosesItWithoutMovingTheGame()
    {
        var h = AfterOpeningAnAttachment();

        Assert.Equal(MessageViewHistory.Step.CloseModal, h.Back(1, modalOpen: true));
    }

    [Fact]
    public void BackFromTheInbox_WalksTheGameHistory()
    {
        var h = AfterOpeningAnAttachment();

        Assert.Equal(MessageViewHistory.Step.GameMove, h.Back(1, modalOpen: false));
    }

    [Fact]
    public void BackFromTheAttachment_LandsOnTheInboxAndRestoresTheMessage()
    {
        var h = AfterOpeningAnAttachment();

        Assert.Equal(MessageViewHistory.Step.GameMove, h.Back(2, modalOpen: false));

        // The game's back leaves the stack shape alone and only moves the index.
        var restore = h.RestoreAfterBack(1, 3, WithCard);
        Assert.NotNull(restore);
        Assert.Equal(Subject, restore.Value.Subject);
    }

    [Fact]
    public void BackToAScreenWithNoMessage_RestoresNothing()
    {
        var h = AfterOpeningAnAttachment();

        Assert.Null(h.RestoreAfterBack(0, 3, WithCard));
    }

    [Fact]
    public void ForwardFromTheInbox_ReopensTheMessageWithoutMovingTheGame()
    {
        var h = AfterOpeningAnAttachment();
        h.NoteSteppedToInbox(1);

        Assert.Equal(MessageViewHistory.Step.RestoreModal, h.Forward(1, modalOpen: false));
    }

    [Fact]
    public void ForwardFromTheOpenMessage_WalksTheGameHistory()
    {
        var h = AfterOpeningAnAttachment();

        Assert.Equal(MessageViewHistory.Step.GameMove, h.Forward(1, modalOpen: true));
    }

    [Fact]
    public void ForwardWhereNoMessageWasOpened_WalksTheGameHistory()
    {
        var h = new MessageViewHistory();
        h.Observe(1, 2, Inbox);

        Assert.Equal(MessageViewHistory.Step.GameMove, h.Forward(1, modalOpen: false));
    }

    [Fact]
    public void TheFullRoundTrip_WalksTheSameMergedSequenceBothWays()
    {
        var h = AfterOpeningAnAttachment();
        var sequence = new List<string>();

        // Back: attachment -> message -> inbox -> the screen under the inbox.
        Assert.Equal(MessageViewHistory.Step.GameMove, h.Back(2, modalOpen: false));
        var restored = h.RestoreAfterBack(1, 3, WithCard);
        Assert.NotNull(restored);
        h.NoteSteppedToMessage(1);
        sequence.Add("message");

        Assert.Equal(MessageViewHistory.Step.CloseModal, h.Back(1, modalOpen: true));
        h.NoteSteppedToInbox(1);
        sequence.Add("inbox");

        Assert.Equal(MessageViewHistory.Step.GameMove, h.Back(1, modalOpen: false));
        Assert.Null(h.RestoreAfterBack(0, 3, WithCard));
        sequence.Add("portal");

        // Forward retraces it: portal -> inbox -> message -> attachment. A forward move only
        // observes — entering the position from below lands on the inbox, not the email.
        Assert.Equal(MessageViewHistory.Step.GameMove, h.Forward(0, modalOpen: false));
        h.Observe(1, 3, WithCard);
        Assert.NotNull(h.SlotAt(1));
        sequence.Add("inbox");

        Assert.Equal(MessageViewHistory.Step.RestoreModal, h.Forward(1, modalOpen: false));
        h.NoteSteppedToMessage(1);
        sequence.Add("message");

        Assert.Equal(MessageViewHistory.Step.GameMove, h.Forward(1, modalOpen: true));
        sequence.Add("attachment");

        Assert.Equal(new[] { "message", "inbox", "portal", "inbox", "message", "attachment" }, sequence);
    }

    [Fact]
    public void DismissingTheView_RetiresItsEntrySoForwardDoesNotResurrectIt()
    {
        var h = AfterOpeningAnAttachment();
        h.NoteDismissed(1);

        Assert.Null(h.SlotAt(1));
        Assert.Equal(MessageViewHistory.Step.GameMove, h.Forward(1, modalOpen: false));
    }

    [Fact]
    public void NavigatingOnFromTheInbox_DropsTheEntryTheWayABrowserClearsForwardHistory()
    {
        var h = AfterOpeningAnAttachment();
        h.RestoreAfterBack(1, 3, WithCard);
        h.NoteSteppedToInbox(1);

        // A push from the inbox: the stack's shape changes and the player was not inside the email.
        h.Observe(2, 3, "PortalScreen, PortalMessagesTool, SquadOverviewCard");

        Assert.Null(h.SlotAt(1));
    }

    [Fact]
    public void NavigatingAwayFromTheOpenMessage_KeepsTheEntry()
    {
        var h = new MessageViewHistory();
        h.Observe(1, 2, Inbox);
        h.NoteOpened(1, Inbox, Subject);

        // The ribbon jump case: the view closes for hygiene, but the player was inside the message,
        // so the entry has to survive for Back to bring it back.
        h.Observe(2, 3, "PortalScreen, PortalMessagesTool, TacticsOverview");

        Assert.NotNull(h.SlotAt(1));
    }

    [Fact]
    public void AnEntryOutsideTheStack_IsDropped()
    {
        var h = AfterOpeningAnAttachment();

        // A save load clears the history to index=-1, count=0 before the portal node arrives.
        h.Observe(-1, 0, "");

        Assert.Equal(0, h.SlotCount);
    }

    [Fact]
    public void AnEntryWhoseStackPositionNowHoldsAnotherScreen_IsDropped()
    {
        var h = new MessageViewHistory();
        h.Observe(1, 2, Inbox);
        h.NoteOpened(1, Inbox, Subject);

        // Index 1 is no longer the Messages panel, so the recorded position cannot be trusted.
        h.Observe(1, 2, "PortalScreen, TacticsOverview");

        Assert.Null(h.SlotAt(1));
    }

    [Fact]
    public void AnEntryPastTheEndOfTheHistoryString_IsNotRecorded()
    {
        var h = new MessageViewHistory();
        h.Observe(1, 2, Inbox);

        // The index does not exist in the shape handed in: record nothing rather than a slot that
        // could later match the wrong position.
        h.NoteOpened(5, Inbox, Subject);

        Assert.Equal(0, h.SlotCount);
    }

    [Fact]
    public void AFailedRestore_DropsTheEntry()
    {
        var h = AfterOpeningAnAttachment();
        h.Drop(1);

        Assert.Null(h.SlotAt(1));
        Assert.Equal(MessageViewHistory.Step.GameMove, h.Forward(1, modalOpen: false));
    }

    [Fact]
    public void TheDumpNamesEachEntryAndWhetherThePlayerWasInsideIt()
    {
        var h = AfterOpeningAnAttachment();
        Assert.Equal($"1*=\"{Subject}\"", h.Describe());

        h.NoteSteppedToInbox(1);
        Assert.Equal($"1=\"{Subject}\"", h.Describe());

        h.NoteDismissed(1);
        Assert.Equal("none", h.Describe());
    }
}
