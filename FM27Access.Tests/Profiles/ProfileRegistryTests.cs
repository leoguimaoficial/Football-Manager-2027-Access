using FM27Access.Navigation.Profiles;

namespace FM27Access.Tests.Profiles;

public class ProfileRegistryTests : IDisposable
{
    public ProfileRegistryTests()
    {
        Speech.Log.LogFn = _ => { };
        Speech.Log.WarnFn = _ => { };
        Speech.Log.ErrorFn = _ => { };
    }

    public void Dispose()
    {
        ProfileRegistry.Clear();
    }

    [Fact]
    public void ActivateForPanel_WithRegisteredProfile_SetsActive()
    {
        var profile = new PanelProfile
        {
            PanelName = "PlayerReport",
            Headings = { new HeadingRule { Match = new StopMatcher { Name = "PlayerName" } } }
        };
        ProfileRegistry.Register(profile);
        ProfileRegistry.ActivateForPanel("PlayerReport");

        Assert.Same(profile, ProfileRegistry.GetActiveProfile());
    }

    [Fact]
    public void ActivateForPanel_WithNoProfile_SetsNull()
    {
        ProfileRegistry.ActivateForPanel("UnknownPanel");
        Assert.Null(ProfileRegistry.GetActiveProfile());
    }

    [Fact]
    public void ClearActiveProfile_ClearsIt()
    {
        var profile = new PanelProfile { PanelName = "Test" };
        ProfileRegistry.Register(profile);
        ProfileRegistry.ActivateForPanel("Test");
        ProfileRegistry.ClearActiveProfile();

        Assert.Null(ProfileRegistry.GetActiveProfile());
    }

    [Fact]
    public void GetHeadingLevel_FirstMatchingRuleWins()
    {
        var profile = new PanelProfile
        {
            PanelName = "Card",
            Headings =
            {
                new HeadingRule { Match = new StopMatcher { Name = "GroupName" }, Level = 2 },
                new HeadingRule { Match = new StopMatcher { TypeName = "SIText" }, Level = 3 }
            }
        };
        ProfileRegistry.Register(profile);
        ProfileRegistry.ActivateForPanel("Card");

        // GroupName matches both rules; the first (level 2) must win, or every group
        // heading would demote to the per-item depth.
        var group = new ElementMatchInfo("GroupName", "SIText", "Text", "");
        var item = new ElementMatchInfo("text", "SIText", "Text", "");
        var button = new ElementMatchInfo("b", "SIButton", "Button", "");

        Assert.Equal(2, ProfileRegistry.GetHeadingLevel(group));
        Assert.Equal(3, ProfileRegistry.GetHeadingLevel(item));
        Assert.Equal(0, ProfileRegistry.GetHeadingLevel(button));
    }

    [Fact]
    public void GetHeadingLevel_NoActiveProfile_ReturnsZero()
    {
        Assert.Equal(0, ProfileRegistry.GetHeadingLevel(new ElementMatchInfo("x", "SIText", "Text", "")));
    }

    [Fact]
    public void IsHeading_WithActiveProfile_MatchesCorrectStop()
    {
        var profile = new PanelProfile
        {
            PanelName = "PlayerReport",
            Headings = { new HeadingRule { Match = new StopMatcher { Name = "PlayerName" } } }
        };
        ProfileRegistry.Register(profile);
        ProfileRegistry.ActivateForPanel("PlayerReport");

        var heading = new ElementMatchInfo("PlayerName", "SIText", "Text", "");
        var notHeading = new ElementMatchInfo("ClubButton", "SIButton", "Button", "");

        Assert.True(ProfileRegistry.IsHeading(heading));
        Assert.False(ProfileRegistry.IsHeading(notHeading));
    }

    [Fact]
    public void IsHeading_WithNoActiveProfile_ReturnsFalse()
    {
        var info = new ElementMatchInfo("PlayerName", "SIText", "Text", "");
        Assert.False(ProfileRegistry.IsHeading(info));
    }

    [Fact]
    public void TryGetRename_WithMatchingRule_ReturnsLabel()
    {
        var profile = new PanelProfile
        {
            PanelName = "Messages",
            Renames = { new RenameRule { Match = new StopMatcher { Name = "dropdown-icon-base", PathContains = "AdviceDropdown" }, Label = "Advice actions" } }
        };
        ProfileRegistry.Register(profile);
        ProfileRegistry.ActivateForPanel("Messages");

        var info = new ElementMatchInfo("dropdown-icon-base", "SIDropdown", "Dropdown", "AdviceDropdown > BindingVariables");
        Assert.Equal("Advice actions", ProfileRegistry.TryGetRename(info));
    }

    [Fact]
    public void TryGetRename_WithNoMatch_ReturnsNull()
    {
        var profile = new PanelProfile
        {
            PanelName = "Messages",
            Renames = { new RenameRule { Match = new StopMatcher { Name = "dropdown-icon-base", PathContains = "AdviceDropdown" }, Label = "Advice actions" } }
        };
        ProfileRegistry.Register(profile);
        ProfileRegistry.ActivateForPanel("Messages");

        var info = new ElementMatchInfo("SearchButton", "SIButton", "Button", "Actions > Buttons");
        Assert.Null(ProfileRegistry.TryGetRename(info));
    }

    [Fact]
    public void TryGetRename_WithNoActiveProfile_ReturnsNull()
    {
        var info = new ElementMatchInfo("dropdown-icon-base", "SIDropdown", "Dropdown", "AdviceDropdown");
        Assert.Null(ProfileRegistry.TryGetRename(info));
    }

    [Fact]
    public void ActivateForPanel_ReactivatesAfterDifferentProfileWasActive()
    {
        var portalProfile = new PanelProfile
        {
            PanelName = "PortalScreen",
            Headings = { new HeadingRule { Match = new StopMatcher { ParentName = "IsDateSeparator" } } }
        };
        var messagesProfile = new PanelProfile
        {
            PanelName = "PortalMessagesTool",
            Headings = { new HeadingRule { Match = new StopMatcher { Name = "MessageTitle" } } }
        };
        ProfileRegistry.Register(portalProfile);
        ProfileRegistry.Register(messagesProfile);

        ProfileRegistry.ActivateForPanel("PortalScreen");
        Assert.Same(portalProfile, ProfileRegistry.GetActiveProfile());

        ProfileRegistry.ActivateForPanel("PortalMessagesTool");
        Assert.Same(messagesProfile, ProfileRegistry.GetActiveProfile());

        ProfileRegistry.ActivateForPanel("PortalScreen");
        Assert.Same(portalProfile, ProfileRegistry.GetActiveProfile());
    }

    [Fact]
    public void IsHidden_WithActiveProfile_MatchesCorrectStop()
    {
        var profile = new PanelProfile
        {
            PanelName = "ManagerSelection",
            Hides = { new HideRule { Match = new StopMatcher { ParentName = "AddNewManager", Classification = "Text" } } }
        };
        ProfileRegistry.Register(profile);
        ProfileRegistry.ActivateForPanel("ManagerSelection");

        var hidden = new ElementMatchInfo("", "SIText", "Text", "ManagerSelectionBox > AddNewManager", "AddNewManager");
        var notHidden = new ElementMatchInfo("AddNewManagerButtonNewGame", "SIButton", "Button", "ManagerSelectionBox > AddNewManager", "AddNewManager");

        Assert.True(ProfileRegistry.IsHidden(hidden));
        Assert.False(ProfileRegistry.IsHidden(notHidden));
    }

    [Fact]
    public void IsHidden_WithNoActiveProfile_ReturnsFalse()
    {
        var info = new ElementMatchInfo("", "SIText", "Text", "AddNewManager", "AddNewManager");
        Assert.False(ProfileRegistry.IsHidden(info));
    }

    [Fact]
    public void IsHidden_DoesNotAffectHeadings()
    {
        var profile = new PanelProfile
        {
            PanelName = "Test",
            Hides = { new HideRule { Match = new StopMatcher { Name = "SomeStop" } } },
            Headings = { new HeadingRule { Match = new StopMatcher { Name = "HeadingStop" } } }
        };
        ProfileRegistry.Register(profile);
        ProfileRegistry.ActivateForPanel("Test");

        var headingInfo = new ElementMatchInfo("HeadingStop", "SIText", "Text", "");
        Assert.True(ProfileRegistry.IsHeading(headingInfo));
        Assert.False(ProfileRegistry.IsHidden(headingInfo));
    }
}
