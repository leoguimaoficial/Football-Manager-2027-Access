using System.Collections.Generic;

namespace FM27Access.Navigation;

/// <summary>
/// Pure harvest bookkeeping for a virtualized dropdown's scroll-on-demand list.
/// The game only renders a moving window of rows; harvesting reads one window at a
/// time (keyed by each row's absolute choice index) and stitches the windows into a
/// contiguous in-order list. The stitching is the only part with no game dependency,
/// so it lives here and is unit-tested; the scrolling and IL2CPP reads stay plugin-side.
/// </summary>
public static class DropdownHarvest
{
    /// <summary>Harvest label and availability from the same absolute native row.
    /// A missing state is a gap, never an implicitly enabled option.</summary>
    public static int AppendContiguous(List<string> choices, List<bool> disabled,
        IReadOnlyDictionary<int, string> window, IReadOnlyDictionary<int, bool> availability)
    {
        if (choices.Count != disabled.Count)
            throw new System.ArgumentException("Dropdown labels and availability are not aligned.");
        int appended = 0;
        while (window.TryGetValue(choices.Count, out var label)
               && availability.TryGetValue(choices.Count, out var isDisabled))
        {
            choices.Add(label);
            disabled.Add(isDisabled);
            appended++;
        }
        return appended;
    }

    /// <summary>
    /// Appends contiguous rows to <paramref name="choices"/> starting at the next
    /// un-harvested absolute index (<c>choices.Count</c>), pulling each label from
    /// <paramref name="window"/> (absolute choice index → label). Stops at the first
    /// gap, so a window that doesn't reach the harvest frontier appends nothing.
    /// Returns the number of rows appended.
    /// </summary>
    public static int AppendContiguous(List<string> choices, IReadOnlyDictionary<int, string> window)
    {
        int appended = 0;
        while (window.TryGetValue(choices.Count, out var label))
        {
            choices.Add(label);
            appended++;
        }
        return appended;
    }
}
