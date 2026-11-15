using FM27Access.Navigation.Profiles;

namespace FM27Access.Tests.Profiles;

public class StopMatcherTests
{
    [Fact]
    public void MatchByName_ExactMatch()
    {
        var matcher = new StopMatcher { Name = "ClubButton" };
        var info = new ElementMatchInfo("ClubButton", "SIButton", "Button", "");
        Assert.True(matcher.Matches(info));
    }

    [Fact]
    public void MatchByName_NoMatch()
    {
        var matcher = new StopMatcher { Name = "ClubButton" };
        var info = new ElementMatchInfo("PlayerButton", "SIButton", "Button", "");
        Assert.False(matcher.Matches(info));
    }

    [Fact]
    public void MatchByTypeName()
    {
        var matcher = new StopMatcher { TypeName = "SIText" };
        var info = new ElementMatchInfo("", "SIText", "Text", "");
        Assert.True(matcher.Matches(info));
    }

    [Fact]
    public void MatchByClassification_CaseInsensitive()
    {
        var matcher = new StopMatcher { Classification = "button" };
        var info = new ElementMatchInfo("", "SIButton", "Button", "");
        Assert.True(matcher.Matches(info));
    }

    [Fact]
    public void MatchByPathContains()
    {
        var matcher = new StopMatcher { PathContains = "report-player-banner-max" };
        var info = new ElementMatchInfo("", "SIText", "Text", "VisualElement(\"report-player-banner-max\") > Panel");
        Assert.True(matcher.Matches(info));
    }

    [Fact]
    public void PathContains_NoMatch()
    {
        var matcher = new StopMatcher { PathContains = "report-player-banner-max" };
        var info = new ElementMatchInfo("", "SIText", "Text", "VisualElement(\"sidebar\") > Panel");
        Assert.False(matcher.Matches(info));
    }

    [Fact]
    public void AllFieldsMustMatch_AND_Logic()
    {
        var matcher = new StopMatcher
        {
            Classification = "Text",
            PathContains = "banner"
        };
        var matchInfo = new ElementMatchInfo("", "SIText", "Text", "VisualElement(\"banner\")");
        var noMatchInfo = new ElementMatchInfo("", "SIButton", "Button", "VisualElement(\"banner\")");

        Assert.True(matcher.Matches(matchInfo));
        Assert.False(matcher.Matches(noMatchInfo));
    }

    [Fact]
    public void EmptyMatcher_MatchesEverything()
    {
        var matcher = new StopMatcher();
        var info = new ElementMatchInfo("anything", "SIButton", "Button", "whatever");
        Assert.True(matcher.Matches(info));
    }

    [Fact]
    public void NullFields_InMatchInfo_DontCrash()
    {
        var matcher = new StopMatcher { Name = "Test" };
        // ElementMatchInfo constructor converts nulls to ""
        var info = new ElementMatchInfo(null, null, null, null);
        Assert.False(matcher.Matches(info));
    }

    [Fact]
    public void MatchByParentName()
    {
        var matcher = new StopMatcher { ParentName = "Goalkeeper" };
        var match = new ElementMatchInfo("", "SIText", "Text", "path", "Goalkeeper");
        var noMatch = new ElementMatchInfo("", "SIText", "Text", "path", "Outfielder");
        Assert.True(matcher.Matches(match));
        Assert.False(matcher.Matches(noMatch));
    }

    [Fact]
    public void MatchByPathStartsWith()
    {
        var matcher = new StopMatcher { PathStartsWith = "VisualElement(\"Frame\") > VisualElement(\"Block\")" };
        var match = new ElementMatchInfo("", "SIText", "Text", "VisualElement(\"Frame\") > VisualElement(\"Block\") > Panel");
        var noMatch = new ElementMatchInfo("", "SIText", "Text", "VisualElement(\"Frame\") > VisualElement(\"Frame\") > VisualElement(\"Block\")");
        Assert.True(matcher.Matches(match));
        Assert.False(matcher.Matches(noMatch));
    }

    [Fact]
    public void MatchByChildIndex()
    {
        // A row title and its description share name, type, and path; only position differs.
        var matcher = new StopMatcher { Name = "text", ChildIndex = 0 };
        var title = new ElementMatchInfo("text", "SIText", "Text", "path", "Frame", childIndex: 0);
        var description = new ElementMatchInfo("text", "SIText", "Text", "path", "Frame", childIndex: 1);
        Assert.True(matcher.Matches(title));
        Assert.False(matcher.Matches(description));
    }

    [Fact]
    public void ChildIndex_UnknownIndex_NeverMatches()
    {
        // -1 means the Plugin layer could not determine the position; a positional rule
        // must not fire on it, even for index 0.
        var matcher = new StopMatcher { ChildIndex = 0 };
        var info = new ElementMatchInfo("text", "SIText", "Text", "path", "Frame");
        Assert.False(matcher.Matches(info));
    }

    [Fact]
    public void ChildIndex_Unset_IgnoresPosition()
    {
        var matcher = new StopMatcher { Name = "text" };
        var info = new ElementMatchInfo("text", "SIText", "Text", "path", "Frame", childIndex: 7);
        Assert.True(matcher.Matches(info));
    }
}
