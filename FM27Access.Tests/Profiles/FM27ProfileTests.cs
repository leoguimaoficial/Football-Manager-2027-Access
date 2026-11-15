using System.Text.Json;
using FM27Access.Navigation.Profiles;

namespace FM27Access.Tests.Profiles;

public class FM27ProfileTests
{
    private static PanelProfile Load(string panel) => JsonSerializer.Deserialize<PanelProfile>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PanelProfiles", panel + ".json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public void CustomDatabaseTitleRulePreservesThePlayerCountInAnotherFrame()
    {
        var profile = Load("GameCreationCustomLeagueSetup");
        Assert.True(profile.IsHidden(new("", "SIText", "Text",
            "VisualElement(\"frame\") > BindingRoot > Panel(\"GameCreationCustomLeagueSetup\")", "frame", 0)));
        Assert.False(profile.IsHidden(new("", "SIText", "Text",
            "VisualElement(\"frame\") > VisualElement(\"player-count\") > VisualElement(\"foolter\") > VisualElement(\"frame\") > BindingRoot > Panel(\"GameCreationCustomLeagueSetup\")", "frame", 0)));
    }

    [Fact]
    public void ManagerTitleRulePreservesTheInstructions()
    {
        var profile = Load("GameCreationManagerSelectionScreen");
        Assert.True(profile.IsHidden(new("text", "SIText", "Text", "VisualElement(\"Header\")", "Header", 0)));
        Assert.False(profile.IsHidden(new("text", "SIText", "Text", "VisualElement(\"Header\")", "Header", 1)));
    }

    [Fact]
    public void LoadConfirmationTitleRulePreservesTheSelectedLeagueSummary()
    {
        var profile = Load("GameCreationLoadDataBaseDialogScreen");
        const string path = "VisualElement > VisualElement(\"frame\") > VisualElement(\"notifications-modal-dialog-default-default\")";
        Assert.True(profile.IsHidden(new("text", "SIText", "Text", path)));
        Assert.False(profile.IsHidden(new("dialog-description", "SIText", "Text", path)));
    }

    [Fact]
    public void AddNationsTitleRulePreservesCountryAndContinentLabels()
    {
        var profile = Load("AddNationsModal");
        Assert.True(profile.IsHidden(new("text", "SIText", "Text",
            "BindableSwitchElement > VisualElement > VisualElement > VisualElement(\"frame\")")));
        Assert.False(profile.IsHidden(new("text", "SIText", "Text",
            "VisualElement > SICheckBox(\"CheckboxDefault\") > BindingExpect")));
        Assert.False(profile.IsHidden(new("continent-tab-title-text", "SIText", "Text",
            "VisualElement > SIRadioButton > SIRadioGroup(\"ContinentTabs\")")));
    }
}
