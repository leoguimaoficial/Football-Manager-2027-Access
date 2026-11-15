// Debug-only, read-only native conformance. Creates managed wrappers for EXISTING
// VisualElement pointers. The probe creates no synthetic UI or business command;
// normal Save As/open/cancel UI verifies ownership without writing a save.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..'),checkpoint='FM27Access QA Squads 20261108-mw8cp1b3';
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,activate}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,nativeBusinessCommandsSubmitted:0,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const out=path.join(root,'artifacts/tests/web/native-tree-identity.json');
 try{
  report.health=JSON.parse(await c.fetchText('/health'));assert.equal(report.health.developerTools,true,'Real native inspection is deliberately unavailable in Release');
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const actual=await page.getByRole('textbox',{name:'File name',exact:true}).inputValue();await activate(page.getByRole('button',{name:'Cancelar',exact:true}));assert.equal(actual,checkpoint);
  report.nativeCursor=await page.evaluate(async()=>await(await fetch('/cursor',{method:'POST',body:'home'})).text());
  assert.match(report.nativeCursor,/^home -> (?!no cursor)/,'The real navigation Host establishes its cursor through the original Home operation');
  const code=`
var core=System.AppDomain.CurrentDomain.GetAssemblies()[0];
var nav=core; var ui=core;
foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()) {
 if(a.ManifestModule.ModuleVersionId.ToString()=="${report.health.coreMvid}") core=a;
 if(a.GetName().Name=="FM27Access.Navigation") nav=a;
 if(a.GetType("UnityEngine.UIElements.VisualElement")!=null) ui=a;
}
var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;
var visual=ui.GetType("UnityEngine.UIElements.VisualElement");
var nativeIndex=visual.GetMethods(f)[0];bool indexFound=false;
foreach(var method in visual.GetMethods(f)) {
 if(method.Name=="IndexOf"&&method.GetParameters().Length==1&&method.GetParameters()[0].ParameterType.FullName=="UnityEngine.UIElements.VisualElement") {nativeIndex=method;indexFound=true;break;}
}
if(!indexFound) throw new System.InvalidOperationException("Native VisualElement.IndexOf signature not found");
var walker=core.GetType("FM27Access.Plugin.TreeWalker");
var handles=(System.Collections.IDictionary)core.GetType("FM27Access.Plugin.Server.ServerHandles").GetField("_handles",f).GetValue(null);
bool found=false;
foreach(System.Collections.DictionaryEntry entry in handles) {
 var field=entry.Value.GetType().GetField("El",f); if(field==null) continue;
 var adapter=field.GetValue(entry.Value); if(adapter==null) continue;
 if(adapter.GetType().Name!="ButtonAdapter") continue;
 var property=adapter.GetType().GetProperty("BackingElement",f); if(property==null) continue;
 var element=property.GetValue(adapter,null); if(element==null) continue;
 var parent=visual.GetProperty("parent",f).GetValue(element,null); if(parent==null) continue;
 // A native button inherits TextElement but can intentionally have empty own
 // text while its child supplies the caption. Use its actual nonempty/non-text
 // parent for classification-cache checks; empty text must remain provisional.
 if((bool)core.GetType("FM27Access.Plugin.ElementBridge").GetMethod("IsEmptyTextElement",f).Invoke(null,new object[]{parent}))continue;
 var childPointer=visual.GetProperty("Pointer",f).GetValue(element,null);
 var parentPointer=visual.GetProperty("Pointer",f).GetValue(parent,null);
 var constructor=visual.GetConstructor(new[]{childPointer.GetType()});
 var freshChild=constructor.Invoke(new object[]{childPointer});
 var freshParent=constructor.Invoke(new object[]{parentPointer});
 int expected=(int)nativeIndex.Invoke(parent,new object[]{freshChild}); if(expected<0) continue;
 System.Console.WriteLine("WRAPPER_DISTINCT|"+(!object.ReferenceEquals(element,freshChild)&&!object.ReferenceEquals(parent,freshParent)));
 System.Console.WriteLine("POINTERS_EQUAL|"+(childPointer.Equals(visual.GetProperty("Pointer",f).GetValue(freshChild,null))&&parentPointer.Equals(visual.GetProperty("Pointer",f).GetValue(freshParent,null))));
 System.Console.WriteLine("EXPECTED_NATIVE_INDEX|"+expected);
 System.Console.WriteLine("ANCESTRY|"+walker.GetMethod("IsDescendantOf",f).Invoke(null,new object[]{freshChild,freshParent}));
 try{System.Console.WriteLine("INDEX|"+walker.GetMethod("IndexInParent",f).Invoke(null,new object[]{freshChild}));}catch(System.Exception e){System.Console.WriteLine("INDEX_ERROR|"+e.GetBaseException().Message);}
 try{System.Console.WriteLine("DIRECT_CHILD|"+walker.GetMethod("FindDirectChildContaining",f).Invoke(null,new object[]{freshParent,freshChild}));}catch(System.Exception e){System.Console.WriteLine("DIRECT_CHILD_ERROR|"+e.GetBaseException().Message);}
 System.Console.WriteLine("SELF_IS_DESCENDANT|"+walker.GetMethod("IsDescendantOf",f).Invoke(null,new object[]{freshChild,freshChild}));
 var targets=System.Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(new[]{visual}));
 targets.GetType().GetMethod("Add").Invoke(targets,new object[]{freshChild});
 System.Console.WriteLine("TAB_SUBTREE_MEMBERSHIP|"+core.GetType("FM27Access.Plugin.Server.ScreenWalker").GetMethod("SubtreeContainsAny",f).Invoke(null,new object[]{element,targets}));
 var screenWalker=core.GetType("FM27Access.Plugin.Server.ScreenWalker");
 var noCoverage=System.Array.CreateInstance(visual,0);
 var coverage=System.Array.CreateInstance(visual,1);coverage.SetValue(freshChild,0);
 var collect=screenWalker.GetMethod("CollectMatchOptions",f);
 var ordinary=collect.Invoke(null,new object[]{element,noCoverage});
 var covered=collect.Invoke(null,new object[]{element,coverage});
 System.Console.WriteLine("UNCOVERED_CONTROL_COUNT|"+ordinary.GetType().GetProperty("Count").GetValue(ordinary,null));
 System.Console.WriteLine("COVERED_CONTROL_COUNT|"+covered.GetType().GetProperty("Count").GetValue(covered,null));
 var cacheType=core.GetType("FM27Access.Plugin.ClassificationCache");
 var cache=System.Activator.CreateInstance(cacheType,true);
 var classify=cacheType.GetMethod("GetOrClassify",f);
 var originalClassification=classify.Invoke(cache,new object[]{parent,null});
 var cachedClassification=cacheType.GetMethod("GetClassification",f).Invoke(cache,new object[]{freshParent});
 System.Console.WriteLine("CACHE_CLASSIFICATION|"+object.Equals(originalClassification,cachedClassification));
 classify.Invoke(cache,new object[]{freshParent,null});
 var cachedEntries=cacheType.GetField("_cache",f).GetValue(cache);
 System.Console.WriteLine("CACHE_ENTRY_COUNT|"+cachedEntries.GetType().GetProperty("Count").GetValue(cachedEntries,null));
 var linear=core.GetType("FM27Access.Plugin.Contexts.LinearContext").GetConstructors(f)[0].Invoke(new object[]{parent,cache});
 cacheType.GetMethod("AttachContext",f).Invoke(cache,new object[]{parent,linear});
 System.Console.WriteLine("CACHE_DISTINCT_NATIVE_ISOLATED|"+(cacheType.GetMethod("GetContext",f).Invoke(cache,new object[]{freshChild})==null&&cacheType.GetMethod("GetClassification",f).Invoke(cache,new object[]{freshChild})==null));
 System.Console.WriteLine("CACHE_CONTEXT|"+object.ReferenceEquals(linear,cacheType.GetMethod("GetContext",f).Invoke(cache,new object[]{freshParent})));
 System.Console.WriteLine("CACHE_ANCESTOR_CONTEXT|"+object.ReferenceEquals(linear,cacheType.GetMethod("FindContextForElement",f).Invoke(cache,new object[]{freshChild})));
 cacheType.GetMethod("Remove",f).Invoke(cache,new object[]{freshParent});
 System.Console.WriteLine("CACHE_CONTEXT_REMOVED|"+(cacheType.GetMethod("GetContext",f).Invoke(cache,new object[]{parent})==null));
 System.Console.WriteLine("CACHE_CLASSIFICATION_REMOVED|"+(cacheType.GetMethod("GetClassification",f).Invoke(cache,new object[]{parent})==null));
 var manager=nav.GetType("FM27Access.Navigation.NavigationManager");
 var panel=manager.GetMethod("GetActivePanel",f).Invoke(null,null);
 if(panel!=null){
  var pointer=visual.GetProperty("Pointer",f).GetValue(panel,null);var fresh=constructor.Invoke(new object[]{pointer});
  System.Console.WriteLine("NATIVE_PANEL_COMPARISON|"+nav.GetType("FM27Access.Navigation.PanelTracker").GetMethod("SameInstance",f).Invoke(null,new object[]{panel,fresh}));
  var actualCache=core.GetType("FM27Access.Plugin.CoreEntry").GetField("Cache",f).GetValue(null);
  System.Console.WriteLine("GLOBAL_NATIVE_PANEL_CONTEXT|"+(cacheType.GetMethod("GetContext",f).Invoke(actualCache,new object[]{fresh})!=null));
  var cursor=manager.GetMethod("GetCurrentElement",f).Invoke(null,null);
  var backing=cursor.GetType().GetProperty("BackingElement",f).GetValue(cursor,null);
  var actualContext=cacheType.GetMethod("FindContextForElement",f).Invoke(actualCache,new object[]{backing});
  System.Console.WriteLine("GLOBAL_NATIVE_CURSOR_CONTEXT|"+(actualContext!=null));
  if(actualContext!=null){var scope=actualContext.GetType().GetProperty("Scope",f).GetValue(actualContext,null);System.Console.WriteLine("GLOBAL_CONTEXT_CONTAINS_CURSOR|"+walker.GetMethod("IsDescendantOf",f).Invoke(null,new object[]{backing,scope}));}
 }
 System.Console.WriteLine("NAVIGATION_MVID|"+nav.ManifestModule.ModuleVersionId);
 found=true;break;
}
System.Console.WriteLine("FOUND|"+found);
`;
  report.native=await page.evaluate(async code=>await(await fetch('/eval',{method:'POST',body:code})).text(),code);
  assert.doesNotMatch(report.native,/error CS|\[exception\]/i,'Native inspection must actually execute');
  report.values=Object.fromEntries(report.native.trim().split(/\r?\n/).filter(l=>l.includes('|')).map(l=>{const at=l.indexOf('|');return[l.slice(0,at),l.slice(at+1)];}));
  const v=report.values;assert.equal(v.FOUND,'True');assert.equal(v.WRAPPER_DISTINCT,'True');assert.equal(v.POINTERS_EQUAL,'True');
  assert.equal(v.NATIVE_PANEL_COMPARISON,'True','Actual native host pointer comparison across wrappers');
  assert.equal(v.ANCESTRY,'True','Existing native parent relationship survives fresh managed wrappers');
  assert.equal(v.INDEX,v.EXPECTED_NATIVE_INDEX,'Same native sibling index as native VisualElement.IndexOf');
  assert.equal(v.DIRECT_CHILD,v.EXPECTED_NATIVE_INDEX,'Direct native-child containment survives fresh wrappers');
  assert.equal(v.SELF_IS_DESCENDANT,'False','The strict descendant contract still excludes self');
  assert.equal(v.TAB_SUBTREE_MEMBERSHIP,'True','Tab backing elements retain native subtree membership across managed wrappers');
  assert.equal(v.UNCOVERED_CONTROL_COUNT,'1','The actual native button remains an accessible fixture option when no column covers it');
  assert.equal(v.COVERED_CONTROL_COUNT,'0','A native control represented by a dedicated fixture column is not duplicated through a fresh wrapper');
  assert.equal(v.CACHE_CLASSIFICATION,'True','The native element retains its classification through a fresh wrapper');
  assert.equal(v.CACHE_ENTRY_COUNT,'1','One native element occupies one cache entry across hierarchy wrappers');
  assert.equal(v.CACHE_CONTEXT,'True','A native container retains its navigation context through a fresh wrapper');
  assert.equal(v.CACHE_DISTINCT_NATIVE_ISOLATED,'True','Different native child and parent identities retain separate classifications and contexts');
  assert.equal(v.CACHE_ANCESTOR_CONTEXT,'True','A child finds the context of its actual native ancestor');
  assert.equal(v.CACHE_CONTEXT_REMOVED,'True','Removing through a fresh wrapper removes the native container context');
  assert.equal(v.CACHE_CLASSIFICATION_REMOVED,'True','Removing through a fresh wrapper removes the native classification');
  assert.equal(v.GLOBAL_NATIVE_PANEL_CONTEXT,'True','The actual Home operation registers a context retrievable through another wrapper over the active native panel');
  assert.equal(v.GLOBAL_NATIVE_CURSOR_CONTEXT,'True','The real native navigation cursor resolves its actual containing context');
  assert.equal(v.GLOBAL_CONTEXT_CONTAINS_CURSOR,'True','The real cursor remains within its native context scope');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
  console.log(JSON.stringify({success:true,module:report.health.coreMvid,values:v}));
 }catch(error){report.failure=error.stack;throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify(report,null,2));await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
