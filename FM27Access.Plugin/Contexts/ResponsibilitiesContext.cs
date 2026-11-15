using System;
using System.Collections.Generic;
using System.Reflection;
using FM27Access.Speech;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Contexts;

/// <summary>
/// Structural reader for the Delegation tab's responsibility groups, which the game draws as a
/// virtualised list of groups each holding its own virtualised list of duties. Only the groups
/// near the scroll position exist in the element tree — and the realised count drifts as the
/// panel settles (observed live falling 4 -> 3 -> 2 on the same section without the list ever
/// reporting an incomplete window), so a walk of the live tree renders whatever happens to be
/// materialised and silently omits the rest.
///
/// This reads the whole thing instead: scroll each group in, wait for its texts to bind, and
/// harvest it as plain data. Rows hold no element references — virtualised rows recycle — so
/// every control re-realises by (group, duty, slot) through <see cref="RealizeControl"/> at
/// action time, the protocol LeagueTableContext and ConditionTableContext already use.
///
/// Main thread only: every method here touches live VisualElements. The waiting between scrolls
/// belongs to the caller (see ResponsibilitiesLoader), which runs on the request thread so game
/// frames advance between reads.
/// </summary>
internal sealed class ResponsibilitiesContext
{
    private static PropertyInfo _indexProperty;
    private static PropertyInfo _contentProperty;
    private static MethodInfo _scrollToMethod;
    private static bool _reflectionInitialized;

    internal const string GroupNameElement = "ResponsibilityGroupName";
    internal const string AssignAllBlock = "AssignAll";
    internal const string TakeAllButton = "TakeControlAll";
    internal const string DelegateAllButton = "DelegateAll";
    internal const string DutyListName = "StreamedObjectList_IndividualResponsibilities";
    internal const string AssignDropdown = "SIDropdown_AssignableStaff";
    internal const string NoStaffBlock = "NoAssignableStaff";
    internal const string TakeControlWrapper = "SIVisible_TakeControl";
    internal const string DelegateWrapper = "SIVisible_DelegateControl";

    // Slot keys naming one control of one duty (or, with duty index -1, of the group itself).
    internal const string SlotTake = "take";
    internal const string SlotDelegate = "delegate";
    internal const string SlotAssign = "assign";
    internal const string SlotTakeAll = "takeall";
    internal const string SlotDelegateAll = "delegateall";

    private readonly VisualElement _list; // the StreamedObjectList hosting the group list

    public ResponsibilitiesContext(VisualElement list)
    {
        EnsureReflection();
        _list = list;
    }

    public VisualElement List => _list;

    private static void EnsureReflection()
    {
        if (_reflectionInitialized) return;
        _reflectionInitialized = true;

        var virtElemType = typeof(SI.UI.VirtualisedList.VirtualisedElement);
        _indexProperty = virtElemType.GetProperty("Index", BindingFlags.Public | BindingFlags.Instance);
        _contentProperty = virtElemType.GetProperty("Content", BindingFlags.Public | BindingFlags.Instance);
        if (_indexProperty == null || _contentProperty == null)
            throw new MissingMemberException("VirtualisedElement", "Index/Content");

        _scrollToMethod = typeof(SI.UI.VirtualisedList).GetMethod("ScrollTo",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            null, new Type[] { typeof(int) }, null);
        if (_scrollToMethod == null)
            throw new MissingMethodException("VirtualisedList", "ScrollTo");
    }

    // --- Data model ---

    internal sealed class Duty
    {
        public int Index;
        public string NativeIdentity;
        public string Title;
        public string Description;
        public bool HasTakeControl, TakeControlEnabled;
        public bool HasDelegate, DelegateEnabled;
        public bool HasAssign, AssignEnabled;
        public string AssignText;
        public List<string> NoStaff = new();
        // The buttons' own captions, read live so the table never restates game text as a mod
        // constant (which would go stale across updates and never translate).
        public string TakeControlLabel, DelegateLabel;
    }

    internal sealed class Group
    {
        public int Index;
        public string NativeIdentity;
        public string Name;
        public bool HasTakeAll, TakeAllEnabled;
        public bool HasDelegateAll, DelegateAllEnabled;
        public string TakeAllLabel, DelegateAllLabel;
        public List<Duty> Duties = new();
        // Set once two consecutive reads agreed on every control's enabled state. A group scrolled
        // in this frame can report a control disabled purely because it has not re-enabled yet, and
        // "both refused" is the one reading the table turns into a claim ("Cannot be changed"), so
        // that claim is made only from a settled read.
        public bool ControlsStable;
        // How many duties the group's own list reports, or -1 when unreadable.
        public int ExpectedDuties = -1;
    }

    /// <summary>
    /// The enabled state of every control in the group, as a comparable string. Two equal
    /// signatures a frame apart mean the group has settled.
    /// </summary>
    internal static string ControlSignature(Group group)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(group.HasTakeAll ? (group.TakeAllEnabled ? '1' : '0') : '-');
        sb.Append(group.HasDelegateAll ? (group.DelegateAllEnabled ? '1' : '0') : '-');
        foreach (var d in group.Duties)
        {
            sb.Append('|');
            sb.Append(d.HasTakeControl ? (d.TakeControlEnabled ? '1' : '0') : '-');
            sb.Append(d.HasDelegate ? (d.DelegateEnabled ? '1' : '0') : '-');
        }
        return sb.ToString();
    }

    /// <summary>Number of groups the game's own list reports, or -1 when unreadable.</summary>
    public int GroupCount()
    {
        var vl = GetVirtualisedList(_list);
        if (vl == null) return -1;
        try { return vl.Items?.TryCast<Il2CppSystem.Collections.ICollection>()?.Count ?? -1; }
        catch (Exception e)
        {
            Degrade.Note($"responsibilities: group count unreadable: {e.GetType().Name}");
            return -1;
        }
    }

    /// <summary>
    /// Scroll group <paramref name="index"/> into the realised window. True once its element
    /// exists and its name has bound; the caller retries across frames while this is false.
    /// </summary>
    public bool ScrollToGroupReady(int index)
    {
        var vl = GetVirtualisedList(_list);
        if (vl == null) return false;
        _scrollToMethod.Invoke(vl, new object[] { index });
        var el = FindByDataIndex(vl, index);
        if (el == null) return false;
        var nameEl = FindDescendantNamed(el, GroupNameElement);
        return nameEl != null && !string.IsNullOrEmpty(ReadText(nameEl));
    }

    /// <summary>Harvest one realised group. Null when its element is not (or no longer) realised.</summary>
    public Group ReadGroup(int index)
    {
        var vl = GetVirtualisedList(_list);
        var el = vl != null ? FindByDataIndex(vl, index) : null;
        if (el == null) return null;

        var group = new Group { Index = index, NativeIdentity = ReadNativeIdentity(el, "responsibilitygroup"), Name = CleanOrNull(ReadTextOf(el, GroupNameElement)) };

        var assignAll = FindDescendantNamed(el, AssignAllBlock);
        if (assignAll != null)
        {
            var take = FindDescendantNamed(assignAll, TakeAllButton);
            var del = FindDescendantNamed(assignAll, DelegateAllButton);
            group.HasTakeAll = take != null;
            group.TakeAllEnabled = take != null && IsEnabled(take);
            group.TakeAllLabel = take != null ? ButtonLabel(take) : null;
            group.HasDelegateAll = del != null;
            group.DelegateAllEnabled = del != null && IsEnabled(del);
            group.DelegateAllLabel = del != null ? ButtonLabel(del) : null;
        }

        var dutyList = FindDescendantNamed(el, DutyListName);
        if (dutyList != null) ReadDuties(dutyList, group);
        return group;
    }

    // The duties of one group. Their own list virtualises too: a tall group can leave one of its
    // duties unrealised (the club's 54th responsibility went missing exactly this way). Read every
    // realised entry at its own data index and, when the list says there are more, scroll the
    // first missing one in so the loader's next pass picks it up. Expected records the game's own
    // count, so a group that never completes is reported rather than quietly shortened.
    private void ReadDuties(VisualElement dutyList, Group group)
    {
        var vl = GetVirtualisedList(dutyList);
        if (vl == null) return;

        try { group.ExpectedDuties = vl.Items?.TryCast<Il2CppSystem.Collections.ICollection>()?.Count ?? -1; }
        catch { group.ExpectedDuties = -1; }

        int active = vl.GetActiveCount();
        var byIndex = new SortedDictionary<int, VisualElement>();
        for (int i = 0; i < active; i++)
        {
            var virtElem = vl.GetActiveItem(i);
            if (virtElem == null) continue;
            int idx = (int)_indexProperty.GetValue(virtElem);
            var content = _contentProperty.GetValue(virtElem);
            if (content == null) continue;
            byIndex[idx] = new VisualElement(((Il2CppSystem.Object)content).Pointer);
        }
        foreach (var kv in byIndex)
            group.Duties.Add(ReadDuty(kv.Value, kv.Key));

        if (group.ExpectedDuties > 0 && group.Duties.Count < group.ExpectedDuties)
            for (int idx = 0; idx < group.ExpectedDuties; idx++)
                if (!byIndex.ContainsKey(idx)) { _scrollToMethod.Invoke(vl, new object[] { idx }); break; }
    }

    /// <summary>True when the group holds every duty its own list reports.</summary>
    internal static bool DutiesComplete(Group group)
        => group.ExpectedDuties <= 0 || group.Duties.Count >= group.ExpectedDuties;

    /// <summary>Number of duties the group's own list reports, or -1 when unreadable.</summary>
    public int DutyCount(int groupIndex)
    {
        var vl = GetVirtualisedList(_list);
        var el = vl != null ? FindByDataIndex(vl, groupIndex) : null;
        var dutyList = el != null ? FindDescendantNamed(el, DutyListName) : null;
        var inner = dutyList != null ? GetVirtualisedList(dutyList) : null;
        if (inner == null) return -1;
        try { return inner.Items?.TryCast<Il2CppSystem.Collections.ICollection>()?.Count ?? -1; }
        catch { return -1; }
    }

    private Duty ReadDuty(VisualElement dutyEl, int index)
    {
        var duty = new Duty { Index = index, NativeIdentity = ReadNativeIdentity(dutyEl, "individualresponsibility") };

        // Title then description: the template's two texts, in its own order.
        var texts = new List<string>();
        CollectTitleTexts(dutyEl, texts);
        if (texts.Count > 0) duty.Title = texts[0];
        if (texts.Count > 1) duty.Description = texts[1];

        var take = FindDescendantUnder(dutyEl, TakeControlWrapper, "SIButton");
        duty.HasTakeControl = take != null;
        duty.TakeControlEnabled = take != null && IsEnabled(take);
        duty.TakeControlLabel = take != null ? ButtonLabel(take) : null;

        var del = FindDescendantUnder(dutyEl, DelegateWrapper, "SIButton");
        duty.HasDelegate = del != null;
        duty.DelegateEnabled = del != null && IsEnabled(del);
        duty.DelegateLabel = del != null ? ButtonLabel(del) : null;

        var assign = FindDescendantNamed(dutyEl, AssignDropdown);
        if (assign != null)
        {
            duty.HasAssign = true;
            // The same element RealizeControl resolves for SlotAssign, and the same signal
            // DropdownSession.Open refuses on, so the rendered state cannot disagree with the open.
            duty.AssignEnabled = IsEnabled(assign);
            duty.AssignText = ReadAssignee(assign);
        }

        var noStaff = FindDescendantNamed(dutyEl, NoStaffBlock);
        if (noStaff != null) CollectTexts(noStaff, duty.NoStaff, 4);

        return duty;
    }

    // The duty's own title and description, read from the text pair the template puts above its
    // controls. Scoped away from the assignee dropdown and the segmented buttons, whose labels
    // would otherwise be picked up as the description.
    private static void CollectTitleTexts(VisualElement root, List<string> into)
    {
        if (into.Count >= 2) return;
        if (!ElementBridge.IsVisible(root)) return;
        string name = root.name;
        if (name == AssignDropdown || name == NoStaffBlock) return;
        if (root.GetIl2CppType().Name == "SIButton") return;
        if (root.GetIl2CppType().Name == "SIText")
        {
            var t = CleanOrNull(ReadText(root));
            if (t != null) into.Add(t);
            return;
        }
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n && into.Count < 2; i++)
            CollectTitleTexts(root.hierarchy[i], into);
    }

    // The assignee the dropdown currently shows: the person's name and their job, the two texts
    // the selection field carries. Null while nothing has bound yet — a group scrolled in this
    // frame has its name before its people — which the loader retries; only once its budget is
    // spent does the caller turn a still-null read into an honest marker.
    private string ReadAssignee(VisualElement dropdown)
    {
        var field = FindDescendantNamed(dropdown, "SelectionField");
        if (field == null) return null;

        // The person's name and job bind separately, and the job lands first — reading whichever
        // texts happen to be present would show a bare "Head Coach" as if that were the assignee.
        // Keyed on the template's own names so a half-bound field reads as not ready at all.
        var nameEl = FindDescendantNamed(field, "PersonName");
        if (nameEl != null)
        {
            var name = CleanOrNull(ReadText(nameEl));
            if (name == null) return null;
            var jobEl = FindDescendantNamed(field, "PersonJob");
            var job = jobEl != null ? CleanOrNull(ReadText(jobEl)) : null;
            return job == null ? name : name + ", " + job;
        }

        var parts = new List<string>();
        CollectTexts(field, parts, 2);
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// True when every duty that has an assignee dropdown has actually read one. The loader waits
    /// on this before accepting a group, so a freshly scrolled-in group is not recorded with its
    /// people still unbound.
    /// </summary>
    internal static bool AssigneesBound(Group group)
    {
        if (group.NativeIdentity == null) return false;
        foreach (var d in group.Duties)
            if (d.NativeIdentity == null || (d.HasAssign && d.AssignText == null)) return false;
        return true;
    }

    // Native reference pairs identify the duty/group across virtualized row and
    // category recycling. The assignee, translated caption and display index do
    // not: Staff and Scouting reuse the same list, indices and panel token.
    private static string ReadNativeIdentity(VisualElement element, string alias)
    {
        // VirtualisedElement.Content is the outer template, above its BindingRoot.
        // Start at the existing native text anchor inside that template.
        var anchor = alias == "responsibilitygroup"
            ? FindDescendantNamed(element, GroupNameElement)
            : FindDescendantOfType(element, "SIText");
        for (var up = anchor ?? element; up != null; up = up.hierarchy.parent)
        {
            var root = Server.BindingRecords.NodeOf(up);
            if (root == null) continue;
            foreach (var child in root.Children)
            {
                if (child.Name != alias) continue;
                var reference = Server.BindingRecords.ValueOf(child)?.Get()?.TryCast<SI.Interop.InteropReference>();
                return Server.BindingRecords.ReferenceIdentity(reference);
            }
        }
        return null;
    }

    private static string FirstText(VisualElement root)
    {
        var one = new List<string>();
        CollectTexts(root, one, 1);
        return one.Count > 0 ? one[0] : null;
    }

    /// <summary>
    /// A button's caption. The per-duty Take Control / Delegate pair hangs its caption on a child
    /// SIText, but the per-group TakeControlAll / DelegateAll buttons carry theirs on the button
    /// element itself and have no text child at all — a descendant-only read returns null there, and
    /// the group's two bulk buttons then render with nothing but their shared group prefix, reading
    /// identically. Try the descendants first (the nested case), then the element's own text.
    /// </summary>
    private static string ButtonLabel(VisualElement button)
        => FirstText(button) ?? CleanOrNull(OwnText(button));

    private static string OwnText(VisualElement el)
    {
        try
        {
            var textEl = el.TryCast<TextElement>();
            return textEl != null && !string.IsNullOrEmpty(textEl.text) ? textEl.text : null;
        }
        catch { return null; }
    }

    private static void CollectTexts(VisualElement root, List<string> into, int max)
    {
        if (into.Count >= max || !ElementBridge.IsVisible(root)) return;
        if (root.GetIl2CppType().Name == "SIText")
        {
            var t = CleanOrNull(ReadText(root));
            if (t != null) into.Add(t);
            if (into.Count >= max) return;
        }
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n && into.Count < max; i++)
            CollectTexts(root.hierarchy[i], into, max);
    }

    // --- The Overview tab's per-staff duty list ---
    //
    // A flat virtualised list of title/description pairs with no controls, so it sweeps by text
    // alone and needs no re-realising handles. It virtualises just as hard as the group list: the
    // manager's 39 duties realised 12 at a time, which the table would otherwise present as all
    // of them.

    internal sealed class StaffDuty
    {
        public string Title;
        public string Description;
    }

    /// <summary>Number of duties the staff list reports, or -1 when unreadable.</summary>
    public int StaffDutyCount() => GroupCount();

    /// <summary>
    /// Scroll staff duty <paramref name="index"/> in and read it, or null while it has not
    /// realised or its title has not bound — the caller retries across frames.
    /// </summary>
    public StaffDuty ReadStaffDuty(int index)
    {
        var vl = GetVirtualisedList(_list);
        if (vl == null) return null;
        _scrollToMethod.Invoke(vl, new object[] { index });
        var el = FindByDataIndex(vl, index);
        if (el == null) return null;

        var texts = new List<string>();
        CollectTexts(el, texts, 2);
        if (texts.Count == 0) return null;
        return new StaffDuty
        {
            Title = texts[0],
            Description = texts.Count > 1 ? texts[1] : null,
        };
    }

    /// <summary>
    /// Re-realises (scrolls in) a control and returns its live element, or null while a recycled
    /// row's controls have not re-bound yet — the handle retries across frames. Duty index -1
    /// addresses the group's own bulk buttons.
    /// </summary>
    public VisualElement RealizeControl(int groupIndex, int dutyIndex, string slot,
        string expectedGroup, string expectedDuty)
    {
        if (_list?.panel == null || !ElementBridge.IsVisibleInHierarchy(_list)
            || string.IsNullOrEmpty(expectedGroup)) return null;
        var vl = GetVirtualisedList(_list);
        if (vl == null) return null;
        _scrollToMethod.Invoke(vl, new object[] { groupIndex });
        var groupEl = FindByDataIndex(vl, groupIndex);
        if (groupEl == null) return null;
        if (ReadNativeIdentity(groupEl, "responsibilitygroup") != expectedGroup) return null;

        if (dutyIndex < 0)
        {
            var assignAll = FindDescendantNamed(groupEl, AssignAllBlock);
            if (assignAll == null) return null;
            return slot == SlotTakeAll
                ? FindDescendantNamed(assignAll, TakeAllButton)
                : FindDescendantNamed(assignAll, DelegateAllButton);
        }

        var dutyList = FindDescendantNamed(groupEl, DutyListName);
        var innerVl = dutyList != null ? GetVirtualisedList(dutyList) : null;
        if (innerVl == null) return null;
        _scrollToMethod.Invoke(innerVl, new object[] { dutyIndex });
        var dutyEl = FindByDataIndex(innerVl, dutyIndex);
        if (dutyEl == null) return null;
        if (string.IsNullOrEmpty(expectedDuty)
            || ReadNativeIdentity(dutyEl, "individualresponsibility") != expectedDuty) return null;

        return slot switch
        {
            SlotTake => FindDescendantUnder(dutyEl, TakeControlWrapper, "SIButton"),
            SlotDelegate => FindDescendantUnder(dutyEl, DelegateWrapper, "SIButton"),
            SlotAssign => FindDescendantNamed(dutyEl, AssignDropdown),
            _ => null,
        };
    }

    // --- Virtualised list access ---

    private static SI.UI.VirtualisedList GetVirtualisedList(VisualElement host)
    {
        if (host == null) return null;
        var vlVE = host.GetIl2CppType().Name == "VirtualisedList" ? host : FindDescendantOfType(host, "VirtualisedList");
        if (vlVE == null) return null;
        return new SI.UI.VirtualisedList(((Il2CppSystem.Object)vlVE).Pointer);
    }

    private VisualElement FindByDataIndex(SI.UI.VirtualisedList vl, int dataIndex)
    {
        int activeCount = vl.GetActiveCount();
        for (int i = 0; i < activeCount; i++)
        {
            var virtElem = vl.GetActiveItem(i);
            if (virtElem == null) continue;
            if ((int)_indexProperty.GetValue(virtElem) != dataIndex) continue;
            var content = _contentProperty.GetValue(virtElem);
            if (content == null) return null;
            return new VisualElement(((Il2CppSystem.Object)content).Pointer);
        }
        return null;
    }

    // --- Tree helpers ---

    private static bool IsEnabled(VisualElement el)
    {
        try { return el.enabledInHierarchy; }
        catch (Exception ex)
        {
            Log.Warn($"[Responsibilities] enabledInHierarchy threw: {ex.Message}");
            return false;
        }
    }

    private static string ReadTextOf(VisualElement root, string name)
    {
        var el = FindDescendantNamed(root, name);
        return el != null ? ReadText(el) : null;
    }

    private static string ReadText(VisualElement el)
    {
        try
        {
            var t = el.Cast<SI.Bindable.SIText>().text;
            return string.IsNullOrEmpty(t) ? null : t;
        }
        catch { return null; }
    }

    private static string CleanOrNull(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var cleaned = TextFilters.CleanForDisplay(raw);
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    internal static VisualElement FindDescendantNamed(VisualElement root, string name)
    {
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var child = root.hierarchy[i];
            if (child.name == name) return child;
            var deeper = FindDescendantNamed(child, name);
            if (deeper != null) return deeper;
        }
        return null;
    }

    // The first descendant of the given type under the named wrapper — the segmented buttons,
    // whose own names are template style variants ("...-active-regular") rather than roles, so
    // the enabling wrapper is what identifies them.
    private static VisualElement FindDescendantUnder(VisualElement root, string wrapperName, string typeName)
    {
        var wrapper = FindDescendantNamed(root, wrapperName);
        if (wrapper == null || !ElementBridge.IsVisible(wrapper)) return null;
        return wrapper.GetIl2CppType().Name == typeName ? wrapper : FindDescendantOfType(wrapper, typeName);
    }

    private static VisualElement FindDescendantOfType(VisualElement root, string typeName)
    {
        int n = root.hierarchy.childCount;
        for (int i = 0; i < n; i++)
        {
            var child = root.hierarchy[i];
            if (child.GetIl2CppType().Name == typeName) return child;
            var deeper = FindDescendantOfType(child, typeName);
            if (deeper != null) return deeper;
        }
        return null;
    }
}
