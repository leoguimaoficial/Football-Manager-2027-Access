using System.Globalization;
using FM.UI;
using SI.Bindable;
using SI.Core;

namespace FM27Access.Plugin.Server;

// FM27 clause editors keep SelectedPrimary/Secondary/LeftRightValue as LOCAL
// UI variables. Undo can restore the backend caption and dependent controls
// while leaving those variables and SIDropdown.SelectedIndex on the old choice.
// Read the actual clause + actual options; synchronize only that native UI cache
// on an EXPLICIT dropdown open, with UpdateHandler deliberately absent.
internal static class NativeClauseSelection
{
    private sealed record Selection(BindingSubsystem Bindings, Bindings.Node Node,
        Bindings.Key Key, TypedValue Choice, int Index);

    internal static int? CurrentIndex(SIDropdown dropdown) => Read(dropdown)?.Index;

    internal static void PrepareOpen(SIDropdown dropdown)
    {
        var selection = Read(dropdown);
        if (selection == null) return;
        var key = selection.Key;
        var current = selection.Bindings.Get(ref key);
        if (OptionKey(current) == OptionKey(selection.Choice)) return;
        // This is not the FMX clause value/provider: it must remain an unaliased
        // local variable with NO native data handler. Never write a business
        // property or fire TriggerSelectionEvent to repair a selected-value cache.
        if (selection.Node.Alias.m_key != 0 || selection.Bindings.GetContextInfo(selection.Node).handler != null)
            throw new InvalidOperationException("The native clause selection owner changed. Refresh before opening it.");
        selection.Bindings.Set(ref key, selection.Choice, Bindings.SetFlags.ForceUpdateValue);
    }

    private static Selection Read(SIDropdown dropdown)
    {
        if (dropdown == null || dropdown.panel == null || !ElementBridge.IsVisibleInHierarchy(dropdown)) return null;
        var bindings = GameSubsystems.Get<BindingSubsystem>();
        var key = dropdown.m_selectionValueBindingKey;
        if (key.m_key == 0 || !bindings.m_nodes.TryGetValue(key.m_key, out var node) || node.Parent == null) return null;
        // These are the roles in the native clause widget, not game-field names,
        // screen captions, option labels or a player/position allowlist.
        var property = node.Name switch
        {
            "SelectedPrimaryValue" => "TransferOfferClauseDropdownValue",
            "SelectedSecondaryValue" => "TransferOfferClauseSecondaryValue",
            "SelectedLeftRightValue" => "TransferOfferClauseLeftRightValue",
            _ => null,
        };
        if (property == null || node.Alias.m_key != 0 || bindings.GetContextInfo(node).handler != null) return null;
        var scope = node.Parent.TryCast<IReadOnlyNode>();
        if (scope == null) return null;
        var clause = BindingRecords.FindNode(scope, "clause", 0);
        var reference = clause == null ? null : BindingRecords.ValueOf(clause)?.Get()?.TryCast<TransferOfferClauseReference>();
        if (reference == null) return null;
        var valueNode = BindingRecords.FindNode(clause, property, 0);
        var listNode = BindingRecords.FindNode(clause, property + "SelectionList", 0);
        if (valueNode == null || listNode == null) return null;
        var caption = BindingRecords.TextOf(BindingRecords.ValueOf(valueNode));
        var source = BindingRecords.ListOf(BindingRecords.ValueOf(listNode));
        if (source == null || source.Count == 0 || source.Count > 200) return null;
        var sourceCaptions = new List<string>();
        for (int i = 0; i < source.Count; i++) sourceCaptions.Add(Caption(source[i]));
        int canonical = ChoiceCaptionMatch.Unique(caption, sourceCaptions);
        if (canonical < 0) return null;
        var sourceChoice = source[canonical];
        var sourceKey = OptionKey(sourceChoice);
        if (sourceKey == null) return null;
        // A lazy closed dropdown can have no realized choices yet. Its own
        // authoritative source still supplies the actual record for ShowDropdown.
        var choices = dropdown.m_choices;
        if (choices == null || choices.Count == 0)
            return new Selection(bindings, node, key, sourceChoice, canonical);
        if (choices.Count > 200) return null;
        var captions = new List<string>();
        for (int i = 0; i < choices.Count; i++) captions.Add(Caption(choices[i]));
        int index = ChoiceCaptionMatch.Unique(caption, captions);
        if (index < 0 || OptionKey(choices[index]) != sourceKey) return null;
        return new Selection(bindings, node, key, choices[index], index);
    }

    private static string Caption(TypedValue value)
    {
        var record = BindingRecords.RecordOf(value);
        if (record == null) return null;
        var fields = BindingRecords.Fields(record);
        return BindingRecords.TextOf(BindingRecords.Field(fields, "DisplayValue"));
    }

    private static string OptionKey(TypedValue value)
    {
        var record = BindingRecords.RecordOf(value);
        if (record == null) return null;
        var fields = BindingRecords.Fields(record);
        var property = BindingRecords.Field(fields, "PropertyValue");
        var number = BindingRecords.NumberOf(property);
        if (number.HasValue && double.IsFinite(number.Value)) return "n:" + number.Value.ToString("R", CultureInfo.InvariantCulture);
        var flag = BindingRecords.FlagOf(property);
        if (flag.HasValue) return "b:" + flag.Value;
        var reference = property?.Get()?.TryCast<SI.Interop.InteropReference>();
        if (reference != null) return BindingRecords.ReferenceIdentity(reference);
        var text = BindingRecords.TextOf(property);
        return text == null ? null : "s:" + text;
    }
}
