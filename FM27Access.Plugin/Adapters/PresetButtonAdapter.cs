using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class PresetButtonAdapter : ButtonAdapter
{
    private static System.Reflection.PropertyInfo _bindingsProperty;

    public PresetButtonAdapter(VisualElement element) : base(element) { }

    // A single computed label, not a primary/secondary split.
    protected override bool SplitsLabel => false;

    public static bool IsPresetButton(VisualElement element)
    {
        var parent = element.hierarchy.parent;
        return parent != null && parent.name == "buttons-task-selection_picture-default";
    }

    public override string GetLabel()
    {
        var index = ReadIndex();
        if (index >= 0)
            return $"Preset {index + 1}";
        return "Preset";
    }

    private int ReadIndex()
    {
        try
        {
            // Find BindingRoot and GridSelector in ancestors
            SI.Bindable.BindingRoot bindingRoot = null;
            SI.Bindable.GridSelector gridSelector = null;
            var ancestor = Element.hierarchy.parent;
            int depth = 0;
            while (ancestor != null && depth < 20)
            {
                if (bindingRoot == null)
                {
                    var br = ancestor.TryCast<SI.Bindable.BindingRoot>();
                    if (br != null) bindingRoot = br;
                }
                if (gridSelector == null)
                {
                    var gs = ancestor.TryCast<SI.Bindable.GridSelector>();
                    if (gs != null) gridSelector = gs;
                }
                if (bindingRoot != null && gridSelector != null) break;
                ancestor = ancestor.hierarchy.parent;
                depth++;
            }

            if (bindingRoot == null || gridSelector == null)
                return -1;

            // Get BindingSubsystem from GridSelector
            _bindingsProperty ??= typeof(SI.Bindable.GridSelector).GetProperty(
                "m_bindings",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (_bindingsProperty == null)
                return -1;

            var bindings = _bindingsProperty.GetValue(gridSelector) as SI.Bindable.BindingSubsystem;
            if (bindings == null)
                return -1;

            // Get the binding node for this item's BindingRoot
            var rootKey = bindingRoot.Key;
            if (!bindings.TryGetNode(ref rootKey, out var rootNode))
                return -1;

            // Find the "Index" child node
            foreach (var child in rootNode.Children)
            {
                if (child.Name != "Index") continue;
                var childKey = child.Key;
                var value = bindings.Get(ref childKey);
                if (value == null) return -1;

                // NumericTypedValue.ToString() returns "SI.Core.NumericTypedValue, N"
                var str = value.ToString();
                var commaIdx = str.LastIndexOf(',');
                if (commaIdx >= 0)
                {
                    var numStr = str.Substring(commaIdx + 1).Trim();
                    if (int.TryParse(numStr, out int idx))
                        return idx;
                }
                return -1;
            }
        }
        catch (System.Exception ex)
        {
            Speech.Log.Warn($"[PresetButton] Failed to read index: {ex.GetType().Name}: {ex.Message}");
        }
        return -1;
    }
}
