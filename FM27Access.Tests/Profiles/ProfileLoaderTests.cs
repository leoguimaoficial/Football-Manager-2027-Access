using FM27Access.Navigation.Profiles;

namespace FM27Access.Tests.Profiles;

public class ProfileLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public ProfileLoaderTests()
    {
        Speech.Log.LogFn = _ => { };
        Speech.Log.WarnFn = _ => { };
        Speech.Log.ErrorFn = _ => { };
        _tempDir = Path.Combine(Path.GetTempPath(), "fm27access-test-profiles-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        ProfileRegistry.Clear();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void LoadsValidProfile()
    {
        var json = """
        {
            "panelName": "PlayerReport",
            "headings": [
                {
                    "match": { "name": "PlayerName" },
                    "level": 1
                }
            ]
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "PlayerReport.json"), json);

        ProfileLoader.LoadAll(_tempDir);

        ProfileRegistry.ActivateForPanel("PlayerReport");
        var profile = ProfileRegistry.GetActiveProfile();
        Assert.NotNull(profile);
        Assert.Equal("PlayerReport", profile.PanelName);
        Assert.Single(profile.Headings);
        Assert.Equal("PlayerName", profile.Headings[0].Match.Name);
    }

    [Fact]
    public void SkipsInvalidJson()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Bad.json"), "not json at all");

        ProfileLoader.LoadAll(_tempDir);

        ProfileRegistry.ActivateForPanel("Bad");
        Assert.Null(ProfileRegistry.GetActiveProfile());
    }

    [Fact]
    public void SkipsMissingPanelName()
    {
        var json = """{ "headings": [] }""";
        File.WriteAllText(Path.Combine(_tempDir, "Empty.json"), json);

        ProfileLoader.LoadAll(_tempDir);
        // No profile registered since panelName is null
    }

    [Fact]
    public void NonexistentDirectory_DoesNotThrow()
    {
        ProfileLoader.LoadAll(Path.Combine(_tempDir, "nonexistent"));
    }

    [Fact]
    public void LoadsProfileWithRenameRules()
    {
        var json = """
        {
            "panelName": "PortalMessagesTool",
            "headings": [],
            "renames": [
                {
                    "match": { "name": "dropdown-icon-base", "pathContains": "AdviceDropdown" },
                    "label": "Advice actions"
                }
            ]
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "PortalMessagesTool.json"), json);

        ProfileLoader.LoadAll(_tempDir);

        ProfileRegistry.ActivateForPanel("PortalMessagesTool");
        var profile = ProfileRegistry.GetActiveProfile();
        Assert.NotNull(profile);
        Assert.Single(profile.Renames);
        Assert.Equal("Advice actions", profile.Renames[0].Label);
        Assert.Equal("dropdown-icon-base", profile.Renames[0].Match.Name);
        Assert.Equal("AdviceDropdown", profile.Renames[0].Match.PathContains);
    }

    [Fact]
    public void DefaultHeadingLevel_IsOne()
    {
        var json = """
        {
            "panelName": "Test",
            "headings": [
                { "match": { "name": "Title" } }
            ]
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "Test.json"), json);

        ProfileLoader.LoadAll(_tempDir);

        ProfileRegistry.ActivateForPanel("Test");
        Assert.Equal(1, ProfileRegistry.GetActiveProfile().Headings[0].Level);
    }

    [Fact]
    public void ParsesChildIndexMatcher()
    {
        var json = """
        {
            "panelName": "Test",
            "headings": [
                { "match": { "name": "text", "childIndex": 0 }, "level": 3 }
            ]
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "Test.json"), json);

        ProfileLoader.LoadAll(_tempDir);

        ProfileRegistry.ActivateForPanel("Test");
        var rule = ProfileRegistry.GetActiveProfile().Headings[0];
        Assert.Equal(0, rule.Match.ChildIndex);
        Assert.Equal(3, rule.Level);
    }

    [Fact]
    public void ChildIndex_AbsentFromJson_IsNull()
    {
        var json = """
        {
            "panelName": "Test",
            "headings": [
                { "match": { "name": "text" } }
            ]
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, "Test.json"), json);

        ProfileLoader.LoadAll(_tempDir);

        ProfileRegistry.ActivateForPanel("Test");
        Assert.Null(ProfileRegistry.GetActiveProfile().Headings[0].Match.ChildIndex);
    }
}
