namespace FM27Access.Navigation;

public static class ElementClassifier
{
    private static readonly Dictionary<string, ElementClassification> KnownTypes = new(StringComparer.Ordinal)
    {
        ["SIButton"] = ElementClassification.Button,
        ["Button"] = ElementClassification.Button,
        ["SIMultiBox"] = ElementClassification.Button,
        ["SICheckBox"] = ElementClassification.Checkbox,
        ["SISlider"] = ElementClassification.Slider,
        ["SIDropdown"] = ElementClassification.Dropdown,
        ["FMCombobox"] = ElementClassification.Combobox,
        ["SIText"] = ElementClassification.Text,
        ["SIProgressBar"] = ElementClassification.ProgressBar,
        ["SIRadialGauge"] = ElementClassification.LayoutWrapper,
        ["SIStepper"] = ElementClassification.LayoutWrapper,
        ["SIImage"] = ElementClassification.Image,
        ["TextField"] = ElementClassification.TextField,
        ["BindableTextEditBox"] = ElementClassification.TextField,
        ["BindableIntEditBox"] = ElementClassification.NumberField,
        ["BindableFloatEditBox"] = ElementClassification.NumberField,
        ["SILongField"] = ElementClassification.LayoutWrapper,
        ["SIRadioButton"] = ElementClassification.RadioButton,
        ["BindableSection"] = ElementClassification.Section,
        ["ButtonGroup"] = ElementClassification.ButtonBar,
        ["UISection"] = ElementClassification.Section,
        ["SIRadioGroup"] = ElementClassification.RadioGroup,
        ["DateSelectorWidget"] = ElementClassification.DateSelector,
        ["ExpandedToggle"] = ElementClassification.Toggle,
        ["Toggle"] = ElementClassification.Toggle,
        ["StreamedTable"] = ElementClassification.Table,
        ["FMStarRatingTemp"] = ElementClassification.StarRating,
        ["DiggableVisualElement"] = ElementClassification.Diggable,
        ["SearchBox"] = ElementClassification.SearchBox,
        ["SIMenu"] = ElementClassification.Dropdown,
        // A carousel's paging-dot indicator: purely decorative chrome whose child Buttons are the
        // page dots. InternalChild stops the walk here so the dots never emit as "unlabeled icon
        // button" stops on either surface — globally, on every DynamicCarousel screen (Squad
        // MedicalCentre, ClubSite tiles, ...). The tile content is a sibling, not a child, so it is
        // untouched.
        ["CarouselPageIndicator"] = ElementClassification.InternalChild,
    };

    private static readonly HashSet<string> InfrastructureTypes = new(StringComparer.Ordinal)
    {
        "VisualElement",
        "SISafeAreaElement",
        "FixedAspectGridLayoutElement",
        "MultiGridLayoutElement",
        "DropTarget",
        "TutorialAnchorElement",
        "TemplateContainer",
        "ScrollView",
        "SIScrollView",
        "Scroller",
        "AutoScroller",
        "BindingRoot",
        "BindingRemapper",
        "BindingVariables",
        "BindingExpect",
        "BindingSetter",
        "SIStyleSetter",
        "SIVisible",
        "SIRenderTextureImage",
        "GridElement",
        "GridLayoutElement",
        "ScalableBindableGridLayoutElement",
        "VariableSizedGridElement",
        "RepeatButton",
        "DataReferenceElement",
        "DebugBindingElement",
        "EventTrigger",
        "AsyncBindableVisualElement",
        "DynamicItem",
        "BindableSwitchElement",
        "SwitchElement",
        "SIEnabled",
        "SIGradient",
        "SIAnimation",
        "Sliding",
        "FMPolarChart",
        "FMPieChart",
        "FMClubTransfersPieChart",
        "FMFSetPieceFrequencyPieChart",
        "FMLineChart",
        "FMMatchMomentumChart",
        "FMXGMatchStoryChart",
        "FMScatterChart",
        "FMPlayerFormChart",
        "FMLeagueHistoryChart",
        "FMStackedBarChart",
        "SIScrollAnimation",
        "SIAspectRatioFitter",
        "SIAutoScrollView",
        "SITaggedElement",
        "SITile",
        "AsyncBindingRoot",
        "BindableCarouselTileRoot",
        "DynamicCarouselTileRoot",
        "BindableDragDropObject",
        "BindableGridLayoutElement",
        "MultiGridLayoutElementVisible",
        "BindableElement",
        "BindableUxmlEntry",
        "BindableUxmlList",
        "ExpandingList",
        "PluginContextMenuBindings",
        "SICarousel",
        "SIFloatingPopupWidget",
        "SIMaskSelector",
        "SISelector",
        "SITabView",
        "GridSelector",
        "ScrollerSlider",
        "DragHandler",
        "StreamedListView",
        "VirtualisedList",
        "ResizablePanes",
        "TableRowNavigatable",
        "DropdownRowNavigatable",
        "NavigatableVisualElement",
        "NavigationFocusser",
        "BaseCardTemplateImpl",
        "StreamedObjectList",
        "StreamedObjectListWithChildren",
        "StreamedObjectList_PlaceTimelineEvents",
        "TutorialActionElement",
        "ContextShortcutsElement",
        "MessagesStreamedObjectList",
        "MessageActionsStreamedObjectList",
        "FMUnbindDynamicBindingRooot",
        "WrappedObjectList",
        "FMCalendarEvent",
        "Calendar",
        "TabGroup",
        "TabbedGridLayoutElement",
        "FMActionButton",
        "FMDraggableObject",
        "FMDropTarget",
        "FMTacticsPanel",
        "FMPitchIndicator",
        "FMSetPiecesPitchWidget",
        "SIDelayEvent",
        "FMBodymovinWidget",
        "FMTacticsShirt",
        "FMPitchPosition",
        "FMTacticsWithAnalysis",
        "FMTacticsWithPhases",
        "FMTacticInstructionsWidget",
        "FMPlayerFormChart",
        "FMLineChart",
        "FMPieChart",
        "SILineChart",
        "VisualElementTemplateGeometryContainer",
        "Rotator",
        "SearchStatePanel",
        "FMMatchSimulationWidget",
        "PitchDrawing",
    };

    private static readonly HashSet<string> SkippedButtonNames = new(StringComparer.Ordinal)
    {
        "ControllerNavigationBlocker",
        "QuickFlickDown",
        "QuickFlickUp",
    };

    private static readonly HashSet<string> DecorativeImageNames = new(StringComparer.Ordinal)
    {
        "BackgroundImage",
        "background_image",
        "BubbleLeftTrail",
        "BubbleRightTrail",
        "BubbleRigthTrail",
        "ClubImage",
        "CompImage",
        "CompLogo",
        "icon",
        "Image",
        "Logo",
        "ManagerImage",
        "NationFlag",
        "PatternImage",
        "PersonImage",
        "ShirtImage",
    };

    private static readonly string[] DecorativeImagePrefixes = new[]
    {
        "DynamicBackgroundImage_",
        "game-concepts-",
        "ui concepts-",
        "ui-concepts-",
    };

    private static readonly HashSet<ElementClassification> WidgetsThatOwnChildren = new()
    {
        ElementClassification.Button,
        ElementClassification.Checkbox,
        ElementClassification.Slider,
        ElementClassification.Dropdown,
        ElementClassification.TextField,
        ElementClassification.NumberField,
        ElementClassification.Stepper,
        ElementClassification.ColumnHeader,
        ElementClassification.DateSelector,
        ElementClassification.Toggle,
        ElementClassification.SearchBox,
        ElementClassification.Combobox,
        ElementClassification.SegmentedToggle,
    };

    private static readonly Dictionary<string, ElementClassification> UssClassLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sibutton"] = ElementClassification.Button,
        ["sicheckbox"] = ElementClassification.Checkbox,
        ["sislider"] = ElementClassification.Slider,
        ["sidropdown"] = ElementClassification.Dropdown,
        ["sitext"] = ElementClassification.Text,
        ["siprogressbar"] = ElementClassification.ProgressBar,
        ["siradialgauge"] = ElementClassification.LayoutWrapper,
        ["sistepper"] = ElementClassification.LayoutWrapper,
        ["siimage"] = ElementClassification.Image,
        ["siradiobutton"] = ElementClassification.RadioButton,
    };

    public static ElementClassification Classify(in ElementDescriptor descriptor)
    {
        // Rule 1: Internal child of a widget
        if (descriptor.ParentClassification.HasValue
            && WidgetsThatOwnChildren.Contains(descriptor.ParentClassification.Value))
        {
            return ElementClassification.InternalChild;
        }

        // Rule 1b: Breadcrumb text (detected via IsInBreadcrumbContainer in Plugin layer)
        if (descriptor.IsInBreadcrumbContainer && descriptor.IsTextElementSubclass && descriptor.HasTextContent)
            return ElementClassification.Breadcrumb;

        // Rule 1c: Step indicator text (wizard-style progress breadcrumbs)
        if (descriptor.IsInStepIndicator && descriptor.IsTextElementSubclass && descriptor.HasTextContent)
            return descriptor.IsActiveStep ? ElementClassification.StepIndicator : ElementClassification.LayoutWrapper;

        // Rule 2: Decorative images (logos, shirts, unnamed) — skip as navigation stops
        if (descriptor.TypeName == "SIImage"
            && (string.IsNullOrEmpty(descriptor.ElementName)
                || DecorativeImageNames.Contains(descriptor.ElementName)
                || HasDecorativePrefix(descriptor.ElementName)))
            return ElementClassification.LayoutWrapper;

        // Rule 3: Controller-only buttons — skip as navigation stops
        if ((descriptor.TypeName == "SIButton" || descriptor.TypeName == "Button")
            && SkippedButtonNames.Contains(descriptor.ElementName))
            return ElementClassification.LayoutWrapper;

        // Rule 4: Known type name
        if (KnownTypes.TryGetValue(descriptor.TypeName, out var known))
        {
            // SIText with no text content is not a useful navigation stop
            if (known == ElementClassification.Text && !descriptor.HasTextContent)
                return ElementClassification.LayoutWrapper;
            return known;
        }

        // Rule 5: TextElement with non-empty text
        if (descriptor.IsTextElementSubclass && descriptor.HasTextContent)
            return ElementClassification.Text;

        // Rule 5b: StreamedObjectList named "Weeks" is a calendar container
        if (descriptor.TypeName == "StreamedObjectList" && descriptor.ElementName == "Weeks")
            return ElementClassification.Calendar;

        // Rule 6: Known infrastructure types
        if (InfrastructureTypes.Contains(descriptor.TypeName))
            return ElementClassification.LayoutWrapper;

        // Rule 7: Unknown IBindableElement (caller must crash)
        if (ContainsInterface(descriptor.InterfaceNames, "IBindableElement"))
            return ElementClassification.Unknown;

        // Rule 8: INotifyValueChanged<bool> (RadioButton safety net)
        // Exclude TextElement subclasses — they implement INotifyValueChanged<string>
        if (!descriptor.IsTextElementSubclass
            && ContainsInterfacePrefix(descriptor.InterfaceNames, "INotifyValueChanged"))
            return ElementClassification.RadioButton;

        // Rule 9: IUISection
        if (ContainsInterface(descriptor.InterfaceNames, "IUISection"))
            return ElementClassification.Section;

        // Rule 10: USS class fallback
        foreach (var uss in descriptor.UssClasses)
        {
            if (UssClassLookup.TryGetValue(uss, out var ussClassification))
                return ussClassification;
        }

        // Rule 11: Default — layout wrapper
        return ElementClassification.LayoutWrapper;
    }

    public static bool IsKnownInfrastructure(string typeName)
    {
        return InfrastructureTypes.Contains(typeName);
    }

    private static bool HasDecorativePrefix(string elementName)
    {
        for (int i = 0; i < DecorativeImagePrefixes.Length; i++)
        {
            if (elementName.StartsWith(DecorativeImagePrefixes[i], StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool ContainsInterface(IReadOnlyList<string> interfaces, string name)
    {
        for (int i = 0; i < interfaces.Count; i++)
        {
            if (interfaces[i] == name)
                return true;
        }
        return false;
    }

    private static bool ContainsInterfacePrefix(IReadOnlyList<string> interfaces, string prefix)
    {
        for (int i = 0; i < interfaces.Count; i++)
        {
            if (interfaces[i].StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
