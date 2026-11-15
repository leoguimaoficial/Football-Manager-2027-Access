using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class IntFieldAdapter : PluginAdapterBase
{
    public IntFieldAdapter(VisualElement element) : base(element)
    {
        PrivateFieldCache.EnsureInitialized();
    }

    public override string Role => "number field";
    public override bool CanActivate => true;

    public override string GetLabel()
    {
        return FindFieldLabel() ?? FindChildText() ?? Element.name;
    }

    /// <summary>
    /// The box's live value and the range it accepts, read fresh. Min/max are null when the box
    /// declares no range, which is a real state — an unbounded box, not a failed read.
    /// </summary>
    internal (int Value, int? Min, int? Max) GetRange()
    {
        var editBox = Element.Cast<SI.Bindable.BindableIntEditBox>();
        var intField = (IntegerField)PrivateFieldCache.IntEditBoxTextField.GetValue(editBox);

        var range = PrivateFieldCache.IntEditBoxRange.GetValue(editBox);
        if (range == null)
            return (intField.value, null, null);

        // SI.Core.RangeInt (min/max), not UnityEngine.RangeInt (start/length). The wrong cast
        // threw, and the throw took the whole state with it, so every int edit box on the
        // vacation card announced its value as nothing at all.
        var rangeInt = (SI.Core.RangeInt)range;
        return (intField.value, rangeInt.min, rangeInt.max);
    }

    /// <summary>
    /// Writes a value and returns the one the box actually holds afterwards, read back rather than
    /// assumed — the game clamps, and reporting the request would tell the player they set
    /// something they did not. The request is clamped here first so the read-back is a
    /// confirmation, not the only thing standing between the player and an out-of-range write.
    /// </summary>
    internal int SetValue(int requested)
    {
        var editBox = Element.Cast<SI.Bindable.BindableIntEditBox>();
        var (_, min, max) = GetRange();

        int wanted = requested;
        if (min.HasValue && wanted < min.Value) wanted = min.Value;
        if (max.HasValue && wanted > max.Value) wanted = max.Value;

        // The IntegerField is what the edit box listens on — BindableIntEditBox registers
        // BindableIntEditBox_OnValueChanged for its ChangeEvent<int> — so assigning the field's
        // value drives the data binding and the box's InputChangedEvents down exactly the path a
        // keystroke would, with no synthetic input events to fake.
        var intField = (IntegerField)PrivateFieldCache.IntEditBoxTextField.GetValue(editBox);
        intField.value = wanted;

        return ((IntegerField)PrivateFieldCache.IntEditBoxTextField.GetValue(editBox)).value;
    }

    public override string GetState()
    {
        var (value, min, max) = GetRange();
        return min.HasValue ? $"{value}, range {min.Value} to {max.Value}" : $"{value}";
    }

    public override void Activate() => Element.Focus();
}
