using FM27Access.Navigation;
using Xunit;

namespace FM27Access.Tests;

/// <summary>
/// The scoping rule that decides whether a menu-bearing holder may speak for the control a player is
/// on. Every case here is a shape observed live in the game via /ctxtrigger and /ctxcensus, so each
/// one guards a distinct way the mark can lie about what the menu contains.
/// </summary>
public class ContextMenuScopeTests
{
    // The game wraps every entity name in link/colour markup; the rule must see through it, or a
    // name link would never match the label it produced.
    private const string RichName = "<color=\"#FFFFFF\"><link=\"0\">Dan Abraham</link></color>";

    [Fact]
    public void SelfHolderIsAlwaysAccepted()
    {
        var v = ContextMenuScope.Judge("Dan Abraham", null, holderIsText: false, depth: 0, holderCount: 1, isSelf: true);
        Assert.Equal(MenuScopeVerdict.Self, v);
        Assert.True(ContextMenuScope.Accepts(v));
    }

    [Fact]
    public void TextHolderMatchingTheLabelIsAccepted()
    {
        var v = ContextMenuScope.Judge("Dan Abraham", RichName, holderIsText: true, depth: 2, holderCount: 1, isSelf: false);
        Assert.Equal(MenuScopeVerdict.LabelMatch, v);
    }

    // A table cell decorates the entity with shirt number and rating; the menu is still that player's.
    [Fact]
    public void TextHolderInsideADecoratedLabelIsAccepted()
    {
        var v = ContextMenuScope.Judge("12, Dan Abraham, 7.4", RichName, holderIsText: true, depth: 2, holderCount: 1, isSelf: false);
        Assert.Equal(MenuScopeVerdict.LabelMatch, v);
    }

    // The message row: the label is the subject, the holder is the sender. Two different things.
    [Fact]
    public void TextHolderNamingSomethingElseIsRejected()
    {
        var v = ContextMenuScope.Judge("Fitness tests", "Mohamed Wright", holderIsText: true, depth: 3, holderCount: 1, isSelf: false);
        Assert.Equal(MenuScopeVerdict.RejectedLabelMismatch, v);
        Assert.False(ContextMenuScope.Accepts(v));
    }

    // The Medical Center tile: several injured players inside, none of them what the tile is called.
    [Fact]
    public void CompositeTileWithRivalHoldersIsRejected()
    {
        var v = ContextMenuScope.Judge("Medical Center", "Kevin Kambere", holderIsText: true, depth: 6, holderCount: 4, isSelf: false);
        Assert.Equal(MenuScopeVerdict.RejectedComposite, v);
    }

    // A shallow mismatch is still a mismatch: distance never rescues a holder that names another entity.
    [Fact]
    public void ShallowTextHolderNamingSomethingElseIsStillRejected()
    {
        var v = ContextMenuScope.Judge("Internationals", "Kevin Kambere", holderIsText: true, depth: 1, holderCount: 1, isSelf: false);
        Assert.Equal(MenuScopeVerdict.RejectedLabelMismatch, v);
    }

    // The staff/player name cell: an SIButton whose own PluginContextMenuBindings sits just inside it.
    [Fact]
    public void ShallowTextlessHolderIsAcceptedAsTheControlsOwnBinding()
    {
        var v = ContextMenuScope.Judge("Dan Abraham", null, holderIsText: false, depth: ContextMenuScope.MaxWrapperDepth, holderCount: 1, isSelf: false);
        Assert.Equal(MenuScopeVerdict.ShallowWrapper, v);
    }

    // The Staff summary tile: a RightClickMenuBinding buried in its Responsibilities sub-block, which
    // opens the MANAGER's own menu from a tile named "Staff".
    [Fact]
    public void DeepTextlessHolderIsRejected()
    {
        var v = ContextMenuScope.Judge("Staff", null, holderIsText: false, depth: ContextMenuScope.MaxWrapperDepth + 1, holderCount: 1, isSelf: false);
        Assert.Equal(MenuScopeVerdict.RejectedDepth, v);
    }

    // A streamed cell read before its text binds has no name to judge by. Falling back to distance
    // keeps a real mark rather than dropping it for a value that simply has not arrived yet.
    [Fact]
    public void UnstreamedTextHolderFallsBackToTheDistanceRule()
    {
        Assert.Equal(MenuScopeVerdict.ShallowWrapper,
            ContextMenuScope.Judge("Dan Abraham", "", holderIsText: true, depth: 1, holderCount: 1, isSelf: false));
        Assert.Equal(MenuScopeVerdict.RejectedDepth,
            ContextMenuScope.Judge("Dan Abraham", "", holderIsText: true, depth: 9, holderCount: 1, isSelf: false));
    }

    // An unreadable label must not turn into a free pass: with nothing to compare, a named holder
    // cannot be shown to belong to the control.
    [Fact]
    public void EmptyOwnerLabelNeverMatchesANamedHolder()
    {
        var v = ContextMenuScope.Judge("", "Mohamed Wright", holderIsText: true, depth: 2, holderCount: 1, isSelf: false);
        Assert.Equal(MenuScopeVerdict.RejectedLabelMismatch, v);
    }

    // A one-character holder (a staff-count digit inside a "7 / 11" tile) is not evidence of identity,
    // so it must not match a label by mere substring accident.
    [Fact]
    public void SingleCharacterHolderIsNotTreatedAsAName()
    {
        var v = ContextMenuScope.Judge("7 / 11", "7", holderIsText: true, depth: 2, holderCount: 1, isSelf: false);
        Assert.Equal(MenuScopeVerdict.RejectedLabelMismatch, v);
    }

    // A holder whose text is pure punctuation (the "/" separating two counts) names nothing at all, so
    // there is no identity to compare and the distance rule decides — the same call as an unstreamed
    // cell. It must never be read as a name that happens to match.
    [Fact]
    public void PunctuationOnlyHolderFallsBackToTheDistanceRule()
    {
        Assert.Equal(MenuScopeVerdict.ShallowWrapper,
            ContextMenuScope.Judge("7 / 11", "/", holderIsText: true, depth: 1, holderCount: 1, isSelf: false));
        Assert.Equal(MenuScopeVerdict.RejectedDepth,
            ContextMenuScope.Judge("7 / 11", "/", holderIsText: true, depth: 9, holderCount: 1, isSelf: false));
    }

    // A training session button: a deep SIButton whose only menu is the ancestor FMCalendarEvent's.
    // The event is one entity, so the control opens that event's menu no matter how deep it sits — the
    // depth cap must not drop it (the live regression this exemption fixes; holder was at depth 9).
    [Fact]
    public void SelfContainedEntityComponentIsAcceptedAtAnyDepth()
    {
        var v = ContextMenuScope.Judge("Recovery", null, holderIsText: false, depth: 9, holderCount: 1,
            isSelf: false, holderIsSelfContainedEntity: true);
        Assert.Equal(MenuScopeVerdict.EntityComponent, v);
        Assert.True(ContextMenuScope.Accepts(v));
    }

    // Without the entity-component flag, that same deep textless holder is still rejected — the
    // exemption is exactly what distinguishes a calendar event from a binding buried in a summary tile.
    [Fact]
    public void DeepTextlessHolderStaysRejectedWithoutTheEntityFlag()
    {
        var v = ContextMenuScope.Judge("Staff", null, holderIsText: false, depth: 9, holderCount: 1,
            isSelf: false, holderIsSelfContainedEntity: false);
        Assert.Equal(MenuScopeVerdict.RejectedDepth, v);
    }

    // Punctuation and case differ between a cell's label and the link text it came from.
    [Fact]
    public void MatchingIgnoresCaseAndPunctuation()
    {
        var v = ContextMenuScope.Judge("ATLÉTICO OTTAWA,", "Atlético Ottawa", holderIsText: true, depth: 2, holderCount: 1, isSelf: false);
        Assert.Equal(MenuScopeVerdict.LabelMatch, v);
    }
}
