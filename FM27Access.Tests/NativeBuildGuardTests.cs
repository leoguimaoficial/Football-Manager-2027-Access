using FM27Access.Speech;

namespace FM27Access.Tests;

public sealed class NativeBuildGuardTests
{
    private const string Identity = "SportsInteractive.FootballManager27";
    private const string OriginalAssembly = "FD0F8210FF0D52F5F3E5F1A4925FD2E3736A3DD167D2E17B355DC1C1173FE534";
    private const string OriginalMetadata = "AB3B75823275EC24CB8FC30FC428C7259F844CACA16A9A1BD650402D0CDF5420";
    private const string CurrentAssembly = "3B393291208F1C06135100EA644911FD8CE9CCDDC93B9248A7F4C7A560B1F3BE";
    private const string CurrentMetadata = "118705D69DD8C981DDEEC4ED815AAB212E676A07AFC6F819D48511E833078BD1";

    [Theory]
    [InlineData("0.9.2409.0", OriginalAssembly, OriginalMetadata)]
    [InlineData("0.9.2435.0", CurrentAssembly, CurrentMetadata)]
    public void AcceptsOnlyTheQualifiedNativePairs(string version, string assembly, string metadata)
    {
        Assert.True(NativeBuildGuard.Matches(Identity, version, assembly, metadata));
        Assert.True(NativeBuildGuard.Matches(Identity, version, assembly.ToLowerInvariant(), metadata.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("0.9.2435.0", OriginalAssembly, CurrentMetadata)]
    [InlineData("0.9.2435.0", CurrentAssembly, OriginalMetadata)]
    [InlineData("0.9.2409.0", CurrentAssembly, CurrentMetadata)]
    [InlineData("0.9.9999.0", CurrentAssembly, CurrentMetadata)]
    [InlineData("0.9.2435.0", "changed", CurrentMetadata)]
    public void RejectsAnAutomaticUpdateOrMixedNativePair(string version, string assembly, string metadata) =>
        Assert.False(NativeBuildGuard.Matches(Identity, version, assembly, metadata));

    [Fact]
    public void SameHashesUnderAnUnqualifiedIdentityAreRejected() =>
        Assert.False(NativeBuildGuard.Matches("Other.Game", "0.9.2435.0", CurrentAssembly, CurrentMetadata));

    [Theory]
    [InlineData(null)]
    [InlineData("<Game>")]
    [InlineData("<Game><Identity Name='Other.Game' Version='0.9.2435.0'/></Game>")]
    [InlineData("<Game><Identity Name='SportsInteractive.FootballManager27' Version='0.9.9999.0'/></Game>")]
    [InlineData("<Game><Identity Name='SportsInteractive.FootballManager27' Version='0.9.2435.0'/></Game>")]
    public void MissingMalformedOrUnknownInstallationsFailWithoutUnityInitialization(string? xml)
    {
        string root = Path.Combine(Path.GetTempPath(), "fm27-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if (xml != null) File.WriteAllText(Path.Combine(root, "MicrosoftGame.config"), xml);
            var result = NativeBuildGuard.Validate(root);
            Assert.False(result.Supported);
            Assert.NotEmpty(result.Reason);
            Assert.NotEmpty(result.Version);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{")]
    [InlineData("{\"Version\":\"0.9.2409.0\"}")]
    public void MissingMalformedOrDifferentDeploymentBindingIsRejected(string? json)
    {
        string root = Path.Combine(Path.GetTempPath(), "fm27-binding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "MicrosoftGame.config"), "<Game><Identity Name='SportsInteractive.FootballManager27' Version='0.9.2435.0'/></Game>");
            string profile = Path.Combine(root, "native-build.json");
            if (json != null) File.WriteAllText(profile, json);
            Assert.False(NativeBuildGuard.Validate(root, profile).Supported);
        }
        finally { Directory.Delete(root, true); }
    }
}
