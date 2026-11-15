using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The tactics Team Instructions section as one editable table, replacing the game's three-way
/// layout (a per-phase tile grid, a phase-selector pitch, and a phase-grouped summary list) and
/// its modal-per-instruction editing flow. One row per instruction of the active In/Out of
/// Possession tab, grouped by phase: Phase | Instruction | Setting, where Setting is a native
/// select holding the instruction's full option list.
///
/// Rows are read live from the game's phase-summary cells ("TeamInstructionCells" under
/// "PhasesTIsVisible"), which list every instruction of the active tab with its current value —
/// the one place all phases are visible at once. The option lists come from the game's static
/// instruction catalog (TacticTeamInstructionData: 28 instructions, each with its ordered,
/// translated option entries), so the select renders complete without opening any game UI.
///
/// Committing a change drives the game's own quick-change path: the summary cell's click opens
/// the instruction's cascading context menu (TacticsTeamInstructionContextMenuModule — one entry
/// per option, activating one commits through the game's SelectedOptionPath binding), the entry
/// matching the chosen option is invoked through the menu's data model, and the menu is closed.
/// The menu never surfaces to the companion: ContextMenuSurface.SuppressCompanionView hides it
/// from the screen token and the body compose for the sub-second commit window. The commit is
/// verified by re-reading the cell's value before reporting success.
///
/// HTML surface only. The speech surface keeps the game's own tiles and modal (its cursor walks
/// the real elements); a TryEmit failure falls back to the generic tile/cell rendering there too.
/// </summary>
internal static class TeamInstructions
{
    private const int FrameMs = 16;
    private const int MenuFillBudgetMs = 2500;   // cell click -> bound menu with entries (async fill)
    private const int VerifyBudgetMs = 1500;     // entry invoke -> cell value re-reads as the target
    private const int SearchDepthCap = 100;      // DFS guard: PhasesTIsVisible sits ~50 levels below the panel root

    public static void RegisterRoutes(Dictionary<string, Route> routes)
    {
        routes["/teaminstructions/set"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.",
            Handler = (q, b) => (Set(Query.Str(q, "id"), Query.Int(q, "opt"), Query.Str(q, "screen")), null),
        };
        routes["/teaminstructions/remotestate"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.",
            Handler = (q, b) => (RemoteState(Query.Str(q, "id")), null),
        };
        routes["/teaminstructions/remoteset"] = new Route
        {
            ContentType = "text/plain",
            LoadingBody = () => "The game is still loading.",
            Handler = (q, b) => (RemoteSet(Query.Str(q, "id"), Query.Int(q, "opt"), Query.Str(q, "screen")), null),
        };
    }

    // --- Static option catalog ---

    // Instruction name -> ordered option names, both via the game's own translations, read once
    // from the TacticTeamInstructionData asset. Static game data (fixed for the session), not
    // game state, so caching does not risk staleness. Null until built; empty after a failed
    // build so the failure is re-attempted on the next render rather than latched forever.
    private static Dictionary<string, List<Option>> _catalog;

    // A paired second instruction sharing the primary's display name (Supporting Runs is two
    // settings, underlaps and overlaps, linked by ExtraInstructionID and both named "Supporting
    // Runs"). Keyed by that shared name; built with the catalog.
    private static Dictionary<string, List<Option>> _secondary;
    private static Dictionary<int, List<Option>> _byNativeId;
    private static Dictionary<int, List<Option>> _secondaryByNativeId;

    internal sealed class Option
    {
        public string Name;   // short name: the collapsed select value and the commit target
        public string Full;   // "Name, desc, effect" - the browsed option text, as the game's
                              // own editor radio rows read ("Much Shorter, Strongly emphasize
                              // shorter passes., Retain the ball.")
        public string Detail; // the option's long prose description followed by the staff
                              // feedback pros/cons - the game editor's detail pane, shown in
                              // the table's Feedback column for the current value
        public string Context;// the full-context name ("Overlap Both Flanks" vs the short
                              // "Both Flanks") - carries the pair-half word PairSuffix mines
    }

    // Main thread. Never throws: a missing asset or unreadable entry degrades to rows without
    // selects (the table still shows live values), noted once per build attempt.
    private static Dictionary<string, List<Option>> Catalog()
    {
        if (_catalog is { Count: > 0 }) return _catalog;
        var map = new Dictionary<string, List<Option>>(StringComparer.Ordinal);
        var sec = new Dictionary<string, List<Option>>(StringComparer.Ordinal);
        _byNativeId = new();
        _secondaryByNativeId = new();
        var extras = new Dictionary<int, int>();
        try
        {
            var datas = UnityEngine.Resources.FindObjectsOfTypeAll<FM.UI.TacticTeamInstructionData>();
            if (datas == null || datas.Length == 0)
            {
                Degrade.Note("team instructions: TacticTeamInstructionData asset not found; rendering values read-only");
                return _catalog = map;
            }
            var entries = datas[0].m_storedDataEntryList;
            if (entries == null)
            {
                Degrade.Note("team instructions: catalog entry list null; rendering values read-only");
                return _catalog = map;
            }
            foreach (var entry in entries)
            {
                string name = entry.NameTranslationID.TranslatedString;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var optionEntries = entry.InstructionOptionEntries;
                if (optionEntries == null || optionEntries.Length < 2) continue;
                var options = new List<Option>(optionEntries.Length);
                foreach (var o in optionEntries)
                {
                    // Cleaned at build time so the JSON attribute payloads embed display-ready
                    // strings and the whole JSON needs only plain HTML escaping.
                    string opt = ServerText.Clean(o.NameTranslationID.TranslatedString);
                    if (string.IsNullOrWhiteSpace(opt)) { options = null; break; }
                    // A missing description degrades to the bare name - still an honest option.
                    string full = opt;
                    var descIds = o.DescriptionTranslationIDList;
                    if (descIds != null)
                    {
                        foreach (var d in descIds)
                        {
                            string line = ServerText.Clean(d.TranslatedString);
                            if (!string.IsNullOrWhiteSpace(line)) full += ", " + line;
                        }
                    }
                    // Some entries (the paired secondaries) carry no short description lines;
                    // their main description is a single sentence, so it stands in rather than
                    // leaving the browsed option a bare name beside its richly-described peers.
                    if (full == opt)
                    {
                        string main = ServerText.Clean(o.MainDescriptionTranslationID.TranslatedString);
                        if (!string.IsNullOrWhiteSpace(main)) full += ", " + main;
                    }
                    // The detail pane text: prose description, then the staff feedback lines,
                    // all sentence-terminated already. Missing pieces just shorten the cell.
                    var detail = new StringBuilder();
                    AppendDetail(detail, o.MainDescriptionTranslationID.TranslatedString);
                    AppendDetailList(detail, o.ProsTranslations);
                    AppendDetailList(detail, o.ConsTranslations);
                    options.Add(new Option
                    {
                        Name = opt,
                        Full = full,
                        Detail = detail.ToString(),
                        Context = ServerText.Clean(o.FullContextName.TranslatedString),
                    });
                }
                if (options == null) continue;
                _byNativeId[(int)entry.InstructionID] = options;
                if (entry.ExtraInstructionID != FM.UI.TacticTeamInstructions.Invalid)
                    extras[(int)entry.InstructionID] = (int)entry.ExtraInstructionID;
                // A duplicated name is a PAIR (one instruction linking the other via
                // ExtraInstructionID, both displayed under one name): the linking entry is the
                // primary - the half the game's summary cell and quick menu speak for - and the
                // linked one goes to the secondary map for its own table row.
                bool isPrimary = entry.ExtraInstructionID != FM.UI.TacticTeamInstructions.Invalid;
                if (!map.ContainsKey(name))
                {
                    map[name] = options;
                }
                else if (isPrimary)
                {
                    sec[name] = map[name];
                    map[name] = options;
                }
                else
                {
                    sec[name] = options;
                }
            }
        }
        catch (Exception ex)
        {
            Degrade.Note($"team instructions: catalog build failed ({ex.GetType().Name}: {ex.Message}); rendering values read-only");
        }
        foreach (var pair in extras)
            if (_byNativeId.TryGetValue(pair.Value, out var extra)) _secondaryByNativeId[pair.Key] = extra;
        _secondary = sec;
        return _catalog = map;
    }

    private static Dictionary<string, List<Option>> SecondaryCatalog()
    {
        Catalog();
        return _secondary ?? new Dictionary<string, List<Option>>(StringComparer.Ordinal);
    }

    // The word distinguishing one half of a pair, mined from an option whose full-context name
    // extends its short name ("Overlap Both Flanks" over "Both Flanks" -> "Overlap"). Also the
    // heading and summary-line token inside the game's editor card ("Overlap", "Overlap - Left").
    // Null when no option distinguishes - the row then renders without a suffix, still functional,
    // and the editor session refuses rather than guessing a section.
    private static string PairSuffix(List<Option> options)
    {
        foreach (var o in options)
        {
            if (string.IsNullOrEmpty(o.Context) || o.Context == o.Name) continue;
            if (!o.Context.EndsWith(o.Name, StringComparison.Ordinal)) continue;
            string prefix = o.Context.Substring(0, o.Context.Length - o.Name.Length).Trim();
            if (prefix.Length > 0) return prefix;
        }
        return null;
    }

    private static void AppendDetail(StringBuilder sb, string raw)
    {
        string line = ServerText.Clean(raw);
        if (string.IsNullOrWhiteSpace(line)) return;
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(line);
    }

    private static void AppendDetailList(StringBuilder sb, SI.Translation.TranslationID[] ids)
    {
        if (ids == null) return;
        foreach (var id in ids) AppendDetail(sb, id.TranslatedString);
    }

    // --- Walk-time consolidation ---

    internal sealed class Row
    {
        public string Phase;
        public string Instruction;     // the summary cell's name - the live-lookup key
        public string Label;           // display name when it differs (a pair half's suffix)
        public string Value;           // null on a remote row: not readable outside the editor
        public List<Option> Options;   // null = no select (catalog miss or value not among options)
        public bool IsRemote;          // value/commit only reachable through the pair editor card
        public string NativeContext;
        public int? NativeId;
        public bool OpenNative;
        public bool Disabled;
    }

    /// <summary>
    /// Called by ScreenWalker on the "team-instruction-tiles" anchor (main thread). Reads the
    /// sibling phase-summary lists into a TeamInstructionsItem and returns true; false means the
    /// shape was not recognised and the caller falls back to the generic tile rendering (the
    /// caller then also keeps the summary subtree, so nothing is lost).
    /// </summary>
    public static bool TryEmit(VisualElement tilesRoot, List<ScreenWalker.RenderItem> items)
    {
        var summaryRoot = FindSummaryRoot(tilesRoot);
        if (summaryRoot == null) return false;
        if (summaryRoot.name == "PhasesAndTeamInstructions" && !CompleteSummary(summaryRoot)) return false;

        var rows = ReadRows(summaryRoot);
        if (rows.Count == 0) return false;
        // A modern FM27 row must keep its actual enum field and tactic/slot
        // context. Incomplete native bindings retain the ordinary fallback.
        if (summaryRoot.name == "PhasesAndTeamInstructions"
            && rows.Exists(row => row.NativeContext == null || row.Options == null)) return false;

        items.Add(new ScreenWalker.TeamInstructionsItem
        {
            Title = ReadSectionTitle(tilesRoot),
            Rows = rows,
        });
        return true;
    }

    private static VisualElement SummaryBelow(VisualElement root)
    {
        foreach (var name in new[] { "PhasesTIsVisible", "PhasesAndTeamInstructions" })
        {
            var found = NativeTreeQuery.All<VisualElement>(root, name).Where(ElementBridge.IsVisibleInHierarchy).ToList();
            if (found.Count == 1 && (name == "PhasesTIsVisible"
                || found[0].GetIl2CppType().Name == "BindableVariableSizedGridElement")) return found[0];
            if (found.Count > 1) return null;
        }
        return null;
    }

    private static bool CompleteSummary(VisualElement root)
    {
        int expected = 0;
        var sources = NativeTreeQuery.All<SI.Bindable.StreamedObjectList>(root, "TeamInstructionCells")
            .Where(ElementBridge.IsVisibleInHierarchy).ToList();
        if (sources.Count == 0) return false;
        foreach (var source in sources)
        {
            var lists = NativeTreeQuery.All<SI.UI.VirtualisedList>(source).Where(ElementBridge.IsVisibleInHierarchy).ToList();
            if (lists.Count != 1) return false;
            var list = lists[0]; int total = ListWindowSurface.ReadItemCount(list);
            if (total < 0) return false;
            var indices = new HashSet<int>();
            for (int i = 0; i < list.GetActiveCount(); i++)
            {
                var item = list.GetActiveItem(i);
                if (item == null || item.Content == null || item.Index < 0 || item.Index >= total
                    || !ElementBridge.IsVisibleInHierarchy(item.Content)) continue;
                var cell = FindByName(item.Content, "CellButton", 20);
                if (cell == null || !ElementBridge.IsVisibleInHierarchy(cell)) return false;
                indices.Add(item.Index);
            }
            if (indices.Count != total) return false;
            expected += total;
        }
        var ordered = new List<VisualElement>(); CollectOrdered(root, 0, ordered);
        return ordered.Count(element => element.name == "CellButton") == expected;
    }

    // Capture the actual native instruction enum plus full tactic reference
    // and current slot. Equal localized captions never establish aliases.
    private static string NativeContext(VisualElement cell, out int? instructionId)
    {
        instructionId = null;
        int depth = 0;
        for (var owner = cell.parent; owner != null && depth++ < 100; owner = owner.parent)
        {
            var node = BindingRecords.NodeOf(owner);
            if (node == null) continue;
            if (!instructionId.HasValue)
            {
                var idNode = BindingRecords.FindNode(node, "teaminstructionid", 4);
                var id = idNode == null ? null : BindingRecords.NumberOf(BindingRecords.ValueOf(idNode));
                if (id.HasValue && id.Value >= 0 && id.Value <= int.MaxValue && id.Value == Math.Floor(id.Value))
                    instructionId = (int)id.Value;
            }
            if (!instructionId.HasValue) continue;
            var tactic = NativeTacticContext.Read(owner);
            if (tactic != null) return tactic + "|instruction:" + instructionId.Value;
        }
        return null;
    }

    // The summary lists are a sibling subtree of the tile grid: ascend from the anchor and probe
    // each ancestor for a visible "PhasesTIsVisible". Bounded: the shared parent is 2 hops up.
    private static VisualElement FindSummaryRoot(VisualElement tilesRoot)
    {
        var node = tilesRoot.hierarchy.parent;
        for (int hops = 0; node != null && hops < 4; hops++, node = node.hierarchy.parent)
        {
            var found = SummaryBelow(node);
            if (found != null) return found;
        }
        return null;
    }

    private static string ReadSectionTitle(VisualElement tilesRoot)
    {
        var frame = FindByName(tilesRoot, "title frame", 6);
        if (frame == null)
        {
            var header = FindByName(tilesRoot, "team-instruction-header", 8);
            return header == null ? null : FindText(header, "title-text");
        }
        var text = FindText(frame, "text");
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    // One pass over the summary subtree in document order: a PhaseHeader sets the current phase
    // name, each CellButton under it becomes a row. A cell whose name or value has not streamed
    // in yet gets an honest unreadable marker instead of a select.
    private static List<Row> ReadRows(VisualElement summaryRoot)
    {
        var rows = new List<Row>();
        var catalog = Catalog();
        string phase = null;

        var ordered = new List<VisualElement>();
        CollectOrdered(summaryRoot, 0, ordered);

        foreach (var el in ordered)
        {
            if (el.name == "PhaseHeader")
            {
                phase = FindText(el, "PhaseText");
                continue;
            }
            if (el.name != "CellButton") continue;

            string name = FindText(el, "text");
            string value = FindText(el, "text-second");
            if (string.IsNullOrWhiteSpace(name))
            {
                Degrade.Note("team instructions: summary cell name unreadable, row degraded");
                rows.Add(new Row { Phase = phase, Instruction = Degrade.Unreadable, Value = Degrade.Unreadable });
                continue;
            }

            var context = NativeContext(el, out int? instructionId);
            var row = new Row
            {
                Phase = phase,
                Instruction = name,
                Value = string.IsNullOrWhiteSpace(value) ? Degrade.Unreadable : value,
                NativeContext = context,
                NativeId = instructionId,
                Disabled = !el.enabledInHierarchy,
            };
            List<Option> options;
            if (instructionId.HasValue) _byNativeId.TryGetValue(instructionId.Value, out options);
            else catalog.TryGetValue(name, out options);
            if (options != null && options.FindIndex(o => o.Name == row.Value) >= 0)
            {
                row.Options = options;
            }
            else if (options == null)
            {
                Degrade.Note($"team instructions: no catalog options for \"{name}\", value rendered read-only");
            }
            else
            {
                Degrade.Note($"team instructions: current value \"{row.Value}\" of \"{name}\" not among its options, rendered read-only");
            }
            rows.Add(row);

            // A paired instruction (Supporting Runs: underlaps + overlaps under one name) gets a
            // second row for the half the summary cell does not speak for. Its current value is
            // only readable inside the game's editor card, so the row is remote: the client loads
            // the value on first interaction and commits through the same card.
            List<Option> secondary;
            if (instructionId.HasValue) _secondaryByNativeId.TryGetValue(instructionId.Value, out secondary);
            else SecondaryCatalog().TryGetValue(name, out secondary);
            if (row.Options != null && secondary != null)
            {
                // FM27's compound entries expose their complete setting
                // through the actual menu/editor. Do not invent a secondary
                // field name by mining translated option-word order.
                if (summaryRoot.name == "PhasesAndTeamInstructions")
                { row.OpenNative = true; continue; }
                string priSuffix = PairSuffix(row.Options);
                if (priSuffix != null) row.Label = name + ", " + priSuffix;
                string secSuffix = PairSuffix(secondary);
                rows.Add(new Row
                {
                    Phase = phase,
                    Instruction = name,
                    Label = name + ", " + (secSuffix ?? "secondary"),
                    Options = secondary,
                    IsRemote = true,
                    NativeContext = row.NativeContext,
                    NativeId = row.NativeId,
                    Disabled = row.Disabled,
                });
            }
        }
        return rows;
    }

    private static void CollectOrdered(VisualElement el, int depth, List<VisualElement> outList)
    {
        if (depth > SearchDepthCap) return;
        // Hidden subtrees are the inactive tab's leftovers; reading them would pair live rows
        // with the other tab's values.
        if (!ElementBridge.IsVisible(el)) return;
        if (el.name is "PhaseHeader" or "CellButton")
        {
            outList.Add(el);
            // A CellButton's own subtree holds no nested cells; PhaseHeader holds no cells either.
            if (el.name == "CellButton") return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectOrdered(el.hierarchy[i], depth + 1, outList);
    }

    // --- Rendering (called from HtmlRenderer.RenderItem) ---

    public static void Render(StringBuilder sb, ScreenWalker.TeamInstructionsItem item)
    {
        if (!string.IsNullOrEmpty(item.Title))
            sb.Append("<h3>").Append(ServerText.EscapeText(item.Title)).Append("</h3>");
        sb.Append("<table><thead><tr><th scope=\"col\">Phase</th><th scope=\"col\">Instruction</th>")
          .Append("<th scope=\"col\">Setting</th><th scope=\"col\">Feedback</th></tr></thead><tbody>");
        foreach (var row in item.Rows)
        {
            string label = row.Label ?? row.Instruction;
            sb.Append("<tr><td>").Append(ServerText.EscapeText(row.Phase ?? "")).Append("</td>")
              .Append("<th scope=\"row\">").Append(ServerText.EscapeText(label)).Append("</th><td>");
            if (row.OpenNative)
            {
                string id = ServerHandles.RegisterKey(new NativeInstructionOpenHandle
                { Instruction = row.Instruction, NativeContext = row.NativeContext },
                    "ti-open|" + row.Phase + "|" + row.NativeContext);
                sb.Append("<button type=\"button\" id=\"").Append(id).Append(row.Disabled ? "\" disabled onclick=\"act('" : "\" onclick=\"act('").Append(id)
                  .Append("')\">").Append(ServerText.EscapeText(row.Instruction + ", " + row.Value)).Append("</button>");
            }
            else if (row.Options == null)
            {
                sb.Append(ServerText.EscapeText(row.Value));
            }
            else if (row.IsRemote)
            {
                // A pair half whose value the game shows nowhere outside its editor card: the
                // collapsed select carries an honest placeholder instead of a value, and the
                // client's first interaction loads the real current value through
                // /teaminstructions/remotestate (a short server-driven card session) before
                // browsing begins. Same keyboard contract as every other row after that.
                string rid = ServerHandles.RegisterKey(
                    new TeamInstructionHandle { Instruction = row.Instruction, Options = row.Options, Remote = true, NativeContext = row.NativeContext, NativeId = row.NativeId },
                    "ti2|" + row.Phase + "|" + (row.NativeContext ?? row.Instruction));
                sb.Append("<select id=\"").Append(rid)
                  .Append(row.Disabled ? "\" disabled data-ti=\"1\" data-ti-remote=\"1\"" : "\" data-ti=\"1\" data-ti-remote=\"1\"")
                  .Append(" data-ti-names=\"").Append(JsonAttr(row.Options, o => o.Name))
                  .Append("\" data-ti-longs=\"").Append(JsonAttr(row.Options, o => o.Full))
                  .Append("\" aria-label=\"").Append(ServerText.EscapeText(label))
                  .Append("\"><option selected>check or change</option></select>");
            }
            else
            {
                // Registered at render time — ComposeBody clears the handle map between the walk
                // and the render, so a walk-time registration would be wiped before it could act.
                // Keyed by phase + instruction, not element identity: the streamed cell element can
                // be rebuilt between renders, but the row's meaning is stable, so focus restore
                // lands back on the same select after a refresh.
                string id = ServerHandles.RegisterKey(
                    new TeamInstructionHandle { Instruction = row.Instruction, Options = row.Options, NativeContext = row.NativeContext, NativeId = row.NativeId },
                    "ti|" + row.Phase + "|" + (row.NativeContext ?? row.Instruction));
                // data-ti (no onchange): FM.ti gives these the same interaction contract as the
                // data-dd dropdown widget - browsing is local, Enter is the only commit, leaving
                // any other way reverts. Like data-dd, the collapsed select holds ONLY the current
                // value (its short name); the full option list - each option carrying the game's
                // per-option description, as the game's own editor reads them - rides along in
                // data attributes and is populated locally on first interaction, so browsing hears
                // the descriptions and the collapsed value stays short. JSON payloads are built
                // from already-escaped fragments via JsEscape'd JSON strings, then HTML-escaped
                // whole by EscapeText when embedded in the attribute.
                int cur = row.Options.FindIndex(o => o.Name == row.Value);
                sb.Append("<select id=\"").Append(id)
                  .Append(row.Disabled ? "\" disabled data-ti=\"1\" data-ti-cur=\"" : "\" data-ti=\"1\" data-ti-cur=\"").Append(cur)
                  .Append("\" data-ti-names=\"").Append(JsonAttr(row.Options, o => o.Name))
                  .Append("\" data-ti-longs=\"").Append(JsonAttr(row.Options, o => o.Full))
                  .Append("\" aria-label=\"").Append(ServerText.EscapeText(label))
                  .Append("\"><option selected>").Append(ServerText.EscapeText(row.Value))
                  .Append("</option></select>");
            }
            // The Feedback column: the CURRENT value's long description and staff feedback
            // pros/cons, the game editor's detail pane brought into the row. Re-rendered with
            // the row on every refresh, so it always describes the setting shown beside it.
            // Empty (an honest nothing) when the row has no catalog options or current match.
            // A remote row's current value is unknowable at render time, so its cell shows the
            // static catalog feedback for EVERY option instead, each labelled - per-option facts
            // that are true regardless of which one is set.
            sb.Append("</td><td>");
            if (row.IsRemote)
            {
                bool first = true;
                foreach (var o in row.Options)
                {
                    if (string.IsNullOrEmpty(o.Detail)) continue;
                    if (!first) sb.Append(' ');
                    first = false;
                    sb.Append(ServerText.Escape(o.Name)).Append(": ").Append(ServerText.Escape(o.Detail));
                }
            }
            else
            {
                var current = row.Options?.Find(o => o.Name == row.Value);
                if (current != null && !string.IsNullOrEmpty(current.Detail))
                    sb.Append(ServerText.Escape(current.Detail));
            }
            sb.Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    // A JSON string-array attribute value: JSON-encode each already-cleaned string, then
    // HTML-escape the whole payload for the attribute context (quotes become entities;
    // JSON.parse sees them back as quotes after the browser decodes the attribute). Plain
    // Escape, not EscapeText - a rich-text clean over assembled JSON could mangle its syntax.
    internal static string JsonAttr(List<Option> options, Func<Option, string> pick)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < options.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(JsonEscape(pick(options[i]))).Append('"');
        }
        sb.Append(']');
        return ServerText.Escape(sb.ToString());
    }

    /// <summary>
    /// The same JSON-array attribute for a plain list of strings — any other widget adopting the
    /// data-ti contract (the opposition grid's two-option segmented cells) encodes its option list
    /// through here, so escaping stays in one place.
    /// </summary>
    internal static string JsonAttr(IReadOnlyList<string> values)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(JsonEscape(values[i])).Append('"');
        }
        sb.Append(']');
        return ServerText.Escape(sb.ToString());
    }

    private static string JsonEscape(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");

    // --- Commit (request thread orchestration) ---

    // Serialized under DropdownSessionManager.SessionGate with the other popup interactions: the
    // commit opens the game's context-menu popup, and interleaving with a dropdown open (or a
    // second commit) would act on the wrong popup.
    private static string Set(string id, int optIndex, string expectedScreen)
    {
        lock (DropdownSessionManager.SessionGate)
        try
        {
            var screenError = MainThreadDispatcher.Invoke(() => ServerHandles.ValidateScreen(expectedScreen));
            if (screenError != null) return screenError;
            var handle = ResolveWithRetry(id, out var resolveErr);
            if (handle == null) return resolveErr;
            // A remote (pair-half) handle must never commit through the quick menu: the menu
            // serves the OTHER half, and their option names collide ("Left"), so the wrong
            // setting would change.
            if (handle.Remote) return "That setting commits through its editor. Refresh the screen.";
            if (optIndex < 0 || optIndex >= handle.Options.Count)
                return "That option is no longer available. Refresh the screen.";
            string target = handle.Options[optIndex].Name;

            // Already the live value (the select was stale, or a duplicate-phase twin committed
            // it): report success without opening anything.
            if (MainThreadDispatcher.Invoke(() => ReadLiveValue(handle.Instruction, handle.NativeContext)) == target)
                return "ok";

            // Hide the transient menu from the companion for the whole commit: the screen token
            // must not move (no client refresh to an "Actions" page) and a mid-commit body build
            // must render the normal page. OpenInFlight additionally stands the streamed-table
            // loaders down, whose row-recycling scrolls would drop the menu's context mid-commit.
            ContextMenuSurface.SuppressCompanionView = true;
            ContextMenuGuard.OpenInFlight = true;
            try
            {
                string clickErr = MainThreadDispatcher.Invoke(() =>
                {
                    DropdownSessionManager.CloseCurrentOnMainThread();
                    var cell = FindCell(handle.Instruction, handle.NativeContext);
                    if (cell == null) return "That instruction is no longer on screen. Refresh.";
                    cell.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
                    return null;
                });
                if (clickErr != null) return clickErr;

                if (!WaitFor(() =>
                    {
                        var menu = Adapters.ContextMenuOpener.PendingMenu();
                        return menu != null && ContextMenuSurface.HasEntries(menu);
                    }, MenuFillBudgetMs))
                {
                    MainThreadDispatcher.Invoke<object>(() => { ContextMenuSurface.Close(); return null; });
                    return "The instruction menu did not open. Refresh the screen.";
                }

                bool invoked = MainThreadDispatcher.Invoke(() =>
                {
                    if (!handle.TryValidate(out _)) return false;
                    var menu = Adapters.ContextMenuOpener.PendingMenu();
                    bool ok = menu != null && ContextMenuSurface.InvokeEntryByName(menu, target);
                    ContextMenuSurface.Close();
                    return ok;
                });
                if (!invoked)
                    return $"Could not find \"{target}\" in the instruction menu. Refresh the screen.";

                // The binding write lands asynchronously; confirm against the live cell before
                // claiming success, so the page can never show a value the game rejected.
                if (WaitFor(() => ReadLiveValue(handle.Instruction, handle.NativeContext) == target, VerifyBudgetMs))
                    return "ok";
                return $"The game did not apply {target}. Refresh the screen.";
            }
            finally
            {
                ContextMenuGuard.OpenInFlight = false;
                ContextMenuSurface.SuppressCompanionView = false;
            }
        }
        catch (Exception ex)
        {
            ServerText.LogBackground($"team instructions set failed: {ex.GetType().Name}: {ex.Message}");
            return "Something went wrong applying the instruction. Refresh the screen.";
        }
    }

    // --- Pair editor sessions (request thread orchestration) ---
    //
    // The secondary half of a paired instruction is shown NOWHERE outside the game's editor card
    // (verified live: with overlap set to Left, the summary cell and tile still read the primary
    // alone), so reading or writing it means driving the card: open the cell's quick menu, invoke
    // its flagged "Go To ..." navigation entry, wait for the GenericDynamicCard, act inside it,
    // close it. The whole session is hidden from the player: the client holds its refresh
    // deferral (FM.core.dd) so the companion never re-renders mid-session, SuppressCompanionView
    // keeps the menu leg out of the screen token, and NavigationSpeechMute drops the card's
    // open/close announcements (machinery churn, not navigation the player performed).

    private const int CardWaitBudgetMs = 3000;
    private const int GoToEntryFlagBit = 8;   // observed on the menu's navigation entry (f=11 vs 3/7)

    private static string RemoteState(string id)
    {
        lock (DropdownSessionManager.SessionGate)
        try
        {
            var handle = ResolveWithRetry(id, out var resolveErr);
            if (handle == null) return resolveErr;
            if (!handle.Remote) return "That setting has no editor session. Refresh the screen.";
            string suffix = PairSuffix(handle.Options);
            if (suffix == null) return "Could not identify the setting. Refresh the screen.";

            return WithPairEditor(handle, () =>
            {
                // The card's texts bind asynchronously after the panel appears; poll for the
                // summary line rather than trusting the first read.
                int idx = -1;
                WaitFor(() => (idx = ReadEditorIndex(suffix, handle.Options)) >= 0, VerifyBudgetMs);
                return idx >= 0 ? "idx:" + idx : "Could not read the current value. Refresh the screen.";
            });
        }
        catch (Exception ex)
        {
            ServerText.LogBackground($"team instructions remotestate failed: {ex.GetType().Name}: {ex.Message}");
            return "Something went wrong reading the setting. Refresh the screen.";
        }
    }

    private static string RemoteSet(string id, int optIndex, string expectedScreen)
    {
        lock (DropdownSessionManager.SessionGate)
        try
        {
            var screenError = MainThreadDispatcher.Invoke(() => ServerHandles.ValidateScreen(expectedScreen));
            if (screenError != null) return screenError;
            var handle = ResolveWithRetry(id, out var resolveErr);
            if (handle == null) return resolveErr;
            if (!handle.Remote) return "That setting has no editor session. Refresh the screen.";
            if (optIndex < 0 || optIndex >= handle.Options.Count)
                return "That option is no longer available. Refresh the screen.";
            string target = handle.Options[optIndex].Name;
            string suffix = PairSuffix(handle.Options);
            Catalog();
            List<Option> primary;
            if (handle.NativeId.HasValue) _byNativeId.TryGetValue(handle.NativeId.Value, out primary);
            else _catalog.TryGetValue(handle.Instruction, out primary);
            string otherSuffix = primary != null ? PairSuffix(primary) : null;
            if (suffix == null || otherSuffix == null)
                return "Could not identify the setting. Refresh the screen.";

            return WithPairEditor(handle, () =>
            {
                // Wait for the card's async text fill (the summary lines are the readiness
                // signal for the option buttons too), and take the primary half's current index
                // as a guard: a mis-scoped click (which would silently change the OTHER setting)
                // is caught, not swallowed.
                int primaryBefore = -1;
                WaitFor(() => (primaryBefore = ReadEditorIndex(otherSuffix, primary)) >= 0, VerifyBudgetMs);

                var targetOption = handle.Options[optIndex];
                string clickErr = MainThreadDispatcher.Invoke(() =>
                {
                    var btn = FindEditorOptionButton(suffix, otherSuffix, targetOption);
                    if (btn == null) return $"Could not find \"{target}\" in the editor. Refresh the screen.";
                    btn.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
                    return null;
                });
                if (clickErr != null) return clickErr;

                // The editor's own summary line ("<half> - <option>") is the verification: it
                // reads the committed binding back, so success here is the game's word.
                if (!WaitFor(() => ReadEditorIndex(suffix, handle.Options) == optIndex, VerifyBudgetMs))
                    return $"The game did not apply {target}. Refresh the screen.";
                // The card re-renders after a commit, so the guard read polls like every other
                // card read; a summary line that never returns is unreadable, not a change, and
                // must not fail a commit the target verify already confirmed.
                int primaryAfter = -1;
                WaitFor(() => (primaryAfter = ReadEditorIndex(otherSuffix, primary)) >= 0, VerifyBudgetMs);
                if (primaryBefore >= 0 && primaryAfter >= 0 && primaryAfter != primaryBefore)
                    return "The paired setting changed unexpectedly. Check both values.";
                if (primaryAfter < 0)
                    MainThreadDispatcher.Invoke<object>(() => { Degrade.Note("team instructions: paired-half guard unreadable after commit"); return null; });
                return "ok";
            });
        }
        catch (Exception ex)
        {
            ServerText.LogBackground($"team instructions remoteset failed: {ex.GetType().Name}: {ex.Message}");
            return "Something went wrong applying the setting. Refresh the screen.";
        }
    }

    // Opens the editor card for the handle's instruction, runs the body, and ALWAYS closes the
    // card and releases the mute/suppression flags, whatever the body returns or throws.
    private static string WithPairEditor(TeamInstructionHandle handle, Func<string> body)
    {
        ContextMenuSurface.SuppressCompanionView = true;
        ContextMenuGuard.OpenInFlight = true;
        bool muted = false;
        VisualElement ownedEditor = null;
        string editorIdentity = null;
        try
        {
            MainThreadDispatcher.Invoke<object>(() =>
            {
                DropdownSessionManager.CloseCurrentOnMainThread();
                NavigationSpeechMute.Push();
                return null;
            });
            muted = true;

            string clickErr = MainThreadDispatcher.Invoke(() =>
            {
                var cell = FindCell(handle.Instruction, handle.NativeContext);
                if (cell == null) return "That instruction is no longer on screen. Refresh.";
                cell.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
                return null;
            });
            if (clickErr != null) return clickErr;

            if (!WaitFor(() =>
                {
                    var menu = Adapters.ContextMenuOpener.PendingMenu();
                    return menu != null && ContextMenuSurface.HasEntries(menu);
                }, MenuFillBudgetMs))
            {
                MainThreadDispatcher.Invoke<object>(() => { ContextMenuSurface.Close(); return null; });
                return "The instruction menu did not open. Refresh the screen.";
            }

            bool navigated = MainThreadDispatcher.Invoke(() =>
            {
                var menu = Adapters.ContextMenuOpener.PendingMenu();
                bool ok = menu != null && ContextMenuSurface.InvokeSingleFlaggedEntry(menu, GoToEntryFlagBit);
                ContextMenuSurface.Close();
                return ok;
            });
            if (!navigated) return "Could not open the instruction editor. Refresh the screen.";

            if (!WaitFor(() => FindEditorCard() != null, CardWaitBudgetMs))
                return "The instruction editor did not open. Refresh the screen.";

            MainThreadDispatcher.Invoke<object>(() =>
            { ownedEditor = FindEditorCard(); editorIdentity = EditorIdentity(ownedEditor); return null; });
            if (ownedEditor == null || editorIdentity == null) return "The instruction editor is not ready. Refresh the screen.";

            return body();
        }
        finally
        {
            try
            {
                MainThreadDispatcher.Invoke<object>(() =>
                { if (ownedEditor != null && EditorIdentity(ownedEditor) == editorIdentity) CloseEditorCard(ownedEditor); return null; });
                WaitFor(() => ownedEditor == null || !ElementBridge.IsVisibleInHierarchy(ownedEditor)
                    || EditorIdentity(ownedEditor) != editorIdentity, 1500);
            }
            catch (Exception ex) { ServerText.LogBackground("pair editor close failed: " + ex.Message); }
            if (muted)
            {
                try { MainThreadDispatcher.Invoke<object>(() => { NavigationSpeechMute.Pop(); return null; }); }
                catch (Exception ex) { ServerText.LogBackground("pair editor unmute failed: " + ex.Message); }
            }
            ContextMenuGuard.OpenInFlight = false;
            ContextMenuSurface.SuppressCompanionView = false;
        }
    }

    // --- Editor card access (main thread) ---

    private static VisualElement FindEditorCard()
    {
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel != null && pv.Panel.name == "GenericDynamicCard") return pv.Panel;
        }
        return null;
    }

    private static string EditorIdentity(VisualElement element)
    {
        var panel = element?.TryCast<SI.Bindable.Panel>();
        var root = panel?.m_bindingRoot?.TryCast<SI.Bindable.BindingRoot>();
        return root == null || root.Key.m_key == 0 ? null : panel.Identifier + ":" + root.Key.m_key;
    }

    private static void CloseEditorCard(VisualElement card)
    {
        if (FindEditorCard()?.Pointer != card.Pointer) return;
        var closeFrame = FindByName(card, "close-button", SearchDepthCap);
        if (closeFrame == null)
        {
            Degrade.Note("team instructions: editor card close button not found");
            return;
        }
        var btn = FindFirstOfType(closeFrame, "SIButton", 8);
        if (btn == null)
        {
            Degrade.Note("team instructions: editor card close button not clickable");
            return;
        }
        btn.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
    }

    // The current option of one pair half, read from the editor's own summary line
    // ("<half> - <option>", e.g. "Overlap - Left"): the index whose composed text exists as an
    // SIText in the card. The option may appear under its short name or its full-context name
    // ("Both Flanks" vs "Overlap Both Flanks"), so both compositions match. -1 when absent -
    // the caller reports, never guesses.
    private static int ReadEditorIndex(string suffix, List<Option> options)
    {
        var card = FindEditorCard();
        if (card == null || options == null) return -1;
        for (int i = 0; i < options.Count; i++)
        {
            if (FindByText(card, suffix + " - " + options[i].Name, SearchDepthCap) != null) return i;
            if (!string.IsNullOrEmpty(options[i].Context)
                && FindByText(card, suffix + " - " + options[i].Context, SearchDepthCap) != null) return i;
        }
        return -1;
    }

    // The clicked half's option button, scoped STRICTLY to its section: buttons between this
    // half's heading text and the other half's heading, in document order. Option names collide
    // between halves ("Left"), so an unscoped match could commit the wrong setting - null means
    // refuse, never guess. A button may carry the option's short name or its full-context name
    // ("Both Flanks" vs "Overlap Both Flanks"), so both label forms match.
    private static VisualElement FindEditorOptionButton(string suffix, string otherSuffix, Option option)
    {
        var card = FindEditorCard();
        if (card == null) return null;
        bool inSection = false;
        VisualElement match = null;
        WalkEditor(card, 0, el =>
        {
            if (match != null) return;
            if (el.GetIl2CppType().Name == "SIText")
            {
                string t = ServerText.Clean(el.Cast<TextElement>().text);
                if (t == suffix) inSection = true;
                else if (t == otherSuffix) inSection = false;
                return;
            }
            if (!inSection || el.GetIl2CppType().Name != "SIButton") return;
            var labelEl = FindFirstOfType(el, "SIText", 6);
            if (labelEl == null) return;
            string label = ServerText.Clean(labelEl.Cast<TextElement>().text);
            if (label == option.Name || (!string.IsNullOrEmpty(option.Context) && label == option.Context))
                match = el;
        });
        return match;
    }

    // Document-order visitor over visible elements. Visits an SIButton but does not descend into
    // it (its label is read by the caller), so a button's own text never flips section state.
    private static void WalkEditor(VisualElement el, int depth, Action<VisualElement> visit)
    {
        if (depth > SearchDepthCap || !ElementBridge.IsVisible(el)) return;
        visit(el);
        if (el.GetIl2CppType().Name == "SIButton") return;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            WalkEditor(el.hierarchy[i], depth + 1, visit);
    }

    private static VisualElement FindFirstOfType(VisualElement root, string typeName, int depthCap)
    {
        if (root.GetIl2CppType().Name == typeName) return root;
        if (depthCap <= 0) return null;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = FindFirstOfType(root.hierarchy[i], typeName, depthCap - 1);
            if (found != null) return found;
        }
        return null;
    }

    private static VisualElement FindByText(VisualElement root, string text, int depthCap)
    {
        if (root.GetIl2CppType().Name == "SIText"
            && ServerText.Clean(root.Cast<TextElement>().text) == text) return root;
        if (depthCap <= 0) return null;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = FindByText(root.hierarchy[i], text, depthCap - 1);
            if (found != null) return found;
        }
        return null;
    }

    // Resolves a handle with a short retry: a body build clears the handle map and re-registers
    // the same ids moments later, so an action landing inside that window would otherwise fail
    // with "Unknown element" (the same transient the dropdown open retries). Request thread.
    private const int ResolveBudgetMs = 800;

    private static TeamInstructionHandle ResolveWithRetry(string id, out string error)
    {
        long start = Environment.TickCount64;
        while (true)
        {
            var resolved = MainThreadDispatcher.Invoke(() =>
            {
                var h = ServerHandles.ResolveTeamInstruction(id, out var err);
                return (Handle: h, Err: err);
            });
            if (resolved.Handle != null)
            {
                error = null;
                return resolved.Handle;
            }
            if (Environment.TickCount64 - start >= ResolveBudgetMs)
            {
                error = resolved.Err;
                return null;
            }
            Thread.Sleep(3 * FrameMs);
        }
    }

    // Polls at ~3-frame cadence, not per-frame: each check DFS-walks the panel tree on the main
    // thread (FindCell), so a tighter loop would spend more frame time searching than settling.
    internal static bool WaitFor(Func<bool> conditionOnMainThread, int timeoutMs)
    {
        int waited = 0;
        while (waited < timeoutMs)
        {
            if (MainThreadDispatcher.Invoke(conditionOnMainThread)) return true;
            Thread.Sleep(3 * FrameMs);
            waited += 3 * FrameMs;
        }
        return false;
    }

    // --- Live cell lookup (main thread) ---

    // Resolves the instruction's summary cell fresh from the current interactive set on every
    // use: the streamed cells are rebuilt by the game at will, so a held element reference could
    // go stale between render and commit. Same-named cells (an instruction listed under two
    // phases) share one underlying instruction, so the first match is authoritative.
    private static VisualElement FindCell(string instruction, string context = null)
    {
        foreach (var pv in InteractivePanels.Current())
        {
            if (pv.Panel == null) continue;
            var summary = SummaryBelow(pv.Panel);
            if (summary == null) continue;
            var cells = new List<VisualElement>();
            CollectOrdered(summary, 0, cells);
            foreach (var el in cells)
            {
                if (el.name != "CellButton") continue;
                if (FindText(el, "text") == instruction && (context == null || NativeContext(el, out _) == context)) return el;
            }
        }
        return null;
    }

    private static string ReadLiveValue(string instruction, string context = null)
    {
        var cell = FindCell(instruction, context);
        return cell == null ? null : FindText(cell, "text-second");
    }

    // --- Tree helpers (main thread) ---

    internal static VisualElement FindByName(VisualElement root, string name, int depthCap)
    {
        if (root.name == name) return root;
        if (depthCap <= 0) return null;
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = FindByName(root.hierarchy[i], name, depthCap - 1);
            if (found != null) return found;
        }
        return null;
    }

    internal static string FindText(VisualElement root, string name, int depthCap = 8)
    {
        var el = FindByName(root, name, depthCap);
        if (el == null || el.GetIl2CppType().Name != "SIText") return null;
        var text = ServerText.Clean(el.Cast<TextElement>().text);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    internal sealed class TeamInstructionHandle : ServerHandles.Handle
    {
        public string Instruction;
        public List<Option> Options;
        public bool Remote;   // a pair half served by the editor-card routes, never the quick menu
        public string NativeContext;
        public int? NativeId;

        // Element-free by design (see FindCell): valid as long as the instruction's cell is
        // resolvable on the current screen.
        public override bool TryValidate(out string error)
        {
            var cell = FindCell(Instruction, NativeContext);
            if (cell == null)
            {
                error = "That instruction is no longer on screen. Refresh.";
                return false;
            }
            if (!cell.enabledInHierarchy) { error = "That instruction is disabled."; return false; }
            error = null;
            return true;
        }
    }

    private sealed class NativeInstructionOpenHandle : ServerHandles.Handle
    {
        internal string Instruction;
        internal string NativeContext;
        public override bool TryValidate(out string error)
        {
            error = FindCell(Instruction, NativeContext) == null ? "That instruction has changed. Refresh." : null;
            return error == null;
        }
        public override string Activate()
        {
            var cell = FindCell(Instruction, NativeContext);
            if (cell == null) return "That instruction has changed. Refresh.";
            var button = new Adapters.ButtonAdapter(cell);
            if (ServerText.IsDisabled(button)) return "That instruction is disabled.";
            button.Activate();
            return "Activated " + Instruction;
        }
    }
}
