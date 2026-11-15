using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace FM27Access.Installer;

public sealed record Language(string Code, string Name)
{
    public override string ToString() => Name;
}
public sealed class LocalizationService
{
    private readonly Dictionary<string, Dictionary<string, string>> resources = new(StringComparer.OrdinalIgnoreCase);
    private readonly string preference;
    public string Code { get; private set; } = "en";
    public IReadOnlyList<Language> Languages { get; }
    public LocalizationService(string cache)
    {
        preference = Path.Combine(cache, "language.json");
        var assembly = Assembly.GetExecutingAssembly();
        foreach (string resource in assembly.GetManifestResourceNames().Where(r => r.Contains(".Resources.") && r.EndsWith(".json")))
        {
            using var input = assembly.GetManifestResourceStream(resource)!;
            string code = resource.Split(".Resources.")[1][..^5];
            resources.Add(code, JsonSerializer.Deserialize<Dictionary<string, string>>(input)!);
        }
        Languages = resources.Select(r => new Language(r.Key, r.Value["languageName"])).OrderBy(l => l.Name).ToArray();
        string detected = CultureInfo.CurrentUICulture.Name;
        if (File.Exists(preference))
            try { detected = Distribution.Read<string>(preference); } catch (Exception ex) when (ex is IOException or JsonException) { }
        Set(detected, false);
    }
    public void Set(string code, bool persist = true)
    {
        string neutral = code.Split('-')[0].ToLowerInvariant();
        Code = resources.ContainsKey(code) ? code : neutral switch
        {
            "pt" => "pt-PT", "zh" => "zh", "no" or "nn" => "nb",
            _ => resources.Keys.FirstOrDefault(c => c == neutral) ?? "en"
        };
        if (persist) Distribution.Save(preference, Code);
    }
    public string this[string key] => resources[Code].GetValueOrDefault(key) ?? resources["en"].GetValueOrDefault(key) ?? throw new KeyNotFoundException(key);
}
