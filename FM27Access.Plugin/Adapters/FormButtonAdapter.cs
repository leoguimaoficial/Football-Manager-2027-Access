using System.Collections.Generic;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

internal class FormButtonAdapter : ButtonAdapter
{
    private static object _pendingManipulator;
    private static int _pendingTicks;
    private static List<SI.Bindable.Tooltip> _pendingTooltips;
    private static int _pendingTooltipIndex;
    private static List<string> _collectedRatings;

    public FormButtonAdapter(VisualElement element) : base(element) { }

    // A single computed label, not a primary/secondary split.
    protected override bool SplitsLabel => false;

    public override string GetLabel() => "Form";

    internal void TriggerAsyncTooltipRead()
    {
        TooltipReflection.EnsureInitialized();
        if (!TooltipReflection.IsAvailable) return;

        CancelPending();

        var tooltips = FindAllTooltipsInSubtree();
        if (tooltips.Count == 0)
        {
            Speech.Log.Info("[FormButton] No Tooltip elements found in subtree.");
            return;
        }

        _pendingTooltips = tooltips;
        _pendingTooltipIndex = 0;
        _collectedRatings = new List<string>();

        Speech.Log.Info($"[FormButton] Found {tooltips.Count} tooltips, starting sequential read.");
        ShowNextTooltip();
    }

    private static void ShowNextTooltip()
    {
        if (_pendingTooltipIndex >= _pendingTooltips.Count)
        {
            SpeakCollectedRatings();
            CancelPending();
            return;
        }

        var tooltip = _pendingTooltips[_pendingTooltipIndex];
        try
        {
            var manipulator = TooltipReflection.GetManipulator(tooltip);
            if (manipulator == null)
            {
                Speech.Log.Warn($"[FormButton] Tooltip {_pendingTooltipIndex} manipulator is null, skipping.");
                _pendingTooltipIndex++;
                ShowNextTooltip();
                return;
            }

            TooltipReflection.Show(manipulator);
            _pendingManipulator = manipulator;
            _pendingTicks = 0;
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[FormButton] Tooltip {_pendingTooltipIndex} Show() failed: {e.GetType().Name}: {e.Message}");
            _pendingTooltipIndex++;
            ShowNextTooltip();
        }
    }

    private static void SpeakCollectedRatings()
    {
        if (_collectedRatings.Count == 0)
        {
            Speech.Log.Info("[FormButton] No ratings collected.");
            return;
        }

        var ratingsText = string.Join(", ", _collectedRatings);
        var announcement = $"{ratingsText}. These are this player's last match ratings";
        Speech.Log.Info($"[FormButton] Speaking: \"{announcement}\"");
        Speech.SpeechPipeline.SpeakInterrupt(announcement);
    }

    internal static void Tick()
    {
        if (_pendingManipulator == null) return;

        _pendingTicks++;
        if (_pendingTicks < 5) return;

        try
        {
            var content = TooltipReflection.TryReadFloatingContent();
            TooltipReflection.Hide(_pendingManipulator);

            if (content != null)
            {
                var rating = ParseRating(content);
                if (rating != null)
                    _collectedRatings.Add(rating);
                else
                    Speech.Log.Info($"[FormButton] Tooltip {_pendingTooltipIndex}: no rating found in \"{content}\"");
            }
            else
            {
                Speech.Log.Info($"[FormButton] Tooltip {_pendingTooltipIndex}: empty");
            }
        }
        catch (System.Exception e)
        {
            Speech.Log.Warn($"[FormButton] Tooltip {_pendingTooltipIndex} read failed: {e.GetType().Name}: {e.Message}");
            try { TooltipReflection.Hide(_pendingManipulator); } catch { }
        }

        _pendingManipulator = null;
        _pendingTooltipIndex++;
        ShowNextTooltip();
    }

    private static string ParseRating(string tooltipContent)
    {
        var parts = tooltipContent.Split(',');
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            var trimmed = parts[i].Trim();
            if (trimmed == "Jump to Top") continue;
            if (float.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
                return trimmed;
            break;
        }
        return null;
    }

    internal static void CancelPending()
    {
        if (_pendingManipulator != null)
        {
            try { TooltipReflection.Hide(_pendingManipulator); } catch { }
            _pendingManipulator = null;
        }
        _pendingTooltips = null;
        _collectedRatings = null;
    }

    private List<SI.Bindable.Tooltip> FindAllTooltipsInSubtree()
    {
        var results = new List<SI.Bindable.Tooltip>();
        FindTooltipsRecursive(Element, results);
        return results;
    }

    private static void FindTooltipsRecursive(VisualElement root, List<SI.Bindable.Tooltip> results)
    {
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var child = root.hierarchy[i];
            if (child.GetIl2CppType().Name == "Tooltip")
            {
                var tooltip = child.TryCast<SI.Bindable.Tooltip>();
                if (tooltip != null)
                    results.Add(tooltip);
            }
            else
            {
                FindTooltipsRecursive(child, results);
            }
        }
    }

    internal static bool IsFormButton(VisualElement element) => FindFormTile(element) != null;

    internal static VisualElement FindFormTile(VisualElement element)
    {
        for (int i = 0; i < element.hierarchy.childCount; i++)
        {
            var child = element.hierarchy[i];
            if (child.name == "tile states-player-form-small") return child;
            // FM27's table-cell variant omits the named tile wrapper. Its
            // immediate content still owns the same HasMatches switch and
            // bound pip list. Do not search unrelated nested content buttons.
            for (int j = 0; j < child.hierarchy.childCount; j++)
                if (child.hierarchy[j].name == "HasMatches") return child;
        }
        return null;
    }
}
