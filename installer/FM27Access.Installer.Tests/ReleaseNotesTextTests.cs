namespace FM27Access.Installer.Tests;

public sealed class ReleaseNotesTextTests
{
    [Fact] public void PlayerNotesRetainHeadingsParagraphsAndReadableBulletsWithoutMarkdown()
    {
        string markdown = "## Easier installation\r\n\r\n- **Repair** appears only once.\r\n- The installer asks before _updating_.\r\n\r\nRead the [user guide](https://example.com/guide).";
        string expected = "Easier installation\n\n• Repair appears only once.\n• The installer asks before updating.\n\nRead the user guide.";
        Assert.Equal(expected.Replace("\n", Environment.NewLine), ReleaseNotesText.Format(markdown));
    }

    [Fact] public void TextEntitiesUnicodeAndLiteralNamesArePreserved()
    {
        Assert.Equal("Correções & melhorias: versão 1.0.2, user_files.xml, version.dll.",
            ReleaseNotesText.Format("Correções &amp; melhorias: versão **1.0.2**, user_files.xml, `version.dll`."));
    }

    [Fact] public void HtmlCommentsSeparatorsAndCodeFenceMarkersAreNotReadAsChangelogSyntax()
    {
        Assert.Equal("Notes" + Environment.NewLine + Environment.NewLine + "Keep your settings.",
            ReleaseNotesText.Format("<!-- internal marker -->\n# Notes\n---\n\n```text\nKeep your settings.\n```"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n  ")]
    public void MissingNotesRemainEmpty(string? value) => Assert.Equal("", ReleaseNotesText.Format(value));
}
