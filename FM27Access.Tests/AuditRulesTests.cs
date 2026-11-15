using FM27Access.Navigation;
using FM27Access.Speech;
using Xunit;

namespace FM27Access.Tests;

/// <summary>
/// The audit's defect-classification rules are the reusable core of the dev-driver audit: the same
/// buckets the renderer, the /audit endpoint, and the autonomous run all key on. They are pure
/// (strings in, category out) so they are tested here without the game, where the live element reads
/// they depend on are the caller's job.
/// </summary>
public class AuditRulesTests
{
    [Fact]
    public void DegradeMarkers_match_the_honest_gap_phrases()
    {
        Assert.True(AuditRules.IsDegradeMarker(Degrade.UnknownControl));
        Assert.True(AuditRules.IsDegradeMarker(Degrade.Unavailable));
        Assert.True(AuditRules.IsDegradeMarker(Degrade.Unreadable));
        Assert.True(AuditRules.IsDegradeMarker("unlabeled icon button"));
        Assert.True(AuditRules.IsDegradeMarker("unlabeled expandable"));
    }

    [Theory]
    [InlineData("Continue")]
    [InlineData("Arsenal")]
    [InlineData("")]
    public void IsDegradeMarker_is_false_for_real_text(string name)
    {
        Assert.False(AuditRules.IsDegradeMarker(name));
    }

    [Fact]
    public void IsDegradeMarker_is_false_for_null()
    {
        Assert.False(AuditRules.IsDegradeMarker(null));
    }

    [Theory]
    [InlineData("button")]
    [InlineData("checkbox")]
    [InlineData("dropdown")]
    [InlineData("searchable dropdown")]
    [InlineData("slider")]
    public void IsInteractiveRole_true_for_operable_controls(string role)
    {
        Assert.True(AuditRules.IsInteractiveRole(role));
    }

    [Theory]
    [InlineData("text")]
    [InlineData("heading")]
    [InlineData("image")]
    [InlineData("progress bar")]
    public void IsInteractiveRole_false_for_informational_roles(string role)
    {
        Assert.False(AuditRules.IsInteractiveRole(role));
    }

    [Theory]
    [InlineData("BookmarkButton")]   // camelHump
    [InlineData("nationality_dropdown")] // underscore
    [InlineData("McTominay")]        // documents the proper-noun false positive — why it is a LEAD, not a finding
    public void LooksLikeDeveloperId_flags_id_shapes_and_camelhump_proper_nouns(string s)
    {
        Assert.True(AuditRules.LooksLikeDeveloperId(s));
    }

    [Theory]
    [InlineData("OK")]               // all-caps acronym, no camelHump
    [InlineData("Continue")]         // single word
    [InlineData("Mark All Read")]    // spaced words, capital follows a space not a lowercase letter
    public void LooksLikeDeveloperId_passes_ordinary_labels(string s)
    {
        Assert.False(AuditRules.LooksLikeDeveloperId(s));
    }

    [Fact]
    public void ClassifyStop_image_role_is_decorative_regardless_of_name()
    {
        Assert.Equal(AuditRules.Finding.ImageStop, AuditRules.ClassifyStop("image", "ManagerImage", false));
    }

    [Fact]
    public void ClassifyStop_degrade_marker_outranks_the_empty_check()
    {
        // The marker is a non-empty name on an interactive control, so it must be recognised as a
        // degrade marker (an honest gap to inventory), never as the EmptyName bug.
        Assert.Equal(AuditRules.Finding.DegradeMarker,
            AuditRules.ClassifyStop("button", "unlabeled icon button", true));
    }

    [Fact]
    public void ClassifyStop_interactive_with_no_name_is_the_empty_bug()
    {
        Assert.Equal(AuditRules.Finding.EmptyName, AuditRules.ClassifyStop("button", "   ", true));
        // canActivate false but an interactive role still counts (a control the adapter under-reports).
        Assert.Equal(AuditRules.Finding.EmptyName, AuditRules.ClassifyStop("checkbox", "", false));
    }

    [Fact]
    public void ClassifyStop_named_controls_and_plain_text_are_ok()
    {
        Assert.Equal(AuditRules.Finding.Ok, AuditRules.ClassifyStop("button", "Continue", true));
        Assert.Equal(AuditRules.Finding.Ok, AuditRules.ClassifyStop("text", "", false)); // empty text is not a control bug
        Assert.Equal(AuditRules.Finding.Ok, AuditRules.ClassifyStop("text", "Region", false));
    }

    [Fact]
    public void OrphanLabel_fires_when_a_label_precedes_an_unnamed_control()
    {
        var label = new AuditRules.Stop("text", "Currency", false);
        var emptyControl = new AuditRules.Stop("dropdown", "", true);
        var degradedControl = new AuditRules.Stop("button", "unlabeled icon button", true);
        Assert.True(AuditRules.IsOrphanLabel(label, emptyControl));
        Assert.True(AuditRules.IsOrphanLabel(label, degradedControl));
    }

    [Fact]
    public void OrphanLabel_does_not_fire_when_the_control_names_itself()
    {
        var label = new AuditRules.Stop("text", "Currency", false);
        var namedControl = new AuditRules.Stop("dropdown", "Currency", true);
        Assert.False(AuditRules.IsOrphanLabel(label, namedControl));
    }

    [Fact]
    public void OrphanLabel_requires_the_preceding_stop_to_be_text()
    {
        var notALabel = new AuditRules.Stop("button", "Save", true);
        var emptyControl = new AuditRules.Stop("dropdown", "", true);
        Assert.False(AuditRules.IsOrphanLabel(notALabel, emptyControl));
    }

    [Fact]
    public void DuplicateAdjacent_fires_only_on_identical_role_and_name()
    {
        var a = new AuditRules.Stop("heading", "Positions", false);
        Assert.True(AuditRules.IsDuplicateAdjacent(a, new AuditRules.Stop("heading", "Positions", false)));
        Assert.False(AuditRules.IsDuplicateAdjacent(a, new AuditRules.Stop("heading", "Attributes", false)));
        Assert.False(AuditRules.IsDuplicateAdjacent(a, new AuditRules.Stop("text", "Positions", false)));
    }

    [Fact]
    public void DuplicateAdjacent_ignores_blank_runs()
    {
        var blank = new AuditRules.Stop("text", "", false);
        Assert.False(AuditRules.IsDuplicateAdjacent(blank, new AuditRules.Stop("text", "", false)));
    }

    [Fact]
    public void DuplicateAdjacent_ignores_repeated_controls_and_degrade_runs()
    {
        // Five honest "unlabeled icon button" ribbon bookmarks in a row are distinct controls, not a
        // duplicated title — the live sweep over-fired here before the rule was scoped to text/heading.
        var bookmark = new AuditRules.Stop("button", "unlabeled icon button", true);
        Assert.False(AuditRules.IsDuplicateAdjacent(bookmark, new AuditRules.Stop("button", "unlabeled icon button", true)));
        // Two real, identically-named buttons (a list of "Remove" actions) are also normal UI.
        var remove = new AuditRules.Stop("button", "Remove", true);
        Assert.False(AuditRules.IsDuplicateAdjacent(remove, new AuditRules.Stop("button", "Remove", true)));
        // A degrade run on informational text is not a duplicated title either.
        var unreadable = new AuditRules.Stop("text", Degrade.Unreadable, false);
        Assert.False(AuditRules.IsDuplicateAdjacent(unreadable, new AuditRules.Stop("text", Degrade.Unreadable, false)));
    }
}
