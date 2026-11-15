// Read-only current realized native controls. Records the actual resolver branch
// before deciding whether a displayed table value was mistaken for a field name.
const assert=require('node:assert/strict');
module.exports=async(c,mvid,ids)=>{
 assert.match(mvid,/^[0-9a-f-]{36}$/);assert.ok(ids.length);for(const id of ids)assert.match(id,/^[-\w:.]+$/);
 const code=`
var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;
System.Reflection.Assembly core=null;foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}"&&a.GetType("FM27Access.Plugin.CoreEntry").GetField("_bridge",f).GetValue(null)!=null)core=a;
if(core==null)throw new System.InvalidOperationException("Require the actual active core");
var handles=core.GetType("FM27Access.Plugin.Server.ServerHandles");var baseAdapter=core.GetType("FM27Access.Plugin.Adapters.PluginAdapterBase");
var printer=core.GetType("FM27Access.Plugin.Diagnostics.TreeDump");var ot=core.GetType("FM27Access.Plugin.Diagnostics.TreeDump+Options");var options=System.Activator.CreateInstance(ot,true);ot.GetField("MaxNodes",f).SetValue(options,140);ot.GetField("MaxDepth",f).SetValue(options,14);ot.GetField("ShowEnabled",f).SetValue(options,true);
foreach(string id in new[]{${ids.map(JSON.stringify).join(',')}}){
object[] args={id,null,null,false};var adapter=handles.GetMethod("ResolveDropdownHandle",f).Invoke(null,args);
if(adapter==null){System.Console.WriteLine("NOT_REALIZED|"+id+"|"+args[1]);continue;}
System.Console.WriteLine("CONTROL|"+id);
foreach(string name in new[]{"GetLabel","GetState","FindSelectionFieldLabel","FindDividedControlLabel","FindPairedNameButtonLabel","FindFieldLabel","FindAllChildText","FindSplitButtonSiblingText","IsValueNamed"}){
System.Reflection.MethodInfo method=null;for(var t=adapter.GetType();t!=null&&method==null;t=t.BaseType)method=t.GetMethod(name,f|System.Reflection.BindingFlags.DeclaredOnly);
if(method==null)throw new System.MissingMethodException(name);System.Console.WriteLine("RESOLVE|"+name+"|"+method.Invoke(adapter,null));}
var element=(UnityEngine.UIElements.VisualElement)baseAdapter.GetField("Element",f).GetValue(adapter);var container=(UnityEngine.UIElements.VisualElement)baseAdapter.GetProperty("FieldLabelContainer",f).GetValue(adapter,null);
System.Console.WriteLine("NATIVE|"+element.GetIl2CppType().Name+"|"+element.name);
if(container!=null){System.Console.WriteLine("LABEL_CONTAINER|"+container.GetIl2CppType().Name+"|"+container.name);var sb=new System.Text.StringBuilder();printer.GetMethod("Write",f).Invoke(null,new object[]{container,sb,options});System.Console.WriteLine(sb.ToString());}
var ancestor=element;for(int depth=0;depth<7&&ancestor!=null;depth++,ancestor=ancestor.hierarchy.parent)System.Console.WriteLine("ANCESTOR|"+depth+"|"+ancestor.GetIl2CppType().Name+"|"+ancestor.name);
var cell=element;for(int depth=0;depth<16&&cell!=null;depth++,cell=cell.hierarchy.parent){var kind=cell.GetIl2CppType().Name;if(kind=="Panel")break;if(kind!="Cell")continue;System.Console.WriteLine("OWNING_NATIVE_CELL");var sb=new System.Text.StringBuilder();var truncated=printer.GetMethod("Write",f).Invoke(null,new object[]{cell,sb,options});System.Console.WriteLine(sb.ToString());System.Console.WriteLine("CELL_TRUNCATED|"+truncated);break;}
}
`;
 const raw=await c.page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);
 assert.doesNotMatch(raw,/\[exception\]|error CS\d+/i);return{raw,realized:raw.split(/\r?\n/).filter(l=>l.startsWith('CONTROL|')).length,unrealized:raw.split(/\r?\n/).filter(l=>l.startsWith('NOT_REALIZED|'))};
};
