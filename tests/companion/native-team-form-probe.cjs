// Independent readonly native dataset. Does not call TeamFormReader or its
// formatter: inspect the same actual model nodes drawn by the game instead.
const assert=require('node:assert/strict');
module.exports=async(c,mvid)=>{
 assert.match(mvid,/^[\da-f-]{36}$/i);
 const code=`
var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
var core=System.AppDomain.CurrentDomain.GetAssemblies()[0];
foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}")core=a;
// Reflection avoids the evaluator's TextElement/ImageConversion compiler issue.
var textType=core.GetType("UnityEngine.UIElements.TextElement");
foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(textType==null)textType=a.GetType("UnityEngine.UIElements.TextElement");
var bindings=SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();
var panel=(UnityEngine.UIElements.VisualElement)core.GetType("FM27Access.Plugin.FM27PanelCatalog").GetMethod("GetForegroundPanel",f).Invoke(null,null);
var stack=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();stack.Push(panel);int ordinal=0;
while(stack.Count>0){
 var el=stack.Pop();
 if(el.GetIl2CppType().Name=="StreamedObjectList"){
  UnityEngine.UIElements.VisualElement cell=null;
  for(var parent=el.parent;parent!=null;parent=parent.parent){if(parent.GetIl2CppType().Name=="Cell"){cell=parent;break;}if(parent.GetIl2CppType().Name=="Panel")break;}
  if(cell!=null&&(bool)core.GetType("FM27Access.Plugin.ElementBridge").GetMethod("IsVisibleInHierarchy",f).Invoke(null,new object[]{el})){
   string team="";var rowScan=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();rowScan.Push(cell.parent);
   while(rowScan.Count>0){
    var candidate=rowScan.Pop();string text=null;
    if(candidate.GetIl2CppType().Name=="SIText"){var wrapper=System.Activator.CreateInstance(textType,new object[]{candidate.Pointer});text=(string)textType.GetProperty("text",f).GetValue(wrapper,null);}
    if(text!=null&&text.Contains("<link=")){team=(string)core.GetType("FM27Access.Plugin.Server.ServerText").GetMethod("Clean",f).Invoke(null,new object[]{text});break;}
    for(int i=candidate.hierarchy.childCount-1;i>=0;i--)rowScan.Push(candidate.hierarchy[i]);
   }
   var list=el.TryCast<SI.Bindable.StreamedObjectList>();var key=list.m_itemsContainerKey;SI.Bindable.IReadOnlyNode container;
   bool bound=bindings.TryGetNode(ref key,out container);System.Console.Write("SERIES|"+(ordinal++)+"|"+team+"|bound="+bound+"|");
   if(bound){int childCount=0;foreach(var item in container.Children){childCount++;foreach(var child in item.Children)if(child.Name=="teamform"){var valueKey=child.Key;System.Console.Write(item.Name+"="+bindings.Get(ref valueKey)?.AsString()+",");}}System.Console.Write("|children="+childCount);}
   System.Console.WriteLine();
   var rankScan=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();rankScan.Push(cell.parent);
   while(rankScan.Count>0){
    var candidate=rankScan.Pop();
    if(candidate.GetIl2CppType().Name=="SIStyleSetter"&&candidate.parent?.parent?.name=="ArrowStateIcon"){
     var records=core.GetType("FM27Access.Plugin.Server.BindingRecords");
     var record=records.GetMethod("RecordAbove",f).Invoke(null,new object[]{candidate,"binding",12,0});
     if(record==null)throw new System.InvalidOperationException("Actual standings arrow has no native row record");
     var fields=(System.Collections.IDictionary)records.GetMethod("Fields",f).Invoke(null,new object[]{record});
     var position=(SI.Core.TypedValue)fields["TablePosition"];var state=(SI.Core.TypedValue)fields["LeagueTableArrowState"];
     var sprite=core.GetType("FM27Access.Plugin.Adapters.SpriteHelper").GetMethod("ReadSpriteName",f).Invoke(null,new object[]{candidate});
     System.Console.WriteLine("RANK|"+(ordinal-1)+"|"+position?.AsString()+"|"+state?.AsString()+"|"+sprite);break;
    }
    for(int i=candidate.hierarchy.childCount-1;i>=0;i--)rankScan.Push(candidate.hierarchy[i]);
   }
  }
 }
 for(int i=el.hierarchy.childCount-1;i>=0;i--)stack.Push(el.hierarchy[i]);
}
`;
 const raw=await c.page.evaluate(async code=>await(await fetch('/eval',{method:'POST',body:code})).text(),code);
 assert.doesNotMatch(raw,/\[compile\]|\[exception\]|error CS/i);
 const rows=[...raw.matchAll(/SERIES\|(\d+)\|([^|]+)\|bound=(True|False)\|([^|]*)\|children=(\d+)/g)].map(m=>({ordinal:Number(m[1]),team:m[2],bound:m[3]==='True',children:Number(m[5]),values:[...m[4].matchAll(/(\d+)=(-?\d+),/g)].map(v=>({order:Number(v[1]),value:Number(v[2])})).sort((a,b)=>a.order-b.order)}));
 assert.ok(rows.length,'Actual current native table series are required');assert.ok(rows.every((r,i)=>r.ordinal===i&&r.bound&&r.children===r.values.length),'Do not treat unbound, missing or unrelated native items as empty form');
 const ranks=[...raw.matchAll(/RANK\|(\d+)\|(\d+)\|(\d+)\|([^\r\n]+)/g)].map(m=>({ordinal:Number(m[1]),position:Number(m[2]),state:Number(m[3]),sprite:m[4]}));
 assert.equal(ranks.length,rows.length,'Every native standings row retains its own position/direction record');
 for(let i=0;i<rows.length;i++){assert.equal(ranks[i].ordinal,i);rows[i].rank=ranks[i];}
 return{at:new Date().toISOString(),mvid,raw,rows};
};
