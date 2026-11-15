using System.Text.Json;
using FM27Access.Platform;
using FM27Access.Speech;

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
