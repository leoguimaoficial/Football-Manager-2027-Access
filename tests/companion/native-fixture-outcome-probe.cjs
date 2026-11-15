// Read the exact realized fixture result button and its actual native glyph.
// Scrolling is readonly; no selection, match command or model edit is issued.
const assert=require('node:assert/strict');
module.exports=async(c,mvid,id)=>{
 assert.match(mvid,/^[\da-f-]{36}$/i);assert.match(id,/^[\da-z]+$/i);
 let observed;
 await c.poll(async()=>{
  const code=`
var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
var core=System.AppDomain.CurrentDomain.GetAssemblies()[0];foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}")core=a;
var handles=core.GetType("FM27Access.Plugin.Server.ServerHandles");var registry=(System.Collections.IDictionary)handles.GetField("_handles",f).GetValue(null);var h=registry["${id}"];
if(h==null||h.GetType().Name!="FixtureRowHandle")throw new System.InvalidOperationException("Actual fixture handle required");
var valid=new object[]{null};if(!(bool)h.GetType().GetMethod("TryValidate",f).Invoke(h,valid))throw new System.InvalidOperationException("Stale fixture: "+valid[0]);
var list=h.GetType().GetField("List",f).GetValue(h);int row=(int)h.GetType().GetField("Row",f).GetValue(h);string identity=(string)h.GetType().GetField("NativeIdentity",f).GetValue(h);
var contextType=core.GetType("FM27Access.Plugin.Contexts.FixtureScheduleContext");var ctx=System.Activator.CreateInstance(contextType,new object[]{list,new string[]{"Result"}});
var button=(UnityEngine.UIElements.VisualElement)contextType.GetMethod("RealizeControl",f).Invoke(ctx,new object[]{row,"Result"});
if(button==null)System.Console.WriteLine("PENDING|button");
else if(!(bool)contextType.GetMethod("MatchesRowIdentity",f).Invoke(ctx,new object[]{row,identity}))System.Console.WriteLine("PENDING|identity");
else{
 var scan=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();scan.Push(button);string sprite=null,score=null,classes="";
 var textType=core.GetType("UnityEngine.UIElements.TextElement");var setterType=core.GetType("SI.Bindable.SIStyleSetter");foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){if(textType==null)textType=a.GetType("UnityEngine.UIElements.TextElement");if(setterType==null)setterType=a.GetType("SI.Bindable.SIStyleSetter");}
 while(scan.Count>0){var e=scan.Pop();
  if(e.GetIl2CppType().Name=="SIText"){var text=System.Activator.CreateInstance(textType,new object[]{e.Pointer});score=(string)core.GetType("FM27Access.Plugin.Server.ServerText").GetMethod("Clean",f).Invoke(null,new object[]{textType.GetProperty("text",f).GetValue(text,null)});}
  if(e.GetIl2CppType().Name=="SIStyleSetter"&&(e.name=="IconStyleSetter"||e.name=="result")){
   sprite=(string)core.GetType("FM27Access.Plugin.Adapters.SpriteHelper").GetMethod("ReadSpriteName",f).Invoke(null,new object[]{e});
   var setter=System.Activator.CreateInstance(setterType,new object[]{e.Pointer});var multi=setterType.GetProperty("m_addedClasses",f);
   if(multi!=null){var items=multi.GetValue(setter,null);if(items!=null){int count=(int)items.GetType().GetProperty("Count").GetValue(items,null);for(int i=0;i<count;i++)classes+="["+items.GetType().GetProperty("Item").GetValue(items,new object[]{i})+"]";}}
   else{var single=setterType.GetProperty("m_addedClass",f);if(single!=null)classes="["+single.GetValue(setter,null)+"]";}
  }
  for(int i=e.hierarchy.childCount-1;i>=0;i--)scan.Push(e.hierarchy[i]);
 }
 if(string.IsNullOrEmpty(sprite)||string.IsNullOrEmpty(score))System.Console.WriteLine("PENDING|native result binding");
 else System.Console.WriteLine("FIXTURE|"+row+"|"+identity+"|"+score+"|"+sprite+"|"+classes);
}
`;
  const raw=await c.page.evaluate(async code=>await(await fetch('/eval',{method:'POST',body:code})).text(),code);
  assert.doesNotMatch(raw,/\[compile\]|\[exception\]|error CS/i);
  // Identity contains pipes, so retain it greedily before the score/asset fields.
  const match=raw.match(/^FIXTURE\|(\d+)\|(.+)\|([^|]+)\|([^|]+)\|([^\r\n]*)/m);
  if(!match)return false;
  observed={row:Number(match[1]),identity:match[2],score:match[3],sprite:match[4],classes:match[5],raw};return true;
 },'exact realized fixture result and native identity');
 return observed;
};
