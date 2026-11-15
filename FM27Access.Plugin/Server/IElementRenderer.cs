using System.Text;
using FM27Access.Navigation;

namespace FM27Access.Plugin.Server;

/// <summary>
/// A custom feature's contribution to screen rendering. HtmlRenderer consults the registered
/// renderers (in order, first match wins) before its generic role switch, so a feature owns
/// its own markup without HtmlRenderer knowing the feature set. Renderers are contributed via
/// <see cref="FeatureRegistry.RegisterRenderers"/>.
/// </summary>
internal interface IElementRenderer
{
    bool CanRender(IAccessibleElement el);
    void Render(StringBuilder sb, IAccessibleElement el);
}
