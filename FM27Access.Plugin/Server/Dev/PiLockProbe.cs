using System.Text;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server.Dev;

/// <summary>
/// Dev-only probe hunting a source for a player instruction's option-level locks that does NOT
/// require opening the game's editor card. The editor's radio rows expose availability through
/// <c>enabledInHierarchy</c>, but reaching them costs a whole card teardown/rebuild per
/// instruction, which is why the browsed list no longer carries lock markers at all. If the
/// tile's own binding node already carries per-option availability, marking becomes free.
///
/// GET /pilockprobe?key=AttackingWidth[&amp;depth=4]. Read-only: walks the tile's BindingRoot node
/// tree, printing child names, typed values, and every DynamicReference property, plus the edit
/// card's radio rows when one happens to be open (for comparing which property drives the radio).
/// </summary>
internal static class PiLockProbe
{
    // open=1: drive the editor open, dump its radios, close it - the only known place the game
    // exposes availability. Runs on the request thread (it waits across frames), main thread only
    // for the reads and clicks, mirroring the shape of the real editor session.
    internal static string RunWithEditor(string key, int depth, bool hold)
    {
        if (string.IsNullOrWhiteSpace(key)) key = "AttackingWidth";
        if (depth <= 0) depth = 4;
        string groupName = "PlayerInstructionGroup" + key;
        var sb = new StringBuilder();

        string err = MainThreadDispatcher.Invoke(() =>
        {
            var tile = PlayerInstructions.FindTile(groupName);
            if (tile == null) return "tile not found";
            var btn = TeamInstructions.FindByName(tile, "layout-tile-interaction-button", 25);
            if (btn == null) return "tile has no interaction button";
            btn.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
            return null;
        });
        if (err != null) return err + "\n";

        if (!TeamInstructions.WaitFor(() => FindEditPanel() != null, 4000))
            return "the editor did not open\n";
        TeamInstructions.WaitFor(() =>
        {
            var e = FindEditPanel();
            return e != null && CountRadios(e, 0) > 1;
        }, 3000);

        MainThreadDispatcher.Invoke<object>(() =>
        {
            var edit = FindEditPanel();
            sb.Append("========== EDIT CARD RADIOS (").Append(groupName).Append(") ==========\n");
            if (edit == null) sb.Append("editor vanished\n");
            else DumpRadios(edit, sb, depth);
            return null;
        });

        if (hold)
        {
            sb.Append("\n(left open on purpose: inspect with /gui, then /pilockprobe?close=1)\n");
            return sb.ToString();
        }

        PopEditor(4);
        return sb.ToString();
    }

    // Every instruction switch inside the editor pushes another page, so leaving takes one Back
    // per page. A single press strands the editor over the card - which is what this probe used
    // to do, and the same trap the real harvest's close had to learn.
    private static void PopEditor(int presses)
    {
        for (int i = 0; i < presses; i++)
        {
            if (FindEditPanel() == null) return;
            MainThreadDispatcher.Invoke<object>(() =>
            {
                var edit = FindEditPanel();
                if (edit == null) return null;
                var back = TeamInstructions.FindByName(edit, "BackButton", 40);
                if (back == null) return null;
                Patches.PanelHistoryBackPatch.AllowBackThroughNextFrame();
                back.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
                return null;
            });
            TeamInstructions.WaitFor(() => FindEditPanel() == null, 1200);
        }
    }

    // sweep=1: the economics question. The open editor still contains every OTHER instruction's
    // tile, so if clicking one switches the editor in place, all instructions' locks can be read
    // from ONE open/close instead of one card teardown per instruction.
    internal static string RunSweep()
    {
        var sb = new StringBuilder();
        long t0 = System.Environment.TickCount64;

        if (FindEditPanel() == null)
        {
            string err = MainThreadDispatcher.Invoke(() =>
            {
                var first = FindFirstTileButton();
                if (first == null) return "no instruction tile on the card";
                first.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
                return null;
            });
            if (err != null) return err + "\n";
            if (!TeamInstructions.WaitFor(() => FindEditPanel() != null, 4000))
                return "the editor did not open\n";
        }
        sb.Append("opened in ").Append(System.Environment.TickCount64 - t0).Append("ms\n\n");

        var seen = new System.Collections.Generic.HashSet<string>();
        for (int step = 0; step < 20; step++)
        {
            string title = MainThreadDispatcher.Invoke(() =>
            {
                var edit = FindEditPanel();
                return edit == null ? null : NextTileTitle(edit, seen);
            });
            if (title == null) break;
            seen.Add(title);

            long ts = System.Environment.TickCount64;
            string before = MainThreadDispatcher.Invoke(() => RadioSignature(FindEditPanel()));
            string clickErr = MainThreadDispatcher.Invoke(() =>
            {
                var edit = FindEditPanel();
                var btn = edit == null ? null : TileButtonByTitle(edit, title);
                if (btn == null) return "tile button vanished";
                btn.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
                return null;
            });
            if (clickErr != null) { sb.Append(title).Append(": ").Append(clickErr).Append('\n'); continue; }

            bool switched = TeamInstructions.WaitFor(
                () => FindEditPanel() != null && RadioSignature(FindEditPanel()) != before, 3000);
            long ms = System.Environment.TickCount64 - ts;
            bool stillOpen = FindEditPanel() != null;

            MainThreadDispatcher.Invoke<object>(() =>
            {
                sb.Append("=== ").Append(title)
                  .Append("  switched=").Append(switched)
                  .Append(" editorStillOpen=").Append(stillOpen)
                  .Append(" in ").Append(ms).Append("ms\n");
                var edit = FindEditPanel();
                if (edit != null) DumpRadios(edit, sb, 0);
                return null;
            });
            if (!stillOpen) { sb.Append("(the editor closed - in-place switching does NOT work)\n"); break; }
        }

        PopEditor(seen.Count + 3);
        sb.Append("\ntotal ").Append(System.Environment.TickCount64 - t0).Append("ms\n");
        return sb.ToString();
    }

    // A stable fingerprint of the editor's current radio set, to detect the in-place switch.
    private static string RadioSignature(VisualElement edit)
    {
        if (edit == null) return "";
        var sb = new StringBuilder();
        SigRec(edit, sb, 0);
        return sb.ToString();
    }

    private static void SigRec(VisualElement el, StringBuilder sb, int d)
    {
        if (d > 40) return;
        string t = null;
        try { t = el.GetIl2CppType().Name; } catch { return; }
        if (t == "SIRadioButton")
        {
            sb.Append(TeamInstructions.FindText(el, "title", 10)).Append(':')
              .Append(el.enabledInHierarchy).Append('|');
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) SigRec(el.hierarchy[i], sb, d + 1);
    }

    private static VisualElement FindFirstTileButton()
    {
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel == null || pv.Panel.name != "PlayerInstructionsCard") continue;
            VisualElement hit = null;
            FirstTileRec(pv.Panel, ref hit, 0);
            if (hit != null) return hit;
        }
        return null;
    }

    private static void FirstTileRec(VisualElement el, ref VisualElement hit, int d)
    {
        if (hit != null || d > 60) return;
        if (el.name == "layout-tile-interaction-button") { hit = el; return; }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) FirstTileRec(el.hierarchy[i], ref hit, d + 1);
    }

    // The title of the next tile inside the editor we have not visited yet.
    private static string NextTileTitle(VisualElement edit, System.Collections.Generic.HashSet<string> seen)
    {
        string found = null;
        TitleRec(edit, seen, ref found, 0);
        return found;
    }

    private static void TitleRec(VisualElement el, System.Collections.Generic.HashSet<string> seen,
        ref string found, int d)
    {
        if (found != null || d > 60) return;
        if (el.name == "layout-tile-interaction-button")
        {
            string t = TeamInstructions.FindText(el, "title-text", 25);
            if (!string.IsNullOrWhiteSpace(t) && !seen.Contains(t)) found = t;
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) TitleRec(el.hierarchy[i], seen, ref found, d + 1);
    }

    private static VisualElement TileButtonByTitle(VisualElement edit, string title)
    {
        VisualElement hit = null;
        ByTitleRec(edit, title, ref hit, 0);
        return hit;
    }

    private static void ByTitleRec(VisualElement el, string title, ref VisualElement hit, int d)
    {
        if (hit != null || d > 60) return;
        if (el.name == "layout-tile-interaction-button")
        {
            if (TeamInstructions.FindText(el, "title-text", 25) == title) hit = el;
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) ByTitleRec(el.hierarchy[i], title, ref hit, d + 1);
    }

    /// <summary>
    /// The tile's own quick-change arrows: does clicking one commit the instruction IN PLACE (no
    /// editor, no card teardown), and does the traversal SKIP the options the game refuses? If
    /// both hold, the game already has a cheap setter that handles locks by construction, and the
    /// mod's editor-session commit and lock harvest are both solving a problem the game solved.
    /// GET /pilockprobe?key=AttackingWidth&amp;arrow=right&amp;times=6
    /// </summary>
    internal static string RunArrow(string key, string dir, int times)
    {
        if (string.IsNullOrWhiteSpace(key)) key = "AttackingWidth";
        string arrow = dir == "left" ? "leftarrowbutton" : "rightarrowbutton";
        if (times <= 0) times = 6;
        string groupName = "PlayerInstructionGroup" + key;
        var sb = new StringBuilder();
        sb.Append("tile ").Append(groupName).Append(", pressing ").Append(arrow).Append('\n');

        string start = MainThreadDispatcher.Invoke(() => TileName(groupName));
        sb.Append("  start: ").Append(start ?? "unreadable").Append('\n');

        for (int i = 0; i < times; i++)
        {
            long t0 = System.Environment.TickCount64;
            string err = MainThreadDispatcher.Invoke(() =>
            {
                var tile = PlayerInstructions.FindTile(groupName);
                if (tile == null) return "tile gone";
                var btn = TeamInstructions.FindByName(tile, arrow, 25);
                if (btn == null) return "arrow not found";
                btn.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
                return null;
            });
            if (err != null) { sb.Append("  ").Append(err).Append('\n'); break; }

            string before = MainThreadDispatcher.Invoke(() => TileName(groupName));
            TeamInstructions.WaitFor(() => TileName(groupName) != before, 1200);
            string now = MainThreadDispatcher.Invoke(() => TileName(groupName));
            bool editorOpened = MainThreadDispatcher.Invoke(() => FindEditPanel() != null);
            sb.Append("  press ").Append(i + 1).Append(": -> ").Append(now ?? "unreadable")
              .Append("  (").Append(System.Environment.TickCount64 - t0).Append("ms")
              .Append(editorOpened ? ", EDITOR OPENED" : ", no editor")
              .Append(")\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Option A: the tile's quick-change arrows are driven by the game's own
    /// <c>QuickChangeTacticInstructionEvent</c> (event ids EventFmxQuickChangePlayerInstruction
    /// Increment/Decrement), attached as an EventContext on the arrow SIButton's EventList - which
    /// is why SimulateButtonClick is a no-op on them. If the mod can fill that context and send the
    /// event, a commit becomes ONE dispatch with no editor and no card teardown, and the traversal
    /// is the game's own - so if it skips unavailable options, locks are handled by construction.
    /// GET /pilockprobe?key=AttackingWidth&amp;qc=dump|right|left&amp;times=N
    /// </summary>
    internal static string RunQuickChange(string key, string mode, int times)
    {
        if (string.IsNullOrWhiteSpace(key)) key = "AttackingWidth";
        if (times <= 0) times = 6;
        string groupName = "PlayerInstructionGroup" + key;
        var sb = new StringBuilder();

        if (mode == "dump")
        {
            MainThreadDispatcher.Invoke<object>(() =>
            {
                var tile = PlayerInstructions.FindTile(groupName);
                if (tile == null) { sb.Append("tile not found\n"); return null; }
                DumpEventOwners(tile, sb, 0);
                return null;
            }, 8000);
            return sb.Length > 0 ? sb.ToString() : "no event lists found on the tile\n";
        }

        if (mode == "inspect")
        {
            MainThreadDispatcher.Invoke<object>(() =>
            {
                var tile = PlayerInstructions.FindTile(groupName);
                if (tile == null) { sb.Append("tile not found\n"); return null; }
                foreach (var a in new[] { "leftarrowbutton", "rightarrowbutton", "layout-tile-interaction-button" })
                    InspectArrow(tile, a, sb);
                return null;
            }, 8000);
            return sb.ToString();
        }

        string arrow = mode == "left" ? "leftarrowbutton" : "rightarrowbutton";
        sb.Append("tile ").Append(groupName).Append(", sending quick-change via ").Append(arrow).Append('\n');
        sb.Append("  start: ").Append(MainThreadDispatcher.Invoke(() => TileName(groupName)) ?? "unreadable").Append('\n');

        for (int i = 0; i < times; i++)
        {
            string before = MainThreadDispatcher.Invoke(() => TileName(groupName));
            long t0 = System.Environment.TickCount64;
            string err = MainThreadDispatcher.Invoke(() => SendQuickChange(groupName, arrow), 8000);
            if (err != null) { sb.Append("  ").Append(err).Append('\n'); break; }

            TeamInstructions.WaitFor(() => TileName(groupName) != before, 1500);
            string now = MainThreadDispatcher.Invoke(() => TileName(groupName));
            bool editor = MainThreadDispatcher.Invoke(() => FindEditPanel() != null);
            sb.Append("  step ").Append(i + 1).Append(": ").Append(before).Append(" -> ").Append(now ?? "unreadable")
              .Append("  (").Append(System.Environment.TickCount64 - t0).Append("ms")
              .Append(editor ? ", EDITOR OPENED" : ", no editor").Append(")\n");
        }
        return sb.ToString();
    }

    // What the game would actually send from this control: the interop id it carries and whether
    // its target and payload are populated. Read-only - the event is filled and disposed, never
    // sent, so an unprimed one cannot throw inside the game's handler.
    private static void InspectArrow(VisualElement tile, string name, StringBuilder sb)
    {
        sb.Append(name).Append(": ");
        var btn = TeamInstructions.FindByName(tile, name, 25);
        if (btn == null) { sb.Append("not found\n"); return; }
        sb.Append("visible=").Append(ElementBridge.IsVisible(btn))
          .Append(" enabled=").Append(btn.enabledInHierarchy).Append('\n');

        var siBtn = btn.TryCast<SI.Bindable.SIButton>();
        var contexts = siBtn?.Events?.Events;
        if (contexts == null || contexts.Length == 0) { sb.Append("    no contexts\n"); return; }
        for (int i = 0; i < contexts.Length; i++)
        {
            var ctx = contexts[i];
            if (ctx == null) continue;
            sb.Append("    ctx ").Append(ctx.GetIl2CppType().Name);
            var plug = ctx.TryCast<FM.GamePlugin.EventToPluginContext>();
            if (plug == null) { sb.Append('\n'); continue; }
            try
            {
                plug.PrepareToSend();
                sb.Append(" sendable=").Append(plug.IsSendable());
                var pooled = plug.GetPooledEvent();
                var evt = pooled?.TryCast<FM.GamePlugin.EventToPlugin>();
                if (evt == null) { sb.Append(" (no pooled event)\n"); continue; }
                sb.Append(" id=").Append(evt.ID)
                  .Append(" target=").Append(evt.Target == null ? "null" : evt.Target.GetIl2CppType().Name)
                  .Append(" data=").Append(evt.DataToSend == null ? "null" : evt.DataToSend.ToString());
                evt.Dispose();
            }
            catch (System.Exception ex) { sb.Append(" threw ").Append(ex.GetType().Name); }
            sb.Append('\n');
        }
    }

    private static string _lastEventId;

    // Main thread. Null on success, else a reason.
    private static string SendQuickChange(string groupName, string arrowName)
    {
        var tile = PlayerInstructions.FindTile(groupName);
        if (tile == null) return "tile gone";
        var btn = TeamInstructions.FindByName(tile, arrowName, 25);
        if (btn == null) return "arrow not found";

        var siBtn = btn.TryCast<SI.Bindable.SIButton>();
        if (siBtn == null) return "arrow is not an SIButton";
        var list = siBtn.Events;
        if (list == null) return "arrow has no EventList";
        var contexts = list.Events;
        if (contexts == null || contexts.Length == 0) return "arrow EventList is empty";

        var events = SI.Core.GameSubsystems.Get<SI.Core.EventSubsystem>();
        if (events == null) return "no EventSubsystem";

        // The arrows carry an EventToPluginContext - the FMX interop bridge - whose id is
        // EventFmxQuickChangePlayerInstruction Increment/Decrement. Filling it from its own
        // bindings and sending is exactly the pattern PlayerInstructionsButtonAdapter uses.
        for (int i = 0; i < contexts.Length; i++)
        {
            var ctx = contexts[i];
            if (ctx == null) continue;
            var plug = ctx.TryCast<FM.GamePlugin.EventToPluginContext>();
            if (plug == null) continue;

            bool sendable;
            try { plug.PrepareToSend(); sendable = plug.IsSendable(); }
            catch (System.Exception ex) { return "PrepareToSend threw: " + ex.GetType().Name; }

            var pooled = plug.GetPooledEvent();
            if (pooled == null) return "GetPooledEvent returned null";
            var evt = pooled.TryCast<FM.GamePlugin.EventToPlugin>();
            if (evt == null) return "pooled event is not an EventToPlugin";
            _lastEventId = evt.ID.ToString();

            // Sending an event whose Target never got filled throws INSIDE the game's own handler
            // (GlobalGameModule), so refuse rather than crash a game module to find out.
            if (evt.Target == null)
            {
                evt.Dispose();
                return $"context not primed: id={_lastEventId}, IsSendable={sendable}, Target=null "
                     + "(the arrow is hover-only, so its bindings are not live)";
            }
            events.SendEvent(evt);
            return null;
        }
        var names = new StringBuilder();
        for (int i = 0; i < contexts.Length; i++)
        {
            if (i > 0) names.Append(", ");
            names.Append(contexts[i] == null ? "null" : contexts[i].GetIl2CppType().Name);
        }
        return "no EventToPluginContext on the arrow; contexts: " + names;
    }

    // Every element under the tile that carries game event contexts, and what they are.
    private static void DumpEventOwners(VisualElement el, StringBuilder sb, int d)
    {
        if (d > 30) return;
        var btn = el.TryCast<SI.Bindable.SIButton>();
        if (btn != null) AppendList(el.name + " (SIButton.Events)", btn.Events, sb);
        var shortcuts = el.TryCast<FM.UI.ContextShortcutsElement>();
        if (shortcuts != null) AppendList(el.name + " (ContextShortcuts.ShortcutEvent)", shortcuts.ShortcutEvent, sb);
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) DumpEventOwners(el.hierarchy[i], sb, d + 1);
    }

    private static void AppendList(string label, SI.Bindable.EventList list, StringBuilder sb)
    {
        if (list == null) return;
        var contexts = list.Events;
        if (contexts == null || contexts.Length == 0) return;
        sb.Append("  ").Append(label).Append(": ");
        for (int i = 0; i < contexts.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(contexts[i] == null ? "null" : contexts[i].GetIl2CppType().Name);
        }
        sb.Append('\n');
    }

    private static string TileName(string groupName)
    {
        var tile = PlayerInstructions.FindTile(groupName);
        return tile == null ? null : TeamInstructions.FindText(tile, "name", 25);
    }

    private static int CountRadios(VisualElement el, int d)
    {
        if (d > 40) return 0;
        string t = null;
        try { t = el.GetIl2CppType().Name; } catch { return 0; }
        if (t == "SIRadioButton") return 1;
        int total = 0, n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) total += CountRadios(el.hierarchy[i], d + 1);
        return total;
    }

    private static VisualElement FindEditPanel()
    {
        foreach (var pv in InteractivePanels.Current())
            if (pv.Panel != null && pv.Panel.name == "PlayerInstructionsEditCard") return pv.Panel;
        return null;
    }

    internal static string Run(string key, int depth)
    {
        if (string.IsNullOrWhiteSpace(key)) key = "AttackingWidth";
        if (depth <= 0) depth = 4;
        string groupName = "PlayerInstructionGroup" + key;

        var sb = new StringBuilder();
        sb.Append("========== TILE \"").Append(groupName).Append("\" ==========\n");

        var tile = PlayerInstructions.FindTile(groupName);
        if (tile == null)
        {
            sb.Append("tile not found on the active panel (is the Player Instructions card open,\n")
              .Append("and is this instruction on the visible tab?)\n");
            return sb.ToString();
        }

        // The tile's OWN roots first - climbing to an ancestor lands on the whole card's root,
        // which carries the player (RoleMask/Position) but nothing per-option.
        int found = 0;
        DumpTileRoots(tile, sb, depth, 0, ref found);
        if (found == 0)
        {
            sb.Append("no BindingRoot inside the tile; falling back to the ancestor root\n");
            var anc = FindAncestorBindingRoot(tile);
            if (anc != null) DumpNode(anc, sb, depth);
        }

        sb.Append("\n---------- tile arrow buttons ----------\n");
        DumpArrows(tile, sb, 0);

        sb.Append("\n---------- tile option stepper ----------\n");
        DumpSteppers(tile, sb, depth, 0);

        sb.Append("\n========== EDIT CARD (only if already open) ==========\n");
        var edit = FindEditPanel();
        if (edit == null) sb.Append("no PlayerInstructionsEditCard open\n");
        else DumpRadios(edit, sb, depth);

        return sb.ToString();
    }

    // Every SIRadioButton in the editor with its enabled state and its own binding node, so the
    // property that drives availability can be matched against something on the tile.
    private static void DumpRadios(VisualElement el, StringBuilder sb, int depth)
    {
        string type = null;
        try { type = el.GetIl2CppType().Name; } catch { return; }
        if (type == "SIRadioButton")
        {
            sb.Append("--- radio enabled=").Append(el.enabledInHierarchy)
              .Append(" text=\"").Append(CollectText(el)).Append("\"\n");
            var root = FindAncestorBindingRoot(el);
            if (root != null) DumpNode(root, sb, depth);
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) DumpRadios(el.hierarchy[i], sb, depth);
    }

    private static string CollectText(VisualElement el)
    {
        var sb = new StringBuilder();
        Collect(el, sb, 0);
        return sb.ToString();
    }

    private static void Collect(VisualElement el, StringBuilder sb, int d)
    {
        if (d > 12) return;
        var t = el.TryCast<TextElement>();
        if (t != null && !string.IsNullOrEmpty(t.text))
        {
            if (sb.Length > 0) sb.Append(" | ");
            sb.Append(ServerText.Clean(t.text));
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) Collect(el.hierarchy[i], sb, d + 1);
    }

    private static SI.Bindable.BindingRoot FindAncestorBindingRoot(VisualElement el)
    {
        var current = el.hierarchy.parent;
        for (int d = 0; current != null && d < 30; d++)
        {
            var root = current.TryCast<SI.Bindable.BindingRoot>();
            if (root != null) return root;
            current = current.hierarchy.parent;
        }
        return null;
    }

    // Every BindingRoot inside the tile, each dumped in full - the option set, if the tile knows
    // it, has to hang off one of these.
    private static void DumpTileRoots(VisualElement el, StringBuilder sb, int depth, int d, ref int found)
    {
        if (d > 30 || found > 8) return;
        var root = el.TryCast<SI.Bindable.BindingRoot>();
        if (root != null)
        {
            found++;
            sb.Append("--- tile BindingRoot #").Append(found)
              .Append(" name=\"").Append(el.name).Append("\"\n");
            DumpNode(root, sb, depth);
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) DumpTileRoots(el.hierarchy[i], sb, depth, d + 1, ref found);
    }

    // The tile's own option cycler. If its domain is the SELECTABLE options rather than every
    // catalog option, its count (and any item list it exposes) is a lock signal that costs nothing.
    private static void DumpSteppers(VisualElement el, StringBuilder sb, int depth, int d)
    {
        if (d > 30) return;
        string type = null;
        try { type = el.GetIl2CppType().Name; } catch { return; }
        if (type == "SIStepper")
        {
            sb.Append("  SIStepper name=\"").Append(el.name).Append("\"\n");
            var st = el.TryCast<SI.Bindable.SIStepper>();
            if (st == null) sb.Append("    cast to SIStepper failed\n");
            else
            {
                // The steps are the segments the game paints; if it only paints selectable
                // options, the count differs from the catalog count for a locked instruction.
                var steps = st.m_steps;
                sb.Append("    steps=").Append(steps == null ? "null" : steps.Count.ToString())
                  .Append(" fill=").Append(st.CurrentFillMode).Append('\n');
            }
            var root = FindAncestorBindingRoot(el);
            if (root != null) DumpNode(root, sb, depth);
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) DumpSteppers(el.hierarchy[i], sb, depth, d + 1);
    }

    // The quick-change arrows cycle only the options the game will accept, so their own enabled
    // state is another candidate signal.
    private static void DumpArrows(VisualElement el, StringBuilder sb, int d)
    {
        if (d > 30) return;
        if (el.name == "leftarrowbutton" || el.name == "rightarrowbutton")
        {
            sb.Append("  ").Append(el.name)
              .Append(" enabled=").Append(el.enabledInHierarchy)
              .Append(" visible=").Append(ElementBridge.IsVisible(el)).Append('\n');
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++) DumpArrows(el.hierarchy[i], sb, d + 1);
    }

    private static void DumpNode(SI.Bindable.BindingRoot root, StringBuilder sb, int maxDepth)
    {
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) { sb.Append("  no BindingSubsystem\n"); return; }
        var rootKey = root.Key;
        if (!bindings.TryGetNode(ref rootKey, out var node)) { sb.Append("  TryGetNode failed\n"); return; }
        int budget = 400;
        Walk(bindings, node, sb, 1, maxDepth, ref budget);
    }

    private static void Walk(SI.Bindable.BindingSubsystem bindings, SI.Bindable.IReadOnlyNode node,
        StringBuilder sb, int depth, int maxDepth, ref int budget)
    {
        if (depth > maxDepth || budget <= 0) return;
        foreach (var child in node.Children)
        {
            if (budget-- <= 0) return;
            var childKey = child.Key;
            var value = bindings.Get(ref childKey);
            sb.Append(new string(' ', depth * 2)).Append("child \"").Append(child.Name)
              .Append("\" = ").Append(value == null ? "null" : value.ToString()).Append('\n');
            DumpReference(value, sb, depth);
            Walk(bindings, child, sb, depth + 1, maxDepth, ref budget);
        }
    }

    private static void DumpReference(SI.Core.TypedValue typedValue, StringBuilder sb, int depth)
    {
        try
        {
            if (typedValue == null) return;
            var dynRef = typedValue.Get()?.TryCast<SI.Bindable.DynamicReference>();
            if (dynRef == null) return;
            var reg = SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
            var keys = dynRef.Keys.GetEnumerator();
            while (keys.MoveNext())
            {
                var k = keys.Current;
                string name;
                try { name = reg.GetInfo((uint)k).Name; } catch { name = "key:" + k; }
                string val;
                try { val = dynRef[k]?.ToString() ?? "null"; }
                catch (System.Exception e) { val = "err:" + e.GetType().Name; }
                sb.Append(new string(' ', depth * 2)).Append("  ref ").Append(name)
                  .Append(" = ").Append(val).Append('\n');
            }
        }
        catch (System.Exception e)
        {
            sb.Append("  ref dump failed: ").Append(e.GetType().Name).Append('\n');
        }
    }
}
