// Read-only native advice flags and actual (including hidden) command widgets.
// Uses existing typed BindingRecords readers on Unity's dispatcher. No action,
// visibility override, realization or business-value write.
const assert=require('node:assert/strict');
module.exports=mvid=>{
assert.match(mvid,/^[0-9a-f-]{36}$/i);
return `{ foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}"){
 var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
 var root=(UnityEngine.UIElements.VisualElement)a.GetType("FM27Access.Plugin.FM27PanelCatalog").GetMethod("GetForegroundPanel",f).Invoke(null,null);
 if(root==null||root.name!="SquadYouthSetupReport")throw new System.InvalidOperationException("Open the actual Youth Setup report before this read-only probe");
 var br=a.GetType("FM27Access.Plugin.Server.BindingRecords");var field=br.GetMethod("Field",f);var fields=br.GetMethod("Fields",f);var flag=br.GetMethod("FlagOf",f);var record=br.GetMethod("RecordOf",f);
 var query=a.GetType("FM27Access.Plugin.NativeTreeQuery").GetMethod("All",f).MakeGenericMethod(new[]{typeof(UnityEngine.UIElements.VisualElement)});
 var wrappers=(System.Collections.IEnumerable)query.Invoke(null,new object[]{root,"actionbuttonvisible"});
 var bt=a.GetType("SI.Bindable.SIButton");var et=a.GetType("FM.GamePlugin.EventToPluginContext");foreach(var aa in System.AppDomain.CurrentDomain.GetAssemblies()){if(bt==null)bt=aa.GetType("SI.Bindable.SIButton");if(et==null)et=aa.GetType("FM.GamePlugin.EventToPluginContext");}
 System.Reflection.MethodInfo cast=null;foreach(var method in typeof(UnityEngine.UIElements.VisualElement).GetMethods())if(method.Name=="TryCast"&&method.IsGenericMethodDefinition&&method.GetParameters().Length==0){cast=method;break;}
 var buttonCast=cast.MakeGenericMethod(new[]{bt});var eventCast=cast.MakeGenericMethod(new[]{et});int rowIndex=0;
 foreach(UnityEngine.UIElements.VisualElement wrapper in wrappers){
  var advice=br.GetMethod("RecordAbove",f).Invoke(null,new object[]{wrapper,"playeradvice",8,3});
  if(advice==null)throw new System.InvalidOperationException("Native advice record absent");
  var data=fields.Invoke(null,new[]{advice});
  object actionable=flag.Invoke(null,new[]{field.Invoke(null,new[]{data,(object)"IsSuggestionActionable"})});
  object actioned=flag.Invoke(null,new[]{field.Invoke(null,new[]{data,(object)"IsSuggestionActioned"})});
  System.Console.WriteLine("ROW|"+rowIndex+++"|visible="+a.GetType("FM27Access.Plugin.ElementBridge").GetMethod("IsVisibleInHierarchy",f).Invoke(null,new object[]{wrapper})+"|actionable="+actionable+"|actioned="+actioned);
  var selected=field.Invoke(null,new[]{data,(object)"DevelopmentCentreSelectionData"});var selectedRecord=record.Invoke(null,new[]{selected});
  if(selectedRecord!=null){var values=(System.Collections.IDictionary)fields.Invoke(null,new[]{selectedRecord});foreach(System.Collections.DictionaryEntry pair in values){var value=pair.Value;System.Console.WriteLine("SELECTION|"+pair.Key+"|"+value.GetType().GetMethod("AsString").Invoke(value,null));}}
  var stack=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();stack.Push(wrapper);
  while(stack.Count>0){var element=stack.Pop();var button=buttonCast.Invoke(element,null);
   if(button!=null){var ad=System.Activator.CreateInstance(a.GetType("FM27Access.Plugin.Adapters.ButtonAdapter"),new object[]{element});
    var events=bt.GetProperty("Events",f).GetValue(button,null);int count=events==null?0:(int)events.GetType().GetProperty("Count").GetValue(events,null);
    System.Console.WriteLine("BUTTON|"+element.Pointer+"|"+element.name+"|label="+ad.GetType().GetMethod("GetLabel").Invoke(ad,null)+"|enabled="+element.enabledInHierarchy+"|events="+count+"|custom="+(bt.GetProperty("m_clickableAction",f).GetValue(button,null)!=null));
    for(int z=0;z<count;z++){var item=events.GetType().GetProperty("Item").GetValue(events,new object[]{z});var ev=eventCast.Invoke(item,null);if(ev!=null)System.Console.WriteLine("EVENT|"+et.GetProperty("m_id",f).GetValue(ev,null));}
   }
   for(int z=element.hierarchy.childCount-1;z>=0;z--)stack.Push(element.hierarchy[z]);
  }
 }
 System.Console.WriteLine("ROWS|"+rowIndex);
} }`;
};
