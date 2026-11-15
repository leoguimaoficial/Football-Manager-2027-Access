using System.Text;
using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
namespace FM27Access.Plugin.Server;
internal sealed class FM27ShortcutCaptureRenderer:IElementRenderer
{
    public bool CanRender(IAccessibleElement element)=>element is FM27ShortcutCaptureAdapter;
    public void Render(StringBuilder sb,IAccessibleElement element)
    {
        var capture=(FM27ShortcutCaptureAdapter)element;
        string id=ServerHandles.Register(new ServerHandles.StopHandle{El=capture},capture),value=ServerText.EscapeText(capture.GetCurrentText());
        sb.Append("<p><input type=\"text\" id=\"").Append(id).Append("\" aria-label=\"").Append(ServerText.EscapeText(capture.GetLabel()))
          .Append("\" value=\"").Append(value).Append("\" data-keycapture=\"1\" data-key-value=\"").Append(value).Append('"');
        if(!capture.Waiting)sb.Append(" readonly");
        if(ServerText.IsDisabled(capture))sb.Append(" disabled");
        sb.Append(" onkeydown=\"FM.keycapture.press(event,this)\"></p>");
    }
}
