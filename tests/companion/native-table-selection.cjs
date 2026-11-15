// Readonly actual reference-bound table selection, independent of DOM checkboxes.
const assert=require('node:assert/strict');
module.exports=async(c,mvid,id)=>{
 assert.match(mvid,/^[\da-f-]{36}$/i);assert.match(id,/^[\da-z]+$/i);
 const code=`var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
var core=System.AppDomain.CurrentDomain.GetAssemblies()[0];foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}")core=a;
var hs=(System.Collections.IDictionary)core.GetType("FM27Access.Plugin.Server.ServerHandles").GetField("_handles",f).GetValue(null);var h=hs["${id}"];if(h==null||h.GetType().Name!="CellHandle")throw new System.InvalidOperationException("Actual table cell handle required");
var identity=(string)h.GetType().GetField("NativeRowIdentity",f).GetValue(h);var table=h.GetType().GetField("Table",f).GetValue(h);var type=core.GetType("FM27Access.Plugin.Contexts.TableContext");
var ctx=System.Activator.CreateInstance(type,f,null,new object[]{table,System.Activator.CreateInstance(core.GetType("FM27Access.Plugin.ClassificationCache"))},null);var list=type.GetMethod("GetVirtualisedList",f).Invoke(ctx,null);
System.Console.WriteLine("ROW_REFERENCE|"+identity);System.Console.WriteLine("MULTI|"+type.GetMethod("IsMultiSelect",f).Invoke(ctx,null));System.Console.WriteLine("NATIVE_SELECTED_COUNT|"+type.GetMethod("SelectedCount",f).Invoke(null,new object[]{list}));`;
 const raw=await c.page.evaluate(async code=>await(await fetch('/eval',{method:'POST',body:code})).text(),code);assert.doesNotMatch(raw,/\[compile\]|\[exception\]|error CS/i);
 const identity=raw.match(/^ROW_REFERENCE\|([^\r\n]+)/m)?.[1],count=Number(raw.match(/^NATIVE_SELECTED_COUNT\|(-?\d+)/m)?.[1]);assert.match(identity,/^FM\.UI\.PersonReference\|/);assert.ok(Number.isInteger(count)&&count>=0);assert.match(raw,/^MULTI\|True/m);
 return{identity,count,raw};
};
