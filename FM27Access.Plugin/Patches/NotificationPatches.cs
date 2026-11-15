using HarmonyLib;
using SI.Bindable;

namespace FM27Access.Plugin.Patches;

[HarmonyPatch(typeof(NotificationManager), "TriggerNewEvent")]
public static class NotificationPatch
{
    [HarmonyPostfix]
    public static void Postfix(string title, string description)
    {
        if (!AccessibilityMod.IsAlive) return;
        PatchTiming.Run("Notification", () =>
        {
            try
            {
                Speech.SpeechPipeline.SpeakInterrupt($"{title}. {description}");
            }
            catch (Exception ex)
            {
                // Speaking one notification failed. Degrade: log and continue rather than killing the
                // mod over a single missed announcement.
                Speech.Log.Error($"[NotificationPatch] notification announce failed, continuing: {ex}");
            }
        });
    }
}
