#nullable enable
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
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

    private sealed class Signatures : ISignatureTypeProvider<string, object?>
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

    private static TypeContract Describe(Image image, TypeDefinitionHandle handle, ISet<string>? referencedMembers = null)
    {
        var reader = image.Reader;
        var type = reader.GetTypeDefinition(handle);
        var signatures = new Signatures(image.Name);
        var layout = type.GetLayout();
        string header = (int)type.Attributes + "|" + signatures.Entity(reader, type.BaseType) + "|" + layout.PackingSize + "|" + layout.Size + "|" + Generics(reader, signatures, type.GetGenericParameters());
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (var h in type.GetInterfaceImplementations()) members.Add(Hash("I|" + signatures.Entity(reader, reader.GetInterfaceImplementation(h).Interface)));
        foreach (var h in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(h);
            if ((method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private) continue;
            if (referencedMembers != null && !referencedMembers.Contains(reader.GetString(method.Name))) continue;
            members.Add(Hash("M|" + reader.GetString(method.Name) + "|" + (int)method.Attributes + "|" + Method(method.DecodeSignature(signatures, null)) + "|" + Generics(reader, signatures, method.GetGenericParameters())));
        }
        foreach (var h in type.GetFields())
        {
            var field = reader.GetFieldDefinition(h);
            if ((field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private) continue;
            if (referencedMembers != null && !referencedMembers.Contains(reader.GetString(field.Name))) continue;
            string value = "";
            if (!field.GetDefaultValue().IsNil) { var constant = reader.GetConstant(field.GetDefaultValue()); value = constant.TypeCode + ":" + Convert.ToBase64String(reader.GetBlobBytes(constant.Value)); }
            members.Add(Hash("F|" + reader.GetString(field.Name) + "|" + (int)field.Attributes + "|" + field.DecodeSignature(signatures, null) + "|" + field.GetOffset() + "|" + value));
        }
        return new TypeContract { Assembly = image.Name, Name = DefinitionName(reader, handle), Header = Hash(header), Members = members.OrderBy(s => s, StringComparer.Ordinal).ToArray() };
    }

    // Application is used only through direct static calls (runInBackground/isFocused),
    // never as a reflection or patch target. Its unrelated advertising API differs by
    // store. Capture its actual binary references instead of requiring unused APIs.
    // All other types retain the conservative full public/reflection contract.
    private static bool DirectMembersOnly(string assembly, string name) =>
        assembly == "UnityEngine.CoreModule" && name == "UnityEngine.Application";

    /// <summary>Include public contracts of referenced interop types and their interop bases,
    /// with actual member references for the explicitly audited direct-only Application API.</summary>
    public static TypeContract[] Capture(string[] modAssemblies, string interopDirectory)
    {
        var images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var path in Directory.EnumerateFiles(interopDirectory, "*.dll"))
            { var image = new Image(path); if (!images.TryAdd(image.Name, image)) { image.Dispose(); throw new InvalidDataException("Duplicate interop assembly identity."); } }
            var pending = new Queue<(string assembly, string name)>();
            var directMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (string path in modAssemblies)
            {
                using var mod = new Image(path);
                foreach (var handle in mod.Reader.TypeReferences)
                { var reference = Reference(mod.Reader, handle); if (images.ContainsKey(reference.assembly)) pending.Enqueue(reference); }
                foreach (var handle in mod.Reader.MemberReferences)
                {
                    var member = mod.Reader.GetMemberReference(handle);
                    if (member.Parent.Kind != HandleKind.TypeReference) continue;
                    var reference = Reference(mod.Reader, (TypeReferenceHandle)member.Parent);
                    if (!DirectMembersOnly(reference.assembly, reference.name)) continue;
                    string key = reference.assembly + "::" + reference.name;
                    if (!directMembers.TryGetValue(key, out var names)) directMembers.Add(key, names = new(StringComparer.Ordinal));
                    names.Add(mod.Reader.GetString(member.Name));
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
                contracts.Add(key, Describe(image, handle, DirectMembersOnly(reference.assembly, reference.name)
                    ? directMembers.GetValueOrDefault(key) ?? new HashSet<string>(StringComparer.Ordinal) : null));
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
