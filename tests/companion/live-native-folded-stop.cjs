// Debug conformance over an EXISTING native control. Only local managed render
// lists are exercised; no synthetic native UI or game command is constructed.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..'),expectDefect=process.argv.includes('--expect-retained');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page}=c;
 const report={at:new Date().toISOString(),success:false,expectDefect,gameBusinessCommands:0,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const out=path.join(root,'artifacts/tests/web/native-folded-stop.json');
 try{
  report.health=JSON.parse(await c.fetchText('/health'));assert.equal(report.health.developerTools,true);
  const code=`
var core=System.AppDomain.CurrentDomain.GetAssemblies()[0];var ui=core;
foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){if(a.ManifestModule.ModuleVersionId.ToString()=="${report.health.coreMvid}")core=a;if(a.GetType("UnityEngine.UIElements.VisualElement")!=null)ui=a;}
var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
var visual=ui.GetType("UnityEngine.UIElements.VisualElement");var sh=core.GetType("FM27Access.Plugin.Server.ServerHandles");
var handles=(System.Collections.IDictionary)sh.GetField("_handles",f).GetValue(null);bool found=false;
foreach(System.Collections.DictionaryEntry entry in handles){
 var field=entry.Value.GetType().GetField("El",f);if(field==null)continue;var adapter=field.GetValue(entry.Value);if(adapter==null||adapter.GetType().Name!="ButtonAdapter")continue;
 var element=sh.GetMethod("NativeElement",f).Invoke(null,new object[]{adapter});if(element==null)continue;
 var parent=visual.GetProperty("parent",f).GetValue(element,null);if(parent==null)continue;
 var pointer=visual.GetProperty("Pointer",f).GetValue(element,null);var constructor=visual.GetConstructors(f)[0];
 foreach(var ctor in visual.GetConstructors(f))if(ctor.GetParameters().Length==1&&ctor.GetParameters()[0].ParameterType==pointer.GetType()){constructor=ctor;break;}
 var fresh=constructor.Invoke(new object[]{pointer});System.Console.WriteLine("SAME_POINTER|"+pointer.Equals(visual.GetProperty("Pointer",f).GetValue(fresh,null)));
 System.Console.WriteLine("MANAGED_REFERENCE_EQUAL|"+object.ReferenceEquals(element,fresh));
 var walker=core.GetType("FM27Access.Plugin.Server.ScreenWalker");var render=walker.GetNestedType("RenderItem",f);var stopType=walker.GetNestedType("StopItem",f);
 var listType=typeof(System.Collections.Generic.List<>).MakeGenericType(new[]{render});var suppress=walker.GetMethod("SuppressStopFor",f);
 var stop=System.Activator.CreateInstance(stopType,true);stopType.GetField("Element",f).SetValue(stop,adapter);
 var exact=System.Activator.CreateInstance(listType);listType.GetMethod("Add").Invoke(exact,new object[]{stop});suppress.Invoke(null,new object[]{exact,element});System.Console.WriteLine("EXACT_REMAINING|"+listType.GetProperty("Count").GetValue(exact,null));
 var alias=System.Activator.CreateInstance(listType);listType.GetMethod("Add").Invoke(alias,new object[]{stop});suppress.Invoke(null,new object[]{alias,fresh});System.Console.WriteLine("ALIAS_REMAINING|"+listType.GetProperty("Count").GetValue(alias,null));
 var other=System.Activator.CreateInstance(listType);listType.GetMethod("Add").Invoke(other,new object[]{stop});suppress.Invoke(null,new object[]{other,parent});System.Console.WriteLine("OTHER_REMAINING|"+listType.GetProperty("Count").GetValue(other,null));
 var bounded=System.Activator.CreateInstance(listType);listType.GetMethod("Add").Invoke(bounded,new object[]{stop});for(int i=0;i<4;i++)listType.GetMethod("Add").Invoke(bounded,new object[]{System.Activator.CreateInstance(stopType,true)});suppress.Invoke(null,new object[]{bounded,fresh});System.Console.WriteLine("OUTSIDE_TAIL_REMAINING|"+listType.GetProperty("Count").GetValue(bounded,null));
 found=true;break;
}
System.Console.WriteLine("FOUND|"+found);
`;
  report.native=await page.evaluate(async code=>await(await fetch('/eval',{method:'POST',body:code})).text(),code);assert.doesNotMatch(report.native,/\[exception\]|error CS/i);
  for(const expected of ['FOUND|True','SAME_POINTER|True','MANAGED_REFERENCE_EQUAL|False','EXACT_REMAINING|0','OTHER_REMAINING|1','OUTSIDE_TAIL_REMAINING|5','ALIAS_REMAINING|'+(expectDefect?1:0)])assert.ok(report.native.includes(expected),expected+'\n'+report.native);
  report.defectReproduced=expectDefect;assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;console.log(JSON.stringify({success:true,module:report.health.coreMvid,defectReproduced:expectDefect,native:report.native}));
 }catch(error){report.failure=error.stack;throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;fs.writeFileSync(out,JSON.stringify(report,null,2));await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
