using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using FM27Access.Speech;

namespace FM27Access.Tests;

public sealed class ManagedApiCompatibilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fm27-api-" + Guid.NewGuid().ToString("N"));
    public ManagedApiCompatibilityTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private string Fixture(string directory, bool extra = false, bool rename = false, bool changeSignature = false, int enumValue = 1)
    {
        Directory.CreateDirectory(directory);
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("NativeFixture") { Version = extra ? new Version(9,0) : new Version(1,0) }, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("NativeFixture");
        var type = module.DefineType("Native.Player", TypeAttributes.Public | TypeAttributes.Class);
        var field = type.DefineField("Available", typeof(int), FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal);
        field.SetConstant(enumValue);
        var method = type.DefineMethod(rename ? "Removed" : "Select", MethodAttributes.Public | MethodAttributes.Static, changeSignature ? typeof(long) : typeof(int), new[]{typeof(int)});
        var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); if(changeSignature)il.Emit(OpCodes.Conv_I8); il.Emit(OpCodes.Ret);
        if(extra){var added=type.DefineMethod("NewSteamFeature",MethodAttributes.Public|MethodAttributes.Static,typeof(void),Type.EmptyTypes);added.GetILGenerator().Emit(OpCodes.Ret);}
        type.CreateType();
        string path = Path.Combine(directory, "NativeFixture.dll"); assembly.Save(path); return path;
    }

    [Fact]
    public void DifferentAssemblyVersionsAndAdditiveApiRemainCompatible()
    {
        string a = Fixture(Path.Combine(root,"reference")), b = Fixture(Path.Combine(root,"variant"),extra:true);
        var requirement=ManagedApiContract.CaptureTypes(a,new[]{"Native.Player"});
        Assert.Empty(ManagedApiContract.Validate(requirement,Path.GetDirectoryName(b)!));
        Assert.NotEqual(File.ReadAllBytes(a),File.ReadAllBytes(b));
    }
    [Theory]
    [InlineData(true,false,1)]
    [InlineData(false,true,1)]
    [InlineData(false,false,2)]
    public void RemovedMethodsChangedSignaturesAndEnumValuesAreRefused(bool rename,bool signature,int enumValue)
    {
        string a=Fixture(Path.Combine(root,"reference")), b=Fixture(Path.Combine(root,"variant"),rename:rename,changeSignature:signature,enumValue:enumValue);
        var requirements=ManagedApiContract.CaptureTypes(a,new[]{"Native.Player"});
        Assert.NotEmpty(ManagedApiContract.Validate(requirements,Path.GetDirectoryName(b)!));
    }
    [Fact]
    public void PersistedContractRoundTripUsesNoTokenOrMvidIdentity()
    {
        string a=Fixture(Path.Combine(root,"reference")), b=Fixture(Path.Combine(root,"variant"));
        var contract=ManagedApiContract.CaptureTypes(a,new[]{"Native.Player"});
        var restored=JsonSerializer.Deserialize<ManagedApiContract.TypeContract[]>(JsonSerializer.Serialize(contract))!;
        Assert.Empty(ManagedApiContract.Validate(restored,Path.GetDirectoryName(b)!));
    }
    [Fact]
    public void MissingAssemblyAndMissingRequirementsNeverBecomeSuccess()
    {
        string a=Fixture(Path.Combine(root,"reference"));
        var requirements=ManagedApiContract.CaptureTypes(a,new[]{"Native.Player"});
        Assert.NotEmpty(ManagedApiContract.Validate(requirements,root));
        Assert.NotEmpty(ManagedApiContract.Validate(Array.Empty<ManagedApiContract.TypeContract>(),root));
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"Assembly\":\"NativeFixture\",\"Name\":\"Native.Player\",\"Header\":null,\"Members\":null}]")]
    public void MalformedJsonEntriesReturnACompatibilityError(string json)
    {
        var requirements=JsonSerializer.Deserialize<ManagedApiContract.TypeContract[]>(json)!;
        Assert.Contains("Malformed",Assert.Single(ManagedApiContract.Validate(requirements,root)));
    }

    [Fact]
    public void ChangedExplicitNativeLayoutIsRefused()
    {
        string FixtureLayout(string directory,int size)
        {
            Directory.CreateDirectory(directory);
            var assembly=new PersistedAssemblyBuilder(new AssemblyName("NativeLayout"),typeof(object).Assembly);
            var module=assembly.DefineDynamicModule("NativeLayout");
            var type=module.DefineType("Native.State",TypeAttributes.Public|TypeAttributes.Sealed|TypeAttributes.ExplicitLayout,typeof(ValueType),PackingSize.Size8,size);
            type.DefineField("Value",typeof(long),FieldAttributes.Public).SetOffset(0);type.CreateType();
            string file=Path.Combine(directory,"NativeLayout.dll");assembly.Save(file);return file;
        }
        string a=FixtureLayout(Path.Combine(root,"layout-reference"),16),b=FixtureLayout(Path.Combine(root,"layout-variant"),24);
        var requirements=ManagedApiContract.CaptureTypes(a,new[]{"Native.State"});
        Assert.Contains(ManagedApiContract.Validate(requirements,Path.GetDirectoryName(b)!),e=>e.Contains("layout/base/constraints changed"));
    }
}
