namespace FM27Access.Navigation;

public readonly struct ElementDescriptor
{
    public readonly string TypeName;
    public readonly string ElementName;
    public readonly IReadOnlyList<string> InterfaceNames;
    public readonly IReadOnlyList<string> UssClasses;
    public readonly bool HasTextContent;
    public readonly bool IsTextElementSubclass;
    public readonly ElementClassification? ParentClassification;
    public readonly bool IsInBreadcrumbContainer;
    public readonly bool IsInStepIndicator;
    public readonly bool IsActiveStep;

    public ElementDescriptor(
        string typeName,
        string elementName,
        IReadOnlyList<string> interfaceNames,
        IReadOnlyList<string> ussClasses,
        bool hasTextContent,
        bool isTextElementSubclass,
        ElementClassification? parentClassification,
        bool isInBreadcrumbContainer = false,
        bool isInStepIndicator = false,
        bool isActiveStep = false)
    {
        TypeName = typeName;
        ElementName = elementName;
        InterfaceNames = interfaceNames;
        UssClasses = ussClasses;
        HasTextContent = hasTextContent;
        IsTextElementSubclass = isTextElementSubclass;
        ParentClassification = parentClassification;
        IsInBreadcrumbContainer = isInBreadcrumbContainer;
        IsInStepIndicator = isInStepIndicator;
        IsActiveStep = isActiveStep;
    }
}
