using System;
using System.Collections.Generic;
using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// Structured readings for the match-report tiles (half-time and full-time report, and the same
/// tiles as pages of the in-match header carousel). Each of these tiles draws a genuine grid —
/// the goal/card timeline, the round's other scores, the live league table, a team's player
/// stats — but the game builds them from streamed lists of deeply nested template rows, so
/// <see cref="ScreenWalker.CollectVisibleTileLines"/>, the tile reader's generic fallback, flattens
/// them into a run of loose lines: a minute, a name, a minute, a name, with nothing tying a value
/// to its column or its team.
///
/// Each reader here recognises one tile by a stable marker element inside its content and folds it
/// into a <see cref="Grid"/> that renders as a semantic table. HTML-only, like the match-stats and
/// chart consolidations: the speech surface keeps the single whole-tile stop.
///
/// Read live per render, never cached. Decline in place: an unrecognised marker, or too few rows to
/// be the grid the reader assumed, returns null and the tile keeps its flat lines — a loose but
/// complete reading is always better than a table that dropped a row.
/// </summary>
internal static class MatchReportTiles
{
    /// <summary>Column headers plus already-cleaned cells, in column order.</summary>
    internal sealed class Grid
    {
        public string[] Headers;
        public List<List<string>> Rows;
        public List<IAccessibleElement> RowActions;
        public bool Provisional;

        // Which column names the row. Screen readers repeat the row header as the context for every
        // cell, so it has to be the cell that identifies the row (the player, the club) — not the
        // first column when that is a shirt position or a ladder rank several rows share.
        public int RowHeader;
    }

    /// <summary>
    /// The best structured reading <paramref name="content"/> supports, or null when it is none of
    /// the known tiles. Ordered most- to least-specific: the lineup reader keys off the plainest
    /// marker, so it is tried last.
    /// </summary>
    public static Grid TryRead(VisualElement content)
    {
        return TryReadMatchEvents(content)
            ?? TryReadLatestScores(content)
            ?? TryReadLiveTable(content)
            ?? TryReadCompactLiveTable(content)
            ?? TryReadLineup(content);
    }

    // --- Match Events ---

    // The viewer's Events mode groups one list per team instead of placing
    // both teams in a two-slot report row. Keep that existing native grouping,
    // the incident/replay action, icon meaning and scorer/assist relationship.
    internal static Grid TryReadTeamIncidentList(VisualElement list)
    {
        var owner=list.parent;
        for(int i=0;i<8&&owner!=null&&owner.name!="pitchEventsView";i++)owner=owner.parent;
        if(owner?.name!="pitchEventsView")return null;
        var buttons=new List<VisualElement>();
        CollectDescendantsNamed(list,"SIButtonGoToIncident",buttons);
        if(buttons.Count==0)return null;
        var rows=new List<List<string>>();var actions=new List<IAccessibleElement>();bool provisional=false;
        foreach(var button in buttons)
        {
            var image=ScreenWalker.FindDescendantNamed(button,"IncidentImage");
            var minute=ReadNamedText(button,"IncidentTime");
            var player=ReadNamedText(button,"Player1Name");
            if(image==null||minute==null||player==null)return null;
            var type=MatchIncidentLabel.FromSprite(SpriteHelper.ReadSpriteName(image));
            if(type==null){provisional=true;type=Degrade.Value("viewer incident icon unreadable");}
            var people=new List<string>{player};var second=ReadNamedText(button,"Player2Name");
            if(second!=null)people.Add(second);
            rows.Add(new(){minute,type,MatchIncidentLabel.FormatPlayers(type,people)});
            actions.Add(new ButtonAdapter(button));
        }
        return new(){Headers=new[]{"Min","Event","Player"},Rows=rows,RowHeader=2,RowActions=actions,Provisional=provisional};
    }

    // The goal/card timeline. Rows live under "ListParent"; each incident is a button holding
    // exactly two content slots, home first and away second (the same left-to-right order the
    // "TeamsBlock" header above them draws the two clubs in). The incident's texts sit in whichever
    // slot its team owns and the other stays empty, so the occupied slot's index IS the team
    // attribution — the one thing the flat line read threw away. The button is
    // "SIButtonGoToIncident" on the live reports (it jumps the match to the incident) and
    // "SIButtonGoToPastMatch" on a finished match's report (opened from the fixture list); the two
    // slots are the same.
    private static readonly string[] IncidentButtonNames = { "SIButtonGoToIncident", "SIButtonGoToPastMatch" };

    private static Grid TryReadMatchEvents(VisualElement content)
    {
        var listParent = ScreenWalker.FindDescendantNamed(content, "ListParent");
        if (listParent == null) return null;

        var teams = ReadTeamsBlock(content);
        var incidents = new List<VisualElement>();
        CollectDescendantsNamed(listParent, IncidentButtonNames, incidents);

        var rows = new List<List<string>>();
        var actions = new List<IAccessibleElement>();
        bool provisional=false;
        foreach (var incident in incidents)
        {
            var texts = new List<string>();
            string eventType = null;
            int slot = -1;
            bool ambiguous = false;
            for (int i = 0; i < incident.hierarchy.childCount; i++)
            {
                var candidate = incident.hierarchy[i];
                if (!ElementBridge.IsVisible(candidate)) continue;
                var slotTexts = new List<string>();
                CollectTexts(candidate, slotTexts);
                if (slotTexts.Count == 0) continue;
                if (slot >= 0) { ambiguous = true; break; }
                slot = i;
                texts = slotTexts;
                // FM27's home-side incident branch leaves the same leading
                // SIImage unnamed; away-side rows retain IncidentImage.
                var image = ScreenWalker.FindDescendantNamed(candidate, "IncidentImage")
                    ?? FindTyped(candidate, "SIImage");
                eventType = MatchIncidentLabel.FromSprite(SpriteHelper.ReadSpriteName(image));
            }
            if (texts.Count == 0) continue;

            // Both slots filled means the layout is not the two-sided one this reader understands,
            // so the team is genuinely unknown for this incident. The minute and player are still
            // true, so the row stays with an honest marker in the team cell rather than vanishing.
            string team = ambiguous
                ? Degrade.Value($"[MatchReportTiles] incident \"{texts[0]}\" filled both team slots; team unknown")
                : (slot == 0 ? teams.Home : teams.Away);

            string minute = texts[0];
            if(eventType==null)provisional=true;
            eventType ??= Degrade.Value($"[MatchReportTiles] incident \"{minute}\" type unreadable");
            var players = texts.Count > 1 ? texts.GetRange(1, texts.Count - 1) : new List<string>();
            string who = MatchIncidentLabel.FormatPlayers(eventType, players);
            rows.Add(new List<string> { minute, team, eventType, who });
            actions.Add(new Adapters.ButtonAdapter(incident));
        }

        // A goalless, cardless match draws an empty list; one incident is still worth a table.
        if (rows.Count == 0) return null;
        return new Grid { Headers = new[] { "Min", "Team", "Event", "Player" }, Rows = rows, RowActions = actions, Provisional=provisional };
    }

    // The two club names above the incident list, home first. Falls back to the positional truth
    // when a name will not read: which side an incident sits on is certain even when the badge
    // text is not, the same convention HtmlRenderer.RenderCarouselTile uses for its stat columns.
    private static (string Home, string Away) ReadTeamsBlock(VisualElement content)
    {
        var block = ScreenWalker.FindDescendantNamed(content, "TeamsBlock");
        if (block == null) return ("Home", "Away");

        var clubs = new List<VisualElement>(2);
        CollectDescendantsNamed(block, "Club", clubs);
        // FM27's same two-sided header names these native links ClubLink.
        if (clubs.Count == 0) CollectDescendantsNamed(block, "ClubLink", clubs);
        var names = new List<string>(2);
        foreach (var club in clubs)
        {
            var t = CleanOrNull(ScreenWalker.ReadFirstText(club));
            if (t != null) names.Add(t);
        }
        return (names.Count > 0 ? names[0] : "Home", names.Count > 1 ? names[1] : "Away");
    }

    // --- Latest Scores ---

    // The round's other fixtures — the report tile (rows under "main-container") and the in-match
    // widget page (rows under the tile's own streamed list) share the row template. Each
    // "Content_Horizontal" row holds a home block, one of three mutually exclusive middle slots
    // (a final score, a live score, or the kick-off time), and an away block. Both team texts are
    // named "HomeTeam" in the template, so the blocks — not the text names — are what tells the
    // two sides apart. A single row is accepted (one game in progress is a normal day; the
    // home/away block pair is signature enough that no other tile's rows resolve here). The
    // widget heads each competition's rows with a "CompName" header; when any row resolves one, a
    // Competition column leads the grid so a multi-competition day keeps each score attributed —
    // as loose lines that name used to float above the run of team names.
    private static Grid TryReadLatestScores(VisualElement content)
    {
        var main = ScreenWalker.FindDescendantNamed(content, "main-container") ?? content;

        var rowEls = new List<VisualElement>();
        CollectDescendantsNamed(main, "Content_Horizontal", rowEls);
        if (rowEls.Count == 0) return null;

        var rows = new List<List<string>>();
        var comps = new List<string>();
        bool anyComp = false;
        foreach (var rowEl in rowEls)
        {
            string home = ReadNamedText(rowEl, "BindingVariables_Home");
            string away = ReadNamedText(rowEl, "BindingVariables_Away");
            string score = ReadNamedText(rowEl, "MatchFinishedTextVisible")
                ?? ReadNamedText(rowEl, "MatchPlayingVisible")
                ?? ReadNamedText(rowEl, "MatchWillBePlayedVisible");
            if (home == null && away == null) continue;
            string comp = CompetitionFor(rowEl, content);
            anyComp |= comp != null;
            comps.Add(comp);
            rows.Add(new List<string> { home ?? "", score ?? "", away ?? "" });
        }

        if (rows.Count == 0) return null;
        if (anyComp)
        {
            for (int i = 0; i < rows.Count; i++)
                rows[i].Insert(0, comps[i] ?? "");
            return new Grid { Headers = new[] { "Competition", "Home", "Score", "Away" }, Rows = rows, RowHeader = 1 };
        }
        return new Grid { Headers = new[] { "Home", "Score", "Away" }, Rows = rows };
    }

    // The competition a fixture row belongs to: the widget lays each competition out as a header
    // holding a "CompName" text followed by that competition's row list under one shared parent,
    // so the nearest preceding-sibling subtree with a CompName — searched at each ancestor level
    // up to the tile content — is the row's header. Null when the tile has none (the report
    // round tile), which drops the column entirely. Pointer comparison, never reference equality,
    // for the IL2CPP wrappers.
    private static string CompetitionFor(VisualElement rowEl, VisualElement content)
    {
        var node = rowEl;
        while (node != null && node.Pointer != content.Pointer)
        {
            var parent = node.hierarchy.parent;
            if (parent == null) break;
            int idx = -1;
            int n = parent.hierarchy.childCount;
            for (int i = 0; i < n; i++)
                if (parent.hierarchy[i].Pointer == node.Pointer) { idx = i; break; }
            for (int i = idx - 1; i >= 0; i--)
            {
                var header = ScreenWalker.FindDescendantNamed(parent.hierarchy[i], "CompName");
                if (header != null) return CleanOrNull(ScreenWalker.ReadFirstText(header));
            }
            node = parent;
        }
        return null;
    }

    // --- Live Table ---

    // The league table as it stands after this result. Each "RowContent" carries the position
    // ("PositionBlock", whose movement arrow is decoration), the club ("TeamName") and the points
    // cell ("CellBlock"). The Match Momentum tile also draws a "ContentBlock", but holds no
    // RowContent rows, so it declines here and keeps its chart legend lines.
    private static Grid TryReadLiveTable(VisualElement content)
    {
        var block = ScreenWalker.FindDescendantNamed(content, "ContentBlock");
        if (block == null) return null;

        var rowEls = new List<VisualElement>();
        CollectDescendantsNamed(block, "RowContent", rowEls);
        if (rowEls.Count < 2) return null;

        var rows = new List<List<string>>();
        var actions = new List<IAccessibleElement>();
        foreach (var rowEl in rowEls)
        {
            string pos = ReadNamedText(rowEl, "PositionBlock");
            // FM27's shared live-table row moved the club caption into its
            // TeamBlock link. Read only this row's native team slot; position
            // and points are siblings and must never become a club name.
            string team = ReadNamedText(rowEl, "TeamName") ?? ReadNamedText(rowEl, "TeamBlock");
            string pts = ReadNamedText(rowEl, "CellBlock");
            if (pos == null && team == null) continue;
            rows.Add(new List<string> { pos ?? "", team ?? "", pts ?? "" });
            var teamBlock = ScreenWalker.FindDescendantNamed(rowEl, "TeamBlock");
            var caption = teamBlock == null ? null : ScreenWalker.FindDescendantNamed(teamBlock, "primary-text");
            var parts = caption == null ? null : SITextLink.ReadParts(caption);
            actions.Add(parts?.Count == 1 && parts[0].CanActivate ? parts[0] : null);
        }

        if (rows.Count < 2) return null;
        return new Grid { Headers = new[] { "Pos", "Team", "Points" }, Rows = rows, RowHeader = 1, RowActions = actions };
    }

    // FM27's dugout uses a different compact native row from the interval
    // report above. Its four cells are rank, linked team, played and points.
    // Retain the original Grid/row action and bounded list-window expansion.
    // The named BindingExpect is the row's binding marker; its parent is the
    // actual instantiated template, not an absolute index in a pooled list.
    internal static Grid TryReadCompactLiveTable(VisualElement content)
    {
        var markers = new List<VisualElement>();
        CollectDescendantsNamed(content, "CompetitionsLeagueTableStagesRow_Short", markers);
        if (markers.Count < 2) return null;
        var rows = new List<List<string>>();
        var actions = new List<IAccessibleElement>();
        var seen = new HashSet<IntPtr>();
        foreach (var marker in markers)
        {
            // An outer streamed list may contain several independent cards.
            // Only its own rows belong in this grid; nested lists retain their
            // competition headings and are read when the walk reaches them.
            if (content.GetIl2CppType().Name is "StreamedObjectList" or "StreamedObjectListWithChildren")
                for (var owner = marker.parent; owner != null && owner.Pointer != content.Pointer; owner = owner.parent)
                    if (owner.GetIl2CppType().Name is "StreamedObjectList" or "StreamedObjectListWithChildren")
                        return null;
            var row = marker.parent;
            if (row == null || !seen.Add(row.Pointer)) return null;
            var cells = new List<string>();
            CollectTexts(row, cells);
            // A changed/partly bound template keeps the complete ordinary
            // reading rather than shifting figures under invented columns.
            if (cells.Count != 4) return null;
            var caption = ScreenWalker.FindDescendantNamed(row, "primary-text");
            if (caption == null || CleanOrNull(ScreenWalker.ReadFirstText(caption)) != cells[1]) return null;
            rows.Add(cells);
            var parts = SITextLink.ReadParts(caption);
            actions.Add(parts?.Count == 1 && parts[0].CanActivate ? parts[0] : null);
        }
        return new Grid
        {
            Headers = new[] { "Pos", "Team", "Played", "Points" },
            Rows = rows, RowHeader = 1, RowActions = actions,
        };
    }

    // --- Per-team player stats ---

    // A team's players with their match ratings. Every row is one template whose direct children
    // are the named cells; "Position" holds either the starting-XI position or the substitute's
    // bench number, and "Subbed" the minute of the swap (empty for a player who played the whole
    // match). The tile draws its own header strip for four of the six columns; the strip is a
    // sibling of the row list and is replaced by this table's headers.
    private static readonly string[] LineupCells = { "Position", "Name", "Subbed", "Goals", "Assists", "Rating" };

    private static Grid TryReadLineup(VisualElement content)
    {
        var rowEls = new List<VisualElement>();
        CollectLineupRows(content, rowEls);
        if (rowEls.Count < 2) return null;

        var rows = new List<List<string>>();
        foreach (var rowEl in rowEls)
        {
            var cells = new List<string>(LineupCells.Length + 1);
            bool any = false;
            foreach (var cellName in LineupCells)
            {
                string text = cellName switch
                {
                    "Subbed" => ReadSubbed(rowEl),
                    "Goals" or "Assists" => ReadNamedText(rowEl, cellName) ?? ReadTallyIcons(rowEl, cellName),
                    _ => ReadNamedText(rowEl, cellName),
                };
                if (text != null) any = true;
                cells.Add(text ?? "");
            }
            cells.Add(ReadCard(rowEl) ?? "");
            if (any) rows.Add(cells);
        }

        if (rows.Count < 2) return null;
        return new Grid
        {
            Headers = new[] { "Pos", "Name", "Sub", "Gls", "Ast", "Rat", "Card" },
            Rows = rows,
            RowHeader = 1,
        };
    }

    // The minute of the swap beside an arrow icon whose sprite says which way the player went
    // ("sub off" on a starter's row, "sub on" on a substitute's; verified 2026-10-05). The minute
    // alone left the direction to be inferred from the Pos column.
    private static string ReadSubbed(VisualElement row)
    {
        var cell = ScreenWalker.FindDescendantNamed(row, "Subbed");
        if (cell == null) return null;
        string minute = CleanOrNull(ScreenWalker.ReadFirstText(cell));
        if (minute == null) return null;
        var icon = FindTyped(cell, "SIImage");
        string label = icon == null ? null : MatchIncidentLabel.FromSprite(SpriteHelper.ReadSpriteName(icon));
        return (label ?? Degrade.Value($"[MatchReportTiles] substitution at {minute}: direction icon unreadable")) + " " + minute;
    }

    // A booked or sent-off player's name switches to a second template: the name inside one of a
    // pair of SIVisible slots, the first for a yellow card and the second for a red, drawn in the
    // card's colour. Everyone else's is a bare SIText. Verified 2026-10-05 across 28 reports: every
    // booked name sat in slot 0 drawn yellow (0.97, 1, 0.07); the one sent-off name (a straight red,
    // matching a "Red card" incident) sat in slot 1 drawn red (1, 0.26, 0.39). The slot decides and
    // the colour must agree; anything else is a card this reader cannot name, and says so.
    private static string ReadCard(VisualElement row)
    {
        var cell = ScreenWalker.FindDescendantNamed(row, "Name");
        if (cell == null || cell.hierarchy.childCount == 0) return null;
        var template = cell.hierarchy[0];
        if (template.GetIl2CppType().Name == "SIText") return null;

        int slot = -1;
        VisualElement text = null;
        for (int i = 0; i < template.hierarchy.childCount; i++)
        {
            var found = FindTyped(template.hierarchy[i], "SIText");
            if (found == null) continue;
            if (text != null) return Degrade.Value("[MatchReportTiles] name drawn in both card slots; card unread");
            slot = i;
            text = found;
        }
        if (text == null) return null;

        var c = text.resolvedStyle.color;
        bool yellow = c.r > 0.8f && c.g > 0.8f && c.b < 0.4f;
        bool red = c.r > 0.8f && c.g < 0.5f && c.b < 0.6f;
        if (slot == 0 && yellow) return "Yellow";
        if (slot == 1 && red) return "Red";
        return Degrade.Value($"[MatchReportTiles] name in card slot {slot} drawn {c}; card unread");
    }

    private static VisualElement FindTyped(VisualElement root, string typeName)
    {
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = root.hierarchy[i];
            if (c.GetIl2CppType().Name == typeName) return c;
            var found = FindTyped(c, typeName);
            if (found != null) return found;
        }
        return null;
    }

    // A lineup row is the element owning both a "Position" and a "Name" child. Both names are
    // common enough on their own that either alone would match unrelated templates; together, and
    // as DIRECT children, they only occur on this row.
    private static void CollectLineupRows(VisualElement root, List<VisualElement> into)
    {
        int n = root.hierarchy.childCount;
        bool hasPosition = false, hasName = false;
        for (int i = 0; i < n; i++)
        {
            var childName = root.hierarchy[i].name;
            if (childName == "Position") hasPosition = true;
            else if (childName == "Name") hasName = true;
        }
        if (hasPosition && hasName)
        {
            if (ElementBridge.IsVisible(root)) into.Add(root);
            return;
        }
        for (int i = 0; i < n; i++)
            CollectLineupRows(root.hierarchy[i], into);
    }

    // --- Formation pitch ---

    // A team's shape drawn on a pitch (the report's lineup carousels, the in-match header's
    // formation pages): one FMPitchIndicator per player, carrying the role ("PositionContainer"),
    // the name ("Player"), the match rating ("Rating") and the shirt number. The read keeps each
    // player whole on one row; a loose "GK, Mollo, 6.2, 1" line never said which number was the
    // rating and which the shirt. An indicator with no player name is an empty pitch slot.
    //
    // Under the shirt sit the icon slots the sighted player reads at a glance: each is an SIVisible
    // that holds its glyph only while the flag is on (verified for GoalVisible: present on every
    // scorer's indicator, absent on everyone else's, one glyph however many goals — so it says
    // "scored", never a count; the lineup page's Gls column and Match Events carry the tally).
    private static readonly (string Slot, string Note)[] FormationFlags =
    {
        ("GoalVisible", "scored"),
        ("RedCardVisible", "sent off"),
        ("injuryvisible", "injured"),
        ("SubVisible", "substituted"),
    };

    internal static Grid TryReadFormation(VisualElement content)
    {
        var indicators = new List<VisualElement>();
        CollectTyped(content, "FMPitchIndicator", indicators);
        if (indicators.Count == 0) return null;

        var rows = new List<List<string>>();
        foreach (var ind in indicators)
        {
            if (!ElementBridge.IsVisible(ind)) continue;
            string name = ReadNamedText(ind, "Player");
            if (name == null) continue;

            var notes = new List<string>();
            foreach (var (slot, note) in FormationFlags)
            {
                var flag = ScreenWalker.FindDescendantNamed(ind, slot);
                if (flag != null && flag.hierarchy.childCount > 0 && ElementBridge.IsVisible(flag.hierarchy[0]))
                    notes.Add(slot switch
                    {
                        "injuryvisible" => InjuryLevel(flag) ?? note,
                        // The arrow's sprite says which way ("sub on" on every flagged player of a
                        // post-match pitch, verified 2026-10-05: the pitch shows who finished).
                        "SubVisible" => MatchIncidentLabel.FromSprite(FirstSprite(flag)) ?? note,
                        _ => note,
                    });
            }
            rows.Add(new List<string>
            {
                ReadNamedText(ind, "PositionContainer") ?? "",
                name,
                ReadNamedText(ind, "Rating") ?? "",
                ReadNamedText(ind, "fm-tactics-shirt-number") ?? "",
                string.Join(", ", notes),
            });
        }

        if (rows.Count == 0) return null;
        return new Grid { Headers = new[] { "Pos", "Name", "Rat", "No.", "Notes" }, Rows = rows, RowHeader = 1 };
    }

    // The injury glyph's sprite names its severity: "level=slightly injured, size=small 16px_2x",
    // "level=injured, ..." (both seen across 28 reports, 2026-10-05). The level is the game's own
    // wording; a sprite without one leaves the plain "injured" the slot itself proves.
    private static readonly System.Text.RegularExpressions.Regex InjurySprite =
        new(@"level=(?<level>[a-z ]+)", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string InjuryLevel(VisualElement flag)
    {
        var m = InjurySprite.Match(FirstSprite(flag) ?? "");
        return m.Success ? m.Groups["level"].Value.Trim() : null;
    }

    private static void CollectTyped(VisualElement root, string typeName, List<VisualElement> into)
    {
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = root.hierarchy[i];
            if (c.GetIl2CppType().Name == typeName) { into.Add(c); continue; }
            CollectTyped(c, typeName, into);
        }
    }

    // --- shared reads ---

    // A goals/assists cell that drew no text. A player who scored gets no number at all: the cell
    // switches to ONE ball glyph whose sprite carries the count as a subscript — "goal-1_2x",
    // "goal-2_2x" (verified 2026-10-05 against two two-goal scorers and four one-goal scorers; the
    // glyph count is always one, so counting glyphs under-reported every brace). A player with none
    // keeps the game's "-" text and never reaches here. A sprite not of that shape is a tally this
    // reader cannot read, and a wrong goal tally is exactly the plausible-but-wrong number the mod
    // must never state; Match Events above lists every goal by minute and scorer.
    private static readonly System.Text.RegularExpressions.Regex TallySprite =
        new(@"^(?<kind>[a-z]+)-(?<n>\d+)(_\d+x)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string ReadTallyIcons(VisualElement row, string cellName)
    {
        var el = ScreenWalker.FindDescendantNamed(row, cellName);
        if (el == null) return null;
        string sprite = FirstSprite(el);
        if (sprite == null) return null;
        var m = TallySprite.Match(sprite);
        string kind = cellName == "Goals" ? "goal" : "assist";
        if (m.Success && m.Groups["kind"].Value == kind) return m.Groups["n"].Value;
        return Degrade.Value($"[MatchReportTiles] {cellName} cell drew sprite \"{sprite}\" and no number; tally unread");
    }

    private static string FirstSprite(VisualElement el)
    {
        if (!ElementBridge.IsVisible(el)) return null;
        string sprite = SpriteHelper.ReadSpriteName(el);
        if (sprite != null) return sprite;
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var found = FirstSprite(el.hierarchy[i]);
            if (found != null) return found;
        }
        return null;
    }

    // The cleaned text of the named sub-element's first text leaf, or null when the element is
    // absent or draws nothing (an unused cell, a player with no rating).
    private static string ReadNamedText(VisualElement row, string name)
    {
        var el = ScreenWalker.FindDescendantNamed(row, name);
        return el != null ? CleanOrNull(ScreenWalker.ReadFirstText(el)) : null;
    }

    private static void CollectTexts(VisualElement el, List<string> texts)
    {
        if (!ElementBridge.IsVisible(el)) return;
        if (el.GetIl2CppType().Name == "SIText")
        {
            var t = CleanOrNull(ScreenWalker.ReadFirstText(el));
            if (t != null) texts.Add(t);
            return;
        }
        int n = el.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            CollectTexts(el.hierarchy[i], texts);
    }

    private static void CollectDescendantsNamed(VisualElement root, string name, List<VisualElement> into)
        => CollectDescendantsNamed(root, new[] { name }, into);

    private static void CollectDescendantsNamed(VisualElement root, string[] names, List<VisualElement> into)
    {
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var c = root.hierarchy[i];
            if (!ElementBridge.IsVisible(c)) continue;
            if (Array.IndexOf(names, c.name) >= 0) { into.Add(c); continue; }
            CollectDescendantsNamed(c, names, into);
        }
    }

    private static string CleanOrNull(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = TextFilters.CleanForDisplay(ServerText.DecodeInlineEntityLinks(raw));
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
