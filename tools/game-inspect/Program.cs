using System.Text.Json;
using FM27Access.Platform;
using FM27Access.Speech;
using FM27Access.Build;

if (args.Length == 3 && args[0] == "validate-api")
{
    var contract = JsonSerializer.Deserialize<ManagedApiContract.TypeContract[]>(File.ReadAllText(args[1]));
    string[] errors = ManagedApiContract.Validate(contract!, args[2]);
    Console.WriteLine(JsonSerializer.Serialize(new { supported = errors.Length == 0, errors }));
    Environment.ExitCode = errors.Length == 0 ? 0 : 1;
    return;
}

if (args.Length == 2 && args[0] == "describe-build-references")
{
    var manifest = BuildReferenceBundle.Describe(args[1]);
    Console.WriteLine(JsonSerializer.Serialize(new { identity = BuildReferenceBundle.Identity(manifest), files = manifest.Files.Length }));
    return;
}
if (args.Length == 2 && args[0] == "build-reference-identity")
{
    Console.WriteLine(BuildReferenceBundle.Identity(BuildReferenceBundle.Validate(args[1])));
    return;
}
if (args.Length == 3 && args[0] == "check-build-reference-baseline")
{
    BuildReferenceBundle.CheckBaseline(BuildReferenceBundle.Describe(args[1]), args[2]);
    Console.WriteLine("FM27 compiler references match the pinned cloud baseline.");
    return;
}
if (args.Length == 3 && args[0] == "check-private-reference-baseline")
{
    BuildReferenceBundle.CheckBaseline(BuildReferenceBundle.Validate(args[1]), args[2]);
    Console.WriteLine("Private compiler input matches the pinned cloud baseline.");
    return;
}
if (args.Length == 3 && args[0] == "export-build-references")
{
    var manifest = BuildReferenceBundle.Export(args[1], args[2]);
    Console.WriteLine(JsonSerializer.Serialize(new { identity = BuildReferenceBundle.Identity(manifest), files = manifest.Files.Length }));
    return;
}
if (args.Length == 2 && args[0] == "validate-build-references")
{
    var manifest = BuildReferenceBundle.Validate(args[1]);
    Console.WriteLine(JsonSerializer.Serialize(manifest.Native with { Path = Path.GetFullPath(args[1]) }));
    return;
}
if (args.Length == 4 && args[0] == "api-contract-from-references")
{
    var manifest = BuildReferenceBundle.Validate(args[2]);
    string[] modules = new[] { "FM27Access.Host", "FM27Access.Plugin", "FM27Access.Navigation", "FM27Access.Speech" }
        .Select(name => Path.Combine(args[1], name + ".dll")).ToArray();
    var contract = ManagedApiContract.Capture(modules, Path.Combine(args[2], "BepInEx/interop"));
    File.WriteAllText(args[3], JsonSerializer.Serialize(contract));
    Console.WriteLine(JsonSerializer.Serialize(new { types = contract.Length, members = contract.Sum(t => t.Members.Length) }));
    return;
}

if (args.Length == 4 && args[0] == "api-contract")
{
    if (!InteropGenerationState.IsCurrent(args[2]))
        throw new InvalidDataException("Generated interop is stale; regenerate locally before capturing a deployment contract.");
    string[] modules = new[] { "FM27Access.Host", "FM27Access.Plugin", "FM27Access.Navigation", "FM27Access.Speech" }
        .Select(name => Path.Combine(args[1], name + ".dll")).ToArray();
    var contract = ManagedApiContract.Capture(modules, InteropGenerationState.AssemblyDirectory(args[2]));
    File.WriteAllText(args[3], JsonSerializer.Serialize(contract));
    Console.WriteLine(JsonSerializer.Serialize(new { types = contract.Length, members = contract.Sum(t => t.Members.Length), file = args[3] }));
    return;
}

if (args.Length == 2 && args[0] == "inspect")
{
    Console.WriteLine(JsonSerializer.Serialize(InstallationInspector.Inspect(args[1]), new JsonSerializerOptions { WriteIndented = true }));
    return;
}
if (args.Length == 2 && args[0] == "discover-steam")
{
    Console.WriteLine(JsonSerializer.Serialize(InstallationInspector.DiscoverSteam(args[1]).ToArray()));
    return;
}
Console.Error.WriteLine("Usage: game-inspect inspect <game-path> | discover-steam <steam-root> | api-contract <mod-directory> <game-path> <output-json>");
Environment.ExitCode = 2;
