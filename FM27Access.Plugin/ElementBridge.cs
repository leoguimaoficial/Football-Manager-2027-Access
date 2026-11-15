using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using FM27Access.Plugin.Contexts;
using UnityEngine.UIElements;

namespace FM27Access.Plugin;

internal static class ElementBridge
{
    internal static ElementClassification ClassifyDescriptor(in ElementDescriptor descriptor, VisualElement element = null)
    {
        // FM26's profile hides the duplicate substitutes strip by its named
        // ancestry. FM27 leaves the instance anonymous; the actual template
        // source still identifies this same strip. Omit its subtree on both
        // surfaces, retaining the separate native team-selection table.
        if (descriptor.TypeName == "TemplateContainer"
            && element?.TryCast<TemplateContainer>()?.templateSource?.name == "TacticalPlannerSubsBlock"
            && (IsInPanel(element, "TacticalPlannerTool")
                || IsInPanel(element, "FullScreenTeamSelection")))
            return ElementClassification.InternalChild;
        if (descriptor.TypeName == "FMSearchableCombobox")
            return ElementClassification.Dropdown;
        // The native standings direction is painted by a style setter rather
        // than SIImage. Reuse the existing informative-image stop on both
        // surfaces; ordinary style wrappers and decorative chevrons stay layout.
        if (descriptor.TypeName == "SIStyleSetter" && SpriteHelper.IsRankingArrow(element))
            return ElementClassification.Image;
        if (descriptor.TypeName == "SIImage" && SpriteHelper.IsFixturePlayerEvent(element))
            return ElementClassification.Image;
        // FM27 SIMenu is an always-realized menu/list, not SIDropdown.
        // Traverse its native entries and reuse their list-selection actions.
        // Treating its pointer as SIDropdown can crash in ShowDropdown.
        if (descriptor.TypeName == "SIMenu")
            return ElementClassification.LayoutWrapper;
        // The last-career tile writes the club/nation name beside its crest.
        // The image has no separate action or meaning; retain the actual text.
        if (descriptor.TypeName == "SIImage" && descriptor.ElementName is "Club" or "Nation"
            && element?.parent?.parent?.parent is { name: "club_container" } clubContainer)
        {
            var caption = FindDescendantNamed(clubContainer, "club_name_data");
            if (caption != null && IsVisible(caption)
                && !string.IsNullOrWhiteSpace(caption.TryCast<TextElement>()?.text))
                return ElementClassification.LayoutWrapper;
        }
        if (descriptor.TypeName == "SIStepper" && StepperAdapter.IsTacticalGaugeName(descriptor.ElementName))
            return ElementClassification.Stepper;
        if (descriptor.TypeName == "SIRadialGauge")
            return RadialGaugeAdapter.IsCaptionedWorkloadArtwork(element)
                ? ElementClassification.LayoutWrapper : ElementClassification.Gauge;
        if (descriptor.TypeName == "DateGrid")
            return ElementClassification.Calendar;
        if (descriptor.TypeName == "Calendar" && element?.TryCast<FM.UI.Widgets.Calendar>() != null)
            return ElementClassification.Calendar;
        // FM27's block is a BindableSection subclass, including legitimately
        // empty data-analysis blocks. Keep its child traversal/section contract.
        if (descriptor.TypeName == "BlockElement")
            return ElementClassification.Section;
        if (descriptor.TypeName is "BindableVariableSizedGridElement" or "TileSearchPanel" or "FMActionButton")
            return ElementClassification.LayoutWrapper;
        // These FM27 painters animate styles or draw the recurring-injury
        // overlay. Neither is an interactive widget. Use the common classifier
        // so HTML and speech both keep meaningful child text without a marker.
        if (descriptor.TypeName is "StyleTransitionLooper" or "RecurringInjuryOverlapDrawer")
            return ElementClassification.LayoutWrapper;
        // The compact chart's actual labelled series is read through the
        // existing chart-table renderer; its template/style children are not
        // an unknown interactive control.
        if(descriptor.TypeName=="FMSpiderChart")return ElementClassification.LayoutWrapper;
        var classification = ElementClassifier.Classify(descriptor);
        // FM27 can wrap a real embedded name link in an SIButton used only
        // for styling. The wrapper cannot submit and has no own command;
        // collapsing it to ButtonAdapter hides the link and acknowledges a
        // no-op. Walk its native link through the ordinary link adapter.
        if (classification == ElementClassification.Button && descriptor.TypeName == "SIButton"
            && element?.TryCast<SI.Bindable.SIButton>() is { CanSubmit: false }
            && !NativeButtonCommand.HasBoundCommand(element) && HasVisibleEmbeddedLink(element, 0))
            return ElementClassification.LayoutWrapper;
        // A readonly breadcrumb can repeat the current panel's own title.
        // Keep ancestor/path context and linked breadcrumbs; omit only the
        // same native title already announced/rendered as the page heading.
        if (classification == ElementClassification.Text && element?.parent?.name == "Breadcrumbs")
        {
            var text = Speech.TextFilters.CleanForDisplay(element.TryCast<TextElement>()?.text ?? "");
            for (var owner = element.parent; owner != null; owner = owner.parent)
            {
                var panel = owner.TryCast<SI.Bindable.Panel>();
                if (panel == null) continue;
                // FM27 persona/dialog panels can leave PanelID.Title unbound;
                // use the same native title resolver as the actual page heading.
                var title = Speech.TextFilters.CleanForDisplay(Server.ServerText.GetTitle(panel) ?? "");
                if (!string.IsNullOrWhiteSpace(text) && string.Equals(text, title, StringComparison.OrdinalIgnoreCase))
                    return ElementClassification.LayoutWrapper;
                break;
            }
        }
        // These shared tile images repeat the adjacent native caption. Keep
        // the written satisfaction/objective/entity information in reading.
        if (classification == ElementClassification.Image && element != null)
        {
            if (SpriteHelper.IsCaptionedRoleArtwork(element)
                || SpriteHelper.IsCaptionedPreferredFootArtwork(element)) return ElementClassification.LayoutWrapper;
            var parent = element.parent;
            // The shared individual-training tile writes both the direction
            // (Improvements/Regressions) and count beside these arrow glyphs.
            // Suppress only the redundant artwork while both native texts are
            // visible/bound. Other arrows or a meaningful tooltip stay readable.
            if (descriptor.TypeName == "SIImage" && descriptor.ElementName == "Arrow"
                && parent?.name == "Stat" && parent.hierarchy.childCount == 2
                && parent.parent?.name is "Improvements" or "Regressions"
                && parent.parent.hierarchy.childCount == 2
                && parent.parent.parent?.name == "IndividualTraining"
                && string.IsNullOrWhiteSpace(element.tooltip))
            {
                var value = FindDescendantNamed(parent, "Value");
                var title = FindDescendantNamed(parent.parent, "Title");
                if (value?.GetIl2CppType().Name == "SIText" && IsVisible(value)
                    && title?.GetIl2CppType().Name == "SIText" && IsVisible(title)
                    && !string.IsNullOrWhiteSpace(value.TryCast<TextElement>()?.text)
                    && !string.IsNullOrWhiteSpace(title.TryCast<TextElement>()?.text))
                    return ElementClassification.LayoutWrapper;
            }
            // The shared add-on browser paints a download placeholder when no
            // preview exists. It is neither content nor the adjacent preview
            // chooser; keep that actual button and all item metadata readable.
            if (descriptor.ElementName == "no-image-fallback"
                && parent?.name == "no-preview-switch"
                && parent.GetIl2CppType().Name == "BindableSwitchElement")
                return ElementClassification.LayoutWrapper;
            if (descriptor.ElementName == "no-preview-image"
                && parent?.name == "no-preview-image-visible"
                && parent.GetIl2CppType().Name == "SIVisible")
                return ElementClassification.LayoutWrapper;
            // Licensed calendar badges draw a background layer separately from
            // their informative competition icon. The alternate unlicensed
            // branch is just a style setter; neither background adds event data.
            if (descriptor.ElementName == "MiddleLayer" && parent?.name == "Licensed"
                && parent.parent?.parent?.name == "backgroundsetter")
            {
                // The same licensed backdrop also appears in a full-screen
                // fixture header, outside tiles/FixtureBackground wrappers.
                // Identify its native painted-layer schema rather than hiding
                // arbitrary images with the same name. The header's date,
                // competition, weather and actual entity links remain siblings.
                var bottom = FindDescendantNamed(parent, "BottomLayer");
                var top = FindDescendantNamed(parent, "TopLayer");
                if (bottom?.GetIl2CppType().Name == "SIStyleSetter"
                    && top?.GetIl2CppType().Name == "SIGradient")
                    return ElementClassification.LayoutWrapper;
                // The same native FixtureBackground is also used by inbox
                // attachments, outside calendar 4x4/8x4 tiles. Its middle
                // layer is background artwork, not the competition caption
                // or the fixture's actual links/data rendered beside it.
                if (parent.parent.parent.parent?.parent?.name == "FixtureBackground")
                    return ElementClassification.LayoutWrapper;
                for (var up = parent; up != null; up = up.parent)
                {
                    if (up.name is "4x4Tile" or "8x4Tile")
                    {
                        if (FindDescendantNamed(up, "CompetitionIcon") != null)
                            return ElementClassification.LayoutWrapper;
                        break;
                    }
                    if (up.GetIl2CppType().Name == "Panel") break;
                }
            }
            var caption = parent != null && descriptor.ElementName == "grade-value"
                ? FindDescendantNamed(parent, "grade-text") : null;
            // The shared native performance-feedback component writes its
            // complete assessment beside the thumbs glyph. Keep that text;
            // the glyph has no additional action or independent state.
            if(parent?.parent?.parent?.name=="HasPerformanceValue")
                caption=FindDescendantNamed(parent,"Label");
            if (descriptor.ElementName == "leading-image" && parent?.parent?.parent?.name == "hierarchy")
                caption = FindDescendantNamed(parent, "text");
            if (caption != null && IsVisible(caption)
                && !string.IsNullOrWhiteSpace(caption.TryCast<TextElement>()?.text))
                return ElementClassification.LayoutWrapper;
            // The shared registration tile writes its open/closed state next
            // to this badge. Suppress the duplicate only while that native
            // caption is visible and bound, in either inbox or dashboard use.
            if (descriptor.ElementName == "status_icon" && parent?.name == "reg_info")
            {
                var status = FindDescendantNamed(parent, "registration_status");
                if (status != null && IsVisible(status)
                    && !string.IsNullOrWhiteSpace(status.TryCast<TextElement>()?.text))
                    return ElementClassification.LayoutWrapper;
            }
            // Result attachments use the same weather badge with a written
            // description. The adjacent native text already includes weather
            // and temperature; the image adds no independent information.
            if (descriptor.ElementName == "WeatherIcon" && parent != null)
            {
                var description = FindDescendantNamed(parent, "WeatherDescription");
                if (description != null && IsVisible(description)
                    && !string.IsNullOrWhiteSpace(description.TryCast<TextElement>()?.text))
                    return ElementClassification.LayoutWrapper;
            }
            // A session header already writes the selected training session
            // beside its category artwork. Keep the native name instead of
            // exposing SessionIcon or repeating the category in speech/HTML.
            if (descriptor.ElementName == "SessionIcon" && parent?.name == "SessionHeader")
            {
                var session = FindDescendantNamed(parent, "SessionName");
                if (session != null && IsVisible(session)
                    && !string.IsNullOrWhiteSpace(session.TryCast<TextElement>()?.text))
                    return ElementClassification.LayoutWrapper;
            }
            // The shared match-talk header writes the competition beside its
            // badge. Keep the native caption/link and omit only its duplicate.
            if (descriptor.ElementName == "comp-icon" && parent?.name == "BottomInfo")
            {
                var competition = FindDescendantNamed(parent, "CompetitionName");
                if (competition != null && IsVisible(competition)
                    && !string.IsNullOrWhiteSpace(competition.TryCast<TextElement>()?.text))
                    return ElementClassification.LayoutWrapper;
            }
            if (descriptor.ElementName == "CompetitionIcon" && parent?.name == "CompetitionIconVisible"
                && parent.parent?.name == "BottomPanel")
                for (int i = 0; i < parent.parent.hierarchy.childCount; i++)
                    if (parent.parent.hierarchy[i].TryCast<TextElement>() is { } text
                        && IsVisible(text) && !string.IsNullOrWhiteSpace(text.text))
                        return ElementClassification.LayoutWrapper;
            if (descriptor.ElementName == "badges-generic-league-large"
                && parent != null && FindDescendantNamed(parent, "NationLink") != null)
                return ElementClassification.LayoutWrapper;
        }
        if (classification == ElementClassification.Image && descriptor.ElementName == "NationImage"
            && element?.hierarchy.parent?.name == "NationRatioFitter"
            && FindDescendantNamed(element.hierarchy.parent, "NationButton") != null)
            return ElementClassification.LayoutWrapper;
        // A split entity-link caption already labels its real image button.
        // Outside StreamedTable cells, do not read that same primary caption a
        // second time as loose text. Text-only player links retain their action.
        if (classification == ElementClassification.Text && descriptor.ElementName == "primary-text"
            && element?.hierarchy.parent?.GetIl2CppType().Name == "NavigableLink"
            && TableRowTextAdapter.FindTableRow(element) == null
            && LinkHasCommandButton(element.hierarchy.parent))
            return ElementClassification.LayoutWrapper;
        // In table cells the written entity link also carries the contextual
        // menu. Keep that original text-link command and omit its duplicate
        // image hit target only inside the same single-button native component.
        if (classification == ElementClassification.Button && descriptor.TypeName == "SIButton"
            && IsDuplicateTableLinkImage(element))
            return ElementClassification.LayoutWrapper;
        // SpeakTo's outer button only transfers navigation focus to its content.
        // It has no native command; keep the actual response buttons beneath it.
        if (classification == ElementClassification.Button && descriptor.TypeName == "SIButton"
            && element?.hierarchy.childCount == 1
            && element.hierarchy[0].name == "AllContainerFocusser"
            && element.TryCast<SI.Bindable.SIButton>()?.Events == null)
            return ElementClassification.LayoutWrapper;
        // This FM27 placeholder is an informative hierarchy badge, despite the
        // decorative prefix inherited from FM26. Promote only decoded assets.
        if (descriptor.TypeName == "SIImage" && descriptor.ElementName == "ui concepts-placeholder"
            && element != null && SpriteHelper.TryHierarchyLabel(SpriteHelper.ReadSpriteName(element)) != null)
        {
            // The shared hierarchy badge can also draw its complete official
            // caption beside the glyph. Keep that text once, in game language;
            // an icon-only badge must retain its informative decoded value.
            var hierarchyBadge = element.hierarchy.parent;
            if (hierarchyBadge?.hierarchy.childCount == 2)
                for (int i = 0; i < hierarchyBadge.hierarchy.childCount; i++)
                {
                    var sibling = hierarchyBadge.hierarchy[i];
                    if (sibling.Pointer != element.Pointer && IsVisible(sibling)
                        && !string.IsNullOrWhiteSpace(sibling.TryCast<TextElement>()?.text))
                        return ElementClassification.LayoutWrapper;
                }
            return ElementClassification.Image;
        }
        // These compact events identify their first involved player only by a
        // portrait. Elsewhere PersonImage keeps the inherited decorative rule.
        if (descriptor.TypeName == "SIImage" && CalendarEventAdapter.IsCompactPortrait(element))
            return ElementClassification.Image;
        if (descriptor.TypeName == "FMPitchPositionIcon" && element?.parent is { } position)
        {
            if (position.GetIl2CppType().Name == "SIVisible" && position.hierarchy.childCount == 1
                && position.parent is { } image && image.GetIl2CppType().Name == "SIImage"
                && image.hierarchy.childCount == 1 && string.IsNullOrWhiteSpace(image.tooltip)
                && image.parent is { } captionedImage) position = captionedImage;
            // The current position-header variant draws its complete native
            // position caption directly beside the readonly pitch glyph.
            if (string.IsNullOrWhiteSpace(element.tooltip) && position.hierarchy.childCount == 2)
                for (int i = 0; i < position.hierarchy.childCount; i++)
                {
                    var sibling = position.hierarchy[i];
                    if (sibling.Pointer != element.Pointer && sibling.TryCast<SI.Bindable.SIText>() is { } text
                        && IsVisibleInHierarchy(sibling) && !string.IsNullOrWhiteSpace(text.text)
                        && !SITextLink.HasLink(text.text)) return ElementClassification.LayoutWrapper;
                }
            var caption = FindDescendantNamed(position, "RoleName");
            if (caption != null && IsVisible(caption)
                && !string.IsNullOrWhiteSpace(caption.TryCast<TextElement>()?.text))
                return ElementClassification.LayoutWrapper;
        }
        if (classification == ElementClassification.Image && element?.parent?.parent?.name == "scaling-icons-form-small"
            && element.parent.parent.parent?.parent is { } moraleRow)
        {
            var caption = FindDescendantNamed(moraleRow, "Morale");
            if (caption != null && IsVisible(caption)
                && !string.IsNullOrWhiteSpace(caption.TryCast<TextElement>()?.text))
                return ElementClassification.LayoutWrapper;
        }
        // The shared body-language badge draws an emoji beside its localized text.
        // Only suppress the duplicate image when that same badge has a readable caption.
        if (classification == ElementClassification.Image && descriptor.ElementName is "BodyLanguageIcon" or "MatchBodyLanguageIcon"
            && element?.parent?.parent is { } badge)
        {
            var caption = FindDescendantNamed(badge, "text");
            if (caption != null && IsVisible(caption)
                && !string.IsNullOrWhiteSpace(caption.TryCast<TextElement>()?.text))
                return ElementClassification.LayoutWrapper;
        }
        // A vision objective's competition crest repeats its bound objective
        // title. The same component appears inside messages as well as cards.
        if (classification == ElementClassification.Image && descriptor.ElementName == "CompetitionIcon"
            && element?.parent?.parent is { name: "Vision-Container" } vision)
        {
            var caption = FindDescendantNamed(vision, "ClubVisionName");
            if (caption != null && IsVisible(caption)
                && !string.IsNullOrWhiteSpace(caption.TryCast<TextElement>()?.text))
                return ElementClassification.LayoutWrapper;
        }
        if (classification == ElementClassification.LayoutWrapper && element != null
            && descriptor.TypeName == "NavigatableVisualElement"
            && GridSelectorSelection.TryNativeListEntry(element, out _, out _)) return ElementClassification.Button;
        // FM27 gave names to decorative tint/mode/own-club images. Apply the
        // native asset adaptation to both speech and HTML, including radio extras.
        return classification == ElementClassification.Image
            && descriptor.ElementName is "TintGradient" or "mode-icon" or "EntityTextureOwnClub"
                or "Card-frame"
                or "EntityTextureClub" or "EntityTextureNation"
                or "EntityTextureComp" or "EntityTextureFixture"
                or "EntityTexturePlayer"
                or "ObjectiveIcon"
                ? ElementClassification.LayoutWrapper : classification;
    }
    private static bool _interfaceReflectionTested;
    private static bool _interfaceReflectionWorks;

    private static readonly HashSet<string> KnownTextElementTypes = new(StringComparer.Ordinal)
    {
        "TextElement", "Label", "SIText",
    };

    // Fallback: static interface mapping for when GetInterfaces() doesn't work in IL2CPP
    private static readonly Dictionary<string, string[]> FallbackInterfaceMap = new(StringComparer.Ordinal)
    {
        ["SIButton"] = new[] { "IBindableElement" },
        ["SICheckBox"] = new[] { "IBindableElement" },
        ["SISlider"] = new[] { "IBindableElement" },
        ["SIDropdown"] = new[] { "IBindableElement" },
        ["SIText"] = new[] { "IBindableElement" },
        ["SIProgressBar"] = new[] { "IBindableElement" },
        ["SIRadialGauge"] = new[] { "IBindableElement" },
        ["SIStepper"] = new[] { "IBindableElement" },
        ["SIImage"] = new[] { "IBindableElement" },
        ["BindableTextEditBox"] = new[] { "IBindableElement" },
        ["BindableIntEditBox"] = new[] { "IBindableElement" },
        ["BindableFloatEditBox"] = new[] { "IBindableElement" },
        ["SIRadioButton"] = new[] { "INotifyValueChanged`1" },
        ["BindableSection"] = new[] { "IBindableElement", "IUISection" },
        ["ButtonGroup"] = new[] { "IBindableElement", "IUISection" },
        ["UISection"] = new[] { "IUISection" },
        ["TabGroup"] = new[] { "IBindableElement", "IUISection" },
        ["TabbedGridLayoutElement"] = new[] { "IBindableElement", "IUISection" },
        ["SIRadioGroup"] = new[] { "IBindableElement" },
        ["DateSelectorWidget"] = new[] { "IBindableElement" },
        ["FMDraggableObject"] = new[] { "IBindableElement" },
        ["FMDropTarget"] = new[] { "IBindableElement" },
        ["FMTacticsPanel"] = new[] { "IBindableElement" },
        ["FMCombobox"] = new[] { "IBindableElement" },
        ["StreamedTable"] = new[] { "IBindableElement" },
        ["StreamedListView"] = new[] { "IBindableElement" },
        ["SearchBox"] = new[] { "IBindableElement" },
        ["SIMenu"] = new[] { "IBindableElement" },
    };

    public static ElementDescriptor Describe(VisualElement element, ElementClassification? parentClassification)
        => Describe(element, parentClassification, element.GetIl2CppType());

    public static ElementDescriptor Describe(VisualElement element, ElementClassification? parentClassification, Il2CppSystem.Type il2cppType)
    {
        var typeName = il2cppType.Name;

        // Infrastructure types resolve at the classifier's infrastructure rule without ever reading
        // InterfaceNames/UssClasses (only the later unknown-widget rules and the diagnostic paths do,
        // none of which an infrastructure type reaches). So skip the GetInterfaces() reflection and the
        // ClassListContains interop for the majority of the tree — every VisualElement/wrapper node.
        bool isInfrastructure = ElementClassifier.IsKnownInfrastructure(typeName);
        var interfaceNames = isInfrastructure ? Array.Empty<string>() : GetInterfaceNames(il2cppType, typeName);
        var ussClasses = isInfrastructure ? (IReadOnlyList<string>)Array.Empty<string>() : GetUssClasses(element);
        var isTextElement = IsTextElementSubclass(element, typeName);
        var hasTextContent = isTextElement && HasNonEmptyText(element);
        var isInBreadcrumb = isTextElement && IsInsideBreadcrumbContainer(element);
        var isInStepIndicator = isTextElement && hasTextContent && IsInsideStepIndicator(element);
        var isActiveStep = isInStepIndicator && IsActiveStepText(element);

        return new ElementDescriptor(
            typeName,
            element.name,
            interfaceNames,
            ussClasses,
            hasTextContent,
            isTextElement,
            parentClassification,
            isInBreadcrumb,
            isInStepIndicator,
            isActiveStep);
    }

    public static ElementClassification ClassifyElement(VisualElement element, ElementClassification? parentClassification)
    {
        var descriptor = Describe(element, parentClassification);
        var classification = ClassifyDescriptor(descriptor, element);
        if(descriptor.TypeName=="SIText"&&FM27ShortcutCaptureAdapter.IsCapture(element))classification=ElementClassification.TextField;
        if(descriptor.TypeName=="ColourPicker")classification=ElementClassification.LayoutWrapper;

        // An element implementing IBindableElement that we cannot map to a known widget. Instead of
        // halting the mod, degrade in place: the cursor lands on it as an "unknown control"
        // (UnknownAdapter) and the type is recorded for /unclassified and the F11 dump. The
        // classifier returns Unknown only for genuinely interactive bound widgets the game added or
        // changed, so this is rare and worth chasing — but never worth tearing the whole mod down.
        if (classification == ElementClassification.Unknown)
        {
            TrackUnclassified(element, descriptor);
            return ElementClassification.Unknown;
        }

        if (classification == ElementClassification.LayoutWrapper
            && descriptor.TypeName != "VisualElement"
            && descriptor.TypeName != "TemplateContainer"
            && !ElementClassifier.IsKnownInfrastructure(descriptor.TypeName))
        {
            TrackUnclassified(element, descriptor);
        }

        // Info buttons ("<setting> info, click for tooltip") only surface their text by showing the
        // game's tooltip, which crashes the game when driven programmatically, and the text is not
        // reachable any other way. With no safe way to voice them they are dead stops, so skip them
        // entirely on both surfaces rather than leave an unusable control beside every setting.
        if (classification == ElementClassification.Button && InfoButtonAdapter.IsInfoButton(element))
            return ElementClassification.LayoutWrapper;

        // A standalone "Actions ▾" style context-menu trigger (a visible, chevron-bearing
        // PluginContextMenuBindings) is infrastructure by default; promote it to a Button so the
        // player can reach and open its menu. The invisible right-click bindings of the same type
        // lack the chevron and stay infrastructure.
        if (classification == ElementClassification.LayoutWrapper
            && descriptor.TypeName == "PluginContextMenuBindings"
            && ContextMenuButtonAdapter.IsStandaloneMenuButton(element))
            return ElementClassification.Button;

        // The Messages inbox list (virtualised: only a window of rows is realised) is
        // infrastructure by default; promote it to a Section so it gets a container context —
        // MessagesListContext, which pages the realisation window at the list's edges instead of
        // letting the cursor silently walk past the unrealised messages.
        if (classification == ElementClassification.LayoutWrapper
            && descriptor.TypeName == "MessagesStreamedObjectList")
            return ElementClassification.Section;

        // Any other streamed list gets the same promotion, but only while it is actually
        // under-realised (items exist outside the realised window — the Injury Table card holds
        // 18 club sections with ~4 realised): a fully realised list stays transparent
        // infrastructure, so short lists (breadcrumbs, attribute columns) gain no container
        // boundary. The promoted list gets StreamedListContext, which pages the realisation
        // window at the edges instead of letting the cursor silently walk past the unrealised
        // items. The calendar's "Weeks" list never reaches here (classified Calendar).
        if (classification == ElementClassification.LayoutWrapper
            && descriptor.TypeName is "StreamedObjectList" or "StreamedObjectListWithChildren"
                or "StreamedObjectList_PlaceTimelineEvents"
            && Server.ListWindowSurface.IsUnderRealised(element))
            return ElementClassification.Section;

        // A selector-mode pitch slot (the Player Search Conditions card's Position filter): the
        // indicator itself is the clickable, stateful control — the game toggles it through
        // FMTacticsPanel.HandleClick — so promote it to a Toggle stop. Its position-text child
        // becomes an internal child (Toggle owns its children), so the label is spoken once.
        if (classification == ElementClassification.LayoutWrapper
            && descriptor.TypeName == "FMPitchIndicator"
            && PositionSelectorAdapter.IsSelectorIndicator(element))
            return ElementClassification.Toggle;

        // A player Fitness detail card draws its level as a bare step bar with no text beside it —
        // on the Injury Risk card the filled count IS the risk ("Increased" only ever renders in
        // the uninjured state), so left as a layout wrapper the level is lost on both surfaces
        // while the Fitness panel's tile, whose button folds the same stepper in, reads it aloud.
        // Promote it to a Stepper stop (StepperAdapter reads filled-of-total); each card's profile
        // renames it, since the template id is the only name it has.
        if (classification == ElementClassification.LayoutWrapper
            && descriptor.TypeName == "SIStepper"
            && IsFitnessLevelBar(element))
            return ElementClassification.Stepper;

        if (classification == ElementClassification.Button && IsSpeakToWrapperButton(element))
            return ElementClassification.SpeakToPanel;

        if (classification == ElementClassification.Text && IsInsideStartingLayout(element))
            return ElementClassification.LayoutWrapper;

        if (classification == ElementClassification.Text && element.name == "fm-tactics-shirt-number")
            return ElementClassification.InternalChild;

        // A slash-count badge (the club staff overview's "9 / 13" filled-roles count) is three
        // SIText leaves — CurrentCount, Slash, text-second — forming one visual value; as separate
        // stops the player arrows through "9", "/", "13". Demote the trailing two to internal
        // children; CurrentCount's adapter (TextRunAdapter) speaks the whole run as one stop.
        if (classification == ElementClassification.Text
            && element.name is "Slash" or "text-second"
            && HasSiblingNamed(element, "CurrentCount"))
            return ElementClassification.InternalChild;

        // Same shape on the Add/Remove Leagues header: "Active Men's Nations" + a bare "(15)"
        // count sibling. The count folds into the header's TextRunAdapter stop.
        if (classification == ElementClassification.Text
            && element.name == "text"
            && HasSiblingNamed(element, "ActiveNations_Header"))
            return ElementClassification.InternalChild;

        // On the tactics pitch, suppress all navigation stops inside FMPitchIndicator except
        // shirt-player-instruction-button — TacticsPlayerAdapter consolidates them into one stop.
        // The Recruitment Focus / transfer-advert pitches are the exception: there the pitch is not
        // a lineup but a position picker, and each occupied slot's button IS the toggle the player
        // chooses positions with (PitchPositionAdapter). Suppressing those left the speech surface
        // with no way to pick a position at all, while the HTML surface — which classifies through
        // the raw ElementClassifier and so never saw this demotion — still showed them.
        if (element.name != "shirt-player-instruction-button"
            && !(classification == ElementClassification.Button
                 && PitchPositionAdapter.IsPitchPositionButton(element))
            && IsOnTacticsPitch(element)
            && TreeWalker.NavigationStops.Contains(classification))
            return ElementClassification.InternalChild;

        if (classification == ElementClassification.Button
            && element.name == "SIButton"
            && HasSiblingSubtreeWithDropdown(element))
            return ElementClassification.InternalChild;

        if (classification == ElementClassification.LayoutWrapper
            && AttributeRowAdapter.IsAttributeRow(element))
            return ElementClassification.Button;

        if (classification == ElementClassification.LayoutWrapper
            && IsOppositionInstructionsToggle(element))
            return ElementClassification.SegmentedToggle;

        return classification;
    }

    // Record an element the classifier could not place (Unknown) or an unexpected layout-wrapper
    // type, so it shows up in /unclassified and the F11 dump. UnclassifiedTracker dedupes by type
    // name, so calling this every time the element is reclassified does not spam the log.
    private static void TrackUnclassified(VisualElement element, ElementDescriptor descriptor)
    {
        string panelTitle = null;
        try
        {
            if (SI.Bindable.PanelManager.Exists())
            {
                var panel = SI.Bindable.PanelManager.Instance.GetOpenPanelInHighestLayer();
                panelTitle = panel?.PanelID.Title.TranslatedString;
            }
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"Failed to read panel title for UnclassifiedTracker: {e.GetType().Name}");
        }

        UnclassifiedTracker.Track(
            descriptor.TypeName,
            element.name ?? "",
            descriptor.InterfaceNames.ToArray(),
            descriptor.UssClasses.ToArray(),
            panelTitle ?? "");
    }

    // Also the demotion predicate DropdownAdapter mirrors: a dropdown whose scan finds a button
    // this rule demoted takes that button's text as its accessible name (the squad-table player
    // cell), so the two must always agree on what counts as a paired button.
    internal static bool HasSiblingSubtreeWithDropdown(VisualElement element)
    {
        var parent = element.hierarchy.parent;
        if (parent == null) return false;
        var grandparent = parent.hierarchy.parent;
        if (grandparent == null) return false;
        int count = grandparent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var sibling = grandparent.hierarchy[i];
            if (sibling.Pointer != parent.Pointer && ContainsDropdown(sibling, 0, 5))
                return true;
        }
        return false;
    }

    private static bool ContainsDropdown(VisualElement element, int depth, int maxDepth)
    {
        if (element.GetIl2CppType().Name == "SIDropdown")
            return true;
        if (depth >= maxDepth) return false;
        int count = element.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            if (ContainsDropdown(element.hierarchy[i], depth + 1, maxDepth))
                return true;
        }
        return false;
    }

    private static bool IsOppositionInstructionsToggle(VisualElement element) =>
        SegmentedToggleAdapter.IsSegmentedPair(element);

    private static bool IsInsideStartingLayout(VisualElement element)
    {
        var current = element.hierarchy.parent;
        for (int i = 0; i < 5 && current != null; i++)
        {
            if (current.name == "StartingLayoutSIVisible")
                return true;
            current = current.hierarchy.parent;
        }
        return false;
    }

    private static bool IsSpeakToWrapperButton(VisualElement element)
    {
        var current = element.hierarchy.parent;
        for (int i = 0; i < 5 && current != null; i++)
        {
            if (current.name == "BindingExpect_SpeakToSidePanel")
                return true;
            current = current.hierarchy.parent;
        }
        return false;
    }

    public static IAccessibleElement CreateAdapter(VisualElement element, ElementClassification classification)
    {
        return classification switch
        {
            ElementClassification.Button => CreateButtonAdapter(element),
            ElementClassification.Checkbox => new CheckBoxAdapter(element),
            ElementClassification.Slider => new SliderAdapter(element),
            ElementClassification.Dropdown => CreateDropdownAdapter(element),
            ElementClassification.Text => CreateTextAdapter(element),
            ElementClassification.ProgressBar => new ProgressBarAdapter(element),
            ElementClassification.Gauge => new RadialGaugeAdapter(element),
            ElementClassification.StarRating => new StarRatingGaugeAdapter(element),
            ElementClassification.Stepper => new StepperAdapter(element),
            ElementClassification.Image => new ImageAdapter(element),
            ElementClassification.TextField => FM27ShortcutCaptureAdapter.IsCapture(element)?new FM27ShortcutCaptureAdapter(element):new TextFieldAdapter(element),
            ElementClassification.NumberField => CreateNumberFieldAdapter(element),
            ElementClassification.RadioButton => CreateRadioButtonAdapter(element),
            ElementClassification.ColumnHeader => new ColumnHeaderAdapter(element),
            ElementClassification.DateSelector => new DateSelectorAdapter(element),
            ElementClassification.Toggle => CreateToggleAdapter(element),
            ElementClassification.Breadcrumb => new BreadcrumbAdapter(element),
            ElementClassification.Diggable => new DiggableAdapter(element),
            ElementClassification.SearchBox => new SearchBoxAdapter(element),
            ElementClassification.Combobox => new ComboboxAdapter(element),
            ElementClassification.StepIndicator => new StepIndicatorAdapter(element),
            ElementClassification.SegmentedToggle => new SegmentedToggleAdapter(element),
            ElementClassification.Unknown => new UnknownAdapter(element),
            _ => throw new ArgumentException($"No adapter for classification: {classification}")
        };
    }

    private static IAccessibleElement CreateToggleAdapter(VisualElement element)
    {
        if (element.GetIl2CppType().Name == "FMPitchIndicator")
            return new PositionSelectorAdapter(element);
        return new ToggleAdapter(element);
    }

    private static IAccessibleElement CreateRadioButtonAdapter(VisualElement element)
    {
        if (RecommendationsDeliveryRadioButtonAdapter.IsDeliveryOptionRadio(element))
            return new RecommendationsDeliveryRadioButtonAdapter(element);
        return new RadioButtonAdapter(element);
    }

    private static IAccessibleElement CreateTextAdapter(VisualElement element)
    {
        if (HasEmbeddedLink(element))
        {
            if (Server.MessageViewSurface.IsNarrativeText(element)) return new TextAdapter(element);
            VisualElement nativeLink = null;
            // The FM27 global-search result component uses the same native
            // SIText link records as table cells, without a table row wrapper.
            for (var parent = element.parent; parent != null; parent = parent.parent)
            {
                if (parent.name == "resultLine") return new TableRowTextAdapter(element);
                if (parent.GetIl2CppType().Name == "NavigableLink" && nativeLink == null) nativeLink = parent;
                if (parent.GetIl2CppType().Name == "Panel") break;
            }
            if (TableRowTextAdapter.FindTableRow(element) != null)
            {
                // In the career-setup View Squad preview the player-name links are display-only: the
                // game wires no navigation or context-menu actions to them before the career starts
                // (verified live — its own click and menu-open paths both no-op). Read them as plain,
                // inert text so the mod does not present a button and menu that do nothing.
                // The Career Start screen's Best XI tab is the same story: its player-name links
                // report LinkHasActions true but the game answers neither activation nor a menu
                // open (verified live). Nothing else on either panel reaches this branch — the
                // club, nation and captain links there sit outside any table row.
                if (IsInPanel(element, "TeamSquadViewCard") || IsInPanel(element, "GameCreationLoadingScreen"))
                    return new InertLinkTextAdapter(element);
                return new TableRowTextAdapter(element);
            }
            // FM27 also uses its real entity-link component outside StreamedTable
            // (suggested lineups, reports and tiles). Keep the FM26 link-tag command;
            // arbitrary rich-text paragraphs are not promoted to a single action.
            if (nativeLink != null && !LinkHasCommandButton(nativeLink)) return new TableRowTextAdapter(element);
        }
        // The lead text of a slash-count badge speaks the whole run ("9 / 13"); its Slash and
        // text-second siblings are demoted to internal children in ClassifyElement.
        if (element.name == "CurrentCount" && HasSiblingNamed(element, "Slash"))
            return new TextRunAdapter(element);
        // The Add/Remove Leagues nations header speaks its count sibling with it
        // ("Active Men's Nations (15)"); the bare "(15)" is demoted in ClassifyElement.
        if (element.name == "ActiveNations_Header")
            return new TextRunAdapter(element);
        return new TextAdapter(element);
    }

    // Keep FM26's ordinary image-button action when the entity component already
    // owns one. Promoting its caption too duplicates the same destination. New
    // text-only NavigableLinks (including recommended players) still need their
    // real SIText link action.
    private static bool LinkHasCommandButton(VisualElement root, int depth = 0)
    {
        if (depth > 8 || !IsVisible(root)) return false;
        if (root.GetIl2CppType().Name == "SIButton"
            && NativeButtonCommand.HasBoundCommand(root)
            && !NavigationManager.IsHidden(new ButtonAdapter(root))) return true;
        for (int i = 0; i < root.hierarchy.childCount; i++)
            if (LinkHasCommandButton(root.hierarchy[i], depth + 1)) return true;
        return false;
    }

    private static bool IsDuplicateTableLinkImage(VisualElement element)
    {
        if (element == null || TableRowTextAdapter.FindTableRow(element) == null) return false;
        for (var parent = element.parent; parent != null; parent = parent.parent)
        {
            var type = parent.GetIl2CppType().Name;
            if (type == "Panel" || type == "StreamedTable") return false;
            if (type != "NavigableLink") continue;
            var caption = FindDescendantNamed(parent, "primary-text");
            return caption != null && IsVisible(caption) && HasEmbeddedLink(caption)
                && CountLinkImageButtons(parent, 0) == 1;
        }
        return false;
    }

    private static int CountLinkImageButtons(VisualElement root, int depth)
    {
        if (depth > 8 || !IsVisible(root)) return 0;
        if (root.GetIl2CppType().Name == "SIButton") return 1;
        int count = 0;
        for (int i = 0; i < root.hierarchy.childCount && count < 2; i++)
            count += CountLinkImageButtons(root.hierarchy[i], depth + 1);
        return count;
    }

    // True when any child of the element's parent carries the given template name. Callers search
    // for a name their own element never has, so the element itself needs no exclusion.
    internal static bool HasSiblingNamed(VisualElement element, string name)
    {
        var parent = element.hierarchy.parent;
        if (parent == null) return false;
        int count = parent.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            if (parent.hierarchy[i].name == name) return true;
        return false;
    }

    // The level step bar of a player Fitness detail card (Training Load, Injury Susceptibility,
    // Injury Risk, Match Load). Anchored on the card body container the game names
    // "frame-fitness-<metric>" plus the step-bar template id, so it can never catch the step bars
    // that live inside tile buttons (the walk stops at the button) or the familiarity/importance
    // bars elsewhere. Depth 6 spans the deepest of the four layouts with room to spare.
    private const string FitnessCardFramePrefix = "frame-fitness-";
    private const string StepBarName = "data_display-graphs-stepper-step_bar";

    internal static bool IsFitnessLevelBar(VisualElement element)
    {
        if (element.name != StepBarName) return false;
        var p = element.hierarchy.parent;
        for (int up = 0; p != null && up < 6; up++)
        {
            var name = p.name;
            if (name != null && name.StartsWith(FitnessCardFramePrefix, StringComparison.Ordinal))
                return true;
            p = p.hierarchy.parent;
        }
        return false;
    }

    // True when a panel named <paramref name="panelName"/> is an ancestor of (or is) the element.
    // Only called for the rare table-row link, so the ancestor walk is not a hot path.
    internal static bool IsInPanel(VisualElement element, string panelName)
    {
        for (var p = element; p != null; p = p.hierarchy.parent)
            if (p.name == panelName)
                return true;
        return false;
    }

    internal static bool HasEmbeddedLink(VisualElement element)
    {
        var text = element.Cast<TextElement>().text;
        return text != null && text.Contains("<link=");
    }

    private static bool HasVisibleEmbeddedLink(VisualElement element, int depth)
    {
        if (!IsVisible(element)) return false;
        if (element.TryCast<SI.Bindable.SIText>() is { } text && SITextLink.HasLink(text.text)) return true;
        if (depth >= 8) return false;
        for (int i = 0; i < element.hierarchy.childCount; i++)
            if (HasVisibleEmbeddedLink(element.hierarchy[i], depth + 1)) return true;
        return false;
    }

    private static IAccessibleElement CreateButtonAdapter(VisualElement element)
    {
        if (element.TryCast<SI.Bindable.SIButton>() is { } meter && !meter.CanSubmit
            && !NativeButtonCommand.HasBoundCommand(element) && ButtonAdapter.OwnRadialGauge(element) != null)
            return new ReadonlyRadialButtonAdapter(element);
        if (RecommendationsAdvancedSettingsButtonAdapter.IsAdvancedSettingsButton(element))
            return new RecommendationsAdvancedSettingsButtonAdapter(element);
        if (element.GetIl2CppType().Name == "PluginContextMenuBindings")
            return new ContextMenuButtonAdapter(element);
        if (FormButtonAdapter.IsFormButton(element))
            return new FormButtonAdapter(element);
        if (element.name == "NewsItemButton")
            return new MessageButtonAdapter(element);
        if (element.name == "shirt-player-instruction-button")
        {
            if (IsOnTacticsPitch(element))
                return new TacticsPlayerAdapter(element);
            return new TacticsShirtAdapter(element);
        }
        if (PlayerInstructionsButtonAdapter.IsPlayerInstructionsButton(element))
            return new PlayerInstructionsButtonAdapter(element);
        if (PitchPositionAdapter.IsPitchPositionButton(element))
            return new PitchPositionAdapter(element);
        if (IsColumnHeaderButton(element))
            return new ColumnHeaderAdapter(element);
        // A bare star-rating button reads as its star count — but a content tile that merely
        // CONTAINS a star widget (the player report's Coach Report tile) must keep its title
        // and text, or the whole tile's label collapses to "3 and a half stars".
        if (element.name != "layout-tile-interaction-button"
            && StarRatingHelper.FindDescendantByType(element, "FMStarRatingTemp") != null)
            return new StarRatingAbilityAdapter(element);
        if (element.name == "BookmarkButton")
            return new BookmarkButtonAdapter(element);
        if (PresetButtonAdapter.IsPresetButton(element))
            return new PresetButtonAdapter(element);
        if (InfoButtonAdapter.IsInfoButton(element))
            return new InfoButtonAdapter(element);
        if (SquadPlayerButtonAdapter.IsSquadPlayerButton(element))
            return new SquadPlayerButtonAdapter(element);
        if (AttributeRowAdapter.IsAttributeRow(element))
            return new AttributeRowAdapter(element);
        if (FamiliarityAdapter.IsFamiliarityIndicator(element))
            return new FamiliarityAdapter(element);
        if (StepperAdapter.IsTacticalGaugeButton(element))
            return new StepperAdapter(element);
        // A training-calendar slot with no action behind it (an empty "Not Available" slot, any slot
        // on an already-simulated day) reads as text, not as a dead button. Mirrors the HTML
        // surface's calendar walk, which applies the same test per slot.
        if (CalendarSlotTextAdapter.IsInertTrainingSlot(element))
            return new CalendarSlotTextAdapter(element);
        return new ButtonAdapter(element);
    }

    private static bool IsColumnHeaderButton(VisualElement element)
    {
        var current = element.hierarchy.parent;
        for (int i = 0; i < 5 && current != null; i++)
        {
            try
            {
                if (current.GetIl2CppType().Name == "ResizablePanes" && current.name == "column-headers")
                    return true;
            }
            catch { }
            current = current.hierarchy.parent;
        }
        return false;
    }

    private static IAccessibleElement CreateDropdownAdapter(VisualElement element)
    {
        try
        {
            if (SearchableDropdownAdapter.IsSearchable(element))
                return new SearchableDropdownAdapter(element);
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"SearchableDropdownAdapter.IsSearchable check failed: {e.GetType().Name}. Falling back to standard dropdown.");
        }
        if (NegotiabilityDropdownAdapter.IsNegotiabilityDropdown(element))
            return new NegotiabilityDropdownAdapter(element);

        return new DropdownAdapter(element);
    }

    private static IAccessibleElement CreateNumberFieldAdapter(VisualElement element)
    {
        var typeName = element.GetIl2CppType().Name;
        if (typeName == "BindableFloatEditBox")
            return new FloatFieldAdapter(element);
        return new IntFieldAdapter(element);
    }

    public static IContainerContext CreateContainerContext(
        VisualElement element, ElementClassification classification, ClassificationCache cache)
    {
        if (classification == ElementClassification.ButtonBar
            && (IsInsideTabGroup(element) || IsTabButtonGroup(element)))
            return new TabContext(element, cache);

        return classification switch
        {
            // The promoted virtualised lists (see ClassifyElement) get their paging contexts:
            // the Messages inbox with its own wording, any other streamed list the generic one.
            ElementClassification.Section when element.GetIl2CppType().Name == "MessagesStreamedObjectList"
                => new MessagesListContext(element, cache),
            ElementClassification.Section when element.GetIl2CppType().Name
                    is "StreamedObjectList" or "StreamedObjectListWithChildren"
                    or "StreamedObjectList_PlaceTimelineEvents"
                => new StreamedListContext(element, cache, Server.ListWindowSurface.Labels.Generic),
            ElementClassification.Section => new LinearContext(element, cache),
            ElementClassification.ButtonBar => new ButtonBarContext(element, cache),
            ElementClassification.RadioGroup => new LinearContext(element, cache),
            ElementClassification.Table => new TableContext(element, cache),
            ElementClassification.SpeakToPanel => new LinearContext(element, cache),
            ElementClassification.Calendar => new CalendarContext(element, cache),
            _ => throw new ArgumentException($"No container context for classification: {classification}")
        };
    }

    private static readonly HashSet<string> TabGroupTypeNames = new(StringComparer.Ordinal)
    {
        "TabGroup",
        "TabbedGridLayoutElement",
    };

    // The game's navigation-tab button template. A ButtonGroup built out of these IS a tab bar
    // even where it sits outside a TabGroup wrapper: the Data Hub's seven analysis tabs are this
    // shape, and without the promotion they read as seven loose buttons in a nameless button bar,
    // with no tablist, no selected-tab semantics and no position in the set.
    private const string TabButtonTemplateName = "navigation-tab-default-default";

    private static bool IsTabButtonGroup(VisualElement element)
        => HasDescendantNamed(element, TabButtonTemplateName, 4);

    private static bool HasDescendantNamed(VisualElement element, string name, int depth)
    {
        if (depth < 0) return false;
        int count = element.hierarchy.childCount;
        for (int i = 0; i < count; i++)
        {
            var child = element.hierarchy[i];
            if (string.Equals(child.name, name, StringComparison.Ordinal)) return true;
            if (HasDescendantNamed(child, name, depth - 1)) return true;
        }
        return false;
    }

    private static bool IsInsideTabGroup(VisualElement element)
    {
        var parent = element.hierarchy.parent;
        while (parent != null)
        {
            if (TabGroupTypeNames.Contains(parent.GetIl2CppType().Name))
                return true;
            parent = parent.hierarchy.parent;
        }
        return false;
    }

    public static bool IsVisible(VisualElement element)
    {
        // One resolvedStyle fetch, not two: this runs for every node of every walk, and each
        // access is its own interop property call.
        var rs = element.resolvedStyle;
        return rs.display != DisplayStyle.None
            && rs.visibility != Visibility.Hidden
            && element.visible;
    }

    internal static bool IsVisibleInHierarchy(VisualElement element)
    {
        // display:none on an FM27 SIVisible ancestor does not change its
        // descendants' own display/visibility values or detach them.
        for (var current = element; current != null; current = current.hierarchy.parent)
            if (!IsVisible(current)) return false;
        return true;
    }

    private static string[] GetInterfaceNames(Il2CppSystem.Type il2cppType, string typeName)
    {
        if (!_interfaceReflectionTested)
            TestInterfaceReflection();

        if (_interfaceReflectionWorks)
        {
            try
            {
                var interfaces = il2cppType.GetInterfaces();
                var names = new string[interfaces.Length];
                for (int i = 0; i < interfaces.Length; i++)
                    names[i] = interfaces[i].Name;
                return names;
            }
            catch
            {
                // Unexpected failure after initial test passed — fall through to fallback
                Speech.Log.Warn($"GetInterfaces() failed unexpectedly for type \"{typeName}\". Using fallback.");
            }
        }

        if (FallbackInterfaceMap.TryGetValue(typeName, out var fallback))
            return fallback;

        return Array.Empty<string>();
    }

    private static void TestInterfaceReflection()
    {
        _interfaceReflectionTested = true;
        try
        {
            // Test on a known type — SIButton implements IBindableElement
            var testType = Il2CppSystem.Type.GetType("SI.Bindable.SIButton, SI.Bindable");
            if (testType != null)
            {
                var interfaces = testType.GetInterfaces();
                if (interfaces != null && interfaces.Length > 0)
                {
                    _interfaceReflectionWorks = true;
                    Speech.Log.Info($"IL2CPP interface reflection works. SIButton has {interfaces.Length} interfaces.");
                    return;
                }
            }

            Speech.Log.Warn("IL2CPP interface reflection returned no results. Using static type-to-interface mapping.");
            _interfaceReflectionWorks = false;
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"IL2CPP interface reflection unavailable ({e.GetType().Name}). Using static type-to-interface mapping.");
            _interfaceReflectionWorks = false;
        }
    }

    private static readonly string[] KnownUssClasses =
    {
        "sibutton", "sicheckbox", "sislider", "sidropdown",
        "sitext", "siprogressbar", "siradialgauge", "sistepper",
        "siimage", "siradiobutton",
    };

    private static List<string> GetUssClasses(VisualElement element)
    {
        // ClassListContains is proven to work in IL2CPP (Phase 0.3).
        // GetClasses() enumeration has IL2CPP interop issues, so check known classes directly.
        var classes = new List<string>();
        foreach (var cls in KnownUssClasses)
        {
            try
            {
                if (element.ClassListContains(cls))
                    classes.Add(cls);
            }
            catch (Exception e)
            {
                Speech.Log.Warn($"ClassListContains('{cls}') failed for element '{element.name}': {e.GetType().Name}");
            }
        }
        return classes;
    }

    private static bool IsTextElementSubclass(VisualElement element, string typeName)
    {
        if (KnownTextElementTypes.Contains(typeName))
            return true;

        try
        {
            return element.TryCast<TextElement>() != null;
        }
        catch (Exception e)
        {
            Speech.Log.Warn($"TryCast<TextElement>() failed for '{element.name}' (type {typeName}): {e.GetType().Name}");
            try
            {
                element.Cast<TextElement>();
                return true;
            }
            catch (Exception e2)
            {
                Speech.Log.Warn($"Cast<TextElement>() also failed for '{element.name}': {e2.GetType().Name}");
                return false;
            }
        }
    }

    private static bool IsInsideStepIndicator(VisualElement element)
        => FindStepIndicatorRoot(element) != null;

    private static VisualElement FindStepIndicatorRoot(VisualElement element)
    {
        if (element.name != "text") return null;

        var parent = element.hierarchy.parent;
        if (parent == null || parent.GetIl2CppType().Name != "SIStyleSetter") return null;

        // Walk up from SIStyleSetter looking for BindingVariables with a BindingExpect parent
        var current = parent.hierarchy.parent;
        for (int i = 0; i < 8 && current != null; i++)
        {
            var typeName = current.GetIl2CppType().Name;
            // FM27 moved the same breadcrumb strip into a ComponentElement.
            // Resolve this nearest strip before an outer binding container.
            if (typeName == "ComponentElement" && HasChevronDescendant(current)) return current;
            if (typeName == "BindingVariables")
            {
                var bvParent = current.hierarchy.parent;
                if (bvParent != null && bvParent.GetIl2CppType().Name == "BindingExpect")
                {
                    // Confirm there are chevron separators among descendants
                    if (HasChevronDescendant(current))
                        return current;
                }
                return null;
            }
            current = current.hierarchy.parent;
        }
        return null;
    }

    private static bool HasChevronDescendant(VisualElement root)
    {
        int childCount = root.hierarchy.childCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = root.hierarchy[i];
            if (child.name == "ui-concepts-chevron-right_small")
                return true;
            if (HasChevronDescendant(child))
                return true;
        }
        return false;
    }

    // True when this subtree holds an FM data row's primary entity-name control: an SIButton
    // "NameButton" or its SIText "NameText" (the player/staff link). Marks the row's name cell so a
    // selection checkbox can be named after the entity it selects rather than the row's stat line —
    // and distinguishes it from the row's other buttons (an Inf badge, a Form button) that share no
    // such name. Returns false on rows without a name cell (a file table), leaving the plain fallback.
    internal static bool HasNameDescendant(VisualElement cell) =>
        FindNameDescendant(cell) != null;

    // The entity-name control itself, so a row label can read the entity's own text rather
    // than the cell's leading text (a squad kit cell renders the shirt number before the
    // name, and "14" is no identity).
    internal static VisualElement FindNameDescendant(VisualElement cell)
    {
        var classic = FindDescendantNamed(cell, "NameText") ?? FindDescendantNamed(cell, "NameButton");
        if (classic != null) return classic;
        // The FM27 player-name component uses a NavigableLink/primary-text
        // instead. Stay inside that native component so a position, condition,
        // club or another button cannot become the row's identity.
        var component = FindDescendantNamed(cell, "player-name-button-template");
        var caption = component == null ? null : FindDescendantNamed(component, "primary-text");
        if (caption != null && IsVisible(caption)) return caption;
        // Staff assignment tables can use an unnamed SIText link instead of
        // the player-name component. Its actual embedded UIRecord identifies
        // a person, so nationality/club links cannot become the row's name.
        return FindPersonLinkDescendant(cell);
    }

    private static VisualElement FindPersonLinkDescendant(VisualElement root)
    {
        if (!IsVisible(root)) return null;
        if (root.GetIl2CppType().Name == "SIText")
        {
            var text = root.Cast<SI.Bindable.SIText>();
            var id = SITextLink.TryExtractLinkId(text.text);
            var data = text.EmbeddedData;
            if (int.TryParse(id, out int index) && index >= 0 && data != null && index < data.Count
                && Server.BindingRecords.PersonOf(Server.BindingRecords.Field(
                    Server.BindingRecords.Fields(data[index]), "UIRecord")) != null)
                return root;
            return null;
        }
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var found = FindPersonLinkDescendant(root.hierarchy[i]);
            if (found != null) return found;
        }
        return null;
    }

    private static VisualElement FindDescendantNamed(VisualElement root, string name)
    {
        if (root.name == name)
            return root;
        int childCount = root.hierarchy.childCount;
        for (int i = 0; i < childCount; i++)
        {
            var found = FindDescendantNamed(root.hierarchy[i], name);
            if (found != null)
                return found;
        }
        return null;
    }

    internal static VisualElement FindVisibleTileTitle(VisualElement root)
    {
        if (!IsVisible(root)) return null;
        if (root.name is "title-text" or "title-text-alt") return root;
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var title = FindVisibleTileTitle(root.hierarchy[i]);
            if (title != null) return title;
        }
        return null;
    }

    private static bool IsActiveStepText(VisualElement element)
    {
        // FM27 keeps every stage opaque and marks the active one with the
        // native primary-text class; the old alpha test would select all stages.
        if (FindStepIndicatorRoot(element)?.GetIl2CppType().Name == "ComponentElement")
            return element.ClassListContains("global-text-primary");
        var color = element.resolvedStyle.color;
        return color.a > 0.95f;
    }

    internal static void CollectStepIndicatorInfo(VisualElement activeStepText, out int stepIndex, out int totalSteps)
    {
        stepIndex = 0;
        totalSteps = 0;

        var bindingVariables = FindStepIndicatorRoot(activeStepText);

        if (bindingVariables == null)
        {
            // No container to count steps from. Leave the position at 0/0 so the caller degrades
            // to a label-only announcement rather than crashing — step position is supplementary.
            Speech.Log.Warn("[StepIndicator] Could not find BindingVariables ancestor; step position unavailable.");
            return;
        }

        CollectStepsRecursive(bindingVariables, activeStepText, ref stepIndex, ref totalSteps);
    }

    private static void CollectStepsRecursive(VisualElement element, VisualElement target, ref int stepIndex, ref int totalSteps)
    {
        if (!IsVisible(element)) return;

        var typeName = element.GetIl2CppType().Name;
        if (typeName == "SIStyleSetter")
        {
            int childCount = element.hierarchy.childCount;
            for (int i = 0; i < childCount; i++)
            {
                var child = element.hierarchy[i];
                if (child.name == "text" && IsTextElementSubclass(child, child.GetIl2CppType().Name))
                {
                    var textEl = child.Cast<TextElement>();
                    if (!string.IsNullOrEmpty(textEl.text))
                    {
                        totalSteps++;
                        if (child.Pointer == target.Pointer)
                            stepIndex = totalSteps;
                    }
                }
            }
            return;
        }

        int count = element.hierarchy.childCount;
        for (int i = 0; i < count; i++)
            CollectStepsRecursive(element.hierarchy[i], target, ref stepIndex, ref totalSteps);
    }

    // A live signature of the step currently shown on an in-place-paginated panel, or "" for a
    // panel with no stepper. Some screens (manager/coach creation) swap their step content
    // without loading a new panel, so the panel identity never changes between steps; the HTML
    // surface folds this signature into the screen token so its SSE refresh repaints each step
    // exactly as it repaints a real screen change. The speech surface gets the same effect from
    // its rescan-after-activation; this is the companion-page equivalent.
    //
    // Detection is structural, never by panel name: a panel "paginates in place" iff it actually
    // contains a stepper. We look for the highlighted breadcrumb stage plus the "CurrentStep"
    // sub-step text (e.g. "1/2"). Either alone can repeat across a transition (two single-step
    // stages both read "1/1"), so both are combined. Read fresh every poll, never cached. (A
    // name-based allow-list previously gated this and silently broke when the game renamed the
    // creation container.)
    internal static string InPlaceStepSignature(object panelObj)
    {
        if (panelObj is not SI.Bindable.Panel panel) return "";

        string activeStage = null;
        string currentStep = null;
        foreach (var text in NativeTreeQuery.All<SI.Bindable.SIText>(panel, "CurrentStep"))
            if (text.GetIl2CppType().Name == "SIText" && IsVisibleInHierarchy(text) && !string.IsNullOrEmpty(text.text))
            { currentStep = text.text; break; }
        foreach (var text in NativeTreeQuery.All<SI.Bindable.SIText>(panel, "text"))
            if (text.GetIl2CppType().Name == "SIText" && IsInsideStepIndicator(text) && IsActiveStepText(text)
                && IsVisibleInHierarchy(text) && !string.IsNullOrEmpty(text.text))
            { activeStage = text.text; break; }

        if (activeStage == null && currentStep == null) return "";
        return (activeStage ?? "") + "|" + (currentStep ?? "");
    }

    // A tool can replace its wizard with its editor after background generation,
    // well beyond the action settle window, without opening another Panel. Observe
    // only the shallow template frontier, never table rows or control values.
    // This uses native branch identity for every panel, not a screen-name timer.
    internal static string RootTemplateSignature(object panelObj)
    {
        if (panelObj is not VisualElement root) return "";
        var templates = new List<(string name, int childCount)>();
        int budget = 96;
        CollectRootTemplates(root, "", 0, templates, ref budget);
        return templates.Count == 0 ? "" : FM27Access.Navigation.ScreenShape.Compose(templates);
    }

    private static void CollectRootTemplates(VisualElement element, string path, int depth,
        List<(string name, int childCount)> templates, ref int budget)
    {
        if (budget-- <= 0 || depth > 8 || !IsVisible(element)) return;
        var type = element.GetIl2CppType().Name;
        if (type == "TemplateContainer")
        {
            templates.Add((path + "/" + element.name, 0));
            return;
        }
        // Content/interactive leaves do not define a page-template frontier.
        if (type is "SIButton" or "SIRadioGroup" or "ButtonGroup" or "StreamedTable" or "StreamedObjectList"
            or "SIDropdown" or "FMSearchableCombobox" or "SIFloatingPopupWidget") return;
        for (int i = 0; i < element.hierarchy.childCount; i++)
            CollectRootTemplates(element.hierarchy[i], path + "/" + i, depth + 1, templates, ref budget);
    }

    private static bool IsInsideBreadcrumbContainer(VisualElement element)
    {
        var current = element.hierarchy.parent;
        for (int i = 0; i < 15 && current != null; i++)
        {
            if (current.name == "ShowStaticTitleSwitch")
                return true;
            current = current.hierarchy.parent;
        }
        return false;
    }

    internal static bool IsOnTacticsPitch(VisualElement element)
    {
        var current = element.hierarchy.parent;
        while (current != null)
        {
            var typeName = current.GetIl2CppType().Name;
            if (typeName == "FMPitchIndicator")
                return true;
            current = current.hierarchy.parent;
        }
        return false;
    }

    internal static VisualElement FindAncestorByTypeName(VisualElement element, string typeName)
    {
        var current = element.hierarchy.parent;
        while (current != null)
        {
            if (current.GetIl2CppType().Name == typeName)
                return current;
            current = current.hierarchy.parent;
        }
        return null;
    }

    private static bool HasNonEmptyText(VisualElement element)
    {
        var textEl = element.Cast<TextElement>();
        return !string.IsNullOrEmpty(textEl.text);
    }

    /// <summary>
    /// A text element whose text is currently empty. Such an element classifies as a non-stop (an empty
    /// SIText downgrades to LayoutWrapper), but a streamed cell binds its text a frame or two after the
    /// element appears, so that verdict is provisional. <see cref="ClassificationCache"/> uses this to
    /// avoid caching the provisional classification, which would otherwise drop the value permanently
    /// once it lands (the cache never re-evaluates a stored entry).
    /// </summary>
    internal static bool IsEmptyTextElement(VisualElement element)
    {
        var typeName = element.GetIl2CppType().Name;
        return IsTextElementSubclass(element, typeName) && !HasNonEmptyText(element);
    }

#if FM27_DEVELOPER_TOOLS
    // Startup diagnostic: a shallow raw-tree dump to the log, via the shared TreeDump walker.
    public static void DumpTree(VisualElement root, int maxDepth = 4)
    {
        var sb = new System.Text.StringBuilder();
        Diagnostics.TreeDump.Write(root, sb, new Diagnostics.TreeDump.Options
        {
            MaxDepth = maxDepth,
            ShowDepthIndex = false,
            ShowChildCount = true,
            TextMaxLen = 60,
        });
        Speech.Log.Info("[DumpTree] === Panel tree dump ===\n" + sb + "[DumpTree] === End dump ===");
    }
#endif
}
