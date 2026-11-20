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

    private (string Path, Type Type) WidgetFixture(string directory, bool unused = true,
        bool required = true, bool broken = false, bool reflected = true, bool overload = true)
    {
        Directory.CreateDirectory(directory);
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("NativeWidgets"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("NativeWidgets");
        var type = module.DefineType("Native.Widget", TypeAttributes.Public);
        if (required)
        {
            var method = type.DefineMethod("Select", MethodAttributes.Public | MethodAttributes.Static,
                broken ? typeof(long) : typeof(int), [typeof(int)]);
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0);
            if (broken) il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Ret);
        }
        if (overload) type.DefineMethod("Select", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), [typeof(string)]).GetILGenerator().Emit(OpCodes.Ret);
        if (unused) type.DefineMethod("_Rebuild_b__123_0", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), Type.EmptyTypes).GetILGenerator().Emit(OpCodes.Ret);
        if (reflected)
        {
            var getter = type.DefineMethod("get_Content", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                typeof(string), Type.EmptyTypes);
            var il = getter.GetILGenerator(); il.Emit(OpCodes.Ldstr, "native"); il.Emit(OpCodes.Ret);
            type.DefineProperty("Content", PropertyAttributes.None, typeof(string), null).SetGetMethod(getter);
        }
        var created = type.CreateType()!;
        string path = Path.Combine(directory, "NativeWidgets.dll"); assembly.Save(path);
        return (path, created);
    }

    private string WidgetConsumer(Type widget, bool reflection = false, bool patch = false, bool direct = true)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("WidgetConsumer"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("WidgetConsumer");
        var type = module.DefineType("Consumer", TypeAttributes.Public);
        var method = type.DefineMethod("Use", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var il = method.GetILGenerator();
        if (direct) { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Call, widget.GetMethod("Select", [typeof(int)])!); il.Emit(OpCodes.Pop); }
        // Include nontrivial operand widths so string scanning must follow IL boundaries.
        il.Emit(OpCodes.Ldc_I8, 0x7200000072); il.Emit(OpCodes.Pop);
        if (reflection) { il.Emit(OpCodes.Ldstr, "Content"); il.Emit(OpCodes.Pop); }
        il.Emit(OpCodes.Ret);
        if (patch)
        {
            var attribute = module.DefineType("HarmonyLib.HarmonyPatch", TypeAttributes.Public, typeof(Attribute));
            var ctor = attribute.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(Type), typeof(string)]);
            var body = ctor.GetILGenerator(); body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Call, typeof(Attribute).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)!);
            body.Emit(OpCodes.Ret);
            var created = attribute.CreateType()!;
            method.SetCustomAttribute(new CustomAttributeBuilder(created.GetConstructor([typeof(Type), typeof(string)])!, [widget, "get_Content"]));
        }
        type.CreateType();
        string path = Path.Combine(root, "WidgetConsumer.dll"); assembly.Save(path); return path;
    }

    [Fact]
    public void RemovedUnusedInternalsAndUnusedOverloadsDoNotBlockAnUpdatedGame()
    {
        var original = WidgetFixture(Path.Combine(root, "original"));
        var updated = WidgetFixture(Path.Combine(root, "updated"), unused: false, reflected: false, overload: false);
        var required = ManagedApiContract.Capture([WidgetConsumer(original.Type)], Path.GetDirectoryName(original.Path)!);
        Assert.Single(Assert.Single(required).Members);
        Assert.Empty(ManagedApiContract.Validate(required, Path.GetDirectoryName(updated.Path)!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActuallyCalledMembersCannotBeRemovedOrChangeSignature(bool broken)
    {
        var original = WidgetFixture(Path.Combine(root, "original"));
        var updated = WidgetFixture(Path.Combine(root, "updated"), required: broken, broken: broken);
        var required = ManagedApiContract.Capture([WidgetConsumer(original.Type)], Path.GetDirectoryName(original.Path)!);
        Assert.Contains(ManagedApiContract.Validate(required, Path.GetDirectoryName(updated.Path)!), e => e.Contains("members changed/missing"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiteralReflectionAndHarmonyAttributeTargetsRemainRequired(bool patch)
    {
        var original = WidgetFixture(Path.Combine(root, "original"));
        var updated = WidgetFixture(Path.Combine(root, "updated"), reflected: false);
        var required = ManagedApiContract.Capture([WidgetConsumer(original.Type, reflection: !patch, patch: patch)], Path.GetDirectoryName(original.Path)!);
        Assert.Equal(2, Assert.Single(required).Members.Length);
        Assert.Contains(ManagedApiContract.Validate(required, Path.GetDirectoryName(updated.Path)!), e => e.Contains("members changed/missing"));
    }

    [Fact]
    public void HarmonyOnlyTargetTypesAreCapturedWithoutAConsumerTypeReference()
    {
        var original = WidgetFixture(Path.Combine(root, "original"));
        var updated = WidgetFixture(Path.Combine(root, "updated"), reflected: false);
        var required = ManagedApiContract.Capture([WidgetConsumer(original.Type, patch: true, direct: false)], Path.GetDirectoryName(original.Path)!);
        Assert.Single(Assert.Single(required).Members);
        Assert.Contains(ManagedApiContract.Validate(required, Path.GetDirectoryName(updated.Path)!), e => e.Contains("members changed/missing"));
    }

    [Fact]
    public void GenericParentMemberReferencesAndFieldsAreCaptured()
    {
        string directory = Path.Combine(root, "generic"); Directory.CreateDirectory(directory);
        var native = new PersistedAssemblyBuilder(new AssemblyName("NativeGeneric"), typeof(object).Assembly);
        var module = native.DefineDynamicModule("NativeGeneric");
        var generic = module.DefineType("Native.Box`1", TypeAttributes.Public);
        var parameter = generic.DefineGenericParameters("T")[0];
        var valueField = generic.DefineField("Value", parameter, FieldAttributes.Public | FieldAttributes.Static);
        var identity = generic.DefineMethod("Read", MethodAttributes.Public | MethodAttributes.Static, parameter, [parameter]);
        var body = identity.GetILGenerator(); body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Ret);
        var closed = generic.CreateType()!.MakeGenericType(typeof(int));
        native.Save(Path.Combine(directory, "NativeGeneric.dll"));
        var consumer = new PersistedAssemblyBuilder(new AssemblyName("GenericConsumer"), typeof(object).Assembly);
        var type = consumer.DefineDynamicModule("GenericConsumer").DefineType("Consumer", TypeAttributes.Public);
        var il = type.DefineMethod("Use", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes).GetILGenerator();
        il.Emit(OpCodes.Ldsfld, TypeBuilder.GetField(closed, valueField));
        il.Emit(OpCodes.Call, TypeBuilder.GetMethod(closed, identity)); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ret);
        type.CreateType(); string file = Path.Combine(root, "GenericConsumer.dll"); consumer.Save(file);
        var required = ManagedApiContract.Capture([file], directory);
        Assert.Equal(2, Assert.Single(required).Members.Length);
        Assert.Empty(ManagedApiContract.Validate(required, directory));
        var missing = ManagedApiContract.CaptureTypes(Path.Combine(directory, "NativeGeneric.dll"), ["Native.Box`1"])[0];
        missing.Members = [];
        Assert.Contains(ManagedApiContract.Compare(required[0], missing), e => e.Contains("members changed/missing"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypeOnlyReferencesPreserveEnumConstantsAndInterfaceMethods(bool isEnum)
    {
        string Fixture(string directory, bool changed)
        {
            Directory.CreateDirectory(directory);
            var assembly = new PersistedAssemblyBuilder(new AssemblyName("NativeTypes"), typeof(object).Assembly);
            var module = assembly.DefineDynamicModule("NativeTypes");
            if (isEnum)
            {
                var native = module.DefineEnum("Native.Mode", TypeAttributes.Public, typeof(int));
                native.DefineLiteral("Required", changed ? 2 : 1); native.CreateType();
            }
            else
            {
                var native = module.DefineType("Native.Mode", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
                native.DefineMethod(changed ? "Removed" : "Select", MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual,
                    typeof(void), Type.EmptyTypes); native.CreateType();
            }
            string file = Path.Combine(directory, "NativeTypes.dll"); assembly.Save(file); return file;
        }
        string original = Fixture(Path.Combine(root, "original"), false), updated = Fixture(Path.Combine(root, "updated"), true);
        // Construct the consumer against the actual emitted definition, without relying on
        // a method call (enum constants are inlined, and interfaces can be type-only uses).
        var context = new System.Runtime.Loader.AssemblyLoadContext("native-types-" + Guid.NewGuid(), isCollectible: true);
        try
        {
            using var input = new MemoryStream(File.ReadAllBytes(original));
            var target = context.LoadFromStream(input).GetType("Native.Mode")!;
            var consumer = new PersistedAssemblyBuilder(new AssemblyName("TypeConsumer"), typeof(object).Assembly);
            var type = consumer.DefineDynamicModule("TypeConsumer").DefineType("Consumer", TypeAttributes.Public);
            type.DefineField("Mode", target, FieldAttributes.Public); type.CreateType();
            string file = Path.Combine(root, "TypeConsumer.dll"); consumer.Save(file);
            var required = ManagedApiContract.Capture([file], Path.GetDirectoryName(original)!);
            Assert.Contains(ManagedApiContract.Validate(required, Path.GetDirectoryName(updated)!), e => e.Contains("members changed/missing"));
        }
        finally { context.Unload(); }
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
