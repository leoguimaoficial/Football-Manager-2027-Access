using System;
using FM27Access.Navigation;

namespace FM27Access.Tests;

public class DisplayTextTests
{
    // A configurable accessible element. Each field stands in for one live read.
    private sealed class Fake : IAccessibleElement
    {
        public string RoleValue = "button";
        public string LabelValue = "";
        public string StateValue = "";
        public string AnnouncementValue = "";
        public string PrimaryValue = "";
        public string DescriptionValue = "";

        public string Role => RoleValue;
        public bool CanActivate => true;
        public bool HasContextMenu => false;
        public string GetLabel() => LabelValue;
        public string GetState() => StateValue;
        public string GetAnnouncement() => AnnouncementValue;
        public string GetPrimaryLabel() => PrimaryValue;
        public string GetDescription() => DescriptionValue;
        public void Activate() { }
        public bool IsVisible() => true;
    }

    // The heading/rename profile policy lives on the global host; swap in a host carrying
    // just these two capabilities for the duration of an action and restore the previous
    // one, so no test leaks profile state into another.
    private static T WithProfile<T>(
        Func<IAccessibleElement, bool> headingFn,
        Func<IAccessibleElement, string> renameFn,
        Func<T> action)
    {
        var prev = NavigationManager.Host;
        new TestHostSpec { IsHeading = headingFn, RenameLabel = renameFn }.Apply();
        try { return action(); }
        finally { NavigationManager.Initialize(prev); }
    }

    // No active rename: the common case for the heading/role tests below.
    private static T WithHeading<T>(Func<IAccessibleElement, bool> fn, Func<T> action)
        => WithProfile(fn, _ => null, action);

    // A description past the inline cutoff (the 85-char sample plus a word), so it stays an
    // on-demand tooltip rather than being folded into the label.
    private const string LongSubtitle =
        "Increased, So far: 6 weeks (Current Injury), Expected to return in 4 weeks - 2 months today";

    [Fact]
    public void Button_LongDescription_AppendsMoreInfoHint()
    {
        // A long tail stays a tooltip; the label advertises the on-demand Alt+I read.
        var el = new Fake { RoleValue = "button", PrimaryValue = "Smith", DescriptionValue = LongSubtitle, AnnouncementValue = "Smith, button, ..." };
        Assert.Equal("Smith, Alt I for more info", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Button_LongDescription_Disabled_NoHint()
    {
        // A disabled button can't take focus, so Alt+I can't reach it: suppress the hint.
        var el = new Fake { RoleValue = "button", PrimaryValue = "Undo", DescriptionValue = LongSubtitle, StateValue = "disabled" };
        Assert.Equal("Undo", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Button_Selected_AppendsSelected()
    {
        // A button-group's selected member appends "selected" last, mirroring the speech surface.
        var el = new Fake { RoleValue = "button", PrimaryValue = "Recruitment", StateValue = "selected" };
        Assert.Equal("Recruitment, selected", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Button_Selected_WithShortDescription_SelectedComesLast()
    {
        // The inlined short tail stays put; "selected" appends after it.
        var el = new Fake { RoleValue = "button", PrimaryValue = "Smith", DescriptionValue = "rating 7.2", StateValue = "selected" };
        Assert.Equal("Smith, rating 7.2, selected", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Button_SelectedAndDisabled_BothConveyed()
    {
        // A selected-but-disabled button still appends "selected"; the disabled state is carried
        // separately by the native attribute on the HTML surface.
        var el = new Fake { RoleValue = "button", PrimaryValue = "Overview", StateValue = "selected, disabled" };
        Assert.Equal("Overview, selected", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Button_NotSelected_NoSuffix()
    {
        // Regression guard: a button whose state lacks the token reads without ", selected".
        var el = new Fake { RoleValue = "button", PrimaryValue = "Squad", StateValue = "" };
        Assert.Equal("Squad", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Button_WarningState_AppendsWarning()
    {
        // A warning-cell button (the contract Transfer Rules cell's triangle icon) carries the
        // "warning" state token; the label conveys it, since the icon is the whole signal.
        var el = new Fake { RoleValue = "button", PrimaryValue = "Transfer Rules", StateValue = "warning" };
        Assert.Equal("Transfer Rules, warning", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Button_ShortDescription_InlinedIntoLabel()
    {
        // A short tail is folded into the visible label after the primary, comma-separated.
        var el = new Fake { RoleValue = "button", PrimaryValue = "Smith", DescriptionValue = "rating 7.2" };
        Assert.Equal("Smith, rating 7.2", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Button_LongDescription_DescriptionOf_ReturnsSecondary()
    {
        var el = new Fake { RoleValue = "button", PrimaryValue = "Smith", DescriptionValue = LongSubtitle };
        Assert.Equal(LongSubtitle, DisplayText.DescriptionOf(el));
    }

    [Fact]
    public void Button_ShortDescription_DescriptionOf_IsEmpty()
    {
        // Folded into the label already, so nothing remains for the on-demand read.
        var el = new Fake { RoleValue = "button", PrimaryValue = "Smith", DescriptionValue = "rating 7.2" };
        Assert.Equal("", DisplayText.DescriptionOf(el));
    }

    [Fact]
    public void Button_LongDescription_DescriptionOf_StripsRichText()
    {
        var el = new Fake { RoleValue = "button", PrimaryValue = "Smith", DescriptionValue = "<color=#ff0000>" + LongSubtitle + "</color>" };
        Assert.Equal(LongSubtitle, DisplayText.DescriptionOf(el));
    }

    [Fact]
    public void Button_DescriptionAtCutoff_IsInlined()
    {
        // Exactly InlineDescriptionMaxLength characters still inlines; only longer falls back.
        var desc = new string('x', DisplayText.InlineDescriptionMaxLength);
        var el = new Fake { RoleValue = "button", PrimaryValue = "Smith", DescriptionValue = desc };
        Assert.Equal("Smith, " + desc, WithHeading(_ => false, () => DisplayText.Of(el)));
        Assert.Equal("", DisplayText.DescriptionOf(el));
        Assert.True(DisplayText.HasInlinedDescription(el));
    }

    [Fact]
    public void Button_DescriptionOverCutoff_StaysTooltip()
    {
        var desc = new string('x', DisplayText.InlineDescriptionMaxLength + 1);
        var el = new Fake { RoleValue = "button", PrimaryValue = "Smith", DescriptionValue = desc };
        Assert.Equal("Smith, Alt I for more info", WithHeading(_ => false, () => DisplayText.Of(el)));
        Assert.Equal(desc, DisplayText.DescriptionOf(el));
        Assert.False(DisplayText.HasInlinedDescription(el));
    }

    [Fact]
    public void HasInlinedDescription_FalseWhenNoDescription()
    {
        // A button with no secondary at all is distinct from one whose secondary was inlined.
        var el = new Fake { RoleValue = "button", PrimaryValue = "Smith", DescriptionValue = "" };
        Assert.False(DisplayText.HasInlinedDescription(el));
    }

    [Fact]
    public void HasInlinedDescription_FalseForNonButton()
    {
        // Only buttons inline; another role's short text is never folded in.
        var el = new Fake { RoleValue = "text", DescriptionValue = "short" };
        Assert.False(DisplayText.HasInlinedDescription(el));
    }

    [Fact]
    public void EmptyPrimary_FallsBackToAnnouncement()
    {
        // A button with no readable primary text reads as its full announcement, not blank.
        var el = new Fake { RoleValue = "button", PrimaryValue = "", AnnouncementValue = "Unnamed, button" };
        Assert.Equal("Unnamed, button", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Checkbox_ReturnsLabel()
    {
        var el = new Fake { RoleValue = "checkbox", LabelValue = "Offside trap", StateValue = "checked", AnnouncementValue = "Offside trap, checkbox, checked" };
        Assert.Equal("Offside trap", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Checkbox_EmptyLabel_DoesNotLeakAnnouncement()
    {
        // The native <input> carries role and state via ARIA. When the label can't be resolved,
        // the name must stay empty rather than falling back to the announcement, which would
        // bake a stale, contradictory "role, state" copy on top of the live ARIA.
        var el = new Fake { RoleValue = "checkbox", LabelValue = "", StateValue = "checked", AnnouncementValue = "checkbox, checked" };
        Assert.Equal("", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void RadioButton_EmptyLabel_DoesNotLeakAnnouncement()
    {
        // Same as the checkbox: a radio renders as a native <input type=radio>, so its name is
        // the bare label and never the role/state-bearing announcement.
        var el = new Fake { RoleValue = "radio button", LabelValue = "", StateValue = "selected", AnnouncementValue = "radio button, selected" };
        Assert.Equal("", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Combobox_ReturnsLabel()
    {
        var el = new Fake { RoleValue = "combobox", LabelValue = "Mentality", AnnouncementValue = "Mentality, combobox" };
        Assert.Equal("Mentality", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void ColumnHeader_ReturnsLabel()
    {
        // A table header renders inside a <th> (which carries the column-header role), so its
        // visible text is the bare column name, never "Age, column header".
        var el = new Fake { RoleValue = "column header", LabelValue = "Age", AnnouncementValue = "Age, column header" };
        Assert.Equal("Age", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void ProgressBar_ComposesLabelAndState()
    {
        var el = new Fake { RoleValue = "progress bar", LabelValue = "Condition", StateValue = "85%", AnnouncementValue = "Condition, progress bar, 85%" };
        Assert.Equal("Condition: 85%", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Stepper_ComposesRenamedLabelAndState()
    {
        // A bare step bar (the Fitness cards' level bars) carries its whole value in the filled
        // count, and its only name is the template id a profile rename replaces — so dropping
        // the state, as the default announcement branch would once renamed, loses the value.
        var el = new Fake
        {
            RoleValue = "stepper",
            LabelValue = "data_display-graphs-stepper-step_bar",
            StateValue = "2 of 3",
            AnnouncementValue = "data_display-graphs-stepper-step_bar, stepper, 2 of 3",
        };
        Assert.Equal("Level: 2 of 3", WithProfile(_ => false, _ => "Level", () => DisplayText.Of(el)));
    }

    [Fact]
    public void StarRating_ReturnsLabel()
    {
        // A star gauge's label is already its whole value, so the visible text is the bare
        // "2 and a half stars", never "2 and a half stars, star rating".
        var el = new Fake { RoleValue = "star rating", LabelValue = "2 and a half stars", AnnouncementValue = "2 and a half stars, star rating" };
        Assert.Equal("2 and a half stars", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void ToggleButton_ReturnsLabel()
    {
        // A segmented toggle's rendered element carries the role, so its visible text is the
        // bare label — the opposition instructions cell read "Player, toggle button" as its
        // button text (and leaked that into the row checkbox's name) before this.
        var el = new Fake { RoleValue = "toggle button", LabelValue = "Player", AnnouncementValue = "Player, toggle button" };
        Assert.Equal("Player", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void UnknownRole_ReturnsAnnouncement()
    {
        // A role without an explicit case (plain text, image, etc.) uses the full
        // announcement, not the bare label.
        var el = new Fake { RoleValue = "text", LabelValue = "ignored label", AnnouncementValue = "Possession 62 percent" };
        Assert.Equal("Possession 62 percent", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Heading_ReturnsLabel_RegardlessOfRole()
    {
        var el = new Fake { RoleValue = "text", LabelValue = "First Team", AnnouncementValue = "First Team, heading" };
        Assert.Equal("First Team", WithHeading(_ => true, () => DisplayText.Of(el)));
    }

    [Fact]
    public void RichTextMarkup_IsStripped()
    {
        var el = new Fake { RoleValue = "button", PrimaryValue = "<b>Smith</b> rating <color=#ff0000>7.2</color>", AnnouncementValue = "" };
        Assert.Equal("Smith rating 7.2", WithHeading(_ => false, () => DisplayText.Of(el)));
    }

    [Fact]
    public void Rename_ReplacesLabelRole()
    {
        // The canonical rename case: an icon-only dropdown whose auto-derived label is
        // confusing. The profile relabels it; the role/state still come from the element.
        var el = new Fake { RoleValue = "combobox", LabelValue = "All, New, Tasks (0)", AnnouncementValue = "All, New, Tasks (0), combobox" };
        Assert.Equal("Advice actions", WithProfile(_ => false, _ => "Advice actions", () => DisplayText.Of(el)));
    }

    [Fact]
    public void Rename_ReplacesAnnouncementForUnknownRole()
    {
        // A role with no explicit case normally returns the full announcement; a rename
        // substitutes for it rather than appending to it.
        var el = new Fake { RoleValue = "text", LabelValue = "ignored", AnnouncementValue = "Possession 62 percent" };
        Assert.Equal("Squad filter", WithProfile(_ => false, _ => "Squad filter", () => DisplayText.Of(el)));
    }

    [Fact]
    public void Rename_ComposesWithProgressState()
    {
        // Progress bars keep their live state appended; only the label portion is renamed.
        var el = new Fake { RoleValue = "progress bar", LabelValue = "Condition", StateValue = "85%", AnnouncementValue = "Condition, progress bar, 85%" };
        Assert.Equal("Fitness: 85%", WithProfile(_ => false, _ => "Fitness", () => DisplayText.Of(el)));
    }

    [Fact]
    public void Rename_TakesPrecedenceOverHeadingLabel()
    {
        // A stop that is both a heading and renamed reads as its rename, not its raw label.
        var el = new Fake { RoleValue = "text", LabelValue = "PersonPersonal", AnnouncementValue = "PersonPersonal, heading" };
        Assert.Equal("Personal details", WithProfile(_ => true, _ => "Personal details", () => DisplayText.Of(el)));
    }

    [Fact]
    public void Rename_ReplacesButtonPrimary_LongDescriptionStillFromElement()
    {
        // A rename substitutes the primary label shown on the button; a long on-demand
        // description is unaffected and still reads off the element.
        var el = new Fake { RoleValue = "button", PrimaryValue = "Confusing", DescriptionValue = LongSubtitle };
        Assert.Equal("Clear name, Alt I for more info", WithProfile(_ => false, _ => "Clear name", () => DisplayText.Of(el)));
        Assert.Equal(LongSubtitle, DisplayText.DescriptionOf(el));
    }

    [Fact]
    public void Rename_WithShortDescription_InlinesAfterRename()
    {
        // The rename supplies the primary; a short tail still folds in after it.
        var el = new Fake { RoleValue = "button", PrimaryValue = "Confusing", DescriptionValue = "subtitle" };
        Assert.Equal("Clear name, subtitle", WithProfile(_ => false, _ => "Clear name", () => DisplayText.Of(el)));
    }

    [Fact]
    public void NoRename_LeavesAnnouncementUntouched()
    {
        // When the profile returns null for a stop, the default-role path is unchanged.
        var el = new Fake { RoleValue = "text", LabelValue = "ignored", AnnouncementValue = "Possession 62 percent" };
        Assert.Equal("Possession 62 percent", WithProfile(_ => false, _ => null, () => DisplayText.Of(el)));
    }
}
