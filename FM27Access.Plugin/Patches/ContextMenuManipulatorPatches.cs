using HarmonyLib;

namespace FM27Access.Plugin.Patches;

/// <summary>
/// Records every <c>ContextMenuDescriptionManipulator</c> against the element it binds to, so menus
/// whose manipulator is not a field on the element are still reachable. See
/// <see cref="Adapters.ManipulatorRegistry"/> for why that shape exists (table column headers) and why
/// only non-field-backed manipulators are kept.
///
/// <c>VisualElement.AddManipulator</c> sets <c>Manipulator.target</c>, whose setter calls
/// <c>RegisterCallbacksOnTarget</c> immediately AFTER assigning it — so the postfix always sees both
/// halves of the pairing.
/// </summary>
[HarmonyPatch(typeof(SI.UI.ContextMenuDescriptionManipulator), "RegisterCallbacksOnTarget")]
public static class ContextMenuManipulatorRegisterPatch
{
    [HarmonyPostfix]
    public static void Postfix(SI.UI.ContextMenuDescriptionManipulator __instance)
    {
        try
        {
            Adapters.ManipulatorRegistry.Register(__instance.target, __instance);
        }
        catch (System.Exception ex)
        {
            // One unrecorded manipulator costs that element its menu mark — a missing affordance the
            // player can see is missing, not a wrong one. This patch fires for every manipulator in
            // the game, so it must never be the thing that takes the mod down.
            Speech.Degrade.Note($"manipulator registry: register failed ({ex.GetType().Name})");
        }
    }
}

/// <summary>
/// The unbind half. The <c>target</c> setter calls <c>UnregisterCallbacksFromTarget</c> BEFORE
/// reassigning, so the prefix still sees the element the manipulator is leaving.
/// </summary>
[HarmonyPatch(typeof(SI.UI.ContextMenuDescriptionManipulator), "UnregisterCallbacksFromTarget")]
public static class ContextMenuManipulatorUnregisterPatch
{
    [HarmonyPrefix]
    public static void Prefix(SI.UI.ContextMenuDescriptionManipulator __instance)
    {
        try
        {
            Adapters.ManipulatorRegistry.Unregister(__instance.target);
        }
        catch (System.Exception ex)
        {
            Speech.Degrade.Note($"manipulator registry: unregister failed ({ex.GetType().Name})");
        }
    }
}
