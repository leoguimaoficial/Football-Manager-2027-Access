using System;
using System.Collections.Generic;
using System.Text;
using FM27Access.Navigation;
using UnityEngine.UIElements;

namespace FM27Access.Plugin.Server;

/// <summary>
/// The "speak to" side panel — the coach/NPC speech-bubble construct the game reuses for
/// introductions, press conferences, team talks and similar interactions. The game wraps the whole
/// panel (speaker header, dialogue bubbles, and — in press conferences — the response-option
/// buttons) in a single SIButton, so the generic classifier collapses all of it into one button
/// stop and the dialogue and the answer choices never reach the page. This renderer detects that
/// container button and instead renders the panel's children through the normal walk/render
/// pipeline, so the dialogue shows as text and each response option shows as its own actionable
/// button (registered like any other button, so activating it answers).
///
/// HTML surface only: the speech surface still sees the single button and reads it via its
/// announcement (Alt+I). See docs/archive/plans/removing panel cycling plan.md.
/// </summary>
internal sealed class SpeakToRenderer : IElementRenderer
{
    // FM27 reuses the same answer button for a different question/answer.
    // Preserve the FM26 child renderer, but keep that command's identity tied
    // to its actual question and caption instead of its recycled UXML path.
    private static readonly Dictionary<IntPtr, VisualElement> Questions = new();
    internal static void ResetQuestions() => Questions.Clear();

    internal static bool IsAnswer(VisualElement element)
    {
        int depth=0;
        for(var parent=element.hierarchy.parent;parent!=null&&depth<8;parent=parent.hierarchy.parent,depth++)
            // Optional actions include the native start/finish steps as well
            // as answer groups. Their recycled controls need the same scope.
            if(parent.name is "SpeakToMeetingAnswerActionButton" or "SpeakToOptionalActionButton")return true;
        return false;
    }

    internal static string AnswerContext(VisualElement element,bool fresh=false)
    {
        if(!IsAnswer(element))return null;
        var panel=element;
        while(panel!=null&&panel.TryCast<SI.Bindable.Panel>()==null)panel=panel.hierarchy.parent;
        if(panel==null)return null;
        if(fresh||!Questions.TryGetValue(panel.Pointer,out var question)
            ||question==null||question.panel==null||!ElementBridge.IsVisibleInHierarchy(question))
        {
            question=null;int budget=20000;
            FindQuestion(panel,ref question,ref budget,0);
            Questions[panel.Pointer]=question;
        }
        var text=question==null?null:ServerText.Clean(ScreenWalker.ReadFirstText(question));
        if(string.IsNullOrWhiteSpace(text))return null;
        return text+"\u001e"+ServerText.Clean(ScreenWalker.ReadFirstText(element));
    }

    private static void FindQuestion(VisualElement element,ref VisualElement question,ref int budget,int depth)
    {
        if(budget--<=0||depth>96||!ElementBridge.IsVisible(element))return;
        if(element.name=="NPCBubbleFrame")
        {
            if(!string.IsNullOrWhiteSpace(ScreenWalker.ReadFirstText(element)))question=element;
            return;
        }
        for(int i=0;i<element.hierarchy.childCount;i++)FindQuestion(element.hierarchy[i],ref question,ref budget,depth+1);
    }

    internal static bool IsQuestion(VisualElement element)
    {
        int depth=0;
        for(var parent=element.hierarchy.parent;parent!=null&&depth<12;parent=parent.hierarchy.parent,depth++)
            if(parent.name=="NPCBubbleFrame")return true;
        return false;
    }

    // The SpeakTo container button sits within a few levels of the BindingExpect_SpeakToSidePanel /
    // SpeakToSidePanel marker (1 level in press conferences, 3 in introductions). The inner response
    // buttons live dozens of levels below that marker, so this shallow bound matches ONLY the outer
    // container and never re-fires on the buttons we render out of it (no re-entrancy).
    private const int MaxAncestorDepth = 6;

    public bool CanRender(IAccessibleElement el)
    {
        if (ServerText.SafeRole(el) != "button") return false;
        if (el is not Adapters.PluginAdapterBase a) return false;
        return IsSpeakToContainer(a.BackingElement);
    }

    public void Render(StringBuilder sb, IAccessibleElement el)
    {
        var container = ((Adapters.PluginAdapterBase)el).BackingElement;
        var items = ScreenWalker.WalkChildren(container);

        if (items.Count == 0)
        {
            // Detected the construct but found no inner content to surface (e.g. bubbles not yet
            // populated): show the speaker label rather than an empty region, so nothing visible
            // goes unannounced.
            sb.Append("<p>").Append(ServerText.Escape(DisplayText.Of(el))).Append("</p>");
            return;
        }

        foreach (var item in items)
            HtmlRenderer.RenderItem(sb, item);
    }

    private static bool IsSpeakToContainer(VisualElement el)
    {
        var ancestor = el.hierarchy.parent;
        for (int depth = 0; ancestor != null && depth < MaxAncestorDepth; depth++)
        {
            var name = ancestor.name;
            if (name == "BindingExpect_SpeakToSidePanel" || name == "SpeakToSidePanel")
                return true;
            ancestor = ancestor.hierarchy.parent;
        }
        return false;
    }
}
