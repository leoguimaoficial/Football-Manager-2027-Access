// Read-only production registration/command metadata. Runtime-checked TryCast
// is invoked through reflection because mcs cannot import some current Unity
// widget metadata. Never construct a widget from an unrelated raw Pointer.
const assert=require('node:assert/strict');
module.exports=(mvid,ids)=>{
 assert.match(mvid,/^[0-9a-f-]{36}$/i);assert.ok(ids.length&&ids.every(id=>/^[-\w:.]+$/.test(id)));
 return `foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}"&&a.GetType("FM27Access.Plugin.CoreEntry").GetField("_bridge",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null)!=null){
 var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
 var sh=a.GetType("FM27Access.Plugin.Server.ServerHandles");var handles=(System.Collections.IDictionary)sh.GetField("_handles",f).GetValue(null);
 var sources=(System.Collections.IDictionary)a.GetType("FM27Access.Plugin.Server.LiveValues").GetField("_sources",f).GetValue(null);
 var bt=a.GetType("SI.Bindable.SIButton");var et=a.GetType("FM.GamePlugin.EventToPluginContext");var dt=a.GetType("FM.UI.Widgets.DateSelectorWidget");var vt=a.GetType("UnityEngine.UIElements.VisualElement");
 foreach(var aa in System.AppDomain.CurrentDomain.GetAssemblies()){
  if(bt==null)bt=aa.GetType("SI.Bindable.SIButton");if(et==null)et=aa.GetType("FM.GamePlugin.EventToPluginContext");
  if(dt==null)dt=aa.GetType("FM.UI.Widgets.DateSelectorWidget");if(vt==null)vt=aa.GetType("UnityEngine.UIElements.VisualElement");
 }
 if(bt==null||et==null||dt==null||vt==null)throw new System.InvalidOperationException("Required current native metadata is absent.");
 System.Reflection.MethodInfo cast=null;
 foreach(var m in vt.GetMethods(f))if(m.Name=="TryCast"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0){cast=m;break;}
 if(cast==null)throw new System.InvalidOperationException("The checked native cast API is absent.");
 var buttonCast=cast.MakeGenericMethod(new[]{bt});var eventCast=cast.MakeGenericMethod(new[]{et});var dateCast=cast.MakeGenericMethod(new[]{dt});
 System.Func<object,string> nativeName=obj=>{
  var t=obj.GetType().GetMethod("GetIl2CppType",f).Invoke(obj,null);
  return (string)t.GetType().GetProperty("FullName",f).GetValue(t,null);
 };
 foreach(var id in new[]{${ids.map(id=>JSON.stringify(id)).join(',')}}){
  var h=handles[id];var source=h==null?sources[id]:null;
  if(h==null&&source==null){System.Console.WriteLine("HANDLE|"+id+"|missing|||||");continue;}
  var af=h==null?null:(h.GetType().GetField("El",f)??h.GetType().GetField("Header",f));var ad=source??(af==null?null:af.GetValue(h));
  var e=ad==null?null:sh.GetMethod("NativeElement",f).Invoke(null,new object[]{ad});
  var events=new System.Collections.Generic.List<string>();var button=e==null?null:buttonCast.Invoke(e,null);
  if(button!=null){
   var list=bt.GetProperty("Events",f).GetValue(button,null);
   if(list!=null){
    var lt=list.GetType();int count=(int)lt.GetProperty("Count",f).GetValue(list,null);
    for(int i=0;i<count;i++){
     var context=lt.GetProperty("Item",f).GetValue(list,new object[]{i});if(context==null)continue;
     var command=eventCast.Invoke(context,null);
     events.Add(nativeName(context)+(command==null?"":"/"+et.GetProperty("m_id",f).GetValue(command,null)));
    }
   }
  }
  var date=e==null?null:dateCast.Invoke(e,null);
  var facts=new System.Collections.Generic.List<string>();
  // Observe the captured guard only; never realize/scroll a virtualized cell.
  // Missing identity is an investigation lead, not proof that an index-native
  // row requires a reference or that its command has been exercised.
  var identityField=h==null?null:h.GetType().GetField("NativeRowIdentity",f);
  if(identityField!=null)facts.Add("Cell.RecordIdentityCaptured="+!string.IsNullOrEmpty((string)identityField.GetValue(h)));
  var fixtureIdentity=h==null?null:h.GetType().GetField("NativeIdentity",f);
  if(fixtureIdentity!=null)facts.Add("Handle.RecordIdentityCaptured="+!string.IsNullOrEmpty((string)fixtureIdentity.GetValue(h)));
  foreach(var guardName in new[]{"RuleContext","ClientContext"}){var gf=h==null?null:h.GetType().GetField(guardName,f);if(gf!=null)facts.Add("Handle."+guardName+"Captured="+!string.IsNullOrEmpty((string)gf.GetValue(h)));}
  var primary=ad==null?null:ad.GetType().GetProperty("HasPrimaryClick",f);
  if(primary!=null){
   facts.Add("Header.PrimaryClick="+primary.GetValue(ad,null));
   facts.Add("Header.ContextMenu="+ad.GetType().GetProperty("HasContextMenu",f).GetValue(ad,null));
   facts.Add("Header.VisibleInHierarchy="+a.GetType("FM27Access.Plugin.ElementBridge").GetMethod("IsVisibleInHierarchy",f).Invoke(null,new object[]{e}));
   var validation=new object[]{null};
   facts.Add("Header.Valid="+h.GetType().GetMethod("TryValidate",f).Invoke(h,validation));
   if(validation[0]!=null)facts.Add("Header.ValidationError="+validation[0]);
  }
  if(e!=null)facts.Add("VisualElement.enabledInHierarchy="+vt.GetProperty("enabledInHierarchy",f).GetValue(e,null));
  if(date!=null)facts.Add("DateSelector.IsFloatingWidget="+dt.GetProperty("IsFloatingWidget",f).GetValue(date,null));
  System.Console.WriteLine("HANDLE|"+id+"|"+(h==null?"readonly":"present")+"|"+(h==null?"ReadOnlySource":h.GetType().Name)+"|"+(ad==null?"":ad.GetType().Name)+"|"+(e==null?"":nativeName(e))+"|"+string.Join(";",events)+"|"+string.Join(";",facts));
 }
}`;
};
