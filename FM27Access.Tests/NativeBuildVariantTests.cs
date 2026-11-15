using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using FM27Access.Platform;
using FM27Access.Speech;

namespace FM27Access.Tests;

// Synthetic installation contracts only; none of these are Steam runtime claims.
public sealed class NativeBuildVariantTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"fm27-variant-"+Guid.NewGuid().ToString("N"));
    private string game="",profile="",api="";
    private NativeBuildGuard.Deployment deployment=new();
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    private static string Hash(string path){using var stream=File.OpenRead(path);using var sha=SHA256.Create();return Convert.ToHexString(sha.ComputeHash(stream));}
    private void Setup(bool steam)
    {
        game=Path.Combine(root,steam?"steamapps/common/FM27":"XboxGames/FM27/Content");
        Directory.CreateDirectory(Path.Combine(game,"fm_Data/il2cpp_data/Metadata"));
        File.WriteAllText(Path.Combine(game,"fm.exe"),"synthetic fixture");
        File.WriteAllText(Path.Combine(game,"fm_Data/app.info"),"Sports Interactive\nFootball Manager 27\n");
        File.WriteAllText(Path.Combine(game,"fm_Data/globalgamemanagers"),"fixture\0"+"6000.3.99f1\0");
        if(steam)File.WriteAllText(Path.Combine(root,"steamapps/appmanifest_999.acf"),"\"AppState\" { \"appid\" \"999\" \"name\" \"Football Manager 27\" \"installdir\" \"FM27\" \"buildid\" \"987654321\" }");
        else File.WriteAllText(Path.Combine(game,"MicrosoftGame.config"),"<Game><Identity Name='SportsInteractive.FootballManager27' Version='0.9.9999.0'/></Game>");
        byte[] pe=new byte[257];pe[0]=(byte)'M';pe[1]=(byte)'Z';BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(60),128);BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(128),0x4550);BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(132),0x8664);pe[256]=(byte)(steam?2:1);
        File.WriteAllBytes(Path.Combine(game,"GameAssembly.dll"),pe);
        byte[] metadata=new byte[9];BinaryPrimitives.WriteUInt32LittleEndian(metadata,0xFAB11BAF);BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(4),39);metadata[8]=2;
        File.WriteAllBytes(Path.Combine(game,"fm_Data/il2cpp_data/Metadata/global-metadata.dat"),metadata);
        string core=Path.Combine(game,"BepInEx/core"),interop=Path.Combine(game,"BepInEx/interop");Directory.CreateDirectory(core);Directory.CreateDirectory(interop);
        string source=typeof(NativeBuildVariantTests).Assembly.Location;
        foreach(string name in new[]{"Il2CppInterop.Generator.dll","Cpp2IL.Core.dll"})File.Copy(source,Path.Combine(core,name));
        File.Copy(source,Path.Combine(interop,Path.GetFileName(source)));
        File.WriteAllText(Path.Combine(interop,"assembly-hash.txt"),InteropGenerationState.ComputeChecksum(game));
        api=Path.Combine(root,"required-api.json");
        var requirements=ManagedApiContract.CaptureTypes(source,new[]{typeof(NativeBuildVariantTests).FullName!});File.WriteAllText(api,JsonSerializer.Serialize(requirements));
        deployment=new NativeBuildGuard.Deployment{Schema=2,Version="reference-label",Assembly=new string('A',64),Metadata=new string('B',64),UnityFamily="6000.3.",MetadataVersion=39,ApiFile="required-api.json",LoaderFiles=new[]{new NativeBuildGuard.FileFingerprint{Relative="BepInEx/core/Cpp2IL.Core.dll",Sha256=Hash(Path.Combine(core,"Cpp2IL.Core.dll"))}}};
        profile=Path.Combine(root,"native-build.json");Save();
    }
    private void Save()=>File.WriteAllText(profile,JsonSerializer.Serialize(deployment));

    [Theory]
    [InlineData("version.dll", "winhttp.dll")]
    [InlineData("winhttp.dll", "version.dll")]
    public void ByteIdenticalSupportedProxyPassesButASecondProxyIsRefused(string proxy, string other)
    {
        Setup(true); File.WriteAllText(Path.Combine(game, proxy), "synthetic Doorstop proxy");
        deployment.LoaderFiles = deployment.LoaderFiles.Append(new NativeBuildGuard.FileFingerprint
            { Relative = proxy, Sha256 = Hash(Path.Combine(game, proxy)) }).ToArray(); Save();
        Assert.True(NativeBuildGuard.Validate(game, profile).Supported);
        File.Copy(Path.Combine(game, proxy), Path.Combine(game, other));
        var result = NativeBuildGuard.Validate(game, profile);
        Assert.False(result.Supported); Assert.Contains("Conflicting Doorstop proxies", result.Reason);
    }

    [Fact]
    public void NullLoaderEntryReturnsAProfileErrorInsteadOfCrashingStartup()
    {
        Setup(false);deployment.LoaderFiles=new NativeBuildGuard.FileFingerprint[]{null!};Save();
        var result=NativeBuildGuard.Validate(game,profile);
        Assert.False(result.Supported);Assert.Contains("profile",result.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewXboxAndSteamBuildLabelsPassWhenTheActualContractMatches(bool steam)
    {
        Setup(steam);var result=NativeBuildGuard.Validate(game,profile);
        Assert.True(result.Supported,result.Reason);Assert.Contains("compatible variant",result.Reason);Assert.Contains("not yet qualified",result.Reason);
        Assert.Equal(steam?"987654321":"0.9.9999.0",result.Version);
    }
    [Fact]
    public void ReferenceNativePairDoesNotRequireTheReferenceStoreBuildLabel()
    {
        Setup(true);var report=InstallationInspector.Inspect(game);deployment.Assembly=report.AssemblyHash;deployment.Metadata=report.MetadataHash;Save();
        Assert.True(NativeBuildGuard.Validate(game,profile).Supported);
    }
    [Fact]
    public void StaleInteropCannotUseACompatibleApiAsABypass()
    {
        Setup(true);File.WriteAllText(Path.Combine(game,"BepInEx/interop/assembly-hash.txt"),"stale");
        var result=NativeBuildGuard.Validate(game,profile);Assert.False(result.Supported);Assert.Contains("stale",result.Reason);
    }
    [Fact]
    public void MissingNativeMemberCannotUseAChangedBuildLabelAsABypass()
    {
        Setup(true);var contract=JsonSerializer.Deserialize<ManagedApiContract.TypeContract[]>(File.ReadAllText(api))!;
        contract[0].Members=contract[0].Members.Append(new string('F',64)).ToArray();File.WriteAllText(api,JsonSerializer.Serialize(contract));
        var result=NativeBuildGuard.Validate(game,profile);Assert.False(result.Supported);Assert.Contains("members changed/missing",result.Reason);
    }
    [Fact]
    public void MetadataOnlyChangesAreNotAssumedFreshByTheLoaderGameAssemblyCache()
    {
        Setup(false);deployment.Assembly=InstallationInspector.Inspect(game).AssemblyHash;Save();
        var result=NativeBuildGuard.Validate(game,profile);Assert.False(result.Supported);Assert.Contains("Metadata changed",result.Reason);
    }
    [Fact]
    public void LoaderAndUnityFamilyMismatchesStillRefuseInitialization()
    {
        Setup(true);deployment.LoaderFiles[0].Sha256=new string('F',64);Save();Assert.False(NativeBuildGuard.Validate(game,profile).Supported);
        File.WriteAllText(Path.Combine(game,"fm_Data/globalgamemanagers"),"6000.4.0f1");Assert.Contains("Unity family",NativeBuildGuard.Validate(game,profile).Reason);
    }
}
