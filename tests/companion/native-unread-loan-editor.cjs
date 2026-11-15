// Debug diagnostic only: use the SAME native tab/ButtonAdapter contracts without
// composing the loan form or enumerating its value controls. This is not a
// vanilla-game comparison: normal panel observation and native cursor remain.
const assert=require('node:assert/strict');
module.exports=async(c,mvid,operation,expectedPointer=null)=>{
 assert.match(mvid,/^[0-9a-f-]{36}$/);assert.ok(['observe','loan-tab','cancel'].includes(operation));
 if(expectedPointer!==null)assert.match(expectedPointer,/^\d+$/);
 const code=`
var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;
System.Reflection.Assembly core=null;foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}"&&a.GetType("FM27Access.Plugin.CoreEntry").GetField("_bridge",f).GetValue(null)!=null)core=a;
if(core==null)throw new System.InvalidOperationException("Require the actual active core");
var panel=core.GetType("FM27Access.Plugin.FM27PanelCatalog").GetMethod("GetForegroundPanel",f).Invoke(null,null);
if(panel==null)throw new System.InvalidOperationException("Actual native foreground is absent");
var id=panel.GetType().GetProperty("PanelID",f).GetValue(panel,null);
string name=(string)id.GetType().GetProperty("name",f).GetValue(id,null);
var root=(UnityEngine.UIElements.VisualElement)panel;
System.Console.WriteLine("PANEL|"+name);System.Console.WriteLine("POINTER|"+root.Pointer.ToInt64());
${operation==='observe'?'':`
if(name!="PlayerReportTransferStatusModalDialog")throw new System.InvalidOperationException("Refuse a different native editor");
${expectedPointer?`if(root.Pointer.ToInt64().ToString()!="${expectedPointer}")throw new System.InvalidOperationException("Native editor changed before action");`:''}
var nodes=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();nodes.Push(root);
var buttons=core.GetType("FM27Access.Plugin.Adapters.ButtonAdapter");object target=null;int candidates=0;
var cache=System.Activator.CreateInstance(core.GetType("FM27Access.Plugin.ClassificationCache"),true);
while(nodes.Count>0){
 var element=nodes.Pop();if(!(bool)core.GetType("FM27Access.Plugin.ElementBridge").GetMethod("IsVisible",f).Invoke(null,new object[]{element}))continue;
 var kind=element.GetIl2CppType().Name;
 ${operation==='loan-tab'?`
 if(kind=="ButtonGroup"){
  var context=System.Activator.CreateInstance(core.GetType("FM27Access.Plugin.Contexts.TabContext"),f,null,new object[]{element,cache},null);
  var tabs=(System.Collections.IEnumerable)context.GetType().GetMethod("GetAllTabs",f).Invoke(context,null);
  foreach(var tab in tabs){var t=tab.GetType();string caption=(string)t.GetMethod("GetLabel",f).Invoke(tab,null);System.Console.WriteLine("TAB|"+caption+"|"+t.GetProperty("IsSelected",f).GetValue(tab,null));if(caption=="Empréstimo"){target=tab;candidates++;}}
  continue;
 }`:`
 if(kind=="SIButton"){
  var adapter=System.Activator.CreateInstance(buttons,f,null,new object[]{element},null);
  string caption=(string)buttons.GetMethod("GetLabel",f).Invoke(adapter,null);
  if(caption=="Cancelar"){if(!element.enabledInHierarchy)throw new System.InvalidOperationException("Actual Cancel is disabled");target=adapter;candidates++;}
  continue;
 }`}
 for(int i=0;i<element.hierarchy.childCount;i++)nodes.Push(element.hierarchy[i]);
}
if(candidates!=1)throw new System.InvalidOperationException("Require one actual native ${operation} target, found "+candidates);
if(core.GetType("FM27Access.Plugin.FM27PanelCatalog").GetMethod("GetForegroundPanel",f).Invoke(null,null).GetType().GetProperty("Pointer",f).GetValue(panel,null).ToString()!=root.Pointer.ToString())throw new System.InvalidOperationException("Foreground changed before action");
${operation==='loan-tab'?`
if(!(bool)target.GetType().GetProperty("IsSelected",f).GetValue(target,null)){target.GetType().GetMethod("Activate",f).Invoke(target,null);System.Console.WriteLine("ACTION|loan-tab");}else System.Console.WriteLine("ACTION|already-selected");
`:`target.GetType().GetMethod("Activate",f).Invoke(target,null);System.Console.WriteLine("ACTION|cancel");`}
`}
`;
 const raw=await c.page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);
 assert.doesNotMatch(raw,/\[exception\]|\[compile\]|error CS\d+/i,'Native diagnostic must stop before any subsequent command on failure');
 return{raw,panel:raw.match(/^PANEL\|(.+)$/m)?.[1],pointer:raw.match(/^POINTER\|(\d+)$/m)?.[1],action:raw.match(/^ACTION\|(.+)$/m)?.[1]};
};
