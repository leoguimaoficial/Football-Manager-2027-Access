#nullable enable
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace FM27Access.Speech;

/// <summary>Read-only, token/MVID-independent contracts; never loads game assemblies.</summary>
public static class ManagedApiContract
{
    public sealed class TypeContract
    {
        public string Assembly { get; set; } = "";
        public string Name { get; set; } = "";
        public string Header { get; set; } = "";
        public string[] Members { get; set; } = Array.Empty<string>();
    }

    private sealed class Image : IDisposable
    {
        private readonly FileStream stream;
        private readonly PEReader pe;
        internal readonly MetadataReader Reader;
        internal readonly string Name;
        internal byte[] Body(MethodDefinition method) => pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
        internal readonly Dictionary<string, TypeDefinitionHandle> Types = new(StringComparer.Ordinal);
        internal Image(string path)
        {
            stream = File.OpenRead(path);
            try
            {
                pe = new PEReader(stream);
                Reader = pe.GetMetadataReader();
                Name = Reader.GetString(Reader.GetAssemblyDefinition().Name);
                foreach (var handle in Reader.TypeDefinitions) Types.Add(DefinitionName(Reader, handle), handle);
            }
            catch { stream.Dispose(); throw; }
        }
        public void Dispose() { pe.Dispose(); stream.Dispose(); }
    }

    private static string DefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var parent = type.GetDeclaringType();
        return parent.IsNil ? Join(reader.GetString(type.Namespace), reader.GetString(type.Name))
            : DefinitionName(reader, parent) + "+" + reader.GetString(type.Name);
    }
    private static string Join(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
    private static (string assembly, string name) Reference(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var parent = Reference(reader, (TypeReferenceHandle)type.ResolutionScope);
            return (parent.assembly, parent.name + "+" + reader.GetString(type.Name));
        }
        string assembly = type.ResolutionScope.Kind == HandleKind.AssemblyReference
            ? reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name)
            : reader.GetString(reader.GetAssemblyDefinition().Name);
        return (assembly, Join(reader.GetString(type.Namespace), reader.GetString(type.Name)));
    }

    private sealed class Signatures : ISignatureTypeProvider<string, object?>, ICustomAttributeTypeProvider<string>
    {
        private readonly string assembly;
        private readonly Action<(string assembly, string name)>? observed;
        internal Signatures(string assembly, Action<(string assembly, string name)>? observed = null)
        { this.assembly = assembly; this.observed = observed; }
        public string GetArrayType(string element, ArrayShape shape) => element + "[rank=" + shape.Rank + ";sizes=" + string.Join(",", shape.Sizes) + ";lower=" + string.Join(",", shape.LowerBounds) + "]";
        public string GetByReferenceType(string type) => type + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fn:" + Method(signature);
        public string GetGenericInstantiation(string type, ImmutableArray<string> arguments) => type + "<" + string.Join(",", arguments) + ">";
        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? context, int index) => "!" + index;
        public string GetModifiedType(string modifier, string type, bool required) => (required ? "modreq(" : "modopt(") + modifier + ")" + type;
        public string GetPinnedType(string type) => "pinned:" + type;
        public string GetPointerType(string type) => type + "*";
        public string GetPrimitiveType(PrimitiveTypeCode code) => code.ToString();
        public string GetSystemType() => "System.Type";
        public bool IsSystemType(string type) => type == "System.Type" || type.EndsWith("::System.Type", StringComparison.Ordinal);
        public string GetTypeFromSerializedName(string name) => name;
        // HarmonyPatch's MethodType/ArgumentType enums both have Int32 storage.
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        public string GetSZArrayType(string type) => type + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        { string name = DefinitionName(reader, handle); observed?.Invoke((assembly, name)); return assembly + "::" + name; }
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        { var type = Reference(reader, handle); observed?.Invoke(type); return type.assembly + "::" + type.name; }
        public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        internal string Entity(MetadataReader reader, EntityHandle handle) => handle.IsNil ? "" : handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
            _ => throw new BadImageFormatException("Unexpected type signature: " + handle.Kind)
        };
    }
    private static string Method(MethodSignature<string> signature) => signature.Header.RawValue + "|" + signature.GenericParameterCount + "|" + signature.RequiredParameterCount + "|" + signature.ReturnType + "(" + string.Join(",", signature.ParameterTypes) + ")";
    private static string Hash(string value)
    { using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))); }
    private static string Generics(MetadataReader reader, Signatures signatures, GenericParameterHandleCollection handles) =>
        string.Join(";", handles.Select(h => { var p = reader.GetGenericParameter(h); return p.Index + ":" + (int)p.Attributes + ":" + string.Join(",", p.GetConstraints().Select(c => signatures.Entity(reader, reader.GetGenericParameterConstraint(c).Type)).OrderBy(s => s, StringComparer.Ordinal)); }));

    private sealed class Usage
    {
        internal readonly HashSet<string> Direct = new(StringComparer.Ordinal);
        internal readonly HashSet<string> Names = new(StringComparer.Ordinal);
        internal bool Contains(string key, string name) => Direct.Contains(key) || Names.Contains(name)
            || new[] { "get_", "set_", "add_", "remove_" }.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal) && Names.Contains(name.Substring(prefix.Length)));
    }

    private static TypeContract Describe(Image image, TypeDefinitionHandle handle, Usage? usage = null)
    {
        var reader = image.Reader;
        var type = reader.GetTypeDefinition(handle);
        var signatures = new Signatures(image.Name);
        var layout = type.GetLayout();
        string header = (int)type.Attributes + "|" + signatures.Entity(reader, type.BaseType) + "|" + layout.PackingSize + "|" + layout.Size + "|" + Generics(reader, signatures, type.GetGenericParameters());
        var members = new HashSet<string>(StringComparer.Ordinal);
        // Enum values are inlined into consumers; interfaces define implementation contracts.
        bool complete = usage == null || (type.Attributes & TypeAttributes.Interface) != 0
            || signatures.Entity(reader, type.BaseType).EndsWith("::System.Enum", StringComparison.Ordinal);
        foreach (var h in type.GetInterfaceImplementations()) members.Add(Hash("I|" + signatures.Entity(reader, reader.GetInterfaceImplementation(h).Interface)));
        foreach (var h in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(h);
            if ((method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private) continue;
            string name = reader.GetString(method.Name);
            if (!complete && !usage!.Contains("M|" + name + "|" + Method(method.DecodeSignature(signatures, null)), name)) continue;
            members.Add(Hash("M|" + reader.GetString(method.Name) + "|" + (int)method.Attributes + "|" + Method(method.DecodeSignature(signatures, null)) + "|" + Generics(reader, signatures, method.GetGenericParameters())));
        }
        foreach (var h in type.GetFields())
        {
            var field = reader.GetFieldDefinition(h);
            if ((field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private) continue;
            string name = reader.GetString(field.Name);
            if (!complete && !usage!.Contains("F|" + name + "|" + field.DecodeSignature(signatures, null), name)) continue;
            string value = "";
            if (!field.GetDefaultValue().IsNil) { var constant = reader.GetConstant(field.GetDefaultValue()); value = constant.TypeCode + ":" + Convert.ToBase64String(reader.GetBlobBytes(constant.Value)); }
            members.Add(Hash("F|" + reader.GetString(field.Name) + "|" + (int)field.Attributes + "|" + field.DecodeSignature(signatures, null) + "|" + field.GetOffset() + "|" + value));
        }
        return new TypeContract { Assembly = image.Name, Name = DefinitionName(reader, handle), Header = Hash(header), Members = members.OrderBy(s => s, StringComparer.Ordinal).ToArray() };
    }

    private static readonly Dictionary<ushort, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(op => unchecked((ushort)op.Value));

    private static void ObserveNames(Image image, ISet<string> names, Action<(string assembly, string name)> observed)
    {
        foreach (var handle in image.Reader.MethodDefinitions)
        {
            var method = image.Reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0) continue;
            byte[] bytes = image.Body(method);
            for (int offset = 0; offset < bytes.Length;)
            {
                ushort value = bytes[offset++];
                if (value == 0xfe) value = (ushort)(0xfe00 | bytes[offset++]);
                if (!Opcodes.TryGetValue(value, out var op)) throw new BadImageFormatException("Unknown consumer IL opcode.");
                if (op.OperandType == OperandType.InlineString)
                    names.Add(image.Reader.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(bytes, offset) & 0xffffff)));
                offset += op.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => checked(4 + 4 * BitConverter.ToInt32(bytes, offset)),
                    _ => 4
                };
                if (offset > bytes.Length) throw new BadImageFormatException("Truncated consumer IL.");
            }
        }
        // Patch names live in custom-attribute blobs, not ldstr instructions.
        foreach (var handle in image.Reader.CustomAttributes)
        {
            var attribute = image.Reader.GetCustomAttribute(handle);
            string attributeType = "";
            if (attribute.Constructor.Kind == HandleKind.MemberReference)
            {
                var constructor = image.Reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (constructor.Parent.Kind == HandleKind.TypeReference)
                    attributeType = Reference(image.Reader, (TypeReferenceHandle)constructor.Parent).name;
            }
            else if (attribute.Constructor.Kind == HandleKind.MethodDefinition)
                attributeType = DefinitionName(image.Reader, image.Reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType());
            if (attributeType != "HarmonyLib.HarmonyPatch") continue;
            var provider = new Signatures(image.Name);
            var decoded = attribute.DecodeValue(provider);
            void Observe(string type, object? value)
            {
                if (value is string name) names.Add(name);
                if (provider.IsSystemType(type) && value is string qualified)
                {
                    int separator = qualified.IndexOf(',');
                    if (separator > 0)
                        observed((qualified.Substring(separator + 1).Split(',')[0].Trim(), qualified.Substring(0, separator).Trim()));
                }
                if (value is ImmutableArray<CustomAttributeTypedArgument<string>> array)
                    foreach (var item in array) Observe(item.Type, item.Value);
            }
            foreach (var argument in decoded.FixedArguments) Observe(argument.Type, argument.Value);
            foreach (var argument in decoded.NamedArguments) Observe(argument.Type, argument.Value);
        }
    }

    /// <summary>Capture actual binary dependencies, literal reflection/patch targets and base
    /// contracts. Unused generated game internals are not prerequisites for accessibility.</summary>
    public static TypeContract[] Capture(string[] modAssemblies, string interopDirectory)
    {
        var images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFiles(interopDirectory, "*.dll"))
            { var image = new Image(path); if (!images.TryAdd(image.Name, image)) { image.Dispose(); throw new InvalidDataException("Duplicate interop assembly identity."); } }
            var pending = new Queue<(string assembly, string name)>();
            var usage = new Usage();
            foreach (string path in modAssemblies)
            {
                using var mod = new Image(path);
                ObserveNames(mod, usage.Names, reference => { if (images.ContainsKey(reference.assembly)) pending.Enqueue(reference); });
                foreach (var handle in mod.Reader.TypeReferences)
                { var reference = Reference(mod.Reader, handle); if (images.ContainsKey(reference.assembly)) pending.Enqueue(reference); }
                foreach (var handle in mod.Reader.MemberReferences)
                {
                    var member = mod.Reader.GetMemberReference(handle);
                    // Include generic TypeSpec parents and inherited declarations. Keys are
                    // deliberately shared across types: an identical use elsewhere can only
                    // conservatively add a requirement, never remove a real dependency.
                    var signatures = new Signatures(mod.Name);
                    bool native = false;
                    new Signatures(mod.Name, reference => native |= images.ContainsKey(reference.assembly))
                        .Entity(mod.Reader, member.Parent.Kind is HandleKind.TypeReference or HandleKind.TypeSpecification
                            ? member.Parent : default);
                    if (!native) continue;
                    string name = mod.Reader.GetString(member.Name);
                    usage.Direct.Add(member.GetKind() == MemberReferenceKind.Method
                        ? "M|" + name + "|" + Method(member.DecodeMethodSignature(signatures, null))
                        : "F|" + name + "|" + member.DecodeFieldSignature(signatures, null));
                }
            }
            var contracts = new Dictionary<string, TypeContract>(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                var reference = pending.Dequeue();
                string key = reference.assembly + "::" + reference.name;
                if (contracts.ContainsKey(key)) continue;
                var image = images[reference.assembly];
                if (!image.Types.TryGetValue(reference.name, out var handle)) throw new InvalidDataException("Referenced native type missing: " + key);
                contracts.Add(key, Describe(image, handle, usage));
                var parent = image.Reader.GetTypeDefinition(handle).BaseType;
                var bases = new Signatures(image.Name, type => { if(images.ContainsKey(type.assembly))pending.Enqueue(type); });
                bases.Entity(image.Reader, parent); // Includes generic base definitions/arguments.
            }
            if (contracts.Count == 0) throw new InvalidDataException("No interop requirements found in the actual mod.");
            return contracts.Values.OrderBy(t => t.Assembly, StringComparer.Ordinal).ThenBy(t => t.Name, StringComparer.Ordinal).ToArray();
        }
        finally { foreach (var image in images.Values) image.Dispose(); }
    }

    public static string[] Compare(TypeContract required, TypeContract? actual)
    {
        var errors = new List<string>();
        string name = required.Assembly + "::" + required.Name;
        if (actual == null || actual.Assembly != required.Assembly || actual.Name != required.Name) return new[] { "Required native type missing: " + name };
        if (actual.Header != required.Header) errors.Add("Native type layout/base/constraints changed: " + name);
        var available = new HashSet<string>(actual.Members ?? Array.Empty<string>(), StringComparer.Ordinal);
        int missing = (required.Members ?? Array.Empty<string>()).Count(m => !available.Contains(m));
        if (missing > 0) errors.Add("Required native members changed/missing: " + name + " (" + missing + ")");
        return errors.ToArray();
    }

    public static TypeContract[] CaptureTypes(string assembly, string[] names)
    {
        using var image = new Image(assembly);
        return names.Select(name => Describe(image, image.Types.TryGetValue(name, out var handle)
            ? handle : throw new InvalidDataException("Missing native type: " + name))).ToArray();
    }

    public static string[] Validate(TypeContract[] requirements, string interopDirectory)
    {
        if (requirements == null || requirements.Length == 0) return new[] { "Missing native API compatibility contract." };
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requirement in requirements)
        {
            if (requirement == null || string.IsNullOrWhiteSpace(requirement.Assembly)
                || string.IsNullOrWhiteSpace(requirement.Name) || !Fingerprint(requirement.Header)
                || requirement.Members == null || requirement.Members.Any(m => !Fingerprint(m))
                || !identities.Add(requirement.Assembly + "::" + requirement.Name))
                return new[] { "Malformed native API compatibility contract." };
        }
        var errors = new List<string>();
        foreach (var group in requirements.GroupBy(t => t.Assembly, StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(group.Key) != group.Key) throw new InvalidDataException("Invalid interop assembly name.");
            string path = Path.Combine(interopDirectory, group.Key + ".dll");
            if (!File.Exists(path)) { errors.Add("Required interop assembly missing: " + group.Key); continue; }
            using var image = new Image(path);
            if (image.Name != group.Key) { errors.Add("Interop identity differs: " + group.Key); continue; }
            foreach (var requirement in group)
                errors.AddRange(Compare(requirement, image.Types.TryGetValue(requirement.Name, out var h) ? Describe(image, h) : null));
        }
        return errors.ToArray();
    }

    private static bool Fingerprint(string? value) => value != null && value.Length == 64
        && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
