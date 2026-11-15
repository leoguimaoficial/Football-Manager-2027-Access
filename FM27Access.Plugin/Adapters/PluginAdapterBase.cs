using System;
using System.Collections.Generic;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal abstract class PluginAdapterBase : AccessibleElementBase
{
    protected readonly VisualElement Element;
    internal VisualElement FieldLabelContainer { get; private set; }

    internal VisualElement BackingElement => Element;

    protected PluginAdapterBase(VisualElement element)
    {
        Element = element;
    }

    public override bool IsVisible() => ElementBridge.IsVisible(Element);

    // The game arms a right-click menu on many sub-elements that are not the entity this control is
    // named after — a summary tile over a buried player, a message row over its sender. A resolved
    // holder is only a real menu when ContextMenuScope agrees the menu belongs to what the label
    // names (judged against the control's own rendered name). Every surface's mark funnels through
    // here, so this is the single gate — and where the dev census observes the raw decision.
    public override bool HasContextMenu
    {
        get
        {
            var resolution = ContextMenuOpener.Resolve(Element);
            if (ContextMenuCensus.Recording) ContextMenuCensus.Record(this, resolution);
            if (resolution.Target == null) return false;
            var verdict = ContextMenuOpener.Verdict(resolution, ContextMenuOpener.OwnerLabel(this));
            return ContextMenuScope.Accepts(verdict);
        }
    }

    // Names of descendant elements whose text the generic collectors must skip (not read,
    // not recurse into). Null by default; subclasses override to suppress sub-widgets that
    // carry their own dedicated handling (e.g. the squad player card reads goals/assists and
    // the shirt number separately, so they must not leak into the joined label).
    protected virtual ISet<string> ExcludedTextNodeNames => null;

    // Descendant TYPES whose whole subtree the generic collectors must skip. Null by default;
    // a subclass overrides when a nested widget of that type is its own control whose texts
    // must not fold into this element's label (a radio's nested sub-option SIRadioGroup).
    protected virtual ISet<string> ExcludedSubtreeTypeNames => null;

    protected string FindChildText()
    {
        return FindChildTextRecursive(Element);
    }

    /// <summary>
    /// Collects all visible text from descendant TextElements, joined with ", ".
    /// Returns null if no text is found.
    /// </summary>
    protected string FindAllChildText()
    {
        var texts = new List<string>();
        CollectAllChildTextRecursive(Element, texts, ExcludedTextNodeNames, ExcludedSubtreeTypeNames);
        return texts.Count > 0 ? string.Join(", ", texts) : null;
    }

    /// <summary>
    /// Best-effort label: child text → tooltip → element name.
    /// Element names that look like developer IDs (contain hyphens) are excluded.
    /// </summary>
    protected string FindBestLabel()
    {
        try
        {
            var textEl = Element.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                return textEl.text;
        }
        catch (Exception e) { Speech.Log.Warn($"TryCast<TextElement>() failed in FindBestLabel: {e.GetType().Name}"); }

        var text = FindAllChildText();
        if (text != null) return text;

        var tooltip = Element.tooltip;
        if (!string.IsNullOrEmpty(tooltip)) return tooltip;

        var name = Element.name;
        if (!string.IsNullOrEmpty(name) && !name.Contains('-'))
            return name;

        return null;
    }

    // True when a string looks like a developer template id rather than user-facing text — it
    // carries an underscore, or a camelHump (a lowercase letter immediately followed by an
    // uppercase one), the PascalCase/camelCase signature of names like "BookmarkButton" or
    // "ClubButton". Hyphenated ids are already rejected upstream (FindBestLabel returns null for
    // them). All-caps acronyms ("OK", "FAQ") and single words ("Continue") have no camelHump, so
    // they are NOT flagged — they stay valid labels. The test itself is canonical in
    // AuditRules.LooksLikeDeveloperId so the dev-driver audit flags id-shaped labels by the exact
    // rule the adapters reject them by.
    // A string with a space is human text, never a UXML/template id — ids are single tokens — so
    // only spaceless strings go through the id-shape test. Without this, a real game label with
    // an intra-word capital ("Name(s) and DoB") is rejected into "unlabeled icon button". The
    // audit keeps the unexempted rule: there it is a soft LEAD for humans, not a rejection.
    protected static bool LooksLikeTemplateId(string s) =>
        s != null && s.IndexOf(' ') < 0 && AuditRules.LooksLikeDeveloperId(s);

    protected static VisualElement FindByName(VisualElement parent, string name)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            if (child.name == name)
                return child;
            var deeper = FindByName(child, name);
            if (deeper != null)
                return deeper;
        }
        return null;
    }

    protected static void CollectAllTexts(VisualElement root, List<string> texts, ISet<string> skipNames = null)
    {
        if (skipNames != null && skipNames.Contains(root.name))
            return;

        try
        {
            var textEl = root.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
            {
                texts.Add(textEl.text);
                return;
            }
        }
        catch (Exception e) { Speech.Log.Warn($"TryCast<TextElement>() failed in CollectAllTexts: {e.GetType().Name}"); }

        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectAllTexts(root.hierarchy[i], texts, skipNames);
    }

    private static string FindChildTextRecursive(VisualElement parent)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            try
            {
                var textEl = child.TryCast<TextElement>();
                if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                    return textEl.text;
            }
            catch (Exception e)
            {
                Speech.Log.Warn($"TryCast<TextElement>() failed for child '{child.name}': {e.GetType().Name}");
            }

            var deeper = FindChildTextRecursive(child);
            if (deeper != null)
                return deeper;
        }
        return null;
    }

    /// <summary>
    /// Text carried by the control's DIRECT siblings — the ones sharing its immediate hierarchy
    /// parent — regardless of child order. A control laid out as "[box] Use current match tactics"
    /// is named by that text; nothing outside the immediate container is considered, so a settings
    /// row's status value further out still loses to the field label.
    /// </summary>
    protected string FindDirectSiblingText()
    {
        var parent = Element.hierarchy.parent;
        if (parent == null) return null;

        var texts = new List<string>();
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var sibling = parent.hierarchy[i];
            if (sibling.Pointer == Element.Pointer) continue;
            if (!ElementBridge.IsVisible(sibling)) continue;
            CollectTextsExcluding(sibling, texts, ExcludedTextNodeNames, ExcludedSubtreeTypeNames);
        }
        return texts.Count > 0 ? string.Join(", ", texts) : null;
    }

    // FindDirectSiblingText for a control that arrives wrapped in its template: the Data Hub cards'
    // "Set Preferred" checkbox nests two levels deep (a TemplateContainer, then the switch frame),
    // each level holding the control alone, and the label sits beside the outer wrapper. A wrapper
    // whose only child is the control is part of the control, so its siblings are the control's row.
    // Null when the control's own parent already holds siblings (FindDirectSiblingText's case) or no
    // single-child wrapper leads to a row. Without this the checkbox fell through to
    // FindFieldLabel, which climbed out of the row and named it after the card's "Tracked by:".
    private const int MaxWrapperDepth = 3;

    protected string FindWrappedRowText()
    {
        var wrapper = Element;
        for (int depth = 0; depth < MaxWrapperDepth; depth++)
        {
            var parent = wrapper.hierarchy.parent;
            if (parent == null) return null;
            if (parent.hierarchy.childCount > 1)
            {
                if (depth == 0) return null;   // the box's own row: FindDirectSiblingText's case
                var texts = new List<string>();
                int count = parent.hierarchy.childCount;
                for (int i = 0; i < count; i++)
                {
                    var sibling = parent.hierarchy[i];
                    if (sibling.Pointer == wrapper.Pointer || !ElementBridge.IsVisible(sibling)) continue;
                    CollectTextsExcluding(sibling, texts, ExcludedTextNodeNames, ExcludedSubtreeTypeNames);
                }
                return texts.Count > 0 ? string.Join(", ", texts) : null;
            }
            wrapper = parent;
        }
        return null;
    }

    protected string FindSiblingText()
    {
        var current = Element;
        for (int depth = 0; depth < 5; depth++)
        {
            var parent = current.parent;
            if (parent == null) return null;

            if (parent.hierarchy.childCount > 1)
            {
                var texts = new List<string>();
                int count = parent.hierarchy.childCount;
                for (int i = 0; i < count; i++)
                {
                    var sibling = parent.hierarchy[i];
                    if (sibling.Pointer == current.Pointer) continue;
                    if (!ElementBridge.IsVisible(sibling)) continue;
                    // An adapter that declares exclusions gets them honored here too, so a sibling
                    // widget's internals (a stepper's "-"/"+", a dropdown's value) never leak into
                    // this control's name. Adapters with no exclusions keep the legacy collector.
                    if (ExcludedTextNodeNames != null || ExcludedSubtreeTypeNames != null)
                        CollectTextsExcluding(sibling, texts, ExcludedTextNodeNames, ExcludedSubtreeTypeNames);
                    else
                        CollectAllTexts(sibling, texts);
                }
                if (texts.Count > 0)
                    return string.Join(", ", texts);
            }

            current = parent;
        }
        return null;
    }

    // Container names that wrap a settings/form row's FIELD LABEL (the text naming the control),
    // as opposed to the control or its value. The game places a row label inside one of these.
    private static readonly string[] LabelFrameNames = { "text frame" };

    /// <summary>
    /// The field label that names this control within a settings/form row — "Currency" for the
    /// currency dropdown, "Match Sounds" for the sounds toggle — read live, or null when the
    /// control sits in no such row (callers then keep their existing value-based naming).
    ///
    /// The label is distinct from the control's value: the value lives inside the widget (a
    /// dropdown's selection) or beside it as a status sibling (a toggle's "On"). Two passes up the
    /// ancestor chain keep them apart: the first accepts text only from a sibling label frame, so a
    /// row whose value sits beside the control still resolves to the real label one frame out; only
    /// if no label frame exists anywhere up the chain does the second pass accept a bare sibling
    /// text element (the currency row labels its dropdown with a bare "Currency" sibling).
    /// </summary>
    protected string FindFieldLabel()
    {
        var columnLabel = FindParallelColumnHeaderLabel();
        if (columnLabel != null) return columnLabel;
        // A control inside a navigation-tab container (NavigationFocusser) is a self-naming tab,
        // not a settings/form row: its accessible name is its own visible text, and any text found
        // up the ancestor chain belongs to PEER TABS, never a field label. Without this guard the
        // walk crosses into the sibling tabs and adopts a neighbour's name — the Squad secondary-tab
        // dropdowns (Training/Youth Setup/Dynamics/More) all resolve to the first tab's "Overview".
        // Bail so each control names itself by its own text via the adapter's value/child-text path.
        if (IsInsideNavigationContainer()) return null;

        return FindPairedControlLabel()
            ?? FindLabelInAncestors(labelFrameOnly: true)
            ?? FindLabelInAncestors(labelFrameOnly: false);
    }

    // A shared form group can name two controls once, with a connector between
    // them. That connector alone (for example "and") is not the second field's
    // purpose. Keep the native group caption and distinguish the two positions;
    // do not infer minimum/maximum for arbitrary paired controls such as dates.
    private string FindPairedControlLabel()
    {
        var child = Element;
        for (int depth = 0; depth < FieldLabelMaxDepth && child.parent != null;
             depth++, child = child.parent)
        {
            var row = child.parent;
            if (row.GetIl2CppType().Name is "Cell" or "Panel" or "FloatingPopupContent") return null;
            var cells = VisibleChildrenOf(row);
            if (cells.Count != 3 || cells[1].TryCast<TextElement>() == null
                || !SubtreeHasControl(cells[0], ControlScanDepth)
                || !SubtreeHasControl(cells[2], ControlScanDepth)) continue;
            int index = cells[0].Pointer == child.Pointer ? 1
                : cells[2].Pointer == child.Pointer ? 2 : 0;
            if (index == 0 || row.parent == null) continue;
            var group = VisibleChildrenOf(row.parent);
            if (group.Count != 2 || group[1].Pointer != row.Pointer
                || SubtreeHasControl(group[0], ControlScanDepth)) continue;
            var label = FirstVisibleText(group[0]);
            if (string.IsNullOrWhiteSpace(label) || IsBareNumber(label)) continue;
            FieldLabelContainer = group[0];
            return label + ", " + index + " of 2";
        }
        return null;
    }

    // FM27's compact records can pair Headers/RightAlignedHeaders with
    // Values/RightAlignedValues. A control's preceding neighbour is a VALUE
    // (last season's position in club expectations), not its column caption.
    // Match actual parallel child indices, keeping the record's leading identity
    // as context when present. No panel names, translated captions or indices.
    protected string FindParallelColumnHeaderLabel()
    {
        var child = Element;
        for (int depth = 0; depth < 12 && child.parent != null; depth++, child = child.parent)
        {
            var values = child.parent;
            if (values.GetIl2CppType().Name is "Cell" or "Panel" or "FloatingPopupContent") return null;
            if (values.name != "RightAlignedValues") continue;
            var row = values.parent;
            var record = row?.parent;
            if (row?.name != "Values" || record == null) return null;
            VisualElement headers = null;
            for (int i = 0; i < record.hierarchy.childCount; i++)
                if (record.hierarchy[i].name == "Headers") headers = record.hierarchy[i];
            if (headers == null) return null;
            VisualElement columns = null;
            for (int i = 0; i < headers.hierarchy.childCount; i++)
                if (headers.hierarchy[i].name == "RightAlignedHeaders") columns = headers.hierarchy[i];
            if (columns == null || columns.hierarchy.childCount != values.hierarchy.childCount) return null;
            int index = -1;
            for (int i = 0; i < values.hierarchy.childCount; i++)
                if (values.hierarchy[i].Pointer == child.Pointer) { index = i; break; }
            if (index < 0) return null;
            var captions = new List<string>();
            CollectAllTexts(columns.hierarchy[index], captions);
            if (captions.Count != 1) return null;
            var identity = new List<string>();
            if (row.hierarchy.childCount > 0 && row.hierarchy[0].Pointer != values.Pointer)
                CollectAllTexts(row.hierarchy[0], identity);
            return identity.Count == 0 ? captions[0] : captions[0] + ", " + string.Join(", ", identity);
        }
        return null;
    }

    // True when an ancestor is a NavigationFocusser — the game's tab/navigation collection wrapper.
    // Settings and form rows never sit inside one, so this only ever excludes nav tabs from
    // field-label resolution.
    private bool IsInsideNavigationContainer()
    {
        var current = Element.parent;
        while (current != null)
        {
            string type;
            try { type = current.GetIl2CppType().Name; }
            catch { type = current.name; }
            if (type == "NavigationFocusser") return true;
            current = current.parent;
        }
        return false;
    }

    // Row labels sit one or two levels out in compact settings rows but up to nine levels out in
    // form screens that nest the control under deep binding wrappers (manager creation's
    // searchable dropdowns), so the walk has to reach well past the immediate parent.
    private const int FieldLabelMaxDepth = 12;

    private string FindLabelInAncestors(bool labelFrameOnly)
    {
        var current = Element;
        // Column index/width of this control within its cell-row, for the 2-column form grid handled
        // below. -1 until the walk passes through a row of parallel control cells; see the capture
        // step at the foot of the loop and ColumnLabel.
        int col = -1;
        int colCount = 0;
        for (int depth = 0; depth < FieldLabelMaxDepth; depth++)
        {
            // A table cell owns its controls. Crossing into adjacent cells can
            // borrow the preceding value/header and shift every menu's label
            // (observed in the conduct fines table). The FM26 table renderer
            // supplies the actual column header for a value-named dropdown.
            if (current.GetIl2CppType().Name is "Cell" or "Panel" or "FloatingPopupContent") return null;
            // Walk and scan on the PHYSICAL hierarchy (hierarchy.parent + hierarchy children), never
            // the logical contentContainer tree (`.parent` / the indexer). At a tile's content-container
            // boundary the two diverge: the logical parent of a settings row is the tile template, whose
            // *physical* children are a single binding wrapper that does not include the row — so a scan
            // mixing logical-parent with physical-children never finds `current` among the siblings, the
            // preceding-only stop below never fires, and the walk descends into an unrelated subtree
            // (the in-game Screen Resolution dropdown wrongly adopting the tile's leading "See all"
            // button text). Staying on one consistent tree keeps `current` among the scanned siblings.
            var parent = current.hierarchy.parent;
            if (parent == null) return null;

            // Only siblings that PRECEDE the control in child order are field-label candidates: across
            // every settings/form row the label sits before its control ("Currency" before the currency
            // dropdown, "Match Sounds" before its toggle). Adopting a following sibling is the over-reach
            // behind the Graphics Quality dropdown grabbing the capability meter's "Your device's
            // Graphics Capability" and the Screen Resolution dropdown grabbing the following "Mode"
            // segmented control's label. Stop the scan at `current` so a trailing control's label can
            // never be mistaken for this control's own.
            //
            // Take the CLOSEST preceding label, bounded by the previous control. A flat form container
            // interleaves many [label, control] pairs as bare siblings with no per-row grouping (the
            // Arrange Friendly dialog: Date header, date picker, Use-for-testimonial header, its
            // dropdown, Match Rules header, its dropdown, ...). A control marks the end of the prior
            // label's run, so resetting the candidate at every control-bearing sibling and keeping the
            // last label text before `current` makes each control adopt the header immediately above it,
            // not the first header in the container (which mislabelled every dropdown in that dialog
            // "Date"). For a simple one-row container — one label, one control — first and closest
            // coincide, so this leaves those rows unchanged.
            string candidate = null;
            VisualElement candidateContainer = null;
            int precedingControlCells = 0;
            int count = parent.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var sibling = parent.hierarchy[i];
                // Interop can return a fresh managed wrapper for the same native
                // element. Compare native identity before treating it as a sibling.
                if (sibling.Pointer == current.Pointer) break;
                if (!ElementBridge.IsVisible(sibling)) continue;

                if (SubtreeHasControl(sibling, ControlScanDepth))
                {
                    candidate = null;
                    precedingControlCells++;
                    continue;
                }

                // A pure-label sibling. In a 2-column form grid the labels live in their own grid,
                // parallel to the controls grid, so the Nth control's label is the Nth label cell —
                // not the first. ColumnLabel maps to column `col` when this sibling is itself such a
                // grid (and falls back to the first text otherwise), fixing the auto-save block where
                // both dropdowns otherwise adopted the first label "Every".
                // A bare number is never a field label — it is a live VALUE that happens to sit
                // before the control (the person-search "16173 People Found" count precedes the
                // staff-interest dropdown, which adopted "16173" as its name). Rejecting it lets
                // such a control fall through to value-based naming instead of a numeric lie.
                string text = labelFrameOnly
                    ? TextInsideLabelFrame(sibling)
                    : ColumnLabel(sibling, col, colCount);
                if (!string.IsNullOrEmpty(text) && !IsBareNumber(text))
                {
                    candidate = text;
                    candidateContainer = sibling;
                    var cells=GetGridCells(sibling);
                    if(cells!=null&&col>=1&&cells.Count==colCount&&col<cells.Count)candidateContainer=cells[col];
                    else if(cells!=null&&col<1)
                    {
                        var controlCells=GetGridCells(current);
                        if(controlCells!=null&&cells.Count==controlCells.Count&&controlCells.All(c=>SubtreeHasControl(c,ControlScanDepth)))candidateContainer=cells[0];
                    }
                }
            }
            if (!string.IsNullOrEmpty(candidate)) {FieldLabelContainer=candidateContainer;return candidate;}

            // Capture the column index once, at the innermost row of parallel control cells this
            // control belongs to: its column is how many control cells precede it there, and the row
            // width is that parent's visible child count. A grid label sibling found higher up is then
            // read at the matching column. Controls outside any such row (every ordinary settings row)
            // never set this, so their resolution is unchanged.
            if (col < 0 && precedingControlCells > 0 && SubtreeHasControl(current, ControlScanDepth))
            {
                col = precedingControlCells;
                colCount = VisibleChildCount(parent);
            }

            current = parent;
        }
        return null;
    }

    // Digits with grouping separators only ("16173", "10,000") — text with any word in it passes.
    private static bool IsBareNumber(string text)
    {
        bool sawDigit = false;
        foreach (var c in text)
        {
            if (char.IsDigit(c)) { sawDigit = true; continue; }
            if (c is ',' or '.' or ' ' or '\u00A0') continue;
            return false;
        }
        return sawDigit;
    }

    // The label text for column `col` of a parallel label grid, else the grid's first text. A 2-column
    // form grid (the auto-save block) lays its labels out as one grid and its controls as a sibling
    // grid; a control in control-column N takes label-column N. GetGridCells unwraps the label sibling
    // to its row of cells; the mapping applies only when that row is the same width as the control row
    // (colCount), so a non-parallel sibling falls back to first-text and ordinary rows are untouched.
    private static string ColumnLabel(VisualElement labelSibling, int col, int colCount)
    {
        if (col >= 1)
        {
            var cells = GetGridCells(labelSibling);
            if (cells != null && cells.Count == colCount && col < cells.Count)
                return FirstVisibleText(cells[col]);
        }
        return FirstVisibleText(labelSibling);
    }

    // The row of parallel cells inside a grid container: descend through single-visible-child wrappers
    // until a node with two or more visible children, and return those children. Null when the
    // container holds no such row (a plain single-text label).
    private static List<VisualElement> GetGridCells(VisualElement container)
    {
        var node = container;
        for (int guard = 0; guard < 8; guard++)
        {
            var children = VisibleChildrenOf(node);
            if (children.Count == 0) return null;
            if (children.Count >= 2) return children;
            node = children[0];
        }
        return null;
    }

    private static int VisibleChildCount(VisualElement parent)
    {
        int n = 0;
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            if (ElementBridge.IsVisible(parent.hierarchy[i])) n++;
        return n;
    }

    private static List<VisualElement> VisibleChildrenOf(VisualElement parent)
    {
        var result = new List<VisualElement>();
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            if (ElementBridge.IsVisible(child)) result.Add(child);
        }
        return result;
    }

    // First visible text inside a sibling whose name marks it a label frame, else null.
    private static string TextInsideLabelFrame(VisualElement sibling)
    {
        var name = sibling.name;
        if (string.IsNullOrEmpty(name)) return null;
        bool isLabelFrame = false;
        for (int i = 0; i < LabelFrameNames.Length; i++)
            if (name.Contains(LabelFrameNames[i])) { isLabelFrame = true; break; }
        if (!isLabelFrame) return null;
        return FirstVisibleText(sibling);
    }

    // How far into a sibling to look for a control before deciding it is pure label text. A pure
    // label sibling — the bare "Currency" text, a "...Title" wrapper holding only text — has NO
    // control anywhere in it; a peer SECTION (the Player Database "8719 Players Found" frame, the
    // "Filters" header bar) owns its own controls but nests them well below its leading text. A
    // shallow scan mistook those sections for label text and let a standalone view dropdown adopt
    // their leading "8719"/"Filters". The scan reaches the full field-label depth so any control in
    // the subtree disqualifies it: a control-free sibling is a label, a control-bearing one is a peer.
    private const int ControlScanDepth = FieldLabelMaxDepth;

    private static readonly string[] ControlTypeNames =
    {
        "SIDropdown", "SIButton", "SICheckBox", "SISlider", "SIRadioButton",
        "TextField", "BindableTextEditBox", "Slider", "Toggle", "FMCombobox", "SIStepper",
    };

    private static bool SubtreeHasControl(VisualElement el, int depthBudget)
    {
        string type;
        try { type = el.GetIl2CppType().Name; }
        catch { type = el.name; }
        for (int i = 0; i < ControlTypeNames.Length; i++)
            if (type == ControlTypeNames[i]) return true;

        if (depthBudget <= 0) return false;
        int count = el.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            if (SubtreeHasControl(el.hierarchy[i], depthBudget - 1)) return true;
        return false;
    }

    private static string FirstVisibleText(VisualElement root)
    {
        try
        {
            var textEl = root.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                return textEl.text;
        }
        catch (Exception e) { Speech.Log.Warn($"TryCast<TextElement>() failed in FirstVisibleText: {e.GetType().Name}"); }

        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            if (!ElementBridge.IsVisible(root.hierarchy[i])) continue;
            var t = FirstVisibleText(root.hierarchy[i]);
            if (!string.IsNullOrEmpty(t)) return t;
        }
        return null;
    }

    // Text this control has folded into its own accessible name (and, for a toggle, its on/off
    // status). The screen walker drops a duplicate stray Text stop matching any of these, so a
    // now-labelled control no longer reads with its label as a separate line above it. Empty for
    // controls that adopt no row label, so nothing is suppressed for them. Value-bearing adapters
    // that resolve a field label override this.
    internal virtual IReadOnlyList<string> FoldedTexts() => Array.Empty<string>();

    // CollectAllTexts semantics (the node ITSELF is a text candidate, a text node ends its branch)
    // with the exclusion sets honored, so a bare sibling text label is still collected while an
    // excluded widget subtree is not.
    private static void CollectTextsExcluding(VisualElement root, List<string> texts, ISet<string> skipNames, ISet<string> skipTypes)
    {
        if (skipNames != null && skipNames.Contains(root.name))
            return;
        if (skipTypes != null && skipTypes.Contains(root.GetIl2CppType().Name))
            return;

        try
        {
            var textEl = root.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
            {
                texts.Add(textEl.text);
                return;
            }
        }
        catch (Exception e) { Speech.Log.Warn($"TryCast<TextElement>() failed in CollectTextsExcluding: {e.GetType().Name}"); }

        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (!ElementBridge.IsVisible(child))
                continue;
            CollectTextsExcluding(child, texts, skipNames, skipTypes);
        }
    }

    private static void CollectAllChildTextRecursive(VisualElement parent, List<string> texts, ISet<string> skipNames = null, ISet<string> skipTypes = null)
    {
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = parent.hierarchy[i];
            if (!ElementBridge.IsVisible(child))
                continue;
            if (skipNames != null && skipNames.Contains(child.name))
                continue;
            if (skipTypes != null && skipTypes.Contains(child.GetIl2CppType().Name))
                continue;

            try
            {
                var textEl = child.TryCast<TextElement>();
                if (textEl != null && !string.IsNullOrEmpty(textEl.text))
                {
                    texts.Add(textEl.text);
                    continue;
                }
            }
            catch (Exception e)
            {
                Speech.Log.Warn($"TryCast<TextElement>() failed for child '{child.name}': {e.GetType().Name}");
            }

            CollectAllChildTextRecursive(child, texts, skipNames, skipTypes);
        }
    }
}

