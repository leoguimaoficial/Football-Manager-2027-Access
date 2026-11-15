using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FM27Access.Navigation;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

// FM27's native bookmark bar is separate from the inherited companion Ctrl
// shortcuts. Reuse the floating-popup, native tab and ordinary form-table HTML.
internal static class NativeBookmarkSurface
{
    internal const string WidgetName = "BookmarksPopupWidget";

    internal static string Title(SI.Bindable.SIFloatingPopupWidget widget)
    {
        if (widget?.name != WidgetName) return null;
        var title = Find(widget.m_floatingPartContent, "BookmarksText")?.TryCast<TextElement>()?.text;
        // Before the native popup has ever bound, this is a companion-only
        // purpose label. Opening never constructs or guesses native choices.
        return string.IsNullOrWhiteSpace(title) ? "Game bookmarks" : TextFilters.CleanForDisplay(title);
    }

    internal static bool TryBuild(VisualElement element, List<ScreenWalker.RenderItem> items)
    {
        if (!FilterPopupSurface.Owns(element, WidgetName)) return false;
        if (element.name == "BookmarksText" && element.GetIl2CppType().Name == "SIText")
        {
            items.Add(new ScreenWalker.StopItem { Element = new Adapters.TextAdapter(element), IsSectionTitle = true });
            return true;
        }
        if (element.name == "MainButtons")
        {
            var buttons = Visible(element, e => e.GetIl2CppType().Name == "SIButton")
                .Select(e => new Adapters.ButtonAdapter(e)).ToList();
            var selected = buttons.Select(b => b.GetState().Split(", ").Contains("selected")).ToArray();
            if (buttons.Count < 2 || selected.Count(s => s) != 1) return false;
            items.Add(new ScreenWalker.TabBarItem
            {
                Tabs = buttons.Select((button, index) => new Adapters.TabItemAdapter(button, index + 1,
                    buttons.Count, selected[index])).ToList(),
            });
            return true;
        }
        if (element.GetIl2CppType().Name != "StreamedObjectList"
            || element.name is not ("CurrenSelectedBookmarks" or "AllBookmarks")) return false;

        var rows = Visible(element, e => e.name == "EditBookmarkItemContent");
        var table = new ScreenWalker.NativeFormTableItem
        {
            Headers = element.name == "CurrenSelectedBookmarks"
                ? new[] { "Bookmark", "Move", "Bookmark bar" } : new[] { "Bookmark", "Bookmark bar" },
            RowHeader = 0,
            Rows = new(),
        };
        foreach (var row in rows)
        {
            var data = Read(row);
            if (data == null)
            {
                // Native rows can precede their binding. Keep the native text
                // and bounded retry; never issue a command without its record.
                table.Provisional = true;
                var text = Find(row, "EditBookmarkItemText")?.TryCast<TextElement>();
                table.Rows.Add(new() { new() { new ScreenWalker.NoteItem { Text = text?.text ?? "Bookmark data is loading." } } });
                continue;
            }
            var open = Visible(row, e => e.GetIl2CppType().Name == "SIButton"
                && Find(e, "EditBookmarkItemText") != null).SingleOrDefault();
            var pin = Find(row, "PinButton");
            if (open == null || pin == null) return false;
            List<ScreenWalker.RenderItem> Cell(VisualElement button, Command command) =>
                button == null ? new() : new() { new ScreenWalker.StopItem { Element = new RecordButton(button, data, command) } };
            var cells = new List<List<ScreenWalker.RenderItem>> { Cell(open, Command.Open) };
            if (table.Headers.Length == 3)
            {
                var move = Cell(Find(row, "MoveUpButton"), Command.Up);
                move.AddRange(Cell(Find(row, "MoveDownButton"), Command.Down));
                cells.Add(move);
            }
            cells.Add(Cell(pin, Command.Pin));
            table.Rows.Add(cells);
        }
        // A realized native window is not an HTML page. Use the inherited
        // native window controls rather than dropping the remaining favorites.
        ListWindowSurface.AppendChrome(items, element, ListWindowSurface.Labels.Generic);
        items.Add(table);
        return true;
    }

    internal static void AppendSignature(VisualElement element, StringBuilder signature)
    {
        if (element.name != "EditBookmarkItemContent" || !FilterPopupSurface.Owns(element, WidgetName)) return;
        var record = Read(element);
        signature.Append("bookmark:").Append(record?.Identity ?? "pending").Append('=')
            .Append(record?.Pinned).Append('|');
    }

    private static VisualElement Find(VisualElement element, string name)
    {
        if (element == null) return null;
        if (element.name == name) return element;
        for (int i = 0; i < element.hierarchy.childCount; i++)
        {
            var found = Find(element.hierarchy[i], name);
            if (found != null) return found;
        }
        return null;
    }

    private static List<VisualElement> Visible(VisualElement root, Func<VisualElement, bool> match)
    {
        var found = new List<VisualElement>();
        void Walk(VisualElement element)
        {
            if (!ElementBridge.IsVisible(element)) return;
            if (match(element)) { found.Add(element); return; }
            for (int i = 0; i < element.hierarchy.childCount; i++) Walk(element.hierarchy[i]);
        }
        Walk(root);
        return found;
    }

    internal sealed record Snapshot(SI.Core.Record Reference, string Identity, string Caption, bool Pinned, string Owner);

    private static Snapshot Read(VisualElement element)
    {
        SI.Core.Record record = null;
        VisualElement list = null;
        for (var parent = element; parent != null; parent = parent.hierarchy.parent)
        {
            if (parent.GetIl2CppType().Name == "VirtualisedList") break;
            var node = BindingRecords.NodeOf(parent);
            if (node == null) continue;
            var row = BindingRecords.FindNode(node, "row", 0);
            record = row == null ? null : BindingRecords.RecordOf(BindingRecords.ValueOf(row));
            break; // Never borrow another row from the enclosing list's bindings.
        }
        if (record == null) return null;
        for (var parent = element; parent != null; parent = parent.hierarchy.parent)
            if (parent.GetIl2CppType().Name == "StreamedObjectList") { list = parent; break; }
        if (list == null) return null;
        var fields = BindingRecords.Fields(record);
        var id = BindingRecords.TextOf(BindingRecords.Field(fields, "ID"));
        var value = BindingRecords.TextOf(BindingRecords.Field(fields, "Value"));
        var property = BindingRecords.TextOf(BindingRecords.Field(fields, "PropertyValue"));
        var caption = BindingRecords.TextOf(BindingRecords.Field(fields, "DisplayValue"));
        var pinned = BindingRecords.FlagOf(BindingRecords.Field(fields, "Ticked"));
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(property)
            || string.IsNullOrWhiteSpace(caption) || !pinned.HasValue) return null;
        return new(record, id + "|" + value + "|" + property, TextFilters.CleanForDisplay(caption),
            pinned.Value, ServerHandles.IdentityPath(list));
    }

    internal enum Command { Open, Up, Down, Pin }

    internal sealed class RecordButton : Adapters.ButtonAdapter, IStableKeyElement
    {
        private readonly Snapshot _record;
        private readonly Command _command;
        internal RecordButton(VisualElement element, Snapshot record, Command command) : base(element)
        { _record = record; _command = command; }

        protected override bool SplitsLabel => false;
        public string StableKey => "native-bookmark:" + _record.Owner + "|" + _record.Identity + "|" + _command;
        public override string GetLabel() => _command switch
        {
            Command.Up => "Move up: " + _record.Caption,
            Command.Down => "Move down: " + _record.Caption,
            Command.Pin => (_record.Pinned ? "Remove from bookmark bar: " : "Add to bookmark bar: ") + _record.Caption,
            _ => _record.Caption,
        };

        internal bool IsCurrent(out string error)
        {
            var current = FilterPopupSurface.Owns(Element, WidgetName) ? Read(Element) : null;
            bool valid = current != null && current.Reference.Pointer == _record.Reference.Pointer
                && current.Identity == _record.Identity && current.Owner == _record.Owner
                && (_command != Command.Pin || current.Pinned == _record.Pinned);
            error = valid ? null : "That bookmark changed or left this view. Refresh before trying again.";
            return valid;
        }

        public override void Activate()
        {
            if (!IsCurrent(out var error)) throw new InvalidOperationException(error);
            base.Activate();
            if (_command == Command.Open) FilterPopupSurface.CloseAfterAction(Element);
        }
    }
}
