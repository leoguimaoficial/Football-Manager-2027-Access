// Read-only native tile state. Use checked runtime casts: mcs cannot import
// the current TextElement metadata directly. Never construct unrelated pointers.
const assert=require('node:assert/strict');
module.exports=mvid=>{
 assert.match(mvid,/^[0-9a-f-]{36}$/i);
 return `foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}"){
 var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
 var root=(UnityEngine.UIElements.VisualElement)a.GetType("FM27Access.Plugin.FM27PanelCatalog").GetMethod("GetForegroundPanel",f).Invoke(null,null);
 var bridge=a.GetType("FM27Access.Plugin.ElementBridge");var titleMethod=bridge.GetMethod("FindVisibleTileTitle",f);var visible=bridge.GetMethod("IsVisibleInHierarchy",f);
 var textType=a.GetType("UnityEngine.UIElements.TextElement");foreach(var aa in System.AppDomain.CurrentDomain.GetAssemblies())if(textType==null)textType=aa.GetType("UnityEngine.UIElements.TextElement");
 System.Reflection.MethodInfo checkedCast=null;foreach(var m in typeof(UnityEngine.UIElements.VisualElement).GetMethods())if(m.Name=="TryCast"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0)checkedCast=m.MakeGenericMethod(new[]{textType});
 if(checkedCast==null)throw new System.Exception("Checked native text cast absent.");
 var sh=a.GetType("FM27Access.Plugin.Server.ServerHandles");var hs=(System.Collections.IDictionary)sh.GetField("_handles",f).GetValue(null);var nativeElement=sh.GetMethod("NativeElement",f);
 var actions=new System.Collections.Generic.HashSet<long>();foreach(System.Collections.DictionaryEntry pair in hs){var h=pair.Value;var field=h.GetType().GetField("El",f);var ad=field==null?null:field.GetValue(h);if(ad==null)continue;var e=(UnityEngine.UIElements.VisualElement)nativeElement.Invoke(null,new object[]{ad});if(e!=null)actions.Add(e.Pointer.ToInt64());}
 var todo=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();todo.Push(root);
 while(todo.Count>0){var e=todo.Pop();
  if(e.name=="layout-tile-interaction-button"&&e.GetIl2CppType().Name=="SIButton"&&(bool)visible.Invoke(null,new object[]{e})){
   var title=(UnityEngine.UIElements.VisualElement)titleMethod.Invoke(null,new object[]{e});var text=title==null?null:checkedCast.Invoke(title,null);
   if(text!=null){
    System.Console.WriteLine("CARD|"+textType.GetProperty("text").GetValue(text,null)+"|"+e.enabledInHierarchy+"|"+e.Pointer+"|"+actions.Contains(e.Pointer.ToInt64()));
    var ad=System.Activator.CreateInstance(a.GetType("FM27Access.Plugin.Adapters.ButtonAdapter"),new object[]{e});var description=(string)ad.GetType().GetMethod("GetDescription").Invoke(ad,null);
    System.Console.WriteLine("DETAIL|"+e.Pointer+"|"+description.Replace("\\r"," ").Replace("\\n"," "));
   }
  }
  for(int z=0;z<e.hierarchy.childCount;z++)todo.Push(e.hierarchy[z]);
 }
}`;
};
