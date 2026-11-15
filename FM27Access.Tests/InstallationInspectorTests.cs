using System.Buffers.Binary;
using FM27Access.Platform;

namespace FM27Access.Tests;

public sealed class InstallationInspectorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fm27-platform-" + Guid.NewGuid());
    public InstallationInspectorTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void StreamingInspectionRejectsInvalidDosHeaderEvenWithAValidPeSignature(int index)
    {
        string game=Game("invalid-header");string file=Path.Combine(game,"GameAssembly.dll");
        byte[] bytes=File.ReadAllBytes(file);bytes[index]=0;File.WriteAllBytes(file,bytes);
        Assert.Contains("DOS header",Assert.Throws<InvalidDataException>(()=>InstallationInspector.Inspect(game)).Message);
    }
    private string Game(string relative)
    {
        string game=Path.GetFullPath(Path.Combine(root, relative));
        Directory.CreateDirectory(Path.Combine(game, "fm_Data", "il2cpp_data", "Metadata"));
        File.WriteAllText(Path.Combine(game,"fm.exe"), "synthetic executable fixture");
        File.WriteAllText(Path.Combine(game,"fm_Data","app.info"), "Sports Interactive\nFootball Manager 27\n");
        byte[] pe=new byte[256]; pe[0]=(byte)'M'; pe[1]=(byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(pe.AsSpan(60), 128);
        BinaryPrimitives.WriteUInt32LittleEndian(pe.AsSpan(128), 0x4550);
        BinaryPrimitives.WriteUInt16LittleEndian(pe.AsSpan(132), 0x8664);
        File.WriteAllBytes(Path.Combine(game,"GameAssembly.dll"), pe);
        byte[] metadata=new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(metadata, 0xFAB11BAF);
        BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(4),39);
        File.WriteAllBytes(Path.Combine(game,"fm_Data","il2cpp_data","Metadata","global-metadata.dat"),metadata);
        File.WriteAllText(Path.Combine(game,"fm_Data","globalgamemanagers"), "fixture\0"+"6000.3.18f1\0");
        return game;
    }
    [Fact]
    public void XboxDetectedFromIdentityRegardlessOfFolderName()
    {
        string game=Game("arbitrary-folder");
        File.WriteAllText(Path.Combine(game,"MicrosoftGame.config"), "<Game><Identity Name=\"SportsInteractive.FootballManager27\" Version=\"0.9.2409.0\"/></Game>");
        var report=InstallationInspector.Inspect(game);
        Assert.Equal("Xbox",report.Store); Assert.Equal("x64",report.Architecture);
        Assert.Equal("6000.3.18f1",report.UnityVersion); Assert.Equal(39,report.MetadataVersion);
        Assert.Equal("Absent",report.LoaderVersion); Assert.Equal(0,report.InteropAssemblyCount);
        Assert.Equal(64,report.MetadataHash.Length);
    }
    [Fact]
    public void SteamManifestConfirmsIdentityWithoutXboxPathHeuristic()
    {
        string game=Game("steamapps/common/XboxGames named folder");
        string apps=Path.Combine(root,"steamapps");
        File.WriteAllText(Path.Combine(apps,"appmanifest_999.acf"), "\"AppState\" { \"appid\" \"999\" \"name\" \"Football Manager 27\" \"installdir\" \"XboxGames named folder\" \"buildid\" \"123\" }");
        var report=InstallationInspector.Inspect(game);
        Assert.Equal("Steam",report.Store); Assert.Equal("123",report.GameVersion);
        Assert.Equal(game,Assert.Single(InstallationInspector.DiscoverSteam(root)));
    }
    [Fact]
    public void NameAloneNeverConfirmsStore()
    {
        var report=InstallationInspector.Inspect(Game("XboxGames/Football Manager 27/Content"));
        Assert.Equal("Unknown",report.Store);
        Assert.Contains(report.Findings, f=>f.Contains("No verified store"));
    }
    [Fact]
    public void WrongGameRejected()
    {
        string game=Game("game");
        File.WriteAllText(Path.Combine(game,"fm_Data","app.info"),"Sports Interactive\nFootball Manager 26");
        Assert.Throws<InvalidDataException>(()=>InstallationInspector.Inspect(game));
    }
    [Fact]
    public void CorruptMetadataRejected()
    {
        string game=Game("game");
        File.WriteAllBytes(Path.Combine(game,"fm_Data","il2cpp_data","Metadata","global-metadata.dat"),new byte[8]);
        Assert.Throws<InvalidDataException>(()=>InstallationInspector.Inspect(game));
    }
    [Fact]
    public void TraversingSteamManifestRejected()
    {
        string game=Game("steamapps/common/game");
        File.WriteAllText(Path.Combine(root,"steamapps","appmanifest_1.acf"),"\"name\" \"Football Manager 27\" \"installdir\" \"../../outside\"");
        Assert.Throws<InvalidDataException>(()=>InstallationInspector.DiscoverSteam(root).ToArray());
    }
    [Theory]
    [InlineData(0)] [InlineData(63)] [InlineData(64)]
    public void InvalidPeHeadersRejected(int length) => Assert.Throws<InvalidDataException>(()=>InstallationInspector.ReadPeArchitecture(new byte[length]));
    [Fact]
    public void AdditionalSteamLibraryDiscovered()
    {
        string game=Game("other/steamapps/common/game");
        Directory.CreateDirectory(Path.Combine(root,"steamapps"));
        string other=Path.Combine(root,"other").Replace("\\","\\\\");
        File.WriteAllText(Path.Combine(root,"steamapps","libraryfolders.vdf"),"\"libraryfolders\" { \"1\" { \"path\" \""+other+"\" } }");
        File.WriteAllText(Path.Combine(root,"other","steamapps","appmanifest_2.acf"),"\"name\" \"Football Manager 27\" \"installdir\" \"game\"");
        Assert.Equal(game,Assert.Single(InstallationInspector.DiscoverSteam(root)));
    }
}
