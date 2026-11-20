using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

// Current FM27 setup uses ordinary UXML cells rather than FM26's named
// league grid. Keep the original native form-table renderer and cell actions.
internal static class GameCreationSummary
{
    private static IEnumerable<VisualElement> Children(VisualElement element)
    {
        for (int i = 0; i < element.hierarchy.childCount; i++)
            if (ElementBridge.IsVisible(element.hierarchy[i])) yield return element.hierarchy[i];
    }

    private static List<VisualElement> Find(VisualElement root, Func<VisualElement, bool> match)
    {
        var result = new List<VisualElement>();
        if (!ElementBridge.IsVisible(root)) return result;
        if (match(root)) { result.Add(root); return result; }
        foreach (var child in Children(root)) result.AddRange(Find(child, match));
        return result;
    }

    private static VisualElement Owner(VisualElement element, string name)
    {
        for (var current = element; current != null; current = current.parent)
            if (current.name == name) return current;
        return null;
    }

    private static bool IsUnder(VisualElement element, VisualElement owner)
    {
        for (var current = element; current != null; current = current.parent)
            if (current.Pointer == owner.Pointer) return true;
        return false;
    }

    private static List<VisualElement> Texts(VisualElement root) => Find(root,
        e => e.TryCast<TextElement>() is { } text && !string.IsNullOrWhiteSpace(text.text));
    private static string Text(VisualElement root) => string.Join(" ", Texts(root)
        .Select(e => ServerText.Clean(e.Cast<TextElement>().text)));

    internal static ScreenWalker.NativeFormTableItem TryTable(VisualElement root,
        Func<VisualElement, List<ScreenWalker.RenderItem>> walk)
    {
        bool summary = root.name == "short-summary-table"
            && Owner(root, "GameCreationGameSummaryScreen") != null;
        bool editor = !summary && Owner(root, "AddRemoveLeaguesCard") != null;
        if (!summary && !editor) return null;
        var children = Children(root).ToList();
        var lists = children.Where(e => e.GetIl2CppType().Name == "StreamedObjectList").ToList();
        if (lists.Count != 1 || children.Count != 2) return null;
        var list = lists[0]; var header = children.First(e => e.Pointer != list.Pointer);
        var headerTexts = Texts(header);
        var headers = headerTexts.Select(e => ServerText.Clean(e.Cast<TextElement>().text)).ToArray();
        if (headers.Length != 5 || Find(header, e => e.name == "frame-header-men-setting").Count != 1
            || Find(header, e => e.name == "frame-header-women-setting").Count != 1) return null;
        var rows = summary ? Find(list, e => e.name == "league-row")
            : Find(list, e => Children(e).Any(c => c.name == "league-nation"));
        var nativeLists = Find(list, e => e.GetIl2CppType().Name == "VirtualisedList");
        // Never present just the realised window as a complete table. The
        // ordinary walker retains its established paging/realisation fallback.
        if (nativeLists.Count != 1 || rows.Count == 0 || rows.Count > StreamedTableLoader.PageRows
            || ListWindowSurface.ReadItemCount(nativeLists[0].TryCast<SI.UI.VirtualisedList>()) != rows.Count)
            return null;
        var owner = Owner(root, summary ? "DatabaseSummary" : "AddRemoveLeaguesCard");
        if (owner == null) return null;
        var plan = new List<List<List<ScreenWalker.RenderItem>>>();
        foreach (var row in rows)
        {
            var cells = Children(row).ToList();
            if (summary && cells.Count == 1) cells = Children(cells[0]).ToList();
            var names = summary
                ? new[] { "league-nation", "league-setting-men", "league-men", "league-setting-women", "league-women" }
                : new[] { "league-nation", "league-men", "league-women", "league-remove" };
            if (!cells.Select(e => e.name).SequenceEqual(names)) return null;
            if (summary)
            {
                var rendered = cells.Select(walk).ToList();
                foreach (int index in new[] { 1, 3 })
                {
                    var styles = Find(cells[index], e => e.GetIl2CppType().Name == "SIStyleSetter");
                    if (styles.Count == 0 && Texts(cells[index]).Count == 0) continue;
                    if (styles.Count != 1) return null;
                    var value = new LegendValue(styles[0], owner);
                    if (string.IsNullOrWhiteSpace(value.GetLabel())) return null;
                    rendered[index] = new() { new ScreenWalker.StopItem { Element = value } };
                }
                plan.Add(rendered);
            }
            else
            {
                var nation = Texts(cells[0]).FirstOrDefault(e => e.name == "primary-text");
                if (nation == null) return null;
                var nationCell = walk(cells[0]); nationCell.AddRange(walk(cells[3]));
                var rendered = new List<List<ScreenWalker.RenderItem>> { nationCell };
                for (int side = 1; side <= 2; side++)
                {
                    string settingName = side == 1 ? "LeagueSetting-Men" : "LeagueSetting-WoMen";
                    var parts = new List<ScreenWalker.RenderItem>();
                    void WalkApart(VisualElement element)
                    {
                        if (element.name != settingName && Find(element, e => e.name == settingName).Count > 0)
                            foreach (var child in Children(element)) WalkApart(child);
                        else parts.AddRange(walk(element));
                    }
                    WalkApart(cells[side]);
                    var rule = new List<ScreenWalker.RenderItem>();
                    var setting = new List<ScreenWalker.RenderItem>();
                    foreach (var part in parts)
                    {
                        bool isSetting = part is ScreenWalker.StopItem s && s.Element is Adapters.PluginAdapterBase ad
                            && Owner(ad.BackingElement, settingName) != null;
                        if (part is ScreenWalker.StopItem stop && stop.Element is Adapters.DropdownAdapter dropdown)
                            stop.Element = new LeagueField(dropdown.BackingElement, nation,
                                headerTexts[side == 1 ? 1 : 3], isSetting ? headerTexts[side == 1 ? 2 : 4] : null);
                        (isSetting ? setting : rule).Add(part);
                    }
                    rendered.Add(rule); rendered.Add(setting);
                }
                plan.Add(rendered);
            }
        }
        return new() { Headers = headers, Rows = plan, RowHeader = 0 };
    }

    // Match actual applied icon identity to this very card's native legend.
    // Neither selected indexes nor an English enum/name map supplies the text.
    private sealed class LegendValue : Adapters.PluginAdapterBase
    {
        private readonly VisualElement owner;
        internal LegendValue(VisualElement element, VisualElement owner) : base(element) => this.owner = owner;
        public override string Role => "text";
        public override bool CanActivate => false;
        public override bool HasContextMenu => false;
        public override void Activate() => throw new NotSupportedException("Legend values are readonly.");
        public override string GetState() => "";
        public override string GetAnnouncement() => GetLabel();
        public override string GetLabel()
        {
            string style = Adapters.StyleSetterClasses.Read(Element);
            if (string.IsNullOrWhiteSpace(style)) return "";
            var matches = new List<VisualElement>();
            foreach (var legend in Find(owner, e => e.name is "playable" or "view-only"))
            {
                var styles = Find(legend, e => e.GetIl2CppType().Name == "SIStyleSetter");
                var captions = Find(legend, e => e.name == "StringValue" && e.TryCast<TextElement>() != null);
                if (styles.Count == 1 && captions.Count == 1 && Adapters.StyleSetterClasses.Read(styles[0]) == style)
                    matches.Add(captions[0]);
            }
            return matches.Count == 1 ? Text(matches[0]) : "";
        }
    }

    private sealed class LeagueField : Adapters.DropdownAdapter
    {
        private readonly VisualElement nation, league, setting;
        internal LeagueField(VisualElement element, VisualElement nation, VisualElement league, VisualElement setting)
            : base(element) { this.nation = nation; this.league = league; this.setting = setting; }
        public override string GetLabel() => string.Join(", ", new[] { Text(nation), Text(league),
            setting == null ? null : Text(setting) }.Where(s => !string.IsNullOrWhiteSpace(s)));
        public override string GetState()
        {
            var fields = Find(Element, e => e.name == "SelectionField");
            string caption = fields.Count == 1 ? Text(fields[0]) : null;
            if (string.IsNullOrWhiteSpace(caption) && setting != null && fields.Count == 1)
            {
                var styles = Find(fields[0], e => e.GetIl2CppType().Name == "SIStyleSetter");
                var owner = Owner(Element, "AddRemoveLeaguesCard");
                if (styles.Count == 1 && owner != null) caption = new LegendValue(styles[0], owner).GetLabel();
            }
            return !string.IsNullOrWhiteSpace(caption) ? caption
                : Adapters.StaticButtonCaption.ReadDropdownValue(Element.TryCast<SI.Bindable.SIDropdown>()) ?? base.GetState();
        }
    }

    internal static void Restructure(List<ScreenWalker.RenderItem> items, object panel)
    {
        if (NavigationManager.GetPanelName(panel) != "GameCreationGameSummaryScreen") return;
        VisualElement Backing(ScreenWalker.RenderItem item) =>
            (item as ScreenWalker.StopItem)?.Element is Adapters.PluginAdapterBase adapter ? adapter.BackingElement : null;
        // The two native metric rows have a caption beside their value wrapper.
        for (int i = items.Count - 2; i >= 0; i--)
        {
            var caption = Backing(items[i]); var value = Backing(items[i + 1]);
            if (caption?.TryCast<TextElement>() == null || value == null
                || Owner(caption, "DatabaseSummary") == null || Owner(value, "DatabaseSummary") == null
                || caption.parent?.Pointer != value.parent?.parent?.Pointer
                && caption.parent?.Pointer != value.parent?.parent?.parent?.Pointer) continue;
            if (((ScreenWalker.StopItem)items[i + 1]).Element.CanActivate) continue;
            var siblings = Children(caption.parent).ToList();
            if (siblings.Count != 2 || siblings[0].Pointer != caption.Pointer
                || siblings[1].GetIl2CppType().Name != "VisualElement"
                || !IsUnder(value, siblings[1])) continue;
            items[i] = new ScreenWalker.LabelValueItem { Label = Text(caption).TrimEnd(':', ' '),
                Value = ((ScreenWalker.StopItem)items[i + 1]).Element };
            items.RemoveAt(i + 1);
        }
        int club = items.FindIndex(i => Backing(i)?.name == "ClubName"
            && Owner(Backing(i), "StartingClubAndDateSummary") != null);
        if (club < 0) return;
        var owner = Owner(Backing(items[club]), "StartingClubAndDateSummary");
        int gender = items.FindIndex(i => Backing(i)?.name == "GenderText" && Owner(Backing(i), "StartingClubAndDateSummary")?.Pointer == owner.Pointer);
        int prediction = items.FindIndex(i => Backing(i)?.name == "PredictedFinish" && Owner(Backing(i), "StartingClubAndDateSummary")?.Pointer == owner.Pointer);
        if (gender == club + 1 && prediction == gender + 1)
        {
            items[gender] = new ScreenWalker.JoinedTextItem { Parts = new() {
                ((ScreenWalker.StopItem)items[gender]).Element, ((ScreenWalker.StopItem)items[prediction]).Element } };
            items.RemoveAt(prediction);
        }
        // The edit command belongs after the information it changes.
        if (club > 0 && Backing(items[club - 1]) is { } edit && Owner(edit, "ClubInfo") != null
            && ((ScreenWalker.StopItem)items[club - 1]).Element.CanActivate)
        {
            var action = items[club - 1]; items.RemoveAt(club - 1);
            int after = club;
            if (after < items.Count && items[after] is ScreenWalker.JoinedTextItem) after++;
            items.Insert(after, action);
        }
    }
}
