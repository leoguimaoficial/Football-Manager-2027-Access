using UnityEngine.UIElements;

namespace FM27Access.Plugin.Adapters;

// An icon/text switch can omit its text branch from the realized hierarchy.
// Its own UXML still carries the game's constant localized SIText binding.
// Read that caption without realizing the branch or displaying a tooltip.
internal static class StaticButtonCaption
{
    private const string CollectionTextFunction = "e281793f1cc6505468b78ca3cc9222da";
    private static readonly Dictionary<string, VisualTreeAsset> _rowAssets=new();
    private static readonly Dictionary<string, SI.Core.DataCollection> _collections=new();
    // Native visual function used by the inspected static localized SIText
    // bindings. This is an asset identity, not a caption or per-screen mapping.
    private const string LocalizedTextFunction = "eae2be944eee9e2449baef416111a087";
    // Same constant TranslationID contract with native TranslationArg inputs
    // (for example gender). Its rendered SIText owns the formatted result.
    private const string FormattedLocalizedTextFunction = "f97abdf5a74842647ba921d183f4a3eb";

    internal static bool IsFormattedLocalizedText(SI.Bindable.SIText text) =>
        text?.m_textBinding?.Kind == SI.Bindable.BindingMethodKind.VisualFunction
        && text.m_textBinding.VisualFunction?.Target == FormattedLocalizedTextFunction;

    internal static string Read(VisualElement button)
    {
        var asset = button.visualElementAsset;
        if (asset?.fullTypeName != "SI.Bindable.SIButton") return null;
        string caption = null;
        int candidates = 0;
        try
        {
            for (int i = 0; i < asset.childCount; i++)
            {
                var variant = asset[i];
                if (variant.fullTypeName != "SI.Bindable.BindableSwitchElement") continue;
                for (int j = 0; j < variant.childCount; j++)
                {
                    var text = variant[j].TryCast<VisualElementAsset>();
                    if (text?.fullTypeName != "SI.Bindable.SIText") continue;
                    candidates++;
                    caption = ReadTextAsset(text);
                    if (caption == null) return null;
                }
            }
        }
        catch (Exception ex)
        {
            Speech.Log.Warn($"[StaticButtonCaption] Native caption read failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        return candidates == 1 && !string.IsNullOrWhiteSpace(caption) ? caption : null;
    }

    // Also identifies an unnamed SelectionField SIText that is a constant
    // localized menu title, rather than a bound selected value.
    internal static string ReadText(VisualElement text)
    {
        // SelectionField also holds runtime TemplateContainers whose UXML
        // asset can be absent. Only an actual SIText owns this text contract.
        if (text == null || text.GetIl2CppType().Name != "SIText") return null;
        try
        {
            var asset = text.visualElementAsset;
            var caption = asset == null || asset.Pointer == IntPtr.Zero
                ? null : ReadTextAsset(asset.TryCast<VisualElementAsset>());
            // Instantiated FM27 label templates can have no readable serialized
            // binding on their asset. The live SIText still owns the same constant
            // TranslationID function. Conditional On/Off/value functions do not
            // satisfy this contract and must keep the ordinary field-label path.
            var nativeText = text.TryCast<SI.Bindable.SIText>();
            return caption ?? ReadLocalizedBinding(nativeText?.m_textBinding, nativeText?.text);
        }
        catch (Exception ex)
        {
            Speech.Log.Warn($"[StaticButtonCaption] Native text binding read failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // An icon-only SIDropdown can leave SelectionField completely blank. Its
    // row UXML still defines the official enum -> DataCollection translation.
    // Read that mapping for the ACTUAL choice value without opening a popup or
    // borrowing the surrounding card's entire body. Strings are translated on
    // every read; only native assets are cached, so language changes still work.
    internal static string ReadDropdownValue(SI.Bindable.SIDropdown dropdown)
    {
        if(dropdown?.RowTemplate==null)return null;
        try
        {
            var reference=dropdown.RowTemplate;
            if(string.IsNullOrEmpty(reference.AssetGUID))return null;
            if(!_rowAssets.TryGetValue(reference.AssetGUID,out var tree))
            {
                tree=reference.Asset?.TryCast<VisualTreeAsset>()??reference.LoadAssetAsync().WaitForCompletion();
                if(tree==null)return null;
                _rowAssets.Add(reference.AssetGUID,tree);
            }
            VisualElementAsset text=null;
            var pending=new Stack<VisualElementAsset>();pending.Push(tree.visualTree);
            while(pending.Count>0)
            {
                var item=pending.Pop();if(item==null)continue;
                if(item.fullTypeName=="SI.Bindable.SIText")
                {if(text!=null)return null;text=item;}
                for(int i=0;i<item.childCount;i++)pending.Push(item[i].TryCast<VisualElementAsset>());
            }
            var binding=text?.serializedData?.TryCast<SI.Bindable.SIText.UxmlSerializedData>()?.TextBinding;
            var function=binding?.Kind==SI.Bindable.BindingMethodKind.VisualFunction?binding.VisualFunction:null;
            // FM27 also serializes the same collection-text function with seven
            // inputs, omitting the explicit translation flag. Keep the exact
            // row path/empty formatting contract and require a TranslationID
            // result below; no arbitrary function or formatting arg is ignored.
            int inputCount=function?.Inputs?.Count??0;
            if(function?.Target!=CollectionTextFunction||(inputCount!=7&&inputCount!=8))return null;
            var source=function.Inputs[3]?.Cast<SI.VisualScripting.AssetReferenceVisualArg>()?.m_value;
            var column=function.Inputs[4]?.Cast<SI.VisualScripting.StringVisualArg>()?.m_value;
            var arguments=function.Inputs[5]?.Cast<SI.VisualScripting.StructListVisualArg<SI.Bindable.TranslationArg>>()?.m_items;
            var translate=inputCount==8?function.Inputs[7]?.Cast<SI.VisualScripting.BooleanVisualArg>():null;
            var pathArg=function.Inputs[6]?.Cast<Il2CppSystem.Object>();
            var pathField=pathArg?.GetIl2CppType().GetField("m_value",Il2CppSystem.Reflection.BindingFlags.Instance|Il2CppSystem.Reflection.BindingFlags.NonPublic);
            var path=pathField?.GetValue(pathArg)?.TryCast<SI.Bindable.BindingPath>();
            // The function must consume this dropdown's row parameter, and no
            // extra formatting args or unhandled path may change its meaning.
            var rowName=string.IsNullOrEmpty(dropdown.RowParamName)?"row":dropdown.RowParamName;
            if(path?.Path!=rowName||(inputCount==8&&translate?.m_value!=true)||arguments?.Length>0
                ||source==null||string.IsNullOrEmpty(source.AssetGUID)||string.IsNullOrEmpty(column))return null;
            SI.Core.TypedValue choice;
            if(dropdown.SelectedIndex>=0)
            {
                if(dropdown.m_choices==null||dropdown.SelectedIndex>=dropdown.m_choices.Count)return null;
                choice=dropdown.m_choices[dropdown.SelectedIndex];
            }
            else
            {
                // Finalized native terms clear their choices/index but retain
                // the bound enum in SelectedValue. Read that actual frozen value
                // only for a disabled, empty selector; an active action menu
                // with index -1 still has no selected choice to announce.
                if(dropdown.enabledInHierarchy||(dropdown.m_choices?.Count??0)!=0)return null;
                choice=dropdown.SelectedValue;
            }
            var number=DropdownChoiceNumber(choice);
            if(number==null||!double.IsFinite(number.Value)||number.Value!=Math.Truncate(number.Value)
                ||number.Value<int.MinValue||number.Value>int.MaxValue)return null;
            if(!_collections.TryGetValue(source.AssetGUID,out var collection))
            {
                collection=source.Asset?.TryCast<SI.Core.DataCollection>()??source.LoadAssetAsync<SI.Core.DataCollection>().WaitForCompletion();
                if(collection==null)return null;
                _collections.Add(source.AssetGUID,collection);
            }
            var value=collection.QuantiseAndMap(column,(int)number.Value)?.Get();
            if(value==null||!value.GetIl2CppType().Equals(Il2CppInterop.Runtime.Il2CppType.Of<SI.Translation.TranslationID>()))return null;
            var caption=value.Unbox<SI.Translation.TranslationID>().TranslatedString;
            return string.IsNullOrWhiteSpace(caption)?null:caption;
        }
        catch(Exception ex)
        {
            Speech.Log.Warn($"[StaticButtonCaption] Native dropdown caption read failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static double? DropdownChoiceNumber(SI.Core.TypedValue choice)
    {
        var number=Server.BindingRecords.NumberOf(choice);
        if(number!=null)return number;
        // The native promise selector wraps each actual enum in a one-field
        // row reference. Its two choices contain RowData0 and RowData2, not
        // ordinal0/1. Unwrap only that demonstrated envelope, never arbitrary
        // records or a guessed property from another binding context.
        var reference=choice?.Get()?.TryCast<SI.Bindable.DynamicReference>();
        if(reference==null)return null;
        var names=SI.Bindable.Reference.Core.PropertyIdentifierSet.Instance;
        SI.Core.TypedValue row=null;
        int count=0;
        foreach(var key in reference.Keys)
        {
            if(++count>1||names.GetInfo(key).Name!="RowData")return null;
            row=reference[key];
        }
        return count==1?Server.BindingRecords.NumberOf(row):null;
    }

    private static string ReadTextAsset(VisualElementAsset text)
    {
        if (text?.fullTypeName != "SI.Bindable.SIText") return null;
        try
        {
            return ReadLocalizedBinding(text.serializedData?.TryCast<SI.Bindable.SIText.UxmlSerializedData>()?.TextBinding);
        }
        catch (Exception ex)
        {
            Speech.Log.Warn($"[StaticButtonCaption] Native text binding read failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string ReadLocalizedBinding(SI.Bindable.BindingMethod<SI.Bindable.SIText> binding, string renderedText = null)
    {
        try
        {
            var function = binding?.Kind == SI.Bindable.BindingMethodKind.VisualFunction ? binding.VisualFunction : null;
            bool formatted = function?.Target == FormattedLocalizedTextFunction
                && function.Inputs?.Count == 4;
            if (!formatted && (function?.Target != LocalizedTextFunction || function.Inputs?.Count != 3)) return null;
            // The third argument is this function's actual TranslationID constant.
            var argument = function.Inputs[2]?.Cast<Il2CppSystem.Object>();
            var type = argument?.GetIl2CppType();
            if (type?.FullName?.StartsWith("SI.VisualScripting.GenericVisualArg`1[[SI.Translation.TranslationID,", StringComparison.Ordinal) != true)
                return null;
            var field = type.GetField("m_value", Il2CppSystem.Reflection.BindingFlags.Instance | Il2CppSystem.Reflection.BindingFlags.NonPublic);
            if (field == null || !field.FieldType.Equals(Il2CppInterop.Runtime.Il2CppType.Of<SI.Translation.TranslationID>())) return null;
            var id = field.GetValue(argument).Unbox<SI.Translation.TranslationID>();
            // Do not reformat the game's gender/other translation arguments or
            // expose an unformatted template from an unrealized asset branch.
            if (formatted) return string.IsNullOrWhiteSpace(renderedText) ? null : renderedText;
            return string.IsNullOrWhiteSpace(id.TranslatedString) ? null : id.TranslatedString;
        }
        catch (Exception ex)
        {
            Speech.Log.Warn($"[StaticButtonCaption] Native text binding read failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
