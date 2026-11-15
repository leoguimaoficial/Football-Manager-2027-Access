using FM27Access.Navigation;
using UnityEngine.UIElements;
using Panel = SI.Bindable.Panel;

namespace FM27Access.Plugin.Server;

// Dashboard and full-Inbox searches have different native filter owners. Use
// verified native Back for the former. A full-Inbox profile return can rebuild
// its search; restore that actual native disclosure/query once for the explicit
// history command. No list is fabricated and no history node is written.
internal static class MessageSearchReturn
{
    internal sealed record Origin(IntPtr Manager, int Index,
        ReplacedMessageHistory.PanelHistorySnapshot Visit, Adapters.TextFieldAdapter Field,
        string Query, int MessageId, bool SameInbox);

    internal static bool IsQueryField(VisualElement element)
    {
        if (element?.GetIl2CppType().Name != "BindableTextEditBox") return false;
        for (var parent = element.hierarchy.parent; parent != null; parent = parent.hierarchy.parent)
        {
            if (parent.name == "MessageFilterVisible") return true;
            if (parent.GetIl2CppType().Name == "Panel") break;
        }
        return false;
    }

    internal static string QueryOpenerId(VisualElement field)
    {
        if (!IsQueryField(field)) return null;
        // Resolve the paired native SearchButton inside the smallest containing
        // component. Never borrow another tile's search or a translated caption.
        for (var parent = field.hierarchy.parent; parent != null; parent = parent.hierarchy.parent)
        {
            if (parent.GetIl2CppType().Name == "Panel") break;
            var buttons = new List<VisualElement>();
            FindOpeners(parent, buttons);
            if (buttons.Count > 1) return null;
            if (buttons.Count == 1)
                return ServerHandles.RenderedNativeId(buttons[0]);
        }
        return null;
    }

    private static void FindOpeners(VisualElement element, List<VisualElement> buttons)
    {
        if (!ElementBridge.IsVisibleInHierarchy(element)) return;
        if (element.name == "SearchButton" && element.GetIl2CppType().Name == "SIButton") buttons.Add(element);
        for (int i = 0; i < element.hierarchy.childCount; i++) FindOpeners(element.hierarchy[i], buttons);
    }

    internal static Origin Capture(Panel panel, int? messageId)
    {
        if (panel == null || !messageId.HasValue) return null;
        var h = GameHistory.TryRead();
        var manager = FM.UI.PanelHistoryManagerModule.Instance?.PanelHistoryManager;
        if (h == null || manager == null || manager.m_ignoreEvents || h.Value.Index >= manager.m_history.Count) return null;
        var visit = new ReplacedMessageHistory.PanelHistorySnapshot(manager.m_history[h.Value.Index]);
        if (!visit.Matches(panel)) return null;
        var fields = new List<Adapters.TextFieldAdapter>();
        Collect(panel, fields);
        if (fields.Count != 1 || string.IsNullOrWhiteSpace(fields[0].GetCurrentText())) return null;
        return new(manager.Pointer, h.Value.Index, visit, fields[0], fields[0].GetCurrentText(),
            messageId.Value, panel.name == "PortalMessagesTool");
    }

    private static void Collect(VisualElement element, List<Adapters.TextFieldAdapter> fields)
    {
        foreach (var field in NativeTreeQuery.All<SI.Bindable.BindableTextEditBox>(element))
            if (ElementBridge.IsVisibleInHierarchy(field) && IsQueryField(field))
                fields.Add(new Adapters.TextFieldAdapter(field));
    }

    internal static void Return(Origin origin, int? messageId)
    {
        if (origin == null) return;
        // The full Inbox is already the destination. Its search is restored
        // during a verified native history return, not by an extra Back here.
        if (origin.SameInbox) return;
        var h = GameHistory.TryRead();
        var manager = FM.UI.PanelHistoryManagerModule.Instance?.PanelHistoryManager;
        if (h == null || manager == null || manager.Pointer != origin.Manager || manager.m_ignoreEvents
            || h.Value.Index != origin.Index + 1 || !h.Value.CanBack
            || origin.Index >= manager.m_history.Count || !origin.Visit.Matches(manager.m_history[origin.Index])
            || messageId != origin.MessageId || origin.Field.BackingElement.panel == null
            || origin.Field.GetCurrentText() != origin.Query)
            throw new InvalidOperationException("The original search changed. The message was closed in the Inbox.");
        var error = NavigationManager.RequestNavigateBack();
        if (error != null) throw new InvalidOperationException("The original search could not be reopened: " + error);
    }

    // Request-thread continuation of ONE explicit history command. FM27 rebuilds
    // the full Inbox with its search collapsed on profile Back. Restore the
    // actual native disclosure/query, never cached result HTML. Every mutation
    // is sent at most once, and every stage rechecks the exact history visit.
    internal static void RestoreInboxSearch(Origin origin, int? messageId, long deadline, Func<bool> superseded)
    {
        if (origin?.SameInbox != true || messageId != origin.MessageId) return;
        bool opened = false, committed = false;
        while (!superseded())
        {
            bool done = MainThreadDispatcher.Invoke(() =>
            {
                if (superseded()) return true;
                if (Environment.TickCount64 >= deadline)
                    throw new InvalidOperationException("The native message search restoration expired.");
                var h = GameHistory.TryRead();
                var manager = FM.UI.PanelHistoryManagerModule.Instance?.PanelHistoryManager;
                var panel = FM27PanelCatalog.GetForegroundPanel();
                if (h == null || manager == null || manager.Pointer != origin.Manager || manager.m_ignoreEvents
                    || h.Value.Index != origin.Index || origin.Index >= manager.m_history.Count
                    || !origin.Visit.Matches(manager.m_history[origin.Index]) || !origin.Visit.Matches(panel))
                    throw new InvalidOperationException("The original message search visit changed.");
                var fields = new List<Adapters.TextFieldAdapter>();
                Collect(panel, fields);
                if (fields.Count > 1) throw new InvalidOperationException("The original message search is ambiguous.");
                if (fields.Count == 0)
                {
                    if (!opened)
                    {
                        var buttons = new List<VisualElement>();
                        FindOpeners(panel, buttons);
                        if (buttons.Count != 1) throw new InvalidOperationException("The native message search opener is unavailable.");
                        var button = new Adapters.ButtonAdapter(buttons[0]);
                        if (!button.CanActivate) throw new InvalidOperationException("The native message search opener is disabled.");
                        if (superseded()) return true;
                        opened = true;
                        button.Activate();
                    }
                    return false;
                }
                string query = fields[0].GetCurrentText();
                if (query == origin.Query) return true;
                if (!string.IsNullOrEmpty(query))
                    throw new InvalidOperationException("The native message search query changed.");
                if (!committed)
                {
                    if (superseded()) return true;
                    if (!fields[0].BackingElement.enabledInHierarchy)
                        throw new InvalidOperationException("The native message search field is disabled.");
                    committed = true;
                    if (!fields[0].SetText(origin.Query))
                        throw new InvalidOperationException("The native message search query could not be restored.");
                }
                return false;
            });
            if (done) return;
            if (Environment.TickCount64 >= deadline)
                throw new InvalidOperationException("The native message search restoration was not observed.");
            Thread.Sleep(40);
        }
    }
}
