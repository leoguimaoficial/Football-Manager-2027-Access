#nullable enable annotations
using FM.UI;
using FM.Input;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
namespace FM27Access.Plugin.Adapters;
internal static class FM27ShortcutBinding
{
    public static bool Waiting(FMInputRebinder rebinder)=>rebinder.m_inputRebindingData!=null&&SI.Input.InputManager.Instance?.TryCast<GameInputManager>()?.m_rebindOperation?.started==true;
    public static FMInputRebinder? Find(VisualElement element)
    {
        for(int i=0;element!=null&&i<20;i++,element=element.hierarchy.parent)
            if(element.TryCast<FMInputRebinder>() is FMInputRebinder rebinder)return rebinder;
        return null;
    }
    public static string ActionLabel(FMInputRebinder root)
    {
        var pending=new Stack<VisualElement>();pending.Push(root);
        while(pending.Count>0)
        {
            var e=pending.Pop();if(e.name=="action-text"&&e.TryCast<TextElement>() is TextElement text)return text.text??"";
            for(int i=e.hierarchy.childCount-1;i>=0;i--)pending.Push(e.hierarchy.ElementAt(i));
        }
        return "";
    }
    public static string Display(FMInputRebinder rebinder,string waitingText)
    {
        var manager=SI.Input.InputManager.Instance?.TryCast<GameInputManager>();
        var data=rebinder.m_inputRebindingData;
        if(manager==null||data?.InputAction?.action==null||manager.m_rebindOperation?.started==true)return waitingText;
        if(!manager.TryResolveBindingIndex(data,rebinder.m_usingAlternativeShortcut,out int index))return waitingText;
        return manager.GetBindingDisplayString(data.InputAction.action,index,data.GetBindingDisplayStringOptions());
    }
    public static void SubmitCode(FMInputRebinder rebinder,string code)
    {
        var keyboard=Keyboard.current??throw new InvalidOperationException("Native keyboard is unavailable.");
        string keyName=code switch {"ControlLeft"=>"LeftCtrl","ControlRight"=>"RightCtrl","ShiftLeft"=>"LeftShift","ShiftRight"=>"RightShift","AltLeft"=>"LeftAlt","AltRight"=>"RightAlt","MetaLeft"=>"LeftMeta","MetaRight"=>"RightMeta","ArrowUp"=>"UpArrow","ArrowDown"=>"DownArrow","ArrowLeft"=>"LeftArrow","ArrowRight"=>"RightArrow","Equal"=>"Equals","BracketLeft"=>"LeftBracket","BracketRight"=>"RightBracket","NumpadAdd"=>"NumpadPlus","NumpadSubtract"=>"NumpadMinus","NumpadDecimal"=>"NumpadPeriod",_=>code.StartsWith("Key",StringComparison.Ordinal)?code[3..]:code.StartsWith("Digit",StringComparison.Ordinal)?"Digit"+code[5..]:code};
        UnityEngine.InputSystem.InputControl control;
        if(code=="Control")control=keyboard.ctrlKey;
        else if(code=="Shift")control=keyboard.shiftKey;
        else if(code=="Alt")control=keyboard.altKey;
        else
        {
            if(!Enum.TryParse<Key>(keyName,true,out var key)||key==Key.None||!Enum.IsDefined(typeof(Key),key))throw new InvalidOperationException("Unsupported native key code.");
            control=keyboard[key];
        }
        var manager=SI.Input.InputManager.Instance?.TryCast<GameInputManager>()??throw new InvalidOperationException("Native input manager is unavailable.");
        var operation=manager.m_rebindOperation??throw new InvalidOperationException("The native shortcut is not waiting for a key.");
        if(rebinder.m_inputRebindingData==null||!operation.started)throw new InvalidOperationException("The native shortcut editor is not ready.");
        if(code=="Escape"){operation.Cancel();return;}
        // Submit a candidate to the game's ALREADY RUNNING rebinding operation.
        // Its completion callbacks own composite parts, conflicts and confirmation.
        // No OS keystroke, arbitrary input dispatch or binding-file edit occurs.
        var candidates=operation.m_Candidates;
        candidates.Clear();candidates.Add(control);operation.m_Candidates=candidates;
        operation.Complete();
    }
    public static object Inspect(FMInputRebinder rebinder)
    {
        var manager=SI.Input.InputManager.Instance?.TryCast<GameInputManager>();
        var data=rebinder.m_inputRebindingData;
        if(manager==null||data==null)return new{available=false};
        var action=data.InputAction?.action;
        int index=-1;
        bool found=manager.TryResolveBindingIndex(data,rebinder.m_usingAlternativeShortcut,out index);
        var paths=new Il2CppSystem.Collections.Generic.List<string>();
        if(found&&action!=null)GameInputManager.TryCollectCompositePaths(action,GameInputManager.GetBindingRootIndex(action,index),paths);
        var bindingPaths=new List<string>();for(int i=0;i<paths.Count;i++)bindingPaths.Add(paths[i]);
        var defaultPaths=new List<string>();
        if(found&&action!=null)
        {
            int root=GameInputManager.GetBindingRootIndex(action,index);var bindings=action.bindings;
            if(bindings[root].isComposite)
            {for(int i=root+1;i<bindings.Count&&bindings[i].isPartOfComposite;i++)if(!string.IsNullOrEmpty(bindings[i].path))defaultPaths.Add(bindings[i].path);}
            else defaultPaths.Add(bindings[index].path);
        }
        return new{available=action!=null,action=action?.name,alternate=rebinder.m_usingAlternativeShortcut,found,index,paths=bindingPaths,defaultPaths,
            operation=manager.m_rebindOperation!=null,targetIndex=manager.m_rebindOperation?.m_TargetBindingIndex};
    }
}

