// Independent actual selected-fixture reference, event records, rendered assets
// and original native row text. No incident label/formatter or command is called.
const assert=require('node:assert/strict');
module.exports=async(c,mvid,id)=>{
 assert.match(mvid,/^[\da-f-]{36}$/i);assert.match(id,/^[\da-z]+$/i);
 const code=`
var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
var core=System.AppDomain.CurrentDomain.GetAssemblies()[0];foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}")core=a;
var records=core.GetType("FM27Access.Plugin.Server.BindingRecords");var bridge=core.GetType("FM27Access.Plugin.ElementBridge");
var handles=(System.Collections.IDictionary)core.GetType("FM27Access.Plugin.Server.ServerHandles").GetField("_handles",f).GetValue(null);var h=handles["${id}"];
if(h==null||h.GetType().Name!="FixtureRowHandle")throw new System.InvalidOperationException("Actual fixture handle required");var valid=new object[]{null};if(!(bool)h.GetType().GetMethod("TryValidate",f).Invoke(h,valid))throw new System.InvalidOperationException("Fixture owner changed");
var expected=(string)h.GetType().GetField("NativeIdentity",f).GetValue(h);var list=(UnityEngine.UIElements.VisualElement)h.GetType().GetField("List",f).GetValue(h);bool selected=false;
for(var up=list.parent;up!=null;up=up.parent){var node=records.GetMethod("NodeOf",f).Invoke(null,new object[]{up});if(node==null)continue;var found=records.GetMethod("FindNode",f).Invoke(null,new object[]{node,"selectedfixture",3});if(found==null)continue;var value=(SI.Core.TypedValue)records.GetMethod("ValueOf",f).Invoke(null,new object[]{found});var reference=value?.Get()?.TryCast<SI.Interop.InteropReference>();var identity=(string)records.GetMethod("ReferenceIdentity",f).Invoke(null,new object[]{reference});System.Console.WriteLine("SELECTED|"+identity);if(identity==expected)selected=true;}
if(!selected)System.Console.WriteLine("INCIDENT_SELECTION_PENDING");
else{
var panel=(UnityEngine.UIElements.VisualElement)core.GetType("FM27Access.Plugin.FM27PanelCatalog").GetMethod("GetForegroundPanel",f).Invoke(null,null);var scan=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();scan.Push(panel);int ordinal=0;
var textType=core.GetType("UnityEngine.UIElements.TextElement");foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(textType==null)textType=a.GetType("UnityEngine.UIElements.TextElement");
while(scan.Count>0){var e=scan.Pop();
 if(e.name=="EventType"&&e.GetIl2CppType().Name=="SIImage"&&(bool)bridge.GetMethod("IsVisibleInHierarchy",f).Invoke(null,new object[]{e})){
  var record=records.GetMethod("RecordAbove",f).Invoke(null,new object[]{e,"Event",6,0});if(record==null)throw new System.InvalidOperationException("Actual incident has no event record");var fields=(System.Collections.IDictionary)records.GetMethod("Fields",f).Invoke(null,new object[]{record});
  var kind=(SI.Core.TypedValue)fields["EventType"];var time=(SI.Core.TypedValue)fields["TimeString"];var sprite=core.GetType("FM27Access.Plugin.Adapters.SpriteHelper").GetMethod("ReadSpriteName",f).Invoke(null,new object[]{e});
  var row=e.parent;while(row!=null&&row.GetIl2CppType().Name!="BindingRoot")row=row.parent;if(row==null)throw new System.InvalidOperationException("Incident row root absent");var rowScan=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();rowScan.Push(row);string texts="",player="";
  var group=row;while(group.parent!=null&&group.parent.name!="KeyEvents")group=group.parent;if(group.parent==null)throw new System.InvalidOperationException("Native event team group absent");int side=0;for(int i=0;i<group.parent.hierarchy.childCount;i++){var sibling=group.parent.hierarchy[i];if(sibling.Pointer==group.Pointer)break;if(sibling.GetIl2CppType().Name=="ComponentElement")side++;}if(side>1)throw new System.InvalidOperationException("Unexpected native event group");
  var owner=group.parent;while(owner!=null&&owner.name!="CalendarToolFixturesGridCompositionSet")owner=owner.parent;if(owner==null)throw new System.InvalidOperationException("Native fixture composition absent");var headers=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();headers.Push(owner);string team=null;while(headers.Count>0){var q=headers.Pop();if(q.name==(side==0?"HomeTeam":"AwayTeam")&&(bool)bridge.GetMethod("IsVisibleInHierarchy",f).Invoke(null,new object[]{q})){bool score=false;for(int j=0;j<q.hierarchy.childCount;j++)if(q.hierarchy[j].name=="Score")score=true;if(score){if(team!=null)throw new System.InvalidOperationException("Ambiguous native score header");team=(string)core.GetType("FM27Access.Plugin.Server.ServerText").GetMethod("Clean",f).Invoke(null,new object[]{core.GetType("FM27Access.Plugin.Server.ScreenWalker").GetMethod("ReadFirstText",f).Invoke(null,new object[]{q})});}}for(int j=q.hierarchy.childCount-1;j>=0;j--)headers.Push(q.hierarchy[j]);}if(string.IsNullOrWhiteSpace(team))throw new System.InvalidOperationException("Native team label absent");
  while(rowScan.Count>0){var q=rowScan.Pop();if(!(bool)bridge.GetMethod("IsVisibleInHierarchy",f).Invoke(null,new object[]{q}))continue;if(q.GetIl2CppType().Name=="SIText"){var text=System.Activator.CreateInstance(textType,new object[]{q.Pointer});var clean=(string)core.GetType("FM27Access.Plugin.Server.ServerText").GetMethod("Clean",f).Invoke(null,new object[]{textType.GetProperty("text",f).GetValue(text,null)});if(q.name=="Name")player=clean;if(!string.IsNullOrWhiteSpace(clean))texts+=(texts.Length==0?"":"~")+clean.Replace("|"," ").Replace("~"," ");}for(int i=q.hierarchy.childCount-1;i>=0;i--)rowScan.Push(q.hierarchy[i]);}
  System.Console.WriteLine("INCIDENT|"+(ordinal++)+"|"+kind?.AsString()+"|"+time?.AsString()+"|"+sprite+"|"+side+"|"+team+"|"+player+"|"+texts);
 }
 for(int i=e.hierarchy.childCount-1;i>=0;i--)scan.Push(e.hierarchy[i]);
}

var ratingScan=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();ratingScan.Push(panel);int ratingCount=0;
while(ratingScan.Count>0){var scope=ratingScan.Pop();if(scope.name=="PlayerRatings"&&(bool)bridge.GetMethod("IsVisibleInHierarchy",f).Invoke(null,new object[]{scope})){
 var rows=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();rows.Push(scope);
 while(rows.Count>0){var row=rows.Pop();var node=records.GetMethod("NodeOf",f).Invoke(null,new object[]{row});var record=node==null?null:(SI.Bindable.IReadOnlyNode)records.GetMethod("FindNode",f).Invoke(null,new object[]{node,"row",0});
 if(record!=null){SI.Core.TypedValue rating=null,person=null,eventValues=null;foreach(var field in record.Children){var value=(SI.Core.TypedValue)records.GetMethod("ValueOf",f).Invoke(null,new object[]{field});if(field.Name=="Rating")rating=value;if(field.Name=="Player")person=value;if(field.Name=="PlayerEvents")eventValues=value;}
 if(rating!=null&&person!=null&&eventValues!=null){var reference=person.Get()?.TryCast<SI.Interop.InteropReference>();var identity=(string)records.GetMethod("ReferenceIdentity",f).Invoke(null,new object[]{reference});if(identity==null)throw new System.InvalidOperationException("Rating player identity absent");var values=eventValues.Get()?.TryCast<Il2CppSystem.Collections.Generic.List<SI.Core.TypedValue>>();if(values==null)throw new System.InvalidOperationException("Actual player event model absent");string codes="";for(int z=0;z<values.Count;z++)codes+=(z==0?"":",")+values[z]?.AsString();
 string name=null,display=null,sprites="";int glyphs=0;var cells=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();cells.Push(row);
 while(cells.Count>0){var cell=cells.Pop();if(!(bool)bridge.GetMethod("IsVisibleInHierarchy",f).Invoke(null,new object[]{cell}))continue;
 if(cell.GetIl2CppType().Name=="SIText"){var text=System.Activator.CreateInstance(textType,new object[]{cell.Pointer});var clean=(string)core.GetType("FM27Access.Plugin.Server.ServerText").GetMethod("Clean",f).Invoke(null,new object[]{textType.GetProperty("text",f).GetValue(text,null)});if(cell.name=="primary-text"){if(name!=null)throw new System.InvalidOperationException("Ambiguous rating player");name=clean;}if(cell.name=="text"){if(display!=null)throw new System.InvalidOperationException("Ambiguous rating value");display=clean;}}
 if(cell.GetIl2CppType().Name=="SIImage"){int depth=0;for(var up=cell.parent;up!=null&&depth++<6;up=up.parent){var own=records.GetMethod("NodeOf",f).Invoke(null,new object[]{up});if(own==null)continue;var evt=(SI.Bindable.IReadOnlyNode)records.GetMethod("FindNode",f).Invoke(null,new object[]{own,"Event",0});if(evt!=null){var val=(SI.Core.TypedValue)records.GetMethod("ValueOf",f).Invoke(null,new object[]{evt});if(records.GetMethod("NumberOf",f).Invoke(null,new object[]{val})!=null){var sprite=core.GetType("FM27Access.Plugin.Adapters.SpriteHelper").GetMethod("ReadSpriteName",f).Invoke(null,new object[]{cell});if(sprite==null)throw new System.InvalidOperationException("Player event asset absent");sprites+=(glyphs++==0?"":",")+sprite;}}break;}}
 for(int z=cell.hierarchy.childCount-1;z>=0;z--)cells.Push(cell.hierarchy[z]);}
 if(glyphs!=values.Count)throw new System.InvalidOperationException("Player event native model/realization count differs");System.Console.WriteLine("RATING|"+(ratingCount++)+"|"+name+"|"+display+"|"+rating.AsString()+"|"+sprites+"|"+codes+"|"+identity);
 }}for(int z=row.hierarchy.childCount-1;z>=0;z--)rows.Push(row.hierarchy[z]);}
 }for(int z=scope.hierarchy.childCount-1;z>=0;z--)ratingScan.Push(scope.hierarchy[z]);}
System.Console.WriteLine("RATING_COUNT|"+ratingCount);
System.Console.WriteLine("INCIDENT_COUNT|"+ordinal);
}
`;
 let observed;
 await c.poll(async()=>{
 const raw=await c.page.evaluate(async code=>await(await fetch('/eval',{method:'POST',body:code})).text(),code);assert.doesNotMatch(raw,/\[compile\]|\[exception\]|error CS/i);
 if(raw.includes('INCIDENT_SELECTION_PENDING'))return false;
 const selected=[...raw.matchAll(/^SELECTED\|([^\r\n]+)/gm)].map(m=>m[1]);assert.ok(selected.length);
 const events=[...raw.matchAll(/^INCIDENT\|(\d+)\|(-?\d+)\|([^|]*)\|([^|]*)\|(\d)\|([^|]*)\|([^|]*)\|([^\r\n]*)/gm)].map(m=>({ordinal:Number(m[1]),kind:Number(m[2]),time:m[3],sprite:m[4],side:Number(m[5]),team:m[6],player:m[7],texts:m[8].split('~')}));
 assert.equal(events.length,Number(raw.match(/^INCIDENT_COUNT\|(\d+)/m)?.[1]));assert.ok(events.every((e,i)=>e.ordinal===i&&e.sprite&&e.texts.includes(e.time)));
 const ratings=[...raw.matchAll(/^RATING\|(\d+)\|([^|]*)\|([^|]*)\|([^|]*)\|([^|]*)\|([^|]*)\|([^\r\n]*)/gm)].map(m=>({ordinal:Number(m[1]),player:m[2],display:m[3],value:Number(m[4]),sprites:m[5]?m[5].split(','):[],codes:m[6]?m[6].split(',').map(Number):[],identity:m[7]}));
 assert.equal(ratings.length,Number(raw.match(/^RATING_COUNT\|(\d+)/m)?.[1]));assert.ok(ratings.every((r,i)=>r.ordinal===i&&r.player&&r.identity&&r.display&&Number(r.display.replace(',','.'))===Number(r.value.toFixed(2))&&r.sprites.length===r.codes.length));
 observed={at:new Date().toISOString(),mvid,selected,events,ratings,raw};return true;
 },'actual selected-fixture reference and native incident records');
 return observed;
};
