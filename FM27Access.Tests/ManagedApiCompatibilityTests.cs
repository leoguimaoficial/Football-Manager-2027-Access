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

    private (string Path, Type Type) ApplicationFixture(string directory, bool advertising, bool brokenFocus = false)
    {
        Directory.CreateDirectory(directory);
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("UnityEngine.CoreModule"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("UnityEngine.CoreModule");
        var type = module.DefineType("UnityEngine.Application", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var focus = type.DefineMethod("get_isFocused", MethodAttributes.Public | MethodAttributes.Static,
            brokenFocus ? typeof(long) : typeof(bool), Type.EmptyTypes);
        focus.GetILGenerator().Emit(OpCodes.Ldc_I4_1);
        if (brokenFocus) focus.GetILGenerator().Emit(OpCodes.Conv_I8);
        focus.GetILGenerator().Emit(OpCodes.Ret);
        type.DefineMethod("set_runInBackground", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [typeof(bool)])
            .GetILGenerator().Emit(OpCodes.Ret);
        if (advertising) type.DefineMethod("RequestAdvertisingIdentifierAsync", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), Type.EmptyTypes).GetILGenerator().Emit(OpCodes.Ret);
        var created = type.CreateType()!;
        string path = System.IO.Path.Combine(directory, "UnityEngine.CoreModule.dll"); assembly.Save(path);
        return (path, created);
    }

    private string ApplicationConsumer(Type application)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("ApplicationConsumer"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("ApplicationConsumer").DefineType("Consumer", TypeAttributes.Public);
        var il = type.DefineMethod("ReadAndConfigure", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes).GetILGenerator();
        il.Emit(OpCodes.Call, application.GetMethod("get_isFocused")!); il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Call, application.GetMethod("set_runInBackground")!); il.Emit(OpCodes.Ret);
        type.CreateType(); string path = Path.Combine(root, "ApplicationConsumer.dll"); assembly.Save(path); return path;
    }

    [Fact]
    public void UnusedSteamAdvertisingApiDoesNotBecomeAnXboxRequirement()
    {
        var steam = ApplicationFixture(Path.Combine(root, "steam"), true);
        var xbox = ApplicationFixture(Path.Combine(root, "xbox"), false);
        string consumer = ApplicationConsumer(steam.Type);
        var required = ManagedApiContract.Capture([consumer], Path.GetDirectoryName(steam.Path)!);
        Assert.Equal(2, Assert.Single(required).Members.Length);
        Assert.Empty(ManagedApiContract.Validate(required, Path.GetDirectoryName(xbox.Path)!));
        var oldOverbroad = ManagedApiContract.CaptureTypes(steam.Path, ["UnityEngine.Application"]);
        Assert.Contains(ManagedApiContract.Validate(oldOverbroad, Path.GetDirectoryName(xbox.Path)!), e => e.Contains("members changed/missing"));
    }

    [Fact]
    public void ApplicationMembersActuallyCalledByTheModStillRequireTheirExactSignatures()
    {
        var steam = ApplicationFixture(Path.Combine(root, "steam"), true);
        var broken = ApplicationFixture(Path.Combine(root, "broken"), false, true);
        var required = ManagedApiContract.Capture([ApplicationConsumer(steam.Type)], Path.GetDirectoryName(steam.Path)!);
        Assert.Contains(ManagedApiContract.Validate(required, Path.GetDirectoryName(broken.Path)!), e => e.Contains("members changed/missing"));
    }

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
