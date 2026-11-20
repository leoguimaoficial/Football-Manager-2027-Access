using UnityEngine;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class ButtonAdapter : PluginAdapterBase
{
    // The honest degrade marker for a button with no readable label. Shared with the server's
    // provisional-compose detection (SseStreamer's marker retry), which re-reads a page that
    // rendered this marker in case the label was still binding at compose time.
    internal const string UnlabeledIconButton = "unlabeled icon button";

    public ButtonAdapter(VisualElement element) : base(element) { }

    private bool IsReadonlyNativeContent => GetType() == typeof(ButtonAdapter)
        && Element.TryCast<SI.Bindable.SIButton>() is { CanSubmit: false }
        && !NativeButtonCommand.HasBoundCommand(Element)
        && !GridSelectorSelection.TryGridEntry(Element, out _, out _)
        // ButtonGroup owns its members' native selection command separately.
        // Specialized adapters likewise retain their intrinsic command paths.
        && FindAncestorButtonGroup() == null;
    public override string Role => IsReadonlyNativeContent ? "text" : "button";
    public override bool CanActivate => !IsReadonlyNativeContent;

    public override string GetLabel()
    {
        if (Element.name == PlayPauseButtonName)
            return PlayPauseLabel();
        var timelineEvent=MatchTimelineInput.EventCaption(Element);
        if(timelineEvent!=null)return timelineEvent;
        // A flag link's own nation is its identity. A wrapped-row fallback can
        // otherwise borrow the adjacent club's caption in compact fixture rows.
        if (Element.name == "NationButton")
            return PairedNationCaption() ?? NativeEntityName() ?? FindChildText() ?? "Nationality";

        // FindBestLabel's last resort is the element name; for an icon-only button that name is a
        // developer template id ("BookmarkButton", "ClubButton"). Speaking it verbatim reads like a
        // bug. Reject the id-shaped fallback so the button degrades to the honest "unlabeled icon
        // button" marker instead (a declarative profile rename, the B11/B14 path, still gives real
        // names where known — it overrides this in DisplayText.Of). Real text labels and acronyms
        // ("9+", "OK", "Continue") have no camelHump, so they pass through unchanged.
        if (Element.parent?.parent?.name == "BindingRoot_SquadOverviewPlayerBlock")
        {
            var rowTexts = new System.Collections.Generic.List<string>();
            CollectTextsTagsLast(rowTexts);
            if (rowTexts.Count > 0) return string.Join(", ", rowTexts);
        }
        var label = FindBestLabel();
        if (label == Element.name) label = StaticButtonCaption.Read(Element)
            ?? NativeButtonCommand.Label(Element.TryCast<SI.Bindable.SIButton>()) ?? label;
        if (label != null && !LooksLikeTemplateId(label)) return label;

        // FM27's shared fixture link puts the hit button beside its rendered
        // date/time/team content. Stay inside that same native component so
        // horizontal fixture rails retain their real match command and context.
        if (Element.name == "MatchLinkButton" && Element.parent?.name == "MatchLink"
            && Element.parent.GetIl2CppType().Name == "ComponentElement")
        {
            var fixture = new System.Collections.Generic.List<string>();
            CollectAllTexts(Element.parent, fixture);
            if (fixture.Count > 0) return string.Join(", ", fixture);
        }

        // Calendar events likewise separate their visual from the linked hit
        // surface. Read only this event's own caption, never another day/fixture.
        var eventLink = Element.hierarchy.parent?.hierarchy.parent;
        if (eventLink?.name == "EventLinkSwitch"
            && eventLink.hierarchy.parent?.name == "CalendarEventBindingExpect")
        {
            var caption = new System.Collections.Generic.List<string>();
            CollectAllTexts(eventLink.hierarchy.parent, caption);
            if (caption.Count > 0) return string.Join(", ", caption);
        }

        if (NativeActionRoot() != null)
        {
            var content = new System.Collections.Generic.List<string>();
            CollectTextsTagsLast(content);
            if (content.Count > 0) return string.Join(", ", content);
        }

        var responseLabel = NativeButtonCommand.ActionLabel(Element.TryCast<SI.Bindable.SIButton>());
        if (responseLabel != null) return responseLabel;

        var actionCaption = GenericActionCaption();
        if (actionCaption != null) return actionCaption;

        var findingCaption = KeyFindingCaption();
        if (findingCaption != null) return findingCaption;

        var favouriteClubCaption = FavouriteClubAddCaption();
        if (favouriteClubCaption != null) return favouriteClubCaption;

        var iconLabel = TryGetIconLabel();
        if (iconLabel != null) return iconLabel;

        // A recognized action icon must win over the adjacent row's contents
        // (for example Delete beside a contract amount). Use the shared row
        // caption only when the compact template has no known icon meaning.
        if (Element.name == "buttons-controls-pc-default")
        {
            var caption = FindWrappedRowText();
            if (!string.IsNullOrWhiteSpace(caption)) return caption;
        }

        // The entity's bound caption is more specific than the generic title
        // of its destination panel (for example BWSL rather than Overview).
        // A reused ClubButton can sit beside a PLAYER link (clause tables).
        // Its own bound club/competition must win over that adjacent caption.
        if (Element.name == "ClubButton")
        {
            var currentCaption = PairedEntityLink.CurrentCaption(Element.TryCast<SI.Bindable.SIButton>());
            if (currentCaption != null) return currentCaption;
            var club = NativeEntityName(out bool boundEntity);
            if (club != null) return club;
            if (boundEntity) return UnlabeledIconButton; // real name still pending
        }
        var linkedName = FindNavigableLinkLabel();
        if (linkedName != null) return linkedName;
        var nativeCommand=NativeButtonCommand.Label(Element.TryCast<SI.Bindable.SIButton>());
        if(nativeCommand!=null)return nativeCommand;

        // FM27's image-only promo button carries its actual destination in the native
        // OpenURL event, rather than a text child. This also covers other image links.
        var destination=NativeExternalLink.Read(Element.TryCast<SI.Bindable.SIButton>());
        if(destination!=null)return "Open external link: "+destination;

        return UnlabeledIconButton;
    }

    private string FavouriteClubAddCaption()
    {
        // The biography's add-row command puts its localized caption beside
        // the icon button. Keep this exact component's text ahead of icon names.
        var template = Element.parent;
        var row = template?.parent;
        if (Element.name != "buttons-controls-pc-default" || row?.name != "add-favourite-club"
            || !ElementBridge.IsInPanel(Element, "ManagerPersonaDetailsScreenContainer")) return null;
        TextElement caption = null;
        int visible = 0;
        for (int i = 0; i < row.hierarchy.childCount; i++)
        {
            var child = row.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            visible++;
            if (child.Pointer == template.Pointer) continue;
            if (caption != null || child.TryCast<SI.Bindable.SIText>() == null) return null;
            caption = child.TryCast<TextElement>();
        }
        return visible == 2 && !string.IsNullOrWhiteSpace(caption?.text) ? caption.text : null;
    }

    private string GenericActionCaption()
    {
        // The shared action widget hides its own bound caption when displaying
        // only its icon. That caption still names this command (not an adjacent
        // table cell or a tooltip). Do not relax visibility for ordinary content.
        VisualElement widget = null;
        for (int i = 0; i < Element.hierarchy.childCount; i++)
            if (Element.hierarchy[i].name == "GenericActionButtonDataWidget")
                widget = Element.hierarchy[i];
        if (widget == null) return null;
        string caption = null;
        bool ambiguous = false;
        ReadActionCaption(widget, ref caption, ref ambiguous);
        return ambiguous ? null : caption;
    }

    private string KeyFindingCaption()
    {
        bool inDataPoint = false;
        for (var parent = Element.parent; parent != null; parent = parent.parent)
        {
            if (parent.name == "DataPointButtonFrame") inDataPoint = true;
            if (parent.name == "KeyFindingsRowExpect" && inDataPoint)
            {
                var title = FindByName(parent, "Title");
                return title != null && ElementBridge.IsVisible(title)
                    ? title.TryCast<TextElement>()?.text : null;
            }
            if (parent.GetIl2CppType().Name is "Panel" or "Cell") break;
        }
        return null;
    }

    private static void ReadActionCaption(VisualElement root, ref string caption, ref bool ambiguous)
    {
        if (root.name == "TooltipVisible") return;
        if (root.GetIl2CppType().Name == "SIText")
        {
            var text = root.TryCast<TextElement>()?.text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (caption != null) ambiguous = true;
                else caption = text;
            }
            return;
        }
        for (int i = 0; i < root.hierarchy.childCount; i++)
            ReadActionCaption(root.hierarchy[i], ref caption, ref ambiguous);
    }

    private string PairedNationCaption()
    {
        // A compact entity row can put its flag inside a sole-child wrapper
        // beside one linked country caption. Stay inside that same row.
        var flagWrapper = Element.parent;
        if (flagWrapper?.hierarchy.childCount == 1 && flagWrapper.parent is { } flagRow
            && flagRow.hierarchy.childCount == 2)
            for (int i = 0; i < flagRow.hierarchy.childCount; i++)
            {
                var sibling = flagRow.hierarchy[i];
                if (sibling.Pointer == flagWrapper.Pointer || !ElementBridge.IsVisible(sibling)) continue;
                var linkedCaption = sibling.TryCast<TextElement>()?.text;
                if (!string.IsNullOrWhiteSpace(linkedCaption) && SITextLink.HasLink(linkedCaption))
                    return linkedCaption;
            }
        // The shared club header places a flag hit surface and its linked
        // country text in the same nation-frame. Stay inside that frame;
        // a wider entity-header search can borrow the club's name instead.
        var frame = Element.parent;
        for (int depth = 0; depth < 8 && frame != null; depth++, frame = frame.parent)
        {
            if (frame.GetIl2CppType().Name == "NavigableLink")
                return FindVisibleLinkedCaption(frame);
            if (frame.name == "nation-frame") break;
            if (frame.GetIl2CppType().Name is "Cell" or "ComponentElement" or "Panel") return null;
        }
        if (frame?.name != "nation-frame") return null;
        string caption = null;
        for (int i = 0; i < frame.hierarchy.childCount; i++)
        {
            var child = frame.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            var text = child.TryCast<TextElement>()?.text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (caption != null) return null;
            caption = text;
        }
        return caption;
    }

    private string NativeEntityName() => NativeEntityName(out _);

    private string NativeEntityName(out bool boundEntity)
    {
        boundEntity = false;
        var events = Element.TryCast<SI.Bindable.SIButton>()?.Events;
        if (events == null) return null;
        var bindings = SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
        if (bindings == null) return null;
        for (int i = 0; i < events.Count; i++)
        {
            var key = events[i].m_sourceKey;
            if (!bindings.TryGetNode(ref key, out var node)) continue;
            var entity = Server.BindingRecords.FindNode(node, "Nation", 0);
            // Conditional nation links bind their booleans in a nested node,
            // two levels below the same entity component's Nation reference.
            // Do not search the message/report tree or borrow another player's flag.
            if (entity == null && Element.name == "NationButton"
                && events[i].TryCast<FM.UI.OpenPanelOrPanelEventContext>() != null)
            {
                var parent = node.Parent;
                for (int depth = 0; depth < 2 && parent != null; depth++, parent = parent.Parent)
                {
                    var nation = Server.BindingRecords.FindNode(parent, "Nation", 0);
                    if (nation != null && Server.BindingRecords.ValueOf(nation)?.Get()?.TryCast<FM.UI.NationReference>() != null)
                    { entity = nation; break; }
                }
            }
            if (entity == null && Element.name == "ClubButton")
            {
                // The same entity can be bound as both Club and activeclub.
                // Equal record identities are aliases, not two ambiguous
                // destinations. Distinct records remain ambiguous.
                int? identity = null;
                foreach (var child in node.Children)
                {
                    var value = Server.BindingRecords.ValueOf(child);
                    var reference = value?.Get()?.TryCast<FM.UI.DatabaseRecordReference>();
                    if (reference == null) continue;
                    if (identity.HasValue && identity.Value != reference.CombinedIndexAndType)
                    { entity = null; break; }
                    identity = reference.CombinedIndexAndType;
                    entity = child;
                }
            }
            if (entity == null) continue;
            boundEntity = true;
            return Server.PlayerNameRequests.NameForEntity(key,
                Server.BindingRecords.ValueOf(entity), Element);
        }
        return null;
    }

    private string FindNavigableLinkLabel()
    {
        // The shared portrait/name component can split the image hit surface
        // from its linked caption. Both are the two children of one binding
        // remapper; do not climb to a card containing other people or actions.
        var paired = Element.parent;
        if (paired?.GetIl2CppType().Name == "BindingRemapper"
            && paired.hierarchy.childCount == 2 && FindByName(Element, "PersonPicture") != null)
        {
            var portraitName = FindVisibleLinkedCaption(paired);
            if (portraitName != null) return portraitName;
        }
        // FM27's entity-link component separates its image button from its text
        // button. Read the caption of that same native component, not another
        // table column, page heading, or a translated-caption routing rule.
        bool inLink = false;
        bool imageComponent = false;
        var current = Element.parent;
        for (int depth = 0; depth < 10 && current != null; depth++, current = current.parent)
        {
            var type = current.GetIl2CppType().Name;
            if (type == "Cell" || type == "Panel") break;
            if (type == "ComponentElement" && Element.name is "ClubButton" or "NationButton"
                && FindDescendant(current, "SIImage") != null)
                imageComponent = true;
            if (type == "NavigableLink") inLink = true;
            // FM27 also places a crest component and its NavigableLink in
            // adjacent branches of the same entity wrapper (including cells).
            // Stop at the cell/panel boundary; never borrow another column.
            if (imageComponent)
            {
                // Report headers separate their crest and plain entity name
                // into LogoFrame/ContentFrame siblings. Resolve that caption
                // before reaching unrelated links lower in the same header.
                if (current.name == "LogoFrame" && current.parent != null)
                    for (int i = 0; i < current.parent.hierarchy.childCount; i++)
                    {
                        var sibling = current.parent.hierarchy[i];
                        if (sibling.name != "ContentFrame") continue;
                        var captions = new System.Collections.Generic.List<string>();
                        CollectAllTexts(sibling, captions);
                        if (captions.Count > 0) return captions[0];
                    }
                var linkedText = FindVisibleLinkedCaption(current);
                if (linkedText != null) return linkedText;
            }
            if (!inLink) continue;
            if (type != "NavigableLink" && type != "ComponentElement") continue;
            var texts = new System.Collections.Generic.List<string>();
            CollectAllTexts(current, texts);
            if (texts.Count > 0) return texts[0];
            if (type == "ComponentElement") break;
        }
        return null;
    }

    private static string FindVisibleLinkedCaption(VisualElement element)
    {
        if (!ElementBridge.IsVisible(element)) return null;
        var text = element.TryCast<TextElement>()?.text;
        if (!string.IsNullOrWhiteSpace(text) && SITextLink.HasLink(text)) return text;
        for (int i = 0; i < element.hierarchy.childCount; i++)
        {
            var caption = FindVisibleLinkedCaption(element.hierarchy[i]);
            if (caption != null) return caption;
        }
        return null;
    }

    // The game's single flow-advance button ("continue-button") relabels itself with the next
    // step's name. Only the post-match "Latest Results" step reads like a link to information
    // rather than the way forward, so only that label gets the prefix; every other label the
    // button takes ("Continue", the dated continue states, team-talk and match-flow steps) is
    // spoken as the game names it. Composed at read time from the live text, so a flow-step
    // change can never leave a stale destination.
    private const string ContinueButtonName = "continue-button";
    private const string AdvancePrefix = "Advance to ";
    private const string AdvanceTargetLabel = "Latest Results";

    private void ApplyContinueAdvancePrefix(System.Collections.Generic.List<string> texts)
    {
        if (Element.name != ContinueButtonName || texts.Count == 0) return;
        var cleaned = Speech.TextFilters.CleanForDisplay(texts[0]);
        if (!string.Equals(cleaned, AdvanceTargetLabel, System.StringComparison.OrdinalIgnoreCase)) return;
        texts[0] = AdvancePrefix + cleaned;
    }

    public override string GetAnnouncement()
    {
        if (IsReadonlyNativeContent) return GetLabel();
        var texts = new System.Collections.Generic.List<string>();
        CollectTextsTagsLast(texts);
        ApplyContinueAdvancePrefix(texts);

        if (texts.Count == 0)
            texts.Add(GetLabel());

        var state = GetState();
        var parts = new System.Collections.Generic.List<string>();
        parts.Add(texts[0]);
        parts.Add(Role);
        for (int i = 1; i < texts.Count; i++)
            parts.Add(texts[i]);
        if (!string.IsNullOrEmpty(state))
            parts.Add(state);
        return string.Join(", ", parts);
    }

    // A plain content button splits its label for the HTML surface: the primary (first
    // collected text, matching GetAnnouncement's texts[0]) is shown; the rest is the
    // description, read on demand. Subclasses whose label is a single computed string or a
    // structured announcement (FormButton, MessageButton, the player card, ...) set this
    // false so their whole label renders, with no secondary. A new such subclass that wants
    // the full label must override this too.
    protected virtual bool SplitsLabel => true;

    // Both re-collect live rather than caching a split.
    public override string GetPrimaryLabel()
    {
        if (!SplitsLabel) return GetLabel();
        var texts = new System.Collections.Generic.List<string>();
        CollectTextsTagsLast(texts);
        ApplyContinueAdvancePrefix(texts);
        return texts.Count > 0 ? texts[0] : GetLabel();
    }

    public override string GetDescription()
    {
        if (!SplitsLabel) return "";
        var texts = new System.Collections.Generic.List<string>();
        CollectTextsTagsLast(texts);
        if (texts.Count <= 1) return "";
        return string.Join(", ", texts.GetRange(1, texts.Count - 1));
    }

    // The game's Speak-To answer template prefixes a dialogue option with a category tag chip
    // ("Target", conversation tone tags) inside a "...-button-tag" frame that comes first in
    // document order. Collected verbatim, the chip takes the primary-label slot and demotes the
    // sentence — the only text distinguishing the options — to a description too long to inline
    // (the Set Target quick chat rendered as three identical "Target" buttons). Tag-frame texts
    // are therefore collected apart and appended after the content texts: the sentence leads on
    // both surfaces, and the short chip folds in behind it.
    private const string TagFrameNameSuffix = "-button-tag";

    // A news tile's headline ("newsHeadlineText") leads on both surfaces. The square news-tile
    // template puts the embedded match card before the headline in document order, so the first
    // collected text was the card's competition name ("CPL Shield") and the headline — the only
    // text identifying the story — sank into the description behind Alt+I. Lead-named texts are
    // collected apart and prepended, mirroring the tag-frame mechanism in the other direction.
    private const string LeadTextNodeName = "newsHeadlineText";

    private void CollectTextsTagsLast(System.Collections.Generic.List<string> texts)
    {
        var leadTexts = new System.Collections.Generic.List<string>();
        var tagTexts = new System.Collections.Generic.List<string>();
        CollectTextsSplittingTags(NativeActionRoot() ?? Element, texts, leadTexts, tagTexts, ExcludedTextNodeNames, ExcludedSubtreeTypeNames);
        if (leadTexts.Count > 0)
            texts.InsertRange(0, leadTexts);
        texts.AddRange(tagTexts);
        var radial = OwnRadialGauge(Element);
        if (radial != null) texts.Add(new RadialGaugeAdapter(radial).GetState());
        MergeColonSeparators(texts);
        if (Element.parent?.parent?.name == "BindingRoot_SquadOverviewPlayerBlock")
        {
            var player = Server.MatchSquadReadout.RowPlayerName(Element);
            if (!string.IsNullOrEmpty(player) && !texts.Exists(text => Speech.TextFilters.CleanForDisplay(text).Contains(player)))
                texts.Insert(System.Math.Min(1, texts.Count), player);
        }
    }

    private VisualElement NativeActionRoot()
    {
        // FM27's quick-instruction component puts its hit surface beside the
        // native title and pending/active pill. Read that same component, as
        // for the shared action widget, without borrowing another row's text.
        if (Element.name == "toggle-overlay" && Element.parent?.name == "tweak-header"
            && Element.parent.parent?.name == "tweak-row") return Element.parent;
        if (Element.name == "expand-toggle" && Element.parent?.name == "player-header"
            && Element.parent.parent?.name == "player-row") return Element.parent;
        if (Element.name != "action-button") return null;
        for (var parent = Element.parent; parent != null; parent = parent.parent)
        {
            var type = parent.GetIl2CppType().Name;
            if (type == "FMActionButton") return parent;
            if (type is "Panel" or "Cell") break;
        }
        return null;
    }

    internal override System.Collections.Generic.IReadOnlyList<string> FoldedTexts()
    {
        if (NativeActionRoot() == null) return System.Array.Empty<string>();
        var texts = new System.Collections.Generic.List<string>();
        CollectTextsTagsLast(texts);
        return texts;
    }

    // Some tile templates draw a label, a literal ":" separator element, and a value as three
    // sibling text nodes (the finance tiles' "Turnover : $220K" rows). Collected as separate
    // fragments they read ", :, " — join each into the single "label: value" fragment the
    // sighted player sees; a colon with no neighbour on one side is dropped.
    private static void MergeColonSeparators(System.Collections.Generic.List<string> texts)
    {
        for (int i = texts.Count - 1; i >= 0; i--)
        {
            if (texts[i] == null || texts[i].Trim() != ":") continue;
            if (i > 0 && i < texts.Count - 1)
            {
                texts[i - 1] = texts[i - 1].TrimEnd() + ": " + texts[i + 1].TrimStart();
                texts.RemoveRange(i, 2);
            }
            else
            {
                texts.RemoveAt(i);
            }
        }
    }

    private static void CollectTextsSplittingTags(
        VisualElement root,
        System.Collections.Generic.List<string> texts,
        System.Collections.Generic.List<string> leadTexts,
        System.Collections.Generic.List<string> tagTexts,
        System.Collections.Generic.ISet<string> skipNames,
        System.Collections.Generic.ISet<string> skipTypes)
    {
        var name = root.name;
        if (skipNames != null && skipNames.Contains(name))
            return;

        if (!string.IsNullOrEmpty(name) && name.EndsWith(TagFrameNameSuffix))
        {
            CollectAllTexts(root, tagTexts, skipNames);
            return;
        }

        try
        {
            var textEl = root.TryCast<TextElement>();
            if (textEl != null && !string.IsNullOrEmpty(textEl.text))
            {
                (name == LeadTextNodeName ? leadTexts : texts).Add(textEl.text);
                return;
            }
        }
        catch (System.Exception e) { Speech.Log.Warn($"TryCast<TextElement>() failed in CollectTextsSplittingTags: {e.GetType().Name}"); }

        // Skip hidden subtrees, like every other collector (CollectTextsExcluding,
        // CollectAllChildTextRecursive): hidden text is text the sighted player cannot see, so
        // reading it can duplicate a label (a scorer row's Left/Right layout branches, one always
        // hidden) or announce data the game is not displaying.
        int count = root.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = root.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            if (skipTypes != null && skipTypes.Contains(child.GetIl2CppType().Name)) continue;
            CollectTextsSplittingTags(child, texts, leadTexts, tagTexts, skipNames, skipTypes);
        }
    }

    // A tile's thumbnail drawing and the captions that only make sense looking at it: a pass map's
    // shirt numbers (the player dots), a momentum chart's "1st Half / 2nd Half" axis and its team
    // key, a zone map's per-zone percentages (whose zone is only where they sit on the pitch). Read
    // into a tile's label they are a run of bare numbers and words that say nothing; the tile's
    // title, fixture and verdict carry its meaning, and the card it opens reads the drawing in full.
    private static readonly System.Collections.Generic.HashSet<string> DrawingTypes = new() { "FMPassMapPanel" };
    private static readonly System.Collections.Generic.HashSet<string> DrawingCaptionNames = new() { "Half_legend", "home-away-graph-legend", "zone frame" };

    protected override System.Collections.Generic.ISet<string> ExcludedTextNodeNames => DrawingCaptionNames;
    protected override System.Collections.Generic.ISet<string> ExcludedSubtreeTypeNames => DrawingTypes;

    public override string GetState()
    {
        var parts = new System.Collections.Generic.List<string>(2);

        if (Element.name == "expand-toggle" && Element.parent?.name == "player-header"
            && Element.parent.parent?.name == "player-row")
        {
            var list = FindByName(Element.parent.parent, "player-tweak-list");
            if (list != null) parts.Add(ElementBridge.IsVisible(list) ? "expanded" : "collapsed");
        }
        if (Element.name == "toggle-overlay" && Element.parent?.name == "tweak-header"
            && Element.parent.parent?.name == "tweak-row")
        {
            var indicator = FindByName(Element.parent.parent, "state-indicator");
            if (indicator != null && ElementBridge.IsVisible(indicator)) parts.Add("selected");
        }

        var stepperInfo = TryGetStepperValue();
        if (stepperInfo != null)
            parts.Add(stepperInfo);

        if (IsSelectedInButtonGroup() || IsSelectedByActiveToggle() || IsSelectedByActiveTabClass()
            || IsSelectedBySegmentedStyle()
            || GridSelectorSelection.IsNativeListEntrySelected(Element))
            parts.Add("selected");
        // The warning-cell template (data_display-cell-standard-warning: contract Transfer Rules,
        // Availability, ...) flags a problem with a triangle icon beside its text. The icon is the
        // whole signal — carry it as state so both surfaces read "Transfer Rules, warning".
        if (HasVisibleWarningIcon())
            parts.Add("warning");
        // The tile template's padlock (a Player Instructions tile whose instruction the game has
        // locked for the player's role or position, and any other tile the game padlocks). State,
        // not disabled: the game keeps a locked tile clickable, and opening it is how the value,
        // description and the game's own lock explanation are read.
        if (HasVisibleLockIcon())
            parts.Add("locked");
        if (!Element.enabledInHierarchy && !VirtualHistoryAvailable())
            parts.Add("disabled");

        return string.Join(", ", parts);
    }

    // A button may own a radial value as well as its caption (the tactics
    // intensity/complexity headers). Nested commands and tooltips belong to
    // their own contexts; ambiguous multiple gauges are not one button value.
    internal static VisualElement OwnRadialGauge(VisualElement root)
    {
        VisualElement found = null;
        bool multiple = false;
        void Read(VisualElement owner, int depth)
        {
            if (depth > 12 || multiple) return;
            for (int i = 0; i < owner.hierarchy.childCount; i++)
            {
                var child = owner.hierarchy[i];
                if (!ElementBridge.IsVisible(child)) continue;
                var type = child.GetIl2CppType().Name;
                if (type is "SIButton" or "Tooltip") continue;
                if (type == "SIRadialGauge")
                { if (found != null) multiple = true; else found = child; }
                else Read(child, depth + 1);
            }
        }
        Read(root, 0);
        return multiple ? null : found;
    }

    internal bool VirtualHistoryAvailable()
    {
        if (!ElementBridge.IsInPanel(Element, "OverviewTitleBar")) return false;
        var direction = Element.name switch { "BackButton" => "back", "ForwardButton" => "forward", _ => null };
        if (direction == null) return false;
        if (Server.ReplacedMessageHistory.HasVirtualMove(direction)) return true;
        Server.MessageViewSurface.ObserveHistory();
        var live = Server.GameHistory.TryRead();
        return direction == "back" ? Server.MessageViewSurface.IsOpen()
            : live != null && !Server.MessageViewSurface.IsOpen()
                && FM27PanelCatalog.GetForegroundPanel()?.name == "PortalMessagesTool"
                && FM27Access.Navigation.MessageViewHistory.Current.SlotAt(live.Value.Index) != null;
    }

    private bool HasVisibleWarningIcon()
    {
        var slot = FindByName(Element, "warning-icon-visible");
        if (slot == null || !ElementBridge.IsVisible(slot)) return false;
        int n = slot.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            if (ElementBridge.IsVisible(slot.hierarchy[i])) return true;
        return false;
    }

    // Same slot pattern as the warning icon: the "lock-visible" wrapper is always visible in the
    // tile template, and the game populates it with the padlock only when the lock applies.
    private bool HasVisibleLockIcon()
    {
        var slot = FindByName(Element, "lock-visible");
        if (slot == null || !ElementBridge.IsVisible(slot)) return false;
        int n = slot.hierarchy.childCount;
        for (int i = 0; i < n; i++)
            if (ElementBridge.IsVisible(slot.hierarchy[i])) return true;
        return false;
    }

    private string TryGetStepperValue()
    {
        var stepper = FindDescendant(Element, "SIStepper");
        if (stepper == null) return null;
        // Tile templates carry a stepper whether or not the tile uses one (the Club screen's tiles do),
        // and a hidden one has no value the player can see — reporting it would describe something that
        // is not on screen, and its empty step list would cry "unavailable" on every read.
        if (!ElementBridge.IsVisible(stepper)) return null;

        PrivateFieldCache.EnsureInitialized();
        if (PrivateFieldCache.StepperSteps == null)
            return Speech.Degrade.Value($"[ButtonAdapter] SIStepper.m_steps contract missing, cannot read \"{Element.name}\"");

        var castStepper = stepper.Cast<SI.Bindable.SIStepper>();
        var steps = (Il2CppSystem.Collections.Generic.List<VisualElement>)PrivateFieldCache.StepperSteps.GetValue(castStepper);
        // A tile template carries a stepper whether or not the tile has one to show, and an unused one
        // has no step list at all (the Club screen's tiles, on every visit). There is no value here to
        // report or to miss, so say nothing — exactly as for a button with no stepper in it. Reading
        // this without the check is what threw a NullReferenceException inside the panel-load
        // announcement and cost that screen its entire arrival announcement.
        if (steps == null) return null;

        int totalSteps = steps.Count;
        int filledSteps = CountFilledSteps(steps);
        return $"{filledSteps} of {totalSteps}";
    }

    internal static int CountFilledSteps(Il2CppSystem.Collections.Generic.List<VisualElement> steps)
    {
        int filled = 0;
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].resolvedStyle.backgroundColor.a > 0.5f)
                filled++;
        }
        return filled;
    }

    internal static VisualElement FindDescendant(VisualElement root, string typeName)
    {
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var child = root.hierarchy[i];
            if (child.GetIl2CppType().Name == typeName)
                return child;
            var found = FindDescendant(child, typeName);
            if (found != null) return found;
        }
        return null;
    }

    private bool IsSelectedByActiveToggle()
    {
        var toggle = FindByName(Element, "active-toggle");
        return toggle != null && ElementBridge.IsVisible(toggle);
    }

    // The game's tab-active styling: a button styled as the selected tab gets this USS class, applied
    // by a wrapping SIStyleSetter (e.g. the suggested-team card's "IPButtonSelected"/"OOPButtonSelected"
    // setters drive the IP/OOP toggle this way rather than through a ButtonGroup). The setter adds the
    // class to its own element — the button's wrapper — so check the button and its immediate parent.
    private const string ActiveTabClass = "tabgroup__tab--active";

    private bool IsSelectedByActiveTabClass()
    {
        if (HasClass(Element, ActiveTabClass)) return true;
        var parent = Element.hierarchy.parent;
        return parent != null && HasClass(parent, ActiveTabClass);
    }

    // FM27's segmented buttons use a style setter rather than ButtonGroup.
    // Read the classes actually applied by that immediate wrapper; the native
    // active family covers leading/middle/trailing positions and native sizes.
    // Preserve the original button/selected-state HTML and live-value path.
    protected bool IsSelectedBySegmentedStyle()
    {
        var parent = Element.hierarchy.parent;
        var setter = parent?.TryCast<SI.Bindable.SIStyleSetter>();
        var classes = setter?.m_addedClasses;
        if (classes == null) return false;
        for (int i = 0; i < classes.Count; i++)
        {
            string style = classes[i];
            if (style != null && style.StartsWith("buttons-segmented-button-row-active-", System.StringComparison.Ordinal)
                && (HasClass(Element, style) || HasClass(parent, style)))
                return true;
        }
        return false;
    }

    private static bool HasClass(VisualElement el, string cls)
    {
        try { return el.ClassListContains(cls); }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[ButtonAdapter] ClassListContains('{cls}') failed: {e.GetType().Name}");
            return false;
        }
    }

private bool IsSelectedInButtonGroup()
    {
        var buttonGroup = FindAncestorButtonGroup();
        if (buttonGroup == null)
            return false;

        try
        {
            var bg = buttonGroup.Cast<SI.Bindable.ButtonGroup>();
            var selectedStyle = bg.SelectedStyle;

            if (!string.IsNullOrEmpty(selectedStyle))
                return Element.ClassListContains(selectedStyle);

            // Fallback: index-based matching (unreliable when hidden slots exist)
            int selectedIndex = bg.GetSelectedIndex();
            var allButtons = new System.Collections.Generic.List<VisualElement>();
            CollectSIButtons(buttonGroup, allButtons);

            return selectedIndex >= 0 && selectedIndex < allButtons.Count
                && allButtons[selectedIndex].Pointer == Element.Pointer;
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[ButtonAdapter] Failed to read ButtonGroup selected state: {e.GetType().Name}");
            return false;
        }
    }

    private VisualElement FindAncestorButtonGroup()
    {
        var current = Element.hierarchy.parent;
        while (current != null)
        {
            if (current.GetIl2CppType().Name == "ButtonGroup")
                return current;
            current = current.hierarchy.parent;
        }
        return null;
    }

    private static void CollectSIButtons(VisualElement root, System.Collections.Generic.List<VisualElement> buttons)
    {
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var child = root.hierarchy[i];
            if (child.GetIl2CppType().Name == "SIButton")
                buttons.Add(child);
            else
                CollectSIButtons(child, buttons);
        }
    }

    public override void Activate()
    {
        if (Element.name == "BackButton")
            Patches.PanelHistoryBackPatch.AllowNextBackEvent = true;

        // Any activated button may legitimately close its screen through the game's history back
        // event (contract negotiation "Exit Talks", quick chat "End Chat"); without this window
        // PanelHistoryBackPatch swallows that and the button silently does nothing.
        Patches.PanelHistoryBackPatch.AllowBackThroughNextFrame();

        // GridSelector cards (team/nation pickers in a VirtualisedList) are chosen through the
        // list's selection model; a plain click leaves the list's default item selected. Route
        // them there so companion /activate selects the same way in-game Enter does. Restricted to
        // the exact generic ButtonAdapter: button subclasses (presets, info, message, ...) share the
        // same GridSelector shape but are driven by their own click command, not list selection.
        if (GetType() == typeof(ButtonAdapter) && GridSelectorSelection.TrySelect(Element, out _))
            return;

        var typeName = Element.GetIl2CppType().Name;
        if (typeName == "SIButton")
        {
            Server.MatchTacticalPanelOwnership.BeforeActivate(Element.Cast<SI.Bindable.SIButton>());
            Server.NativeEditSessionRefresh.BeforeActivate(Element.Cast<SI.Bindable.SIButton>());
            // FM27's native operation owns its command and click lifecycle.
            // Sending a supplementary ClickEvent can enter the same callback
            // again after native state changed (the shortcut rebinder crashed
            // inside SendEvent). Attachment alone does not make that safe.
            Element.Cast<SI.Bindable.SIButton>().SimulateButtonClick();
        }
        else
        {
            var evt = new NavigationSubmitEvent();
            Element.SendEvent(evt);
        }
    }

    // The in-match play/pause toggle is icon-only, and its icon IS the live playback state: the
    // game swaps a play and a pause sprite as the match runs and halts. Compose the label from
    // the sprite at read time so the player hears the available action and the current state in
    // one breath ("Pause (match playing)"); the bare template name ("playpause") said neither.
    // The icon shows the available ACTION (pause bars while the match runs), so the state clause
    // is its inverse. A sprite naming neither state — or both, like the button's own name — is
    // untrusted: fall back to the neutral action pair (honest, just less informative) and log
    // once. Kept in Plugin rather than Navigation so it stays hot-reloadable.
    private const string PlayPauseButtonName = "playpause";

    private string PlayPauseLabel()
    {
        int childCount = Element.hierarchy.childCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = Element.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;
            var sprite = SpriteHelper.ReadSpriteName(child);
            if (string.IsNullOrEmpty(sprite)) continue;
            var s = sprite.ToLowerInvariant();
            bool pause = s.Contains("pause");
            bool play = s.Contains("play");
            if (pause && !play) return "Pause (match playing)";
            if (play && !pause) return "Play (match paused)";
            if (_loggedUnknownIcons.Add(sprite))
                Speech.Log.Warn($"[ButtonAdapter] playpause sprite \"{sprite}\" names neither state; using neutral label.");
        }
        return "Play/Pause";
    }

    private string TryGetIconLabel()
    {
        int childCount = Element.hierarchy.childCount;
        List<string> probed = null;
        for (int i = 0; i < childCount; i++)
        {
            var child = Element.hierarchy[i];
            if (!ElementBridge.IsVisible(child)) continue;

            // Try reading the background image asset name from the child
            string assetName = null;
            try
            {
                var bg = child.resolvedStyle.backgroundImage;
                assetName = bg.sprite?.name
                    ?? bg.vectorImage?.name
                    ?? bg.texture?.name;
            }
            catch
            {
                // resolvedStyle access can fail in IL2CPP
            }

            var childName = child.name;
            (probed ??= new List<string>()).Add($"childName=\"{childName}\" assetName=\"{assetName}\"");

            // Try asset name, then child element name
            if (!string.IsNullOrEmpty(assetName))
            {
                var mapped = MapIconToLabel(assetName);
                if (mapped != null) return mapped;
            }
            if (!string.IsNullOrEmpty(childName))
            {
                var mapped = MapIconToLabel(childName);
                if (mapped != null) return mapped;
            }
        }

        var ownName = Element.name;
        if (!string.IsNullOrEmpty(ownName))
        {
            var mapped = MapIconToLabel(ownName);
            if (mapped != null) return mapped;
        }

        // No icon mapped on an icon-only button. Surface each unrecognized icon once (like
        // /unclassified) instead of every frame, so a genuinely unknown icon stays visible in
        // /log without flooding it.
        if (probed != null)
        {
            string sig = string.Join(" | ", probed);
            if (_loggedUnknownIcons.Add(sig))
                Speech.Log.Warn($"[ButtonAdapter] Unmapped icon button: {sig}");
        }

        return null;
    }

    // Probe signatures already reported, so an unmapped icon logs once, not every frame.
    private static readonly HashSet<string> _loggedUnknownIcons = new();

    private static readonly Dictionary<string, string> IconLabels = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["x-icon"] = "Close",
        ["home_icon"] = "Home",
        ["cross_small_2x"] = "Close",
        ["ui-concepts-delete_small"] = "Delete",
        ["delete_small_2x"] = "Delete",
        // The pending-tactical-changes toast's pencil (opens tactics to review the queued changes).
        ["edit_small_2x"] = "Edit",
        ["game-concepts-scouting-magnifying-glass"] = "Search",
        ["chevron left_small_2x"] = "Back",
        ["chevron right_small_2x"] = "Forward",
        // The Squad Planner order column's reorder arrows; generic direction labels here, refined
        // to "Move up"/"Move down" by that panel's profile renames.
        ["chevron top_small_2x"] = "Up",
        ["chevron down_small_2x"] = "Down",
        // The Squad Planner row's remove action (drops the player from the position plan).
        ["remove from list_small_2x"] = "Remove from list",
        ["expand_small_2x"] = "Collapse/Expand Pitch View",
        ["stats_small_2x"] = "Stats",
        ["cog_small_2x"] = "Settings",
        ["previous-highlight_small_2x"] = "Previous Highlight",
        ["rewind_ten_seconds_small_2x"] = "Rewind 10 Seconds",
        ["skip-ten-seconds_small_2x"] = "Skip 10 Seconds",
        ["next-highlight_small_2x"] = "Next Highlight",
        ["player instructions_small_2x"] = "Player Instructions",
        ["game-concepts-fmpedia_medium"] = "FMPedia",
        ["game-concepts-fmpedia_small"] = "FMPedia",
        ["dashboard-hud-fmpedia-button"] = "FMPedia",
        ["advice-dugout_large"] = "Advice",
        ["dashboard-hud-fm-button"] = "FM Menu",
        ["ui-concepts-plus_small"] = "Plus icon",
        // Tactics pitch header (live-verified 2026-07-06): opens the Formation picker dialog
        // (the game's own dialog title).
        ["formation_small_2x"] = "Formation",
        // Opens the pitch display checkboxes (Role Movement Icons / Player Relationships /
        // Ability Rating). Generic key, so the label stays generic.
        ["view_small_2x"] = "View options",
        // The two pitch analysis toggles: position-suitability overlay, and the per-player
        // team-tactics/roles view.
        ["tactics-visualiser-suitability-positions_small_2x"] = "Position suitability view",
        ["team-tactics_small_2x"] = "Team tactics view",
    };

    private static string MapIconToLabel(string iconId)
    {
        if (IconLabels.TryGetValue(iconId, out var label))
            return label;
        // FM27 ships the same icon families at additional density scales.
        // Reuse the original semantic mapping regardless of the raster scale.
        var canonical = System.Text.RegularExpressions.Regex.Replace(iconId, @"_\d+x$", "_2x");
        if (IconLabels.TryGetValue(canonical, out label)) return label;
        return null;
    }
}

