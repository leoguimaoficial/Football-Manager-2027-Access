using System.Reflection;
using HarmonyLib;

namespace FM27Access.Plugin.Adapters;

internal static class PrivateFieldCache
{
    private static bool _initialized;

    // SIRadialGauge
    internal static PropertyInfo GaugeCurrent;
    internal static PropertyInfo GaugeMinValue;
    internal static PropertyInfo GaugeMaxValue;

    // BindableTextEditBox
    internal static PropertyInfo TextEditBoxTextField;

    // BindableIntEditBox
    internal static PropertyInfo IntEditBoxTextField;
    internal static PropertyInfo IntEditBoxRange;

    // SIStepper
    internal static PropertyInfo StepperSteps;
    internal static PropertyInfo StepperCurrentValue;
    internal static PropertyInfo StepperStepLimits;
    internal static PropertyInfo StepperRangeMin;
    internal static PropertyInfo StepperRangeMax;

    internal static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        GaugeCurrent = Resolve(typeof(SI.Bindable.SIRadialGauge), "m_current");
        GaugeMinValue = Resolve(typeof(SI.Bindable.SIRadialGauge), "m_radialMinValue");
        GaugeMaxValue = Resolve(typeof(SI.Bindable.SIRadialGauge), "m_radialMaxValue");

        TextEditBoxTextField = Resolve(typeof(SI.Bindable.BindableTextEditBox), "m_textField");

        IntEditBoxTextField = Resolve(typeof(SI.Bindable.BindableIntEditBox), "m_textField");
        IntEditBoxRange = Resolve(typeof(SI.Bindable.BindableIntEditBox), "m_range");

        StepperSteps = Resolve(typeof(SI.Bindable.SIStepper), "m_steps");
        StepperCurrentValue = Resolve(typeof(SI.Bindable.SIStepper), "m_currentValue");
        StepperStepLimits = Resolve(typeof(SI.Bindable.SIStepper), "m_stepLimits");
        StepperRangeMin = Resolve(typeof(SI.Bindable.SIStepper), "m_currentRangeMin");
        StepperRangeMax = Resolve(typeof(SI.Bindable.SIStepper), "m_currentRangeMax");
    }

    private static PropertyInfo Resolve(Type type, string fieldName)
    {
        var prop = AccessTools.Property(type, fieldName);
        if (prop == null)
        {
            Speech.Log.Error($"Private property not found via AccessTools: {type.Name}.{fieldName}");
            throw new MissingMemberException(type.Name, fieldName);
        }
        return prop;
    }
}

