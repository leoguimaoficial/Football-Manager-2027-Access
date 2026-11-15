using FM27Access.Plugin;
using Xunit;

namespace FM27Access.Tests;

public class NavigationSpeechMuteTests
{
    [Theory]
    [InlineData("Preferências e Acessibilidade", "Close, button")]
    [InlineData("O que há de novo no FM27?", "BGImage, image")]
    public void AutomaticEntryKeepsTitleWithoutTheCursorStop(string title, string firstStop)
    {
        NavigationSpeechMute.PanelEntry(() => title, () =>
            Assert.Equal(title, NavigationSpeechMute.Resolve(title + ". " + firstStop)));
        Assert.Equal(firstStop, NavigationSpeechMute.Resolve(firstStop));
    }

    [Fact]
    public void RestoredTitleIsReadAfterTheActualLandingChanges()
    {
        string title = "Dialog";
        bool restored = NavigationSpeechMute.PanelEntry(() => title, () =>
        {
            title = "Mensagens";
            Assert.Equal("Mensagens", NavigationSpeechMute.Resolve("Mensagens. first message, button"));
            return true;
        });
        Assert.True(restored);
        Assert.Equal("explicit cursor", NavigationSpeechMute.Resolve("explicit cursor"));
    }

    [Fact]
    public void ArrivalMuteStillWinsAndScopesRestoreAfterFailure()
    {
        Assert.Throws<InvalidOperationException>(() => NavigationSpeechMute.PanelEntry(() => "Portal", () =>
        {
            NavigationSpeechMute.Run(() => Assert.Null(NavigationSpeechMute.Resolve("first control")));
            Assert.Equal("Portal", NavigationSpeechMute.Resolve("first control"));
            throw new InvalidOperationException();
        }));
        Assert.False(NavigationSpeechMute.Active);
        Assert.Equal("explicit description", NavigationSpeechMute.Resolve("explicit description"));
    }

    [Fact]
    public void NestedPanelEntriesRestoreTheirOwnTitle()
    {
        NavigationSpeechMute.PanelEntry(() => "Portal", () =>
        {
            NavigationSpeechMute.PanelEntry(() => "Mensagens", () =>
                Assert.Equal("Mensagens", NavigationSpeechMute.Resolve("control")));
            Assert.Equal("Portal", NavigationSpeechMute.Resolve("control"));
        });
        Assert.Equal("requested read", NavigationSpeechMute.Resolve("requested read"));
    }
}
