using System;
using System.Collections.Generic;
using System.Reflection;
using FM.UI;
using SI.Bindable;
using SI.Core;
using SI.UI;
using UnityEngine.UIElements;
using FM27Access.Speech;

namespace FM27Access.Plugin.Tactics;

/// <summary>
/// Executes a tactics pitch position change by reconstructing the game's own drag-drop payload
/// from the source player's bound data and invoking the destination's <c>FMDropTarget.Drop</c> —
/// the same call a real drag ends in — gated on the game's own <c>CanDrop</c> validity check.
/// No pointer events are synthesized; the move runs through the live game logic, so an invalid
/// target is rejected by the game, not by us. Nothing is cached: every call re-reads the live
/// VisualElement tree.
/// </summary>
internal static class PitchMoveEngine
{
    internal readonly struct Result
    {
        public readonly bool Ok;
        public readonly string Message;
        public Result(bool ok, string message) { Ok = ok; Message = message; }
    }

    private static readonly BindingFlags AllInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>The visible In/Out-of-Possession pitch, or null (Combined view, or not on screen).</summary>
    internal static VisualElement FindPitch()
    {
        VisualElement found = null;
        foreach (var panel in Server.InteractivePanels.Current())
        {
            foreach (var pitch in NativeTreeQuery.All<VisualElement>(panel.Panel, "interactable-pitch-expanded"))
            {
                if (!ElementBridge.IsVisibleInHierarchy(pitch)) continue;
                if (found != null && found.Pointer != pitch.Pointer) return null;
                found = pitch;
            }
        }
        return found;
    }

    internal static VisualElement FindIndicator(VisualElement pitch, string slot)
    {
        if (pitch.GetIl2CppType().Name == "FMPitchIndicator" && pitch.name == slot) return pitch;
        int count = pitch.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindIndicator(pitch.hierarchy[i], slot);
            if (found != null) return found;
        }
        return null;
    }

    private static VisualElement FindByTypeName(VisualElement root, string typeName)
    {
        if (root.GetIl2CppType().Name == typeName) return root;
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var found = FindByTypeName(root.hierarchy[i], typeName);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Move the player in <paramref name="srcSlot"/> onto <paramref name="destSlot"/>. If the
    /// destination is occupied the game swaps the two; if empty the player relocates. Returns an
    /// honest gap result when the source/target can't be found or the game rejects the drop.
    /// </summary>
    internal static Result TryMove(VisualElement pitch, string srcSlot, string destSlot)
    {
        if (pitch == null) return new Result(false, "The pitch isn't open. Select In or Out of Possession.");
        if (srcSlot == destSlot) return new Result(false, "That player is already there.");

        DragAndDropSubsystem subsystem = null;
        try
        {
            var occupants = PitchGrid.ReadOccupants(pitch);
            occupants.TryGetValue(srcSlot, out var srcPlayer);
            occupants.TryGetValue(destSlot, out var destPlayer);
            srcPlayer = Clean(srcPlayer);
            destPlayer = Clean(destPlayer);

            var srcInd = FindIndicator(pitch, srcSlot);
            var destInd = FindIndicator(pitch, destSlot);
            if (srcInd == null || destInd == null)
                return new Result(false, "Couldn't find those positions on the pitch.");

            var srcDragEl = FindByTypeName(srcInd, "FMDraggableObject");
            if (srcDragEl == null)
                return new Result(false, "There's no player to move from that position.");

            // Occupied destination → drop on its draggable (a swap); empty → its drop target.
            var targetEl = FindByTypeName(destInd, "FMDraggableObject")
                           ?? FindByTypeName(destInd, "FMDropTarget");
            if (targetEl == null)
                return new Result(false, "That position can't accept a player.");

            var src = srcDragEl.Cast<FMDraggableObject>();
            var target = targetEl.Cast<FMDropTarget>();

            subsystem = GameSubsystems.Get<DragAndDropSubsystem>();
            if (subsystem == null)
                return new Result(false, "The drag-and-drop system isn't available.");
            if (subsystem.IsDragging) subsystem.CancelDrag();

            // Reconstruct the drag the game would have started, then drive the subsystem through
            // its real begin/drop cycle so it fires the swap and cleans itself up. CanDrop against
            // the live drag state is the game's own validity gate ("only when valid by the game").
            var obj = BuildDragObject(pitch, src);
            var sr = srcDragEl.worldBound;
            var srcCenter = new UnityEngine.Vector2(sr.x + sr.width / 2f, sr.y + sr.height / 2f);
            subsystem.BeginDrag(srcDragEl, srcCenter, obj.Cast<IDragDropObject>());

            // Move the drag over the destination so the subsystem's current drop target (which it
            // resolves by Pick at the drag position, not from the Drop argument) becomes the target.
            // Without this it drops back onto the source position and nothing changes.
            var tr = targetEl.worldBound;
            var destCenter = new UnityEngine.Vector2(tr.x + tr.width / 2f, tr.y + tr.height / 2f);
            subsystem.SetDragPosition(destCenter);

            if (!target.CanDrop(subsystem.State))
            {
                subsystem.CancelDrag();
                return new Result(false, $"That player can't move to {destSlot}.");
            }

            subsystem.Drop(target.Cast<IDropTarget>());

            string who = srcPlayer ?? "player";
            string message = string.IsNullOrEmpty(destPlayer)
                ? $"Moved {who} to {destSlot}"
                : $"Swapped {who} and {destPlayer}";
            return new Result(true, message);
        }
        catch (Exception e)
        {
            try { subsystem?.CancelDrag(); } catch { /* best-effort cleanup */ }
            Log.Error($"[PitchMove] Move {srcSlot}->{destSlot} failed: {e}");
            return new Result(false, "The move couldn't be completed.");
        }
    }

    private const int MoveApplyStepMs = 50;
    private const int MoveApplyBudgetMs = 2000;

    // What the player hears when the move's outcome is unknown: the pitch never showed it applied,
    // or the watch for it broke. Both leave the formation in a state the mod cannot vouch for, and
    // a player who cannot look at the pitch needs telling that rather than a hopeful "Moved".
    private const string Unconfirmed =
        "The game did not confirm the position change. Check the formation before trying again.";

    /// <summary>
    /// Server entry point for /tactics/move on the REQUEST thread: runs the move, then holds the
    /// reply until the engine's async apply is visible in the pitch occupants (polled across frames,
    /// like the streamed-table reads), so the client can refresh the moment the reply lands and
    /// render the post-move formation instead of guessing with a timer.
    ///
    /// A move the pitch never confirms within the budget reports exactly that. The player cannot see
    /// the formation to check it, so "Moved X to AMC" has to mean the game agrees X is at AMC; the
    /// settle window may still bring a late apply into view, but announcing the move as done on the
    /// strength of that would be a guess dressed as a fact.
    /// </summary>
    internal static string MoveAndSettle(string source, string dest, string expectedScreen = null, string sourceId = null)
    {
        Server.PitchMoveSourceHandle owner = null;
        VisualElement ownedPitch = null;
        var (message, ok, before) = Server.MainThreadDispatcher.Invoke(() =>
        {
            var stale = Server.ServerHandles.ValidateScreen(expectedScreen);
            if(stale!=null)return(stale,false,(Dictionary<string,string>)null);
            owner = Server.ServerHandles.ResolvePitchMoveSource(sourceId);
            if (sourceId != null && (owner == null || owner.Slot != source))
                return ("That pitch source is no longer available. Refresh.", false, (Dictionary<string,string>)null);
            if (owner != null && !owner.TryValidate(out var error))
                return (error, false, (Dictionary<string,string>)null);
            var pitch = owner?.Pitch ?? FindPitch();
            ownedPitch = pitch;
            var occupants = pitch != null ? PitchGrid.ReadOccupants(pitch) : null;
            // Who is being moved has to be known BEFORE the move: it is the only thing the apply
            // watch can recognise arriving at the destination. Without it every later read is
            // unconfirmable, so refuse the move rather than fire it blind and guess at the result.
            if (occupants == null || !occupants.TryGetValue(source, out var sourcePlayer)
                || string.IsNullOrEmpty(sourcePlayer))
            {
                Degrade.Note($"[PitchMove] no readable player at {source}; move to {dest} refused");
                return ("The game did not expose the player at the original position. Try again.", false, occupants);
            }
            var result = TryMove(pitch, source, dest);
            return (result.Message, result.Ok, occupants);
        });
        if (!ok || before == null) return message;

        long deadline = Environment.TickCount64 + MoveApplyBudgetMs;
        try
        {
            while (Environment.TickCount64 < deadline)
            {
                // Check before sleeping: the Invoke below lands on the next frame's drain anyway,
                // and the engine often applies within that frame — sleeping first taxed every
                // move a flat step for nothing.
                bool? applied = Server.MainThreadDispatcher.Invoke<bool?>(() =>
                {
                    if(Server.ServerHandles.ValidateScreen(expectedScreen)!=null)return null;
                    var pitch = ownedPitch;
                    // Leaving the pitch is not evidence that the move applied.
                    // End the wait without converting disappearance into success.
                    if (pitch == null || !ElementBridge.IsVisibleInHierarchy(pitch)
                        || owner != null && !owner.OwnsPitch()) return null;
                    return PitchMoveConfirmation.IsApplied(before, PitchGrid.ReadOccupants(pitch), source, dest);
                });
                if (applied == true) return message;
                if (applied == null) return Unconfirmed;
                System.Threading.Thread.Sleep(MoveApplyStepMs);
            }
            // Request thread: Log.* would hard-crash off the main thread, use the background log.
            Server.ServerText.LogBackground(
                $"[PitchMove] {source}->{dest} not fully applied after {MoveApplyBudgetMs}ms; reporting it unconfirmed.");
        }
        catch (Exception e)
        {
            // The watcher broke, so the move's outcome is unknown — which is the same thing the
            // player needs to hear as a move that timed out, not a reason to claim it succeeded.
            Server.ServerText.LogBackground($"[PitchMove] apply-wait failed: {e.Message}; reporting it unconfirmed.");
        }
        return Unconfirmed;
    }

    private static BindableDragDropObject BuildDragObject(VisualElement pitch, FMDraggableObject src)
    {
        var payload = new PluginDragDropPayload
        {
            PluginID = src.PluginID,
            PluginTarget = src.PluginTarget,
            Data = src.DragData,
            BindingData = src.BindablePayload,
            ExtraData = ReadExtraData(src),
        };

        var tags = new Il2CppSystem.Collections.Generic.List<string>();
        var phase = ReadPossession(pitch);
        if (!phase.HasValue) throw new InvalidOperationException("The native pitch phase is unavailable.");
        tags.Add(phase.Value ? "TacticShape-IP" : "TacticShape-OOP");

        // BeginDrag immediately calls IDragDropObject.DragStarted, which loads the drag visuals from
        // this asset reference — a null reference throws there. Reuse the source draggable's own
        // template so the visuals load exactly as a real drag's would.
        return new BindableDragDropObject(
            ReadVisualsTemplate(src),
            default,
            tags,
            payload.Cast<IBindableDragDropPayload>());
    }

    private static UnityEngine.AddressableAssets.AssetReferenceT<UnityEngine.UIElements.VisualTreeAsset> ReadVisualsTemplate(FMDraggableObject src)
    {
        var prop = src.GetType().GetProperty("DragVisualsTemplate", AllInstance);
        return prop?.GetValue(src) as UnityEngine.AddressableAssets.AssetReferenceT<UnityEngine.UIElements.VisualTreeAsset>;
    }

    private static Il2CppSystem.Collections.Generic.Dictionary<int, TypedValue> ReadExtraData(FMDraggableObject src)
    {
        // The per-player extra data (key 0 = valid flag, key 1 = the player's lineup index) lives in
        // the protected m_extraData field on FMDropTarget — IL2CPP surfaces it as a property of that
        // name. Without it the dragged payload has no source identity and the game treats the drop as
        // an insert (shifting the whole lineup) rather than a swap.
        var prop = src.GetType().GetProperty("m_extraData", AllInstance);
        var value = prop?.GetValue(src);
        return value as Il2CppSystem.Collections.Generic.Dictionary<int, TypedValue>;
    }

    internal static bool? ReadPossession(VisualElement pitch)
    {
        var native = pitch?.TryCast<FMTacticsWithAnalysis>();
        var prop = native?.GetType().GetProperty("m_inPossession", AllInstance);
        return prop?.GetValue(native) is bool b ? b : null;
    }

    private static string Clean(string raw)
        => string.IsNullOrEmpty(raw) ? raw : TextFilters.CleanForDisplay(raw);
}
