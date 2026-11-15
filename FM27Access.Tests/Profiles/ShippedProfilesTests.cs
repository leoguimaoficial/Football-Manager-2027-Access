using FM27Access.Navigation.Profiles;

namespace FM27Access.Tests.Profiles;

// Loads the real shipped panel profiles (FM27Access.Plugin/PanelProfiles) and checks they parse
// and that the team-selection rules hit exactly the stops they were written for. ProfileLoader
// skips malformed JSON with only a log line, so a typo in a shipped profile would otherwise strip
// a whole panel's rules in production with no test failing.
public class ShippedProfilesTests : IDisposable
{
    private static readonly string ProfilesDir = FindPanelProfilesDir();

    public ShippedProfilesTests()
    {
        Speech.Log.LogFn = _ => { };
        Speech.Log.WarnFn = _ => { };
        Speech.Log.ErrorFn = _ => { };
        ProfileLoader.LoadAll(ProfilesDir);
    }

    public void Dispose() => ProfileRegistry.Clear();

    private static string FindPanelProfilesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "FM27Access.Plugin", "PanelProfiles");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("PanelProfiles directory not found above " + AppContext.BaseDirectory);
    }

    private static PanelProfile Activate(string panelName)
    {
        ProfileRegistry.ActivateForPanel(panelName);
        var profile = ProfileRegistry.GetActiveProfile();
        Assert.NotNull(profile);
        return profile;
    }

    [Fact]
    public void PortalKeepsWidgetHeadingsSearchAndInteractiveTiles()
    {
        var profile = Activate("PortalScreen");
        Assert.Equal(2, profile.TryGetHeadingLevel(new("title-link-button", "SIButton", "Button", "")));
        Assert.False(profile.IsHidden(new("layout-tile-interaction-button", "SIButton", "Button", "")));
        Assert.Equal("Search messages", profile.TryGetRename(new("SearchButton", "SIButton", "Button", "")));
        Assert.False(profile.IsHidden(new("SearchButton", "SIButton", "Button", "")));
    }

    [Fact]
    public void PortalBudgetRuleKeepsTheActualMoneyAndSpentCaptions()
    {
        var profile = Activate("PortalScreen");
        const string path = "VisualElement(\"tile-states-transfer-budget-medium\")";
        Assert.True(profile.IsHidden(new("", "SIProgressBar", "ProgressBar", path)));
        Assert.False(profile.IsHidden(new("text", "SIText", "Text", path)));
        Assert.False(profile.IsHidden(new("", "SIProgressBar", "ProgressBar", "VisualElement(\"other-widget\")")));
    }

    [Fact]
    public void CoachStyleQuestionHeadsTheSectionWithoutHeadingItsHelp()
    {
        var profile = Activate("ManagerPersonaManagementStyleScreenContainer");
        Assert.Equal(2, profile.TryGetHeadingLevel(new("text", "SIText", "Text", "", "header", 0)));
        Assert.Equal(0, profile.TryGetHeadingLevel(new("text", "SIText", "Text", "", "header", 1)));
        Assert.False(profile.IsHidden(new("text", "SIText", "Text", "", "header", 1)));
    }

    [Fact]
    public void TutorialKeepsItsExplanationWhenTheNativeTitleBecomesThePageHeading()
    {
        var profile = Activate("TutorialDialog");
        Assert.True(profile.IsHidden(new("", "SIText", "Text", "", "FrameTexts", 0)));
        Assert.False(profile.IsHidden(new("", "SIText", "Text", "", "FrameTexts", 1)));
        Assert.True(profile.IsHidden(new("Background", "SIImage", "Image", "")));
        Assert.False(profile.IsHidden(new("EndTutorialButton", "SIButton", "Button", "")));
    }

    [Fact]
    public void EveryShippedProfileRegisters()
    {
        foreach (var file in Directory.GetFiles(ProfilesDir, "*.json"))
        {
            var panelName = Path.GetFileNameWithoutExtension(file);
            ProfileRegistry.ActivateForPanel(panelName);
            var profile = ProfileRegistry.GetActiveProfile();
            Assert.True(profile != null, $"{panelName}.json did not register (malformed JSON or wrong panelName)");
            Assert.Equal(panelName, profile.PanelName);
        }
    }

    [Fact]
    public void PlayerRecommendations_HidesDuplicateNationFlagButKeepsNamedCountry()
    {
        var profile = Activate("RecruitmentPlayerRecommendationsCard");

        var flag = new ElementMatchInfo(
            "NationButton", "SIButton", "Button",
            "TemplateContainer(\"NationFlag_Small\") > BindingExpect(\"NationFlag_Small\") > BindingSetter > BindableSwitchElement(\"IsClickable\")",
            "IsClickable");
        var namedCountry = new ElementMatchInfo(
            "data_display-cell-country-small-left-aligned", "SIButton", "Button",
            "BindingRemapper > TemplateContainer > BindingExpect(\"binding_expect\") > BindableSwitchElement",
            "binding_expect");

        Assert.True(profile.IsHidden(flag));
        Assert.Null(profile.TryGetRename(flag));
        Assert.False(profile.IsHidden(namedCountry));
    }

    [Fact]
    public void PlayerRecommendations_HeadsProsAndConsAndHidesTheirDecorativeIcons()
    {
        var profile = Activate("RecruitmentPlayerRecommendationsCard");

        var pros = new ElementMatchInfo(
            "text", "SIText", "Text",
            "VisualElement(\"Frame\") > VisualElement(\"pros\") > VisualElement(\"middle-column\") > BindingExpect",
            "Frame");
        var cons = new ElementMatchInfo(
            "text", "SIText", "Text",
            "VisualElement(\"Frame\") > VisualElement(\"cons\") > VisualElement(\"middle-column\") > BindingExpect",
            "Frame");
        var bullet = new ElementMatchInfo(
            "game concepts-red card", "SIImage", "Image",
            "BindingExpect > VisualElement(\"data_display-string-list-item-pro-con\") > VisualElement(\"data_display-string-list-item-default\")",
            "BindingExpect");

        Assert.Equal(2, profile.TryGetHeadingLevel(pros));
        Assert.Equal(2, profile.TryGetHeadingLevel(cons));
        Assert.True(profile.IsHidden(bullet));
    }

    [Fact]
    public void PlayerRecommendationSettings_HidesDeliveryHeadersFoldedIntoRadioLabels()
    {
        var profile = Activate("PlayerRecommendationsAdvancedSettingsDialog");

        var header = new ElementMatchInfo(
            "", "SIText", "Text",
            "BindingRemapper > TemplateContainer > BindingExpect > VisualElement(\"HeaderTextContainer\")",
            "HeaderTextContainer");
        var rowLabel = new ElementMatchInfo(
            "SIText", "SIText", "Text",
            "BindingVariables > VisualElement > SIEnabled(\"SIEnabled\") > VisualElement",
            "VisualElement");

        Assert.True(profile.IsHidden(header));
        Assert.False(profile.IsHidden(rowLabel));
    }

    // The substitutes-strip chips (S-number, name, position, player-card button) are hidden on the
    // tactics planner: the team-selection table carries the same players in full.
    [Fact]
    public void TacticalPlannerTool_HidesSubstituteChipStops()
    {
        var profile = Activate("TacticalPlannerTool");
        string chipPath = "VisualElement(\"HasPlayer\") > BindableSwitchElement > VisualElement(\"FM\") > FMDraggableObject(\"FMDraggableObject\")";

        Assert.True(profile.IsHidden(new ElementMatchInfo("sub", "SIText", "Text", chipPath, "FMDraggableObject")));
        Assert.True(profile.IsHidden(new ElementMatchInfo("name", "SIText", "Text", chipPath, "FMDraggableObject")));
        Assert.True(profile.IsHidden(new ElementMatchInfo("position", "SIText", "Text", chipPath, "FMDraggableObject")));
        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "PlayerSelectionCard", "SIButton", "Button",
            "SIVisible(\"OpenCardVisible\") > FMDraggableObject(\"FMDraggableObject\")", "OpenCardVisible")));
    }

    // The whole strip goes, header included: every slot it offers is already a row of the
    // selection table. The header is in reach of the section wrapper; each empty slot's "Sn"/"Pick"/picker
    // trio sits far deeper and is caught by the NoPlayer container instead.
    [Fact]
    public void TacticalPlannerTool_HidesSubstituteStripLikeFM26()
    {
        var profile = Activate("TacticalPlannerTool");
        string headerPath = "VisualElement(\"Header\") > SIStyleSetter > BindingExpect(\"tactics-pitch-both-sub section\") > TemplateContainer";

        Assert.True(profile.IsHidden(new ElementMatchInfo("title", "SIText", "Text", headerPath, "Header")));
        Assert.True(profile.IsHidden(new ElementMatchInfo("text", "SIText", "Text", headerPath, "")));

        string emptySlot = "SIStyleSetter(\"is-fmc-tactics-switch\") > VisualElement(\"NoPlayer\") > BindableSwitchElement > BindingSetter";
        Assert.True(profile.IsHidden(new ElementMatchInfo("sub", "SIText", "Text", emptySlot, "is-fmc-tactics-switch")));
        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "pick", "SIText", "Text",
            "SIStyleSetter > " + emptySlot, "")));
        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "dropdown-base-no-background", "SIDropdown", "Dropdown",
            "TemplateContainer(\"NonXIDropdown\") > BindableSwitchElement(\"is-fmc-tactics\") > SIVisible(\"notInMatch\") > VisualElement(\"NoPlayer\")", "NonXIDropdown")));
    }

    // The pitch header's collapse toggle goes on every tab, including Combined, where no pitch
    // grid renders and the walk's own removal therefore never fired. The Formation button beside
    // it — one SIVisible deep, not two — is the control the strip exists for and must survive.
    [Fact]
    public void TacticalPlannerTool_PreservesNativePitchExpandAndFormation()
    {
        var profile = Activate("TacticalPlannerTool");

        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "", "SIButton", "Button",
            "SIVisible > SIVisible > VisualElement(\"RHS\") > TutorialAnchorElement", "")));
        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "", "SIButton", "Button",
            "SIVisible > VisualElement(\"RHS\") > TutorialAnchorElement > BindingExpect", "")));
    }

    // The chip hides key on parentName FMDraggableObject — the literal element name unique to the
    // strip. Same-named texts elsewhere (the pitch cards' draggable is named "tactics-draggable",
    // the table's is "DragObject") must not be caught.
    [Fact]
    public void TacticalPlannerTool_ChipHidesDoNotReachPitchOrTable()
    {
        var profile = Activate("TacticalPlannerTool");

        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "PlayerNameText", "SIText", "Text", "FMDraggableObject(\"tactics-draggable\")", "tactics-draggable")));
        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "name", "SIText", "Text", "FMDraggableObject(\"tactics-draggable\")", "tactics-draggable")));
        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "Pick", "SIText", "Text", "BindableSwitchElement > VisualElement(\"VisualElement\")", "")));
    }

    [Fact]
    public void TacticalPlannerTool_RenamesOppositionCrest()
    {
        var profile = Activate("TacticalPlannerTool");

        Assert.Equal("Opposition club site", profile.TryGetRename(new ElementMatchInfo(
            "ClubButton", "SIButton", "Button",
            "BindingSetter(\"ActiveClubIconSetter\") > SIEnabled(\"ActiveClubEnabled\") > SIAspectRatioFitter(\"ClubRatioFitter\") > SIVisible(\"IsClickable\")", "IsClickable")));
        // The Opposition rename is scoped to the opponent-crest widget: a same-named crest button
        // elsewhere in the panel must not be relabelled as the opposition.
        Assert.Null(profile.TryGetRename(new ElementMatchInfo(
            "ClubButton", "SIButton", "Button", "SIVisible(\"IsClickable\") > SIAspectRatioFitter(\"ClubRatioFitter\")", "IsClickable")));
    }

    // FM27's empty Name-cell dropdown is disabled. Its Esc-cell picker remains
    // the native way to assign the position; the role button opens its details.
    [Fact]
    public void TacticalPlannerTool_PreservesPositionPickerAndRoleDetails()
    {
        var profile = Activate("TacticalPlannerTool");

        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "XIButton", "SIButton", "Button", "BindableSwitchElement(\"Interactions\") > SIEnabled", "Interactions")));
        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "", "SIButton", "Button", "VisualElement(\"buttons-hybrid-split-button\") > BindingRemapper", "")));
        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "XIButtonRow", "SIButton", "Button", "BindableSwitchElement(\"Interactions\") > SIEnabled", "Interactions")));
    }

    // The substitute rows' Pkd-cell slot dropdown goes too (per user). The Player cell's dropdown
    // is the same template name one cell over — it changes who holds the slot and must survive, so
    // the rule leans on the "Interactions" wrapper that only the Pkd control has.
    [Fact]
    public void TacticalPlannerTool_HidesPkdSlotDropdownNotThePlayerDropdown()
    {
        var profile = Activate("TacticalPlannerTool");

        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "dropdown-base-no-background", "SIDropdown", "Dropdown",
            "TemplateContainer > BindableSwitchElement(\"Interactions\") > SIEnabled > VisualElement", "")));
        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "dropdown-base-no-background", "SIDropdown", "Dropdown",
            "TemplateContainer > BindingVariables > SIEnabled > VisualElement", "")));
    }

    // --- The create-a-tactic wizard, the same panel's layout when the club has no tactic yet ---
    //
    // Paths below are the live 8-level leaf-first ancestor chains the mod builds for these stops
    // (the F10 / GET /classify dumps print the same chain top-down, four levels).

    private const string WizardStepTitlePath =
        "BindableSwitchElement > VisualElement(\"header\") > BindingExpect > TemplateContainer";
    private const string WizardStyleNamePath =
        "BindableSwitchElement > VisualElement(\"regular-state\") > BindableSwitchElement > VisualElement(\"details_container\")";
    private const string WizardInstructionTitlePath =
        "BindingRemapper > VisualElement > VisualElement(\"unity-content-container\") > VisualElement(\"unity-content-viewport\")";
    private const string WizardStepBarPath =
        "VisualElement(\"other-info\") > SIVisible > VisualElement(\"body\") > BindingExpect";
    // The formation steps' preview: the phase's chosen formation name, and the caption beneath it.
    private const string WizardPreviewValuePath =
        "BindingRemapper > VisualElement > VisualElement(\"regular-state\") > BindableSwitchElement";
    private const string WizardPreviewCaptionPath =
        "VisualElement > VisualElement(\"regular-state\") > BindableSwitchElement > VisualElement(\"details_container\")";

    // Every section the wizard shows becomes a landmark: the step title in the tool header, and on
    // step 1 the selected style's name over its detail pane, the staff-feedback list, the two
    // instruction lists, and the step roadmap's title.
    [Fact]
    public void TacticalPlannerTool_WizardSectionsAreHeadings()
    {
        var profile = Activate("TacticalPlannerTool");

        var stepTitle = new ElementMatchInfo("", "SIText", "Text", WizardStepTitlePath, "");
        Assert.True(profile.IsHeading(stepTitle));
        Assert.Equal(2, profile.TryGetHeadingLevel(stepTitle));

        var styleName = new ElementMatchInfo("selected-tactic-name", "SIText", "Text", WizardStyleNamePath, "");
        Assert.True(profile.IsHeading(styleName));
        Assert.Equal(2, profile.TryGetHeadingLevel(styleName));

        var feedback = new ElementMatchInfo("staff-feedback-title", "SIText", "Text",
            "VisualElement(\"title-and-feedback\") > VisualElement(\"staff-feedback\") > VisualElement > SIVisible", "title-and-feedback");
        Assert.Equal(3, profile.TryGetHeadingLevel(feedback));

        var instructions = new ElementMatchInfo("", "SIText", "Text", WizardInstructionTitlePath, "");
        Assert.True(profile.IsHeading(instructions));
        Assert.Equal(3, profile.TryGetHeadingLevel(instructions));

        var stepBarTitle = new ElementMatchInfo("", "SIText", "Text", WizardStepBarPath, "other-info");
        Assert.True(profile.IsHeading(stepBarTitle));
        Assert.Equal(2, profile.TryGetHeadingLevel(stepBarTitle));
    }

    // The formation steps reuse "selected-tactic-name" for their preview values and sit their
    // captions under the same "regular-state" pane, one level deeper than step 1's detail title.
    // Neither is a section: promoting them would scatter headings through a two-line preview. The
    // step roadmap's own entries ("1. Tactical Style") must stay plain text under its title too.
    [Fact]
    public void TacticalPlannerTool_WizardHeadingsSpareThePreviewAndStepList()
    {
        var profile = Activate("TacticalPlannerTool");

        Assert.False(profile.IsHeading(new ElementMatchInfo(
            "selected-tactic-name", "SIText", "Text", WizardPreviewValuePath, "")));
        Assert.False(profile.IsHeading(new ElementMatchInfo(
            "", "SIText", "Text", WizardPreviewCaptionPath, "")));
        Assert.False(profile.IsHeading(new ElementMatchInfo(
            "", "SIText", "Text",
            "SIStyleSetter > VisualElement(\"other-info\") > SIVisible > VisualElement(\"body\")", "")));
    }

    // The wizard's empty-state pane repeats the step title it sits beside ("Select In Possession
    // Formation" under "Choose Formation (In Possession)"), so the duplicate goes; the hint text
    // beside it says something new and stays.
    [Fact]
    public void TacticalPlannerTool_HidesEmptyStateDuplicateTitleOnly()
    {
        var profile = Activate("TacticalPlannerTool");

        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "text", "SIText", "Text",
            "VisualElement(\"Frame\") > VisualElement(\"ip-empty\") > VisualElement(\"empty-states\") > BindableSwitchElement", "Frame")));
        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "text", "SIText", "Text",
            "VisualElement(\"Frame\") > VisualElement(\"oop-empty\") > VisualElement(\"empty-states\") > BindableSwitchElement", "Frame")));
        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "", "SIText", "Text",
            "VisualElement(\"ip-empty\") > VisualElement(\"empty-states\") > BindableSwitchElement > VisualElement(\"details_container\")", "ip-empty")));
    }

    // The wizard's step-back control is labelled "Back" by the game, indistinguishable from the
    // title bar's screen-history Back. The forward controls name their destination, so they keep
    // the game's own text and gain their direction in CoreEntry — no profile rename may claim them.
    [Fact]
    public void TacticalPlannerTool_RenamesOnlyTheWizardBackControl()
    {
        var profile = Activate("TacticalPlannerTool");

        Assert.Equal("Previous step", profile.TryGetRename(new ElementMatchInfo(
            "", "SIButton", "Button",
            "SIVisible(\"back-vis\") > VisualElement > VisualElement(\"header\") > BindingExpect", "back-vis")));
        Assert.Null(profile.TryGetRename(new ElementMatchInfo(
            "", "SIButton", "Button",
            "SIEnabled > SIVisible(\"ip-vis\") > VisualElement > VisualElement(\"header\")", "SIEnabled")));
        Assert.Null(profile.TryGetRename(new ElementMatchInfo(
            "", "SIButton", "Button",
            "SIEnabled > SIVisible(\"oop-vis\") > VisualElement > VisualElement(\"header\")", "SIEnabled")));
    }

    // The wizard rules share the panel with the full planner (the layout once a tactic exists), so
    // each is anchored to a chain the planner does not have. The planner's own stops keep the
    // headings, hides and renames they had before the wizard rules landed.
    [Fact]
    public void TacticalPlannerTool_WizardRulesSpareTheFullPlanner()
    {
        var profile = Activate("TacticalPlannerTool");

        var instructionHeader = new ElementMatchInfo(
            "text", "SIText", "Text",
            "VisualElement(\"title frame\") > BindingExpect(\"team-instruction-header\") > TemplateContainer", "title frame");
        Assert.Equal(2, profile.TryGetHeadingLevel(instructionHeader));
        Assert.Equal("Instruction details", profile.TryGetRename(instructionHeader));

        var phaseText = new ElementMatchInfo("PhaseText", "SIText", "Text",
            "VisualElement(\"PhaseHeader\") > VisualElement", "PhaseHeader");
        Assert.Equal(3, profile.TryGetHeadingLevel(phaseText));

        // A planner text under a scroll viewport, but not under the wizard's BindingRemapper block.
        Assert.False(profile.IsHeading(new ElementMatchInfo(
            "PlayerNameText", "SIText", "Text",
            "VisualElement > VisualElement(\"unity-content-container\") > VisualElement(\"unity-content-viewport\")", "")));
        // A planner button under a header, but not the wizard's back slot.
        Assert.Null(profile.TryGetRename(new ElementMatchInfo(
            "analysis-button", "SIButton", "Button",
            "SIVisible(\"analysis-vis\") > VisualElement > VisualElement(\"header\") > BindingExpect", "analysis-vis")));
    }

    // The Match Squad screen shares the table template. The whole substitutes strip is hidden
    // (its content is duplicated by the selection table's Pkd column): the section header by its
    // "sub section" ancestor, every chip stop by the subs' own FMDraggableObject wrapper — which
    // must never catch the pitch chips' differently named "tactics-draggable" wrapper. The Pkd
    // cells' XIButton is hidden too (per user, the cell keeps only its position text); the
    // Tactics Planner profile keeps its own XIButton rename untouched.
    [Fact]
    public void FullScreenTeamSelection_HidesSubstitutesStripAndXIButton()
    {
        var profile = Activate("FullScreenTeamSelection");

        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "XIButton", "SIButton", "Button", "BindableSwitchElement(\"Interactions\") > SIEnabled", "Interactions")));
        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "title", "SIText", "Text",
            "VisualElement(\"Header\") > SIStyleSetter > BindingExpect(\"tactics-pitch-both-sub section\")", "Header")));
        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "PlayerSelectionCard", "SIButton", "Button",
            "SIVisible(\"OpenCardVisible\") > FMDraggableObject(\"FMDraggableObject\") > VisualElement(\"FM\")", "OpenCardVisible")));
        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "sub", "SIText", "Text",
            "FMDraggableObject(\"FMDraggableObject\") > VisualElement(\"FM\") > BindableSwitchElement", "FMDraggableObject")));
        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "fm-tactics-shirt-number", "SIText", "Text",
            "Image(\"fm-tactics-shirt-kit-image\") > FMTacticsShirt(\"FMTacticsShirt\") > FMDraggableObject(\"tactics-draggable\")", "fm-tactics-shirt-kit-image")));
    }

    // The in-match tactics panel is a third panel carrying the same selection table, so it needed
    // its own copy of the Pkd-cell rules: the textless picker button (which reads as the bare
    // element name) and the substitute rows' slot-reorder dropdown. The Player cell's dropdown one
    // cell over is the same template name and performs the substitution, so it must survive —
    // the rule leans on the "Interactions" wrapper that only the Pkd control has.
    [Fact]
    public void MatchTactics_HidesPkdPickerAndSlotDropdownNotThePlayerDropdown()
    {
        var profile = Activate("MatchTactics");

        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "XIButton", "SIButton", "Button", "BindableSwitchElement(\"Interactions\") > SIEnabled", "Interactions")));
        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "dropdown-base-no-background", "SIDropdown", "Dropdown",
            "TemplateContainer > BindableSwitchElement(\"Interactions\") > SIEnabled > VisualElement", "")));
        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "dropdown-base-no-background", "SIDropdown", "Dropdown",
            "TemplateContainer > BindingVariables > SIEnabled > VisualElement", "")));
    }

    // The in-match panel renders the planner's templates, so the two profiles must agree on every
    // stop they share. Asserted stop-by-stop rather than by comparing rule lists: the paths differ
    // in shape between the panels, and it is the verdict that has to match, not the JSON.
    [Fact]
    public void MatchTacticsMatchesThePlannerOnTheSharedStops()
    {
        var shared = new[]
        {
            // The pitch view-mode controls.
            new ElementMatchInfo("buttons-button-base-default", "SIButton", "Button",
                "VisualElement(\"view-pitch-checkboxes\") > BindingVariables > BindingExpect > TemplateContainer", "view-pitch-checkboxes"),
            new ElementMatchInfo("analysis-button", "SIButton", "Button",
                "BindingSetter > SIEnabled > SIVisible(\"analysis-toggle-visible\") > SIStyleSetter", "BindingSetter"),
            new ElementMatchInfo("analysis-button", "SIButton", "Button",
                "SIEnabled > BindingSetter > SIVisible > SIVisible(\"analysis-toggle-visible\")", "SIEnabled"),
            // A filled slot's chip remains represented by the selection table.
            new ElementMatchInfo("sub", "SIText", "Text",
                "VisualElement(\"HasPlayer\") > BindableSwitchElement > VisualElement(\"FM\") > FMDraggableObject(\"FMDraggableObject\")", "FMDraggableObject"),
        };

        var inMatch = Activate("MatchTactics");
        var planner = Activate("TacticalPlannerTool");

        foreach (var stop in shared)
        {
            Assert.True(planner.IsHidden(stop), $"planner should hide {stop.Name}/{stop.AncestorPath}");
            Assert.True(inMatch.IsHidden(stop), $"MatchTactics should hide {stop.Name}/{stop.AncestorPath}");
        }

        // The Formation button sits one SIVisible shallower and must survive on both.
        var formation = new ElementMatchInfo("", "SIButton", "Button",
            "SIVisible > VisualElement(\"RHS\") > TutorialAnchorElement > BindingExpect", "SIVisible");
        Assert.False(planner.IsHidden(formation));
        Assert.False(inMatch.IsHidden(formation));
    }

    // The create-a-tactic wizard is the planner's no-tactic state and cannot be reached from inside
    // a match, so its rules are deliberately absent here. Without this, "port the rest of the
    // planner" reads as an oversight rather than a decision.
    [Fact]
    public void MatchTacticsOmitsTheWizardRules()
    {
        var profile = Activate("MatchTactics");

        Assert.Equal(0, profile.TryGetHeadingLevel(new ElementMatchInfo(
            "SIText", "SIText", "Text", WizardStepTitlePath, "header")));
        Assert.Null(profile.TryGetRename(new ElementMatchInfo(
            "", "SIButton", "Button", "SIVisible(\"back-vis\") > VisualElement", "back-vis")));
        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "text", "SIText", "Text", "VisualElement(\"empty-states\") > BindingExpect", "empty-states")));
    }

    // The Club Vision negotiation meeting draws its sections as plain text among the board's prose,
    // so the whole card read as one undifferentiated run. Each section title becomes a landmark and
    // each objective's name a landmark under it, so the objectives can be walked one at a time.
    // The rules key on the template's own names; the same-named texts that are content, not
    // titles, must stay plain.
    [Fact]
    public void ClubVisionNegotiation_HeadsMeetingSectionsAndEachObjective()
    {
        var profile = Activate("ClubVisionNegotiationCard");

        Assert.Equal(2, profile.TryGetHeadingLevel(new ElementMatchInfo(
            "", "SIText", "Text",
            "VisualElement(\"MeetingInfo\") > BindingVariables(\"BindingVariables\") > VisualElement(\"VisualElement\")", "MeetingInfo")));
        Assert.Equal(2, profile.TryGetHeadingLevel(new ElementMatchInfo(
            "text", "SIText", "Text",
            "VisualElement(\"Frame\") > VisualElement(\"NameAndHierarhy\") > VisualElement(\"Frame\") > VisualElement(\"TopFrame\")", "Frame")));
        Assert.Equal(2, profile.TryGetHeadingLevel(new ElementMatchInfo(
            "NoObjectivesText", "SIText", "Text",
            "VisualElement(\"Frame\") > BindingExpect > TemplateContainer > BindingRemapper(\"BoardVision\")", "Frame")));
        Assert.Equal(2, profile.TryGetHeadingLevel(new ElementMatchInfo(
            "ActionableTitle", "SIText", "Text",
            "VisualElement(\"ActionableSection\") > VisualElement(\"BoardObjectives\")", "ActionableSection")));
        Assert.Equal(2, profile.TryGetHeadingLevel(new ElementMatchInfo(
            "InformationalTitle", "SIText", "Text",
            "VisualElement(\"InformationalSection\") > VisualElement(\"BoardObjectives\")", "InformationalSection")));
        // Each objective's name nests one level below its section title.
        Assert.Equal(3, profile.TryGetHeadingLevel(new ElementMatchInfo(
            "ClubVisionName", "SIText", "Text",
            "VisualElement > NavigatableVisualElement > VisualElement > SIStyleSetter", "")));
    }

    // The role line beside the speaker's name, the meeting's location and host line, and the
    // "no informational objectives" message are all content the game draws with the same element
    // names as the titles above them. Heading any of them would scatter landmarks through prose.
    [Fact]
    public void ClubVisionNegotiation_HeadingsSpareTheProseBesideThem()
    {
        var profile = Activate("ClubVisionNegotiationCard");

        Assert.False(profile.IsHeading(new ElementMatchInfo(
            "text", "SIText", "Text",
            "VisualElement(\"NameAndHierarhy\") > VisualElement(\"Frame\") > VisualElement(\"TopFrame\") > VisualElement(\"RightFrame\")", "NameAndHierarhy")));
        Assert.False(profile.IsHeading(new ElementMatchInfo(
            "", "SIText", "Text",
            "VisualElement(\"BottomFrame\") > VisualElement(\"MeetingInfo\") > BindingVariables(\"BindingVariables\")", "BottomFrame")));
        Assert.False(profile.IsHeading(new ElementMatchInfo(
            "NoObjectivesText", "SIText", "Text",
            "SIVisible > BindingVariables > VisualElement(\"InformationalSection\") > VisualElement(\"BoardObjectives\")", "")));
    }

    // The card's decoration: the title bar's club crest (which reads as an unlabeled icon button)
    // and the per-objective category sprite, five of which read only as "ObjectiveIcon, image".
    // The objective's name and importance carry everything either conveys.
    [Fact]
    public void ClubVisionNegotiation_HidesTheCrestAndObjectiveIcons()
    {
        var profile = Activate("ClubVisionNegotiationCard");

        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "ClubButton", "SIButton", "Button",
            "SIVisible(\"IsClickable\") > SIAspectRatioFitter(\"ClubRatioFitter\") > SIEnabled(\"ActiveClubEnabled\") > BindingSetter(\"ActiveClubIconSetter\")", "IsClickable")));
        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "ObjectiveIcon", "SIImage", "Image",
            "SIVisible(\"ObjectiveIconVisible\") > VisualElement > NavigatableVisualElement > VisualElement", "ObjectiveIconVisible")));
        // The 5-Year Plan's season objectives draw a competition crest in the same slot.
        Assert.True(profile.IsHidden(new ElementMatchInfo(
            "CompetitionIcon", "SIImage", "Image",
            "SIVisible(\"CompetitionIconVisible\") > VisualElement > NavigatableVisualElement > VisualElement", "CompetitionIconVisible")));
        // The objective's own name and its Delete button are the row's content and control; both stay.
        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "ClubVisionName", "SIText", "Text",
            "VisualElement > NavigatableVisualElement > VisualElement > SIStyleSetter", "")));
        Assert.False(profile.IsHidden(new ElementMatchInfo(
            "", "SIButton", "Button",
            "SIVisible(\"RemoveVisible\") > VisualElement(\"FrameRemove\") > VisualElement > SIStyleSetter", "RemoveVisible")));
    }

    // The 5-Year Plan tab's "add an objective" combobox carries no label of its own, so the label
    // resolver reached back to the nearest preceding text and announced it as "Board Visions" — the
    // section title above it, which says nothing about what the control does.
    [Fact]
    public void ClubVisionNegotiation_NamesTheAddObjectiveCombobox()
    {
        var profile = Activate("ClubVisionNegotiationCard");

        Assert.Equal("Competition objective", profile.TryGetRename(new ElementMatchInfo(
            "inputs-combobox-default-default", "SIDropdown", "Dropdown",
            "BindingVariables(\"SelectedObjectiveBindingVariable\") > SIEnabled(\"HasObjectivesToAdd\") > SIVisible(\"FiveYearPlan-Visible\") > SIEnabled(\"LeftButtons\")",
            "SelectedObjectiveBindingVariable")));
        // A same-template combobox elsewhere on the card keeps whatever the game gives it.
        Assert.Null(profile.TryGetRename(new ElementMatchInfo(
            "inputs-combobox-default-default", "SIDropdown", "Dropdown",
            "BindingVariables > SIEnabled > VisualElement(\"Body\")", "BindingVariables")));
    }

    // The out-of-match panels must not inherit the in-match rules, nor lose their own: the three
    // profiles are independent files keyed by panel name, and a swap between them would silently
    // strip a panel the user asked to leave alone.
    [Fact]
    public void MatchTacticsRulesDoNotLeakIntoOtherPanels()
    {
        var pkdButton = new ElementMatchInfo(
            "XIButton", "SIButton", "Button", "BindableSwitchElement(\"Interactions\") > SIEnabled", "Interactions");

        Assert.True(Activate("MatchTactics").IsHidden(pkdButton));
        // The planner's empty-slot commands were qualified separately in F194.
        // Match-only profiles retain their inherited behavior pending a match.
        Assert.False(Activate("TacticalPlannerTool").IsHidden(pkdButton));
        Assert.True(Activate("FullScreenTeamSelection").IsHidden(pkdButton));

        // TeamSquadReport shares neither the Pkd column nor these rules.
        ProfileRegistry.ActivateForPanel("TeamSquadReport");
        Assert.False(ProfileRegistry.GetActiveProfile().IsHidden(pkdButton));
    }
}
