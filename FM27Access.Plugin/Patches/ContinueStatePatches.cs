using FM.UI;
using HarmonyLib;

namespace FM27Access.Plugin.Patches;

[HarmonyPatch(typeof(ContinueManagerModule), "SetContinueState")]
public static class ContinueStatePatch
{
    private static ContinueManagerModule.ContinueFlowState s_previousState;

    /// <summary>
    /// True while the game is simulating (continue flow in the Processing state). Read by
    /// InteractivePanels to retire the ProcessingCard from the render set the moment the sim
    /// ends, instead of waiting seconds for its fade-out to shrink it to 0x0. Resets to false
    /// on /reload; the worst case of a reload mid-sim is the page returning to the base screen
    /// early, which the next continue corrects.
    /// </summary>
    internal static bool IsProcessing { get; private set; }

    [HarmonyPostfix]
    public static void Postfix(ContinueManagerModule.ContinueFlowState newState)
    {
        if (!AccessibilityMod.IsAlive) return;
        PatchTiming.Run("ContinueState", () =>
        {
            try
            {
                var prev = s_previousState;
                s_previousState = newState;
                IsProcessing = newState == ContinueManagerModule.ContinueFlowState.Processing;

                if (newState == ContinueManagerModule.ContinueFlowState.Processing &&
                    prev != ContinueManagerModule.ContinueFlowState.Processing)
                {
                    Speech.SpeechPipeline.SpeakInterrupt("Simulating");
                }
                else if (prev == ContinueManagerModule.ContinueFlowState.Processing &&
                         newState != ContinueManagerModule.ContinueFlowState.Processing)
                {
                    // The dismissal's restore announcement ("OverviewTitleBar. Back, button") is
                    // pure arrival chatter — the arrival report names the landing. Muted; the
                    // panel-stack bookkeeping inside still runs.
                    NavigationSpeechMute.Run(Navigation.NavigationManager.DismissLoadingScreen);
                }

                // The arrival report owns everything spoken after a sim ends ("Advanced 16 hours
                // to ...", with "Simulation complete" as its fallback) — it sees every transition
                // so it can capture the clock at sim start and speak once the flow settles.
                Server.ArrivalReport.OnContinueState(prev, newState);
            }
            catch (Exception ex)
            {
                // A continue/simulation state announcement failed. Degrade: log and continue.
                Speech.Log.Error($"[ContinueStatePatch] continue-state handling failed, continuing: {ex}");
            }
        });
    }
}
