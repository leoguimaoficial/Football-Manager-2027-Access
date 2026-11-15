using FM27Access.Navigation;

namespace FM27Access.Tests;

public class ElementClassifierTests
{
    private static readonly string[] NoInterfaces = Array.Empty<string>();
    private static readonly string[] NoUssClasses = Array.Empty<string>();

    private static ElementDescriptor Desc(
        string typeName,
        string[] interfaces = null,
        string[] ussClasses = null,
        bool hasTextContent = false,
        bool isTextElementSubclass = false,
        ElementClassification? parentClassification = null,
        string elementName = "",
        bool isInStepIndicator = false,
        bool isActiveStep = false)
    {
        return new ElementDescriptor(
            typeName,
            elementName,
            interfaces ?? NoInterfaces,
            ussClasses ?? NoUssClasses,
            hasTextContent,
            isTextElementSubclass,
            parentClassification,
            isInStepIndicator: isInStepIndicator,
            isActiveStep: isActiveStep);
    }

    // --- Rule 2: Known type names ---

    [Theory]
    [InlineData("SIButton", ElementClassification.Button)]
    [InlineData("SICheckBox", ElementClassification.Checkbox)]
    [InlineData("SISlider", ElementClassification.Slider)]
    [InlineData("SIDropdown", ElementClassification.Dropdown)]
    [InlineData("FMCombobox", ElementClassification.Combobox)]
    [InlineData("SIText", ElementClassification.LayoutWrapper)]
    [InlineData("SIProgressBar", ElementClassification.ProgressBar)]
    [InlineData("SIRadialGauge", ElementClassification.LayoutWrapper)]
    [InlineData("FMStarRatingTemp", ElementClassification.StarRating)]
    [InlineData("SIStepper", ElementClassification.LayoutWrapper)]
    [InlineData("BindableTextEditBox", ElementClassification.TextField)]
    [InlineData("BindableIntEditBox", ElementClassification.NumberField)]
    [InlineData("BindableFloatEditBox", ElementClassification.NumberField)]
    [InlineData("SIRadioButton", ElementClassification.RadioButton)]
    [InlineData("BindableSection", ElementClassification.Section)]
    [InlineData("ButtonGroup", ElementClassification.ButtonBar)]
    [InlineData("SIRadioGroup", ElementClassification.RadioGroup)]
    [InlineData("UISection", ElementClassification.Section)]
    [InlineData("SearchBox", ElementClassification.SearchBox)]
    [InlineData("CarouselPageIndicator", ElementClassification.InternalChild)]
    public void KnownTypeName_ClassifiesCorrectly(string typeName, ElementClassification expected)
    {
        var result = ElementClassifier.Classify(Desc(typeName));
        Assert.Equal(expected, result);
    }

    // --- Rule 1: Internal child ---

    [Fact]
    public void TextInsideButton_IsInternalChild()
    {
        var desc = Desc("TextElement",
            isTextElementSubclass: true,
            hasTextContent: true,
            parentClassification: ElementClassification.Button);
        Assert.Equal(ElementClassification.InternalChild, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void TextInsideCheckbox_IsInternalChild()
    {
        var desc = Desc("Label",
            isTextElementSubclass: true,
            hasTextContent: true,
            parentClassification: ElementClassification.Checkbox);
        Assert.Equal(ElementClassification.InternalChild, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void ChildInsideCombobox_IsInternalChild()
    {
        var desc = Desc("SIButton", parentClassification: ElementClassification.Combobox);
        Assert.Equal(ElementClassification.InternalChild, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void TextInsideSection_IsNotInternalChild()
    {
        var desc = Desc("Label",
            isTextElementSubclass: true,
            hasTextContent: true,
            parentClassification: ElementClassification.Section);
        Assert.Equal(ElementClassification.Text, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void ElementInsideLayoutWrapper_IsNotInternalChild()
    {
        var desc = Desc("SIButton", parentClassification: ElementClassification.LayoutWrapper);
        Assert.Equal(ElementClassification.Button, ElementClassifier.Classify(desc));
    }

    // --- Rule 2: Decorative images ---

    [Theory]
    [InlineData("background_image")]
    [InlineData("ClubImage")]
    [InlineData("CompImage")]
    [InlineData("CompLogo")]
    [InlineData("ManagerImage")]
    [InlineData("NationFlag")]
    [InlineData("PatternImage")]
    [InlineData("PersonImage")]
    [InlineData("ShirtImage")]
    public void DecorativeImage_IsLayoutWrapper(string imageName)
    {
        var desc = Desc("SIImage", elementName: imageName);
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Theory]
    [InlineData("game-concepts-transfer-value_small")]
    [InlineData("game-concepts-ability")]
    [InlineData("ui concepts-placeholder")]
    public void GameConceptsImage_IsLayoutWrapper(string imageName)
    {
        var desc = Desc("SIImage", elementName: imageName);
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void UnnamedImage_IsLayoutWrapper()
    {
        var desc = Desc("SIImage", elementName: "");
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void NonDecorativeImage_IsImage()
    {
        var desc = Desc("SIImage", elementName: "SomeUnknownImage");
        Assert.Equal(ElementClassification.Image, ElementClassifier.Classify(desc));
    }

    // --- Rule 3: Skipped buttons ---

    [Fact]
    public void ControllerNavigationBlocker_IsLayoutWrapper()
    {
        var desc = Desc("SIButton", elementName: "ControllerNavigationBlocker");
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    // --- Rule 5: TextElement with text ---

    [Fact]
    public void TextElementWithContent_IsText()
    {
        var desc = Desc("Label", isTextElementSubclass: true, hasTextContent: true);
        Assert.Equal(ElementClassification.Text, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void TextElementEmpty_IsLayoutWrapper()
    {
        var desc = Desc("Label", isTextElementSubclass: true, hasTextContent: false);
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    // --- Rule 4: Infrastructure types ---

    [Theory]
    [InlineData("VisualElement")]
    [InlineData("TemplateContainer")]
    [InlineData("ScrollView")]
    [InlineData("BindingRoot")]
    [InlineData("MessagesStreamedObjectList")]
    [InlineData("MessageActionsStreamedObjectList")]
    [InlineData("FMUnbindDynamicBindingRooot")]
    [InlineData("WrappedObjectList")]
    [InlineData("Rotator")]
    [InlineData("SISafeAreaElement")]
    [InlineData("FixedAspectGridLayoutElement")]
    [InlineData("MultiGridLayoutElement")]
    [InlineData("DropTarget")]
    [InlineData("TutorialAnchorElement")]
    public void InfrastructureType_IsLayoutWrapper(string typeName)
    {
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(Desc(typeName)));
    }

    [Fact]
    public void TutorialActionElement_WithIBindable_IsLayoutWrapper()
    {
        var desc = Desc("TutorialActionElement", interfaces: new[] { "IBindableElement" });
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void DynamicCarouselTileRoot_WithIBindable_IsLayoutWrapper()
    {
        var desc = Desc("DynamicCarouselTileRoot", interfaces: new[] { "IBindableElement" });
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void ContextShortcutsElement_WithIBindable_IsLayoutWrapper()
    {
        var desc = Desc("ContextShortcutsElement", interfaces: new[] { "IBindableElement" });
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void FMCalendarEvent_WithIBindable_IsLayoutWrapper()
    {
        var desc = Desc("FMCalendarEvent", interfaces: new[] { "IBindableElement" });
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void Calendar_WithIBindable_IsLayoutWrapper()
    {
        var desc = Desc("Calendar", interfaces: new[] { "IBindableElement" });
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void StreamedObjectList_Weeks_IsCalendar()
    {
        var desc = Desc("StreamedObjectList", elementName: "Weeks", interfaces: new[] { "IBindableElement" });
        Assert.Equal(ElementClassification.Calendar, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void StreamedObjectList_NotWeeks_IsLayoutWrapper()
    {
        var desc = Desc("StreamedObjectList", interfaces: new[] { "IBindableElement" });
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void TabGroup_WithIBindable_IsLayoutWrapper()
    {
        var desc = Desc("TabGroup", interfaces: new[] { "IBindableElement", "IUISection" });
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void TabbedGridLayoutElement_WithIBindable_IsLayoutWrapper()
    {
        var desc = Desc("TabbedGridLayoutElement", interfaces: new[] { "IBindableElement", "IUISection" });
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void SIDelayEvent_WithIBindable_IsLayoutWrapper()
    {
        var desc = Desc("SIDelayEvent", interfaces: new[] { "IBindableElement", "IEventOwner" });
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    // --- Rule 5: Unknown IBindableElement ---

    [Fact]
    public void UnknownIBindableElement_ReturnsUnknown()
    {
        var desc = Desc("FutureWidget", interfaces: new[] { "IBindableElement" });
        Assert.Equal(ElementClassification.Unknown, ElementClassifier.Classify(desc));
    }

    // --- Rule 6: INotifyValueChanged fallback ---

    [Fact]
    public void INotifyValueChanged_FallsBackToRadioButton()
    {
        var desc = Desc("SomeToggle", interfaces: new[] { "INotifyValueChanged`1" });
        Assert.Equal(ElementClassification.RadioButton, ElementClassifier.Classify(desc));
    }

    // --- Rule 7: IUISection ---

    [Fact]
    public void IUISection_IsSection()
    {
        var desc = Desc("CustomPanel", interfaces: new[] { "IUISection" });
        Assert.Equal(ElementClassification.Section, ElementClassifier.Classify(desc));
    }

    // --- Rule 8: USS class fallback ---

    [Fact]
    public void UssClassFallback_ClassifiesCorrectly()
    {
        var desc = Desc("MyCustomButton", ussClasses: new[] { "sibutton" });
        Assert.Equal(ElementClassification.Button, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void UssClassFallback_CaseInsensitive()
    {
        var desc = Desc("CustomWidget", ussClasses: new[] { "SICheckBox" });
        Assert.Equal(ElementClassification.Checkbox, ElementClassifier.Classify(desc));
    }

    // --- Rule 9: Default ---

    [Fact]
    public void UnknownNonWidget_IsLayoutWrapper()
    {
        var desc = Desc("SomethingWeird");
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    // --- Priority ordering ---

    [Fact]
    public void InternalChild_TakesPriorityOverKnownType()
    {
        // An SIButton inside another SIButton — internal child wins
        var desc = Desc("SIButton", parentClassification: ElementClassification.Button);
        Assert.Equal(ElementClassification.InternalChild, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void KnownType_TakesPriorityOverTextElement()
    {
        // SIText is both a known type and a TextElement subclass — known type wins
        var desc = Desc("SIText", isTextElementSubclass: true, hasTextContent: true);
        Assert.Equal(ElementClassification.Text, ElementClassifier.Classify(desc));
    }

    [Theory]
    [InlineData(ElementClassification.TabGroup)]
    [InlineData(ElementClassification.ButtonBar)]
    [InlineData(ElementClassification.RadioGroup)]
    [InlineData(ElementClassification.Section)]
    [InlineData(ElementClassification.SpeakToPanel)]
    public void ContainerChildren_AreNotInternalChild(ElementClassification containerType)
    {
        var desc = Desc("Label",
            isTextElementSubclass: true,
            hasTextContent: true,
            parentClassification: containerType);
        Assert.Equal(ElementClassification.Text, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void SIText_WithContent_IsText()
    {
        var desc = Desc("SIText", hasTextContent: true);
        Assert.Equal(ElementClassification.Text, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void SIText_WithoutContent_IsLayoutWrapper()
    {
        var desc = Desc("SIText", hasTextContent: false);
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void DropdownInsideSpeakToPanel_IsDropdown()
    {
        var desc = Desc("SIDropdown", parentClassification: ElementClassification.SpeakToPanel);
        Assert.Equal(ElementClassification.Dropdown, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void ButtonInsideSpeakToPanel_IsButton()
    {
        var desc = Desc("SIButton", parentClassification: ElementClassification.SpeakToPanel);
        Assert.Equal(ElementClassification.Button, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void KnownType_TakesPriorityOverIBindableElement()
    {
        // SIButton implements IBindableElement but is a known type — known type wins
        var desc = Desc("SIButton", interfaces: new[] { "IBindableElement" });
        Assert.Equal(ElementClassification.Button, ElementClassifier.Classify(desc));
    }

    // --- Rule 1c: Step indicator ---

    [Fact]
    public void ActiveStepIndicator_IsStepIndicator()
    {
        var desc = Desc("SIText",
            isTextElementSubclass: true,
            hasTextContent: true,
            isInStepIndicator: true,
            isActiveStep: true);
        Assert.Equal(ElementClassification.StepIndicator, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void InactiveStepIndicator_IsLayoutWrapper()
    {
        var desc = Desc("SIText",
            isTextElementSubclass: true,
            hasTextContent: true,
            isInStepIndicator: true,
            isActiveStep: false);
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void StepIndicator_WithoutTextContent_ClassifiesNormally()
    {
        var desc = Desc("SIText",
            isTextElementSubclass: true,
            hasTextContent: false,
            isInStepIndicator: true,
            isActiveStep: true);
        Assert.Equal(ElementClassification.LayoutWrapper, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void ButtonInsideRadioButton_IsButton()
    {
        var desc = Desc("SIButton", parentClassification: ElementClassification.RadioButton);
        Assert.Equal(ElementClassification.Button, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void TextInsideRadioButton_IsText()
    {
        var desc = Desc("Label",
            isTextElementSubclass: true,
            hasTextContent: true,
            parentClassification: ElementClassification.RadioButton);
        Assert.Equal(ElementClassification.Text, ElementClassifier.Classify(desc));
    }

    [Fact]
    public void InternalChild_TakesPriorityOverStepIndicator()
    {
        var desc = Desc("SIText",
            isTextElementSubclass: true,
            hasTextContent: true,
            parentClassification: ElementClassification.Button,
            isInStepIndicator: true,
            isActiveStep: true);
        Assert.Equal(ElementClassification.InternalChild, ElementClassifier.Classify(desc));
    }
}
