using System.Text;
using FM27Access.Navigation;
using FM27Access.Plugin.Adapters;
using FM27Access.Plugin.Contexts;
using FM27Access.Speech;
using SI.Translation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>HTML presentation of the game's set-piece priority lists. Cell handles retain
/// the game's column and element indices; the speech surface uses its normal adapters.</summary>
internal static class SetPieceTakers
{
    internal const string PanelName = "SetPieceTakerPopupCard";
    private const string BlockTemplate = "SetPieceTakersBlockTemplate";

    // FM27 binds a taker row to an inline record rather than directly to its
    // PersonReference. The ordinal is mutable presentation; the real player,
    // set-piece type and side identify the command's native subject/context.
    internal static string RowIdentity(SI.Core.TypedValue binding)
    {
        var record = BindingRecords.RecordOf(binding);
        if (record == null) return null;
        var fields = BindingRecords.Fields(record);
        if (fields.Count != 4 || !fields.ContainsKey("SetPieceType")
            || !fields.ContainsKey("SetPieceSide") || !fields.ContainsKey("Index")
            || !fields.TryGetValue("Player", out var value)) return null;
        var person = value?.Get()?.TryCast<FM.UI.PersonReference>();
        string identity = BindingRecords.ReferenceIdentity(person);
        var kind = BindingRecords.NumberOf(fields["SetPieceType"]);
        var side = BindingRecords.NumberOf(fields["SetPieceSide"]);
        if (identity == null || kind == null || side == null
            || !double.IsFinite(kind.Value) || !double.IsFinite(side.Value)) return null;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return "set-piece:" + kind.Value.ToString("R", culture) + ":"
            + side.Value.ToString("R", culture) + "|" + identity;
    }

    // Runtime translation GameIDs, resolved against the current game language each render.
    internal static string Text(int id)
    {
        var value = ServerText.Clean(new TranslationID(id, string.Empty).TranslatedString);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        Degrade.Note($"set-piece takers: translation {id} unavailable");
        return Degrade.Unavailable;
    }

    internal static VisualElement Block(VisualElement el)
    {
        if (!ElementBridge.IsInPanel(el, PanelName)) return null;
        for (var p = el.hierarchy.parent; p != null; p = p.hierarchy.parent)
            // FM27 gives instances side-specific names; the shared template
            // asset retains its identity.
            if (p.name == BlockTemplate
                || p.TryCast<TemplateContainer>()?.templateSource?.name == BlockTemplate) return p;
        return null;
    }

    internal static VisualElement Title(VisualElement block)
    {
        // The block's label precedes its Add Player dropdown and StreamedTable.
        var frame = Find(block, v => v.name == "Frame 1000003515")
            ?? Find(block, v => v.name == "Header");
        return frame == null ? null : Find(frame, v => v.GetIl2CppType().Name == "SIText");
    }

    private static VisualElement Find(VisualElement root, Func<VisualElement, bool> match)
    {
        if (!ElementBridge.IsVisible(root)) return null;
        if (match(root)) return root;
        for (int i = 0; i < root.hierarchy.childCount; i++)
        {
            var found = Find(root.hierarchy[i], match);
            if (found != null) return found;
        }
        return null;
    }

    internal static string TitleText(VisualElement title)
        => ServerText.Clean(title.Cast<SI.Bindable.SIText>().text);

    internal static string TitleId(VisualElement title)
        => LiveValues.RegisterReadOnly(ElementBridge.CreateAdapter(title, ElementClassification.Text));

    internal static bool TryRenderTable(StringBuilder sb, ScreenWalker.TableItem t)
    {
        var block = Block(t.Table);
        if (block == null || t.Rows == null || t.Headers.Count is not (4 or 7) || t.PageCount > 1 || t.Truncated)
            return false;
        // Drag gutter, priority (ordinal/up/down), player and remove. The
        // inherited seven-column variant also carries three player attributes;
        // the inspected FM27 template omits those columns.
        // A different template shape retains the generic presentation instead of guessing.
        int removeColumn = t.Headers.Count - 1;
        foreach (var row in t.Rows)
        {
            if (row == null || row.Cells.Count != t.Headers.Count || row.Cells[0].Elements.Count != 0
                || row.Cells[1].Elements.Count != 3 || row.Cells[1].Elements[0].CanActivate
                || !row.Cells[1].Elements[1].CanActivate || !row.Cells[1].Elements[2].CanActivate
                || row.Cells[removeColumn].Elements.Count != 1 || !row.Cells[removeColumn].Elements[0].CanActivate)
                return false;
        }
        var title = Title(block);
        if (title == null) return false;
        string section = TitleText(title);
        string path = ServerHandles.IdentityPath(t.Table);
        string titleId = LiveValues.AllocateId(path + "@takers");
        string priority = Text(5485);
        string remove = Text(1560);
        sb.Append("<table id=\"").Append(titleId).Append("\" aria-label=\"")
          .Append(ServerText.Escape(section)).Append("\" data-takers=\"1\"><thead><tr>");
        for (int c = 1; c < t.Headers.Count; c++)
        {
            var header = t.Headers[c];
            if (c == 1) header = new TableHeader { Text = priority };
            if (c == removeColumn) header = new TableHeader { Text = remove };
            HtmlRenderer.RenderHeader(sb, header, path, c);
        }
        sb.Append("</tr></thead><tbody>");
        foreach (var row in t.Rows)
        {
            var player = row.Cells[2].Elements.FirstOrDefault(e => e.CanActivate && !e.IsDropdown && !e.IsToggle);
            string playerName = ServerText.Clean(player.Text);
            if (string.IsNullOrWhiteSpace(playerName))
            {
                Degrade.Note("set-piece takers: player name unavailable");
                playerName = Degrade.Unavailable;
            }
            sb.Append("<tr data-taker-row=\"").Append(ServerText.Escape(playerName))
              .Append("\" data-taker-section=\"").Append(titleId).Append("\">");
            for (int c = 1; c < t.Headers.Count; c++)
            {
                sb.Append(c == 2 ? "<th scope=\"row\">" : "<td>");
                if (c == 1 || c == removeColumn)
                {
                    var cell = row.Cells[c];
                    for (int e = 0; e < cell.Elements.Count; e++)
                    {
                        if (e > 0) sb.Append(' ');
                        var item = cell.Elements[e];
                        if (!item.CanActivate) { sb.Append(ServerText.EscapeText(item.Text)); continue; }
                        string action = c == removeColumn ? "remove" : e == 1 ? "up" : "down";
                        string label = c == removeColumn ? remove : priority + ": " + Text(e == 1 ? 10627 : 10628);
                        // Use the same record identity and revalidation as all
                        // other table commands. Priority changes recycle the
                        // native row; its index must not identify the player.
                        string id = HtmlRenderer.RegisterCellHandle(t.Table, path,
                            row.DataIndex, c, e, row.NativeIdentity);
                        sb.Append("<button type=\"button\" id=\"").Append(id)
                          .Append("\" data-taker=\"").Append(ServerText.Escape(playerName))
                          .Append("\" data-taker-section=\"").Append(titleId)
                          .Append("\" data-taker-action=\"").Append(action)
                          .Append("\" onclick=\"fmtaker('").Append(id).Append("')\"");
                        if (item.IsDisabled) sb.Append(" disabled");
                        sb.Append('>').Append(ServerText.Escape(label + ", " + playerName + ", " + section))
                          .Append("</button>");
                    }
                }
                else
                {
                    // The shirt's graphic is decorative; its separate jersey number remains.
                    // Inspect only a realised row, preserving every original handle index.
                    int first = 0;
                    if (c == 2 && t.Context.RealizeCellElement(row.DataIndex, c, 0, scroll: false) is PluginAdapterBase a
                        && a.BackingElement.name == "Bib" && a.BackingElement.GetIl2CppType().Name == "SIImage") first = 1;
                    HtmlRenderer.RenderCell(sb, t.Table, path, row.DataIndex, c, row, row.Cells[c], playerName, t.Headers[c].Text, first);
                }
                sb.Append(c == 2 ? "</th>" : "</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table><p role=\"status\" aria-live=\"polite\" aria-atomic=\"true\" data-taker-status=\"")
          .Append(titleId).Append("\"></p>");
        return true;
    }
}

internal sealed class SetPieceTakersRenderer : IElementRenderer
{
    public bool CanRender(IAccessibleElement el)
    {
        if (el is not PluginAdapterBase a || !ElementBridge.IsInPanel(a.BackingElement, SetPieceTakers.PanelName)) return false;
        string role = ServerText.SafeRole(el);
        if (role == "button") return a.BackingElement.name == "buttons-controls-pc-default";
        var block = SetPieceTakers.Block(a.BackingElement);
        if (block == null) return false;
        return role == "dropdown" || (role == "text" && SetPieceTakers.Title(block)?.Pointer == a.BackingElement.Pointer);
    }

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        var backing = ((PluginAdapterBase)el).BackingElement;
        string role = ServerText.SafeRole(el);
        if (role == "text")
        {
            sb.Append("<h3 id=\"").Append(SetPieceTakers.TitleId(backing)).Append("\">")
              .Append(ServerText.EscapeText(SetPieceTakers.TitleText(backing))).Append("</h3>");
            return;
        }
        string id = ServerHandles.Register(new ServerHandles.StopHandle { El = el }, el);
        if (role == "button")
        {
            sb.Append("<p><button type=\"button\" id=\"").Append(id)
              .Append("\" onclick=\"act('").Append(id).Append("')\">")
              .Append(ServerText.Escape(SetPieceTakers.Text(4156))).Append("</button></p>");
            return;
        }
        var title = SetPieceTakers.Title(SetPieceTakers.Block(backing));
        string label = ServerText.Label(el);
        if (title != null) label += ", " + SetPieceTakers.TitleText(title);
        sb.Append("<p><select data-taker-add=\"1\" id=\"").Append(id).Append("\" aria-label=\"")
          .Append(ServerText.Escape(label)).Append('"');
        sb.Append(ServerText.IsDisabled(el) ? " disabled" : " data-dd=\"1\"");
        sb.Append("><option selected>").Append(ServerText.EscapeText(ServerText.State(el))).Append("</option></select></p>");
    }
}
