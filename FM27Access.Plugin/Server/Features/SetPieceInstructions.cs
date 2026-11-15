using System;
using System.Collections.Generic;
using System.Text;
using FM.UI;
using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using FM27Access.Speech;
using SI.Bindable;
using SI.Core;
using SI.UI;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The set-piece screen's instruction chips (A1, C2, …) and their categories (Aerial Threats,
/// Box Threats, …). In the game, a chip's own dropdown only changes the instruction WITHIN its
/// category; moving a chip between categories — including populating an empty one — is
/// drag-and-drop only ("Drag an instruction into this category to populate"). This feature adds
/// the non-drag path: each chip renders its instruction dropdown (valued with the instruction
/// name the game displays, not the dropdown's bare index) followed by a "Move" select listing
/// the other categories. Committing one reconstructs the game's own drag payload and drives
/// <c>DragAndDropSubsystem</c> through its real begin/drop cycle, gated on the game's
/// <c>CanDrop</c> — an invalid move is refused by the game, not by us. Everything is read live
/// at render and action time.
/// </summary>
internal static class SetPieceInstructions
{
    internal const string PanelName = "OnThePitchSetPiecesTool";
    private const string CategoryFrame = "Frame 1000002452";
    private const string ChipSlotText = "tk-txt";
    private const string ChipInstructionText = "buttons-button-base-default-text";

    /// <summary>The chip (nearest FMDraggableObject ancestor holding a slot chip) this dropdown
    /// belongs to, or null when the dropdown is not a set-piece instruction chip's.</summary>
    internal static VisualElement Chip(VisualElement el)
    {
        if (!ElementBridge.IsInPanel(el, PanelName)) return null;
        var chip = ElementBridge.FindAncestorByTypeName(el, "FMDraggableObject");
        if (chip == null || ScreenWalker.FindDescendantNamed(chip, ChipSlotText) == null) return null;
        return chip;
    }

    internal static string SlotOf(VisualElement chip)
        => ReadNamedText(chip, ChipSlotText);

    internal static string InstructionOf(VisualElement chip)
    {
        var original = ReadNamedText(chip, ChipInstructionText);
        if (original != null) return original;
        // FM27 puts the instruction in the primary dropdown's SelectionField
        // instead of the old sibling button. Keep the same chip/table contract.
        var container = ScreenWalker.FindDescendantNamed(chip, "dropdown-container");
        var field = container == null ? null : ScreenWalker.FindDescendantNamed(container, "SelectionField");
        return field == null ? null : ReadNamedText(field, "label");
    }

    private static string ReadNamedText(VisualElement chip, string name)
    {
        var el = ScreenWalker.FindDescendantNamed(chip, name);
        var text = el?.TryCast<TextElement>();
        if (text == null || string.IsNullOrEmpty(text.text)) return null;
        var clean = TextFilters.CleanForDisplay(text.text);
        return string.IsNullOrEmpty(clean) ? null : clean;
    }

    /// <summary>One category block: its frame, name, header button and drop target.</summary>
    internal sealed class Category
    {
        public VisualElement Frame;
        public VisualElement HeaderButton;
        public VisualElement DropTarget;
        public string Name;
    }

    // The categories on screen, memoised per frame: every chip on a render pass shares one
    // panel scan. Element references only — every text is re-read live where it is used.
    private static List<Category> _categories;
    private static int _categoriesFrame = -1;

    internal static List<Category> Categories(VisualElement any)
    {
        int frame = UnityEngine.Time.frameCount;
        if (_categories != null && _categoriesFrame == frame) return _categories;
        var list = new List<Category>();
        VisualElement panel = null;
        for (var p = any; p != null; p = p.hierarchy.parent)
            if (p.name == PanelName) { panel = p; break; }
        if (panel != null) CollectCategories(panel, list);
        _categories = list;
        _categoriesFrame = frame;
        return list;
    }

    private static void CollectCategories(VisualElement el, List<Category> list)
    {
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = el.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            if (child.name == CategoryFrame || IsBoundCategory(child))
            {
                var cat = ReadCategory(child);
                if (cat != null) list.Add(cat);
                continue;
            }
            CollectCategories(child, list);
        }
    }

    private static bool IsBoundCategory(VisualElement element)
    {
        if (element.name != "Content") return false;
        for (int i = 0; i < element.hierarchy.childCount; i++)
            if (element.hierarchy[i].name == "HasCategoryType") return true;
        return false;
    }

    // A category frame holds a header (SIButton whose first text is the category name) and an
    // FMDraggableObject wrapping the chip list — the drop target a real drag lands on. A frame
    // missing either is not a category; skip it rather than guess.
    private static Category ReadCategory(VisualElement frame)
    {
        VisualElement header = FindDescendantOfType(frame, "SIButton", stopAtType: "FMDraggableObject");
        VisualElement drop = FindDescendantOfType(frame, "FMDraggableObject", stopAtType: null);
        if (header == null || drop == null) return null;
        string name = ScreenWalker.ReadFirstText(header);
        if (string.IsNullOrEmpty(name)) return null;
        return new Category
        {
            Frame = frame,
            HeaderButton = header,
            DropTarget = drop,
            Name = TextFilters.CleanForDisplay(name),
        };
    }

    private static VisualElement FindDescendantOfType(VisualElement root, string typeName, string stopAtType)
    {
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            string t = child.GetIl2CppType().Name;
            if (t == typeName) return child;
            if (stopAtType != null && t == stopAtType) continue;
            var found = FindDescendantOfType(child, typeName, stopAtType);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>The category a chip currently sits in (its next FMDraggableObject ancestor).</summary>
    internal static Category CategoryOf(VisualElement chip, List<Category> categories)
    {
        var catDrop = chip.hierarchy.parent == null
            ? null
            : ElementBridge.FindAncestorByTypeName(chip.hierarchy.parent, "FMDraggableObject");
        if (catDrop == null) return null;
        foreach (var cat in categories)
            if (cat.DropTarget.Pointer == catDrop.Pointer) return cat;
        return null;
    }

    // ---- The move itself: the game's own drag-drop cycle, driven programmatically. ----
    // Mirrors Tactics.PitchMoveEngine: reconstruct the drag payload from the source chip's
    // bound data, BeginDrag, position over the destination, gate on CanDrop, Drop.

    internal readonly struct MoveResult
    {
        public readonly bool Ok;
        public readonly string Message;
        public MoveResult(bool ok, string message) { Ok = ok; Message = message; }
    }

    private static readonly System.Reflection.BindingFlags AllInstance =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
        | System.Reflection.BindingFlags.Instance;

    internal static MoveResult TryMove(VisualElement chipEl, Category dest, string slot)
    {
        DragAndDropSubsystem subsystem = null;
        try
        {
            var src = chipEl.Cast<FMDraggableObject>();
            var target = dest.DropTarget.Cast<FMDropTarget>();

            subsystem = GameSubsystems.Get<DragAndDropSubsystem>();
            if (subsystem == null)
                return new MoveResult(false, "The drag-and-drop system isn't available.");
            if (subsystem.IsDragging) subsystem.CancelDrag();

            var obj = BuildDragObject(src);
            var sr = chipEl.worldBound;
            subsystem.BeginDrag(chipEl, new UnityEngine.Vector2(sr.x + sr.width / 2f, sr.y + sr.height / 2f),
                obj.Cast<IDragDropObject>());

            var tr = dest.DropTarget.worldBound;
            subsystem.SetDragPosition(new UnityEngine.Vector2(tr.x + tr.width / 2f, tr.y + tr.height / 2f));

            if (!target.CanDrop(subsystem.State))
            {
                subsystem.CancelDrag();
                return new MoveResult(false, $"The game refused moving {slot} to {dest.Name}.");
            }

            subsystem.Drop(target.Cast<IDropTarget>());
            return new MoveResult(true, null);
        }
        catch (Exception e)
        {
            try { subsystem?.CancelDrag(); } catch { /* best-effort cleanup */ }
            Log.Error($"[SetPieceMove] {slot} -> {dest.Name} failed: {e}");
            return new MoveResult(false, "The move couldn't be completed.");
        }
    }

    private static BindableDragDropObject BuildDragObject(FMDraggableObject src)
    {
        var payload = new PluginDragDropPayload
        {
            PluginID = src.PluginID,
            PluginTarget = src.PluginTarget,
            Data = src.DragData,
            BindingData = src.BindablePayload,
            ExtraData = ReadExtraData(src),
        };

        // A real drag carries the draggable's own tags; reuse them so the destination's
        // acceptance check sees exactly what it would in a pointer drag.
        var tags = src.Tags ?? new Il2CppSystem.Collections.Generic.List<string>();

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
        var prop = src.GetType().GetProperty("m_extraData", AllInstance);
        return prop?.GetValue(src) as Il2CppSystem.Collections.Generic.Dictionary<int, TypedValue>;
    }

    // ---- Walk-time consolidation: one table per category. ----

    /// <summary>One chip's live pieces, held between walk and render of a single body compose:
    /// the chip element, its slot text, and the classified dropdown stops found inside it.</summary>
    internal sealed class ChipRow
    {
        public VisualElement Chip;
        public string Slot;
        public IAccessibleElement Primary;    // the instruction dropdown (inside dropdown-container)
        public IAccessibleElement Secondary;  // the fallback dropdown, when the chip carries one
    }

    /// <summary>
    /// Called by ScreenWalker on a chip (main thread). The first chip walked in a category reads
    /// every chip under the category's drop target into a SetPieceChipTableItem — the table the
    /// player navigates as a grid instead of a loose run of stops — and returns true; the rest of
    /// the category's chips resolve to the already-emitted item and are consumed. False means the
    /// shape was not recognised and the caller falls back to the generic walk, which keeps
    /// today's flat rendering, so nothing can vanish.
    /// </summary>
    internal static bool TryEmitChipTable(VisualElement chip, List<ScreenWalker.RenderItem> items,
        Action<VisualElement, List<ScreenWalker.RenderItem>> walk)
    {
        // A chip sits inside its category's drop target; the drop target itself (also an
        // FMDraggableObject whose subtree holds slot texts) has no such ancestor and falls
        // through to the generic recursion that reaches the chips.
        var dropTarget = chip.hierarchy.parent == null
            ? null
            : ElementBridge.FindAncestorByTypeName(chip.hierarchy.parent, "FMDraggableObject");
        if (dropTarget == null) return false;

        foreach (var it in items)
            if (it is ScreenWalker.SetPieceChipTableItem prior && prior.DropTarget.Pointer == dropTarget.Pointer)
                return true;   // this category's table already emitted; the chip is one of its rows

        var categories = Categories(chip);
        var own = CategoryOf(chip, categories);
        if (own == null) return false;

        var rows = new List<ChipRow>();
        foreach (var sibling in CollectChips(dropTarget))
        {
            var row = ReadChipRow(sibling, walk);
            if (row == null) return false;   // one unrecognised chip declines the whole table
            rows.Add(row);
        }
        if (rows.Count == 0) return false;

        bool hasFallback = false;
        foreach (var row in rows)
            if (row.Secondary != null) hasFallback = true;

        items.Add(new ScreenWalker.SetPieceChipTableItem
        {
            Category = own.Name,
            DropTarget = dropTarget,
            Rows = rows,
            HasFallback = hasFallback,
        });
        return true;
    }

    // The category's chips in document order: FMDraggableObject descendants holding a slot text.
    // No descent into a chip — chips do not nest.
    private static List<VisualElement> CollectChips(VisualElement dropTarget)
    {
        var chips = new List<VisualElement>();
        CollectChipsRec(dropTarget, chips);
        return chips;
    }

    private static void CollectChipsRec(VisualElement el, List<VisualElement> chips)
    {
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = el.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            if (child.GetIl2CppType().Name == "FMDraggableObject"
                && ScreenWalker.FindDescendantNamed(child, ChipSlotText) != null)
            {
                chips.Add(child);
                continue;
            }
            CollectChipsRec(child, chips);
        }
    }

    // One chip into a row, via the same classification walk the flat path uses. The expected
    // shape is strict — the slot's own text stop, exactly one instruction dropdown, at most one
    // fallback dropdown, nothing else — and anything outside it returns null so the category
    // declines to the flat rendering rather than a table silently swallowing a control.
    private static ChipRow ReadChipRow(VisualElement chip,
        Action<VisualElement, List<ScreenWalker.RenderItem>> walk)
    {
        string slot = SlotOf(chip);
        if (slot == null) return null;

        var stops = new List<ScreenWalker.RenderItem>();
        int count = chip.hierarchy.childCount;
        for (int i = 0; i < count; i++) walk(chip.hierarchy[i], stops);

        var row = new ChipRow { Chip = chip, Slot = slot };
        int textStops = 0;
        foreach (var it in stops)
        {
            if (it is not ScreenWalker.StopItem stop) return null;
            if (stop.Element is DropdownAdapter dd)
            {
                if (HasAncestorNamed(dd.BackingElement, "dropdown-container"))
                {
                    if (row.Primary != null) return null;
                    row.Primary = stop.Element;
                }
                else
                {
                    if (row.Secondary != null) return null;
                    row.Secondary = stop.Element;
                }
                continue;
            }
            // The slot label is the chip's one legitimate text stop (the table's row header
            // replaces it); a second text — or any other control — is a shape we don't know.
            if (ServerText.SafeRole(stop.Element) == "text" && ++textStops == 1) continue;
            return null;
        }
        if (row.Primary == null) return null;
        return row;
    }

    internal static bool HasAncestorNamed(VisualElement el, string name)
    {
        for (var p = el.hierarchy.parent; p != null; p = p.hierarchy.parent)
            if (p.name == name) return true;
        return false;
    }

    // ---- Rendering (called from HtmlRenderer.RenderItem) ----

    public static void RenderChipTable(StringBuilder sb, ScreenWalker.SetPieceChipTableItem item)
    {
        sb.Append("<table aria-label=\"").Append(ServerText.EscapeText(item.Category ?? ""))
          .Append("\"><thead><tr><th scope=\"col\">Slot</th><th scope=\"col\">Instruction</th>");
        if (item.HasFallback) sb.Append("<th scope=\"col\">Fallback</th>");
        sb.Append("<th scope=\"col\">Move</th></tr></thead><tbody>");
        foreach (var row in item.Rows)
        {
            sb.Append("<tr><th scope=\"row\">").Append(ServerText.EscapeText(row.Slot)).Append("</th><td>");
            AppendInstructionSelect(sb, row.Primary, InstructionLabel(row.Chip, row.Primary, secondary: false));
            sb.Append("</td>");
            if (item.HasFallback)
            {
                sb.Append("<td>");
                if (row.Secondary != null)
                    AppendInstructionSelect(sb, row.Secondary, InstructionLabel(row.Chip, row.Secondary, secondary: true));
                sb.Append("</td>");
            }
            sb.Append("<td>");
            // A disabled instruction offers no move either — the game greys the whole chip.
            if (!ServerText.IsDisabled(row.Primary))
                AppendMoveSelect(sb, row.Chip, row.Slot);
            sb.Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    /// <summary>The name a chip dropdown reads as: the sibling instruction text for the primary,
    /// the dropdown's own displayed value for the self-labelling fallback. Degrades to an honest
    /// marker when unreadable.</summary>
    internal static string InstructionLabel(VisualElement chip, IAccessibleElement el, bool secondary)
    {
        string instruction = secondary
            ? ReadOwnText(((DropdownAdapter)el).BackingElement)
            : InstructionOf(chip);
        if (instruction == null)
        {
            Degrade.Note("set-piece chip: instruction text unreadable");
            instruction = Degrade.Value("instruction unreadable");
        }
        return instruction;
    }

    /// <summary>The instruction select, valued with the instruction name the game displays (the
    /// generic path's bare "2 of 7" index reads like a priority). Not value-tracked: the tracked
    /// state would overwrite the name with the index; the settle-window re-render keeps it
    /// current. Shared by the flat renderer and the table so the two cannot drift.</summary>
    internal static void AppendInstructionSelect(StringBuilder sb, IAccessibleElement el, string instruction)
    {
        if (ServerText.IsDisabled(el))
        {
            string did = LiveValues.RegisterReadOnly(el);
            sb.Append("<select id=\"").Append(did).Append("\" disabled aria-label=\"")
              .Append(ServerText.EscapeText(instruction)).Append("\"><option selected>")
              .Append(ServerText.EscapeText(instruction)).Append("</option></select>");
            return;
        }
        string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
        sb.Append("<select id=\"").Append(id).Append("\" data-dd=\"1\" aria-label=\"")
          .Append(ServerText.EscapeText(instruction)).Append("\"><option selected>")
          .Append(ServerText.EscapeText(instruction)).Append("</option></select>");
    }

    /// <summary>The move select: current category as the resting value, every other category as a
    /// committable option. Each option's value is a registered move handle; its data-h is the
    /// destination category's name, matched against the header buttons after the move so focus
    /// lands on the category (and its new count) the instruction went to. data-spmove puts it
    /// under FM.spmove's keyboard contract — arrows browse locally, Enter is the only commit,
    /// leaving reverts — like every other select on the surface (an inline onchange committed on
    /// the first arrow press, before the player could hear the options). Emits nothing when the
    /// chip's category cannot be resolved.</summary>
    internal static void AppendMoveSelect(StringBuilder sb, VisualElement chip, string slot)
    {
        var categories = Categories(chip);
        var own = CategoryOf(chip, categories);
        if (own == null || categories.Count < 2 || slot == null) return;

        string chipPath = ServerHandles.IdentityPath(chip);
        sb.Append("<select data-spmove=\"1\" aria-label=\"").Append(ServerText.Escape("Move " + slot))
          .Append("\"><option selected value=\"\">")
          .Append(ServerText.EscapeText(own.Name)).Append("</option>");
        foreach (var cat in categories)
        {
            if (cat.Frame.Pointer == own.Frame.Pointer) continue;
            string moveId = ServerHandles.RegisterKey(
                new SetPieceMoveHandle { Chip = chip, CategoryFrame = cat.Frame },
                chipPath + "@move#" + ServerHandles.IdentityPath(cat.Frame));
            sb.Append("<option value=\"").Append(moveId).Append("\" data-h=\"")
              .Append(ServerText.EscapeText(cat.Name)).Append("\">")
              .Append(ServerText.EscapeText(cat.Name)).Append("</option>");
        }
        sb.Append("</select>");
    }

    // The secondary dropdown's displayed value, read from its own SelectionField subtree.
    internal static string ReadOwnText(VisualElement dropdown)
    {
        var raw = ScreenWalker.ReadFirstText(dropdown);
        if (string.IsNullOrEmpty(raw)) return null;
        var clean = TextFilters.CleanForDisplay(raw);
        return string.IsNullOrEmpty(clean) ? null : clean;
    }
}

/// <summary>
/// A pending chip-to-category move. Validated and dispatched on the main thread through the
/// standard /activate path; the reply is the outcome the player hears. The move applies
/// asynchronously (the category lists rebind), so the handle waits — like the tactics pitch
/// move — for the destination's own count text to change before reporting success, and reports
/// honestly when the game never confirms.
/// </summary>
internal sealed class SetPieceMoveHandle : ServerHandles.Handle
{
    public VisualElement Chip;
    public VisualElement CategoryFrame;

    // Waits across frames for the game to apply the move: resolve on the main thread, act from
    // the request thread with per-frame Invokes.
    public override bool DispatchesAcrossFrames => true;

    public override bool TryValidate(out string error)
    {
        error = null;
        if (Chip.panel == null || !ElementBridge.IsVisible(Chip)
            || CategoryFrame.panel == null || !ElementBridge.IsVisible(CategoryFrame))
        {
            error = "That element is no longer on screen. Refresh.";
            return false;
        }
        return true;
    }

    private const int ApplyStepMs = 50;
    private const int ApplyBudgetMs = 2000;

    public override string Activate()
    {
        var (message, ok, before) = MainThreadDispatcher.Invoke(() =>
        {
            var categories = SetPieceInstructions.Categories(Chip);
            SetPieceInstructions.Category dest = null;
            foreach (var cat in categories)
                if (cat.Frame.Pointer == CategoryFrame.Pointer) dest = cat;
            if (dest == null)
                return ("That category is no longer on screen. Refresh.", false, (string)null);

            string slot = SetPieceInstructions.SlotOf(Chip);
            if (slot == null)
            {
                Degrade.Note("[SetPieceMove] chip slot text unreadable; move refused");
                return ("The game did not expose that instruction. Refresh.", false, (string)null);
            }

            // The destination's header count (" (4/9)") is the confirmation signal: it changes
            // the moment the game applies the move. Read it before dispatching.
            string countBefore = ServerText.Clean(HeaderText(dest));
            var result = SetPieceInstructions.TryMove(Chip, dest, slot);
            if (!result.Ok) return (result.Message, false, (string)null);
            return ($"Moved {slot} to {dest.Name}", true, countBefore);
        });
        if (!ok) return message;

        long deadline = Environment.TickCount64 + ApplyBudgetMs;
        try
        {
            while (Environment.TickCount64 < deadline)
            {
                bool applied = MainThreadDispatcher.Invoke(() =>
                {
                    // A vanished category means the screen moved on; nothing left to wait for.
                    if (CategoryFrame.panel == null) return true;
                    var categories = SetPieceInstructions.Categories(CategoryFrame);
                    foreach (var cat in categories)
                        if (cat.Frame.Pointer == CategoryFrame.Pointer)
                            return ServerText.Clean(HeaderText(cat)) != before;
                    return true;
                });
                if (applied) return message;
                System.Threading.Thread.Sleep(ApplyStepMs);
            }
            ServerText.LogBackground("[SetPieceMove] move not confirmed within budget; reporting it unconfirmed.");
        }
        catch (Exception e)
        {
            ServerText.LogBackground($"[SetPieceMove] apply-wait failed: {e.Message}; reporting it unconfirmed.");
        }
        return "The game did not confirm the move. Check the categories before trying again.";
    }

    private static string HeaderText(SetPieceInstructions.Category cat)
    {
        var texts = new List<string>();
        CollectTexts(cat.HeaderButton, texts);
        return string.Join(" ", texts);
    }

    private static void CollectTexts(VisualElement el, List<string> texts)
    {
        var textEl = el.TryCast<TextElement>();
        if (textEl != null && !string.IsNullOrEmpty(textEl.text)) texts.Add(textEl.text);
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectTexts(el.hierarchy[i], texts);
    }
}

/// <summary>
/// Renders a set-piece instruction chip's dropdown as the game-backed select it already is —
/// but valued with the instruction name the game displays beside it (the generic path shows
/// the dropdown's bare "2 of 7" index, which reads like a priority) — followed by the mod's
/// "Move" select listing the other categories. Falls back to the generic presentation when the
/// chip's shape doesn't hold.
/// </summary>
internal sealed class SetPieceInstructionRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el)
    {
        if (el is not DropdownAdapter a) return false;
        return SetPieceInstructions.Chip(a.BackingElement) != null;
    }

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        var backing = ((DropdownAdapter)el).BackingElement;
        var chip = SetPieceInstructions.Chip(backing);
        string slot = SetPieceInstructions.SlotOf(chip);

        // A "Stay Back If Needed" chip carries a second, self-labelling dropdown (the fallback
        // instruction, its value inside its own SelectionField). It reads as its own value and
        // gets no Move select — the chip moves as a whole through the primary's.
        bool secondary = !SetPieceInstructions.HasAncestorNamed(backing, "dropdown-container");

        // The instruction select's accessible name is its displayed value (the slot letter is
        // the separate text stop just before it), so the option carries the same name.
        sb.Append("<p>");
        SetPieceInstructions.AppendInstructionSelect(sb, el,
            SetPieceInstructions.InstructionLabel(chip, el, secondary));
        // A disabled instruction offers no move either — the game greys the whole chip.
        if (!secondary && slot != null && !ServerText.IsDisabled(el))
        {
            sb.Append(' ');
            SetPieceInstructions.AppendMoveSelect(sb, chip, slot);
        }
        sb.Append("</p>");
    }
}
