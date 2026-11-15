using System.Text.Json;
using FM27Access.Speech;

namespace FM27Access.Navigation.Profiles;

public static class ProfileLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static void LoadAll(string profileDirectory)
    {
        if (!Directory.Exists(profileDirectory))
        {
            Log.Info($"[ProfileLoader] No profile directory at: {profileDirectory}");
            return;
        }

        var files = Directory.GetFiles(profileDirectory, "*.json");
        Log.Info($"[ProfileLoader] Found {files.Length} profile(s) in: {profileDirectory}");

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<PanelProfile>(json, JsonOptions);
                if (profile == null || string.IsNullOrEmpty(profile.PanelName))
                {
                    Log.Warn($"[ProfileLoader] Skipping invalid profile: {file}");
                    continue;
                }
                ProfileRegistry.Register(profile);
            }
            catch (Exception ex)
            {
                Log.Warn($"[ProfileLoader] Failed to load profile \"{file}\": {ex.Message}");
            }
        }
    }
}
