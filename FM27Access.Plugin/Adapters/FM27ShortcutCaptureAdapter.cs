using UnityEngine.UIElements;
namespace FM27Access.Plugin.Adapters;

// FM27's rebinder exposes capture state as SIText. Preserve the normal HTML text-field
// presentation while submitting physical keys to the game's own rebinding operation.
internal sealed class FM27ShortcutCaptureAdapter:TextFieldAdapter
{
    private readonly FM.UI.FMInputRebinder rebinder;
    internal FM27ShortcutCaptureAdapter(VisualElement element):base(element)=>rebinder=FM27ShortcutBinding.Find(element)??throw new InvalidOperationException("Native rebinder is missing.");
    internal static bool IsCapture(VisualElement element)=>element.name=="action-shorcut-test"&&FM27ShortcutBinding.Find(element)!=null;
    internal bool Waiting=>FM27ShortcutBinding.Waiting(rebinder);
    public override string GetLabel()=>FM27ShortcutBinding.ActionLabel(rebinder);
    public override string GetPrimaryLabel()=>GetLabel();
    internal override string FieldLabel()=>GetLabel();
    public override string GetState()=>GetCurrentText();
    public override string GetCurrentText()=>FM27ShortcutBinding.Display(rebinder,Element.TryCast<TextElement>()?.text??"");
    public override bool SetText(string value)
    {
        if(!Waiting)throw new InvalidOperationException("Native shortcut capture is not active.");
        FM27ShortcutBinding.SubmitCode(rebinder,value);return true;
    }
    public override void Activate()=>Element.Focus();
}
