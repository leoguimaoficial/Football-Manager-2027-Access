using System.Reflection;
using System.Text.Json;
using FM27Access.Navigation.Profiles;
using UnityEngine.UIElements;
namespace FM27Access.Plugin;

internal static class OriginalProfiles
{
    // Reuse the original managed registry, including external profile overrides.
    // It exposes only the ambient cursor profile; ownership reads need its same
    // definitions by panel without changing that ambient selection.
    private static readonly Dictionary<string, PanelProfile> ByPanel =
        (Dictionary<string, PanelProfile>)typeof(ProfileRegistry)
            .GetField("_profiles", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    private static IntPtr _lastElement;
    private static int _lastFrame = -1;
    private static PanelProfile _lastProfile;

    private static void Register(PanelProfile profile)
    {
        ProfileRegistry.Register(profile);
    }

    internal static PanelProfile ForElement(VisualElement element)
    {
        // A streamed row and an SSE value read can run between another panel's
        // profile activations. Resolve the real owner rather than borrowing the
        // speech cursor's ambient profile. Cache structural ownership only for
        // this element in this frame; pooled elements may move on the next one.
        int frame = UnityEngine.Time.frameCount;
        if (_lastElement == element.Pointer && _lastFrame == frame) return _lastProfile;
        PanelProfile profile = null;
        for (var parent = element; parent != null; parent = parent.hierarchy.parent)
        {
            // Guided tutorial steps float outside their owner Panel. Resolve
            // that native ownership explicitly, never through the ambient profile.
            if (Server.TutorialContent.OwnsFloatingRoot(parent))
            {
                ByPanel.TryGetValue("TutorialDialog", out profile);
                break;
            }
            if (parent.GetIl2CppType().Name != "Panel") continue;
            var panel = parent.TryCast<SI.Bindable.Panel>();
            ByPanel.TryGetValue(panel?.PanelID.name ?? parent.name ?? "", out profile);
            break;
        }
        _lastElement = element.Pointer;
        _lastFrame = frame;
        _lastProfile = profile;
        return profile;
    }

    internal static void Load(Assembly assembly)
    {
        var options=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,ReadCommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true};
        int count=0;
        foreach(string name in assembly.GetManifestResourceNames().Where(n=>n.StartsWith("FM27Access.Plugin.PanelProfiles.",StringComparison.Ordinal)))
        {
            using var stream=assembly.GetManifestResourceStream(name);
            var profile=JsonSerializer.Deserialize<PanelProfile>(stream,options);
            if(profile==null||string.IsNullOrEmpty(profile.PanelName))throw new InvalidDataException(name);
            Register(profile);count++;
            if(profile.PanelName=="PreferencesFMMenuCard")
            {
                // Native FM27 PanelID observed at runtime; the preview widget IDs are unchanged.
                var alias=JsonSerializer.Deserialize<PanelProfile>(assembly.GetManifestResourceStream(name),options);
                alias.PanelName="PreferencesFullscreenMenuCard";Register(alias);
            }
        }
        Speech.Log.Info($"[OriginalProfiles] Loaded {count} bundled profiles derived from FM26; native panel/widget matching still applies.");
    }
}
