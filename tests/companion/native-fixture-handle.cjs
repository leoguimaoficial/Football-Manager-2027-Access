// Read-only existing production handles and the actual native header callbacks.
// No scroll, realization, event dispatch, record writes or method patch.
const assert=require('node:assert/strict');
module.exports=(mvid,id)=>{
 assert.match(mvid,/^[a-f\d-]{36}$/i);assert.match(id,/^[-\w]+$/);
 return `foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}"){
 var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
 var sh=a.GetType("FM27Access.Plugin.Server.ServerHandles");var handles=(System.Collections.IDictionary)sh.GetField("_handles",f).GetValue(null);var h=handles["${id}"];
 if(h==null)throw new System.Exception("Actual fixture command absent");
 var identity=(string)h.GetType().GetField("NativeIdentity",f).GetValue(h);var list=(UnityEngine.UIElements.VisualElement)h.GetType().GetField("List",f).GetValue(h);
 var args=new object[]{null};var valid=h.GetType().GetMethod("TryValidate",f).Invoke(h,args);
 System.Console.WriteLine("HANDLE|"+identity);System.Console.WriteLine("VALID|"+valid+"|"+args[0]);
 System.Console.WriteLine("VISIBLE|"+a.GetType("FM27Access.Plugin.ElementBridge").GetMethod("IsVisibleInHierarchy",f).Invoke(null,new object[]{list}));
 var ct=a.GetType("FM27Access.Plugin.Contexts.FixtureScheduleContext");var wrapper=list.parent;
 while(wrapper!=null&&!(bool)ct.GetMethod("IsScheduleWrapper",f).Invoke(null,new object[]{wrapper}))wrapper=wrapper.parent;
 if(wrapper==null)continue;
 var bt=a.GetType("UnityEngine.UIElements.Button");foreach(var aa in System.AppDomain.CurrentDomain.GetAssemblies())if(bt==null)bt=aa.GetType("UnityEngine.UIElements.Button");
 var headers=(System.Collections.IEnumerable)ct.GetMethod("ReadHeaders",f).Invoke(null,new object[]{wrapper});
 foreach(var header in headers){var ht=header.GetType();var e=(UnityEngine.UIElements.VisualElement)ht.GetField("Button",f).GetValue(header);System.Reflection.MethodInfo cast=null;
 foreach(var m in e.GetType().GetMethods(f))if(m.Name=="TryCast"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0){cast=m;break;}
 var button=cast.MakeGenericMethod(new[]{bt}).Invoke(e,null);var click=bt.GetProperty("clickable",f).GetValue(button,null);
 bool primary=click!=null&&(click.GetType().GetProperty("clicked",f).GetValue(click,null)!=null||click.GetType().GetProperty("clickedWithEventInfo",f).GetValue(click,null)!=null);
 System.Console.WriteLine("HEADER|"+ht.GetField("Key",f).GetValue(header)+"|"+primary+"|"+ht.GetField("HasContextMenu",f).GetValue(header)+"|"+ht.GetField("SortState",f).GetValue(header));}
 }`;
};
