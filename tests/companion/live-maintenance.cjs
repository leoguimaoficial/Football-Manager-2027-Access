// Real production client in an owned Portuguese QA career with a native tactic.
// Transport faults below are controlled interceptions, separate from native
// action evidence. No simulation, save, transfer or tactical edit is submitted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],speech=[],logs=[],controlledRequests=new Map();
 function controlled(route,reason){const request=c.requestInfo(route.request());assert.ok(request,'Observed intercepted request identity');controlledRequests.set(request.id,reason);}
 page.on('request',r=>{const u=new URL(r.url());if(u.pathname==='/speak')speech.push(u.searchParams.get('text'));if(u.pathname==='/clientlog')logs.push(r.postData());});
 const failure=()=>speech.filter(t=>t&&t.includes('could not be confirmed')).length;
 const search=()=>page.getByRole('button',{name:'Pesquisar',exact:true});
 async function navigate(uid,title){
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first();
  const filter=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await filter.fill(await entry.locator(':scope > span').textContent());await entry.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');
  assert.match(await(await response).text(),/^Opening /);
  await poll(()=>page.getByRole('heading',{name:title,exact:true}).count(),'native '+title);await settled();await filter.fill('');await settled();
 }
 let releaseMirror,originalPhase;
 try{
  if(!await page.getByRole('heading',{name:'Tácticas',exact:true}).count())await navigate(195478016,'Tácticas');
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'save identity');
  const ownedSave=await file.inputValue();assert.match(ownedSave,/^FM27Access QA /);
  await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));await settled();
  // The native Panoramic view intentionally has no compact pitch. Other live
  // suites can leave it selected; establish and restore the actual phase.
  const phase=page.getByRole('tab',{name:'Com Posse de Bola',exact:true});
  originalPhase=await phase.locator('..').getByRole('tab',{selected:true}).textContent();
  if(await phase.getAttribute('aria-selected')!=='true')await c.activate(phase);
  const cells=page.locator('#fm-pitch-browse td');
  await poll(async()=>{const texts=await cells.allTextContents();return texts.length===11&&texts.every(t=>!t.includes('number ')&&!t.includes("couldn't read"));},'eleven named native occupants');
  const occupants=await cells.allTextContents();
  await page.locator('#bc-GK').focus();await page.keyboard.press('ArrowUp');
  const arrow=await page.evaluate(()=>({id:document.activeElement.id,text:document.activeElement.textContent}));
  assert.match(arrow.id,/^bc-D/);assert.ok(occupants.includes(arrow.text));
  const classification=await c.fetchText('/classify');assert.doesNotMatch(classification,/\[Image\].*name="Card-frame"/);
  assert.equal(await page.getByText('Additional native bookmarks: (+2)',{exact:true}).count(),1);
  const overflowId=await page.getByText('Additional native bookmarks: (+2)',{exact:true}).getAttribute('id');
  assert.match(await c.fetchText('/describe?id='+encodeURIComponent(overflowId)),/more selected bookmarks than fit/);
  const nativeBookmarks=await c.fetchText('/bookmarkpopup?op=module');assert.match(nativeBookmarks,/selected \(5\):/);
  const edit=page.getByRole('button',{name:/^Edit bookmarks/});await edit.focus();
  const described=page.waitForResponse(r=>new URL(r.url()).pathname==='/speak');await page.keyboard.press('Alt+i');await described;
  assert.ok(speech.some(t=>t&&t.includes('separate from the game')));
  checks.push('Eleven named compact-pitch occupants, spatial arrows, no decorative Card-frame speech stop, native five-bookmark/two-overflow context and original Alt+I speech');

  // An isolated native TextField exercises masking/description/write feedback
  // without entering an authentication screen or accessing a real password.
  const module=JSON.parse(await c.fetchText('/health')).coreMvid;
  const passwordCode='var protectedField=new UnityEngine.UIElements.TextField();protectedField.isPasswordField=true;protectedField.value="QA-only-first-sentinel";var protectedAsm=System.AppDomain.CurrentDomain.GetAssemblies()[0];foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.GetName().Name=="FM27Access.Plugin"&&a.ManifestModule.ModuleVersionId.ToString()=="'+module+'")protectedAsm=a;var fieldType=protectedAsm.GetType("FM27Access.Plugin.Adapters.TextFieldAdapter");var fieldAdapter=System.Activator.CreateInstance(fieldType,new object[]{protectedField});var stopType=protectedAsm.GetType("FM27Access.Plugin.Server.ServerHandles+StopHandle");var protectedHandle=System.Activator.CreateInstance(stopType);stopType.GetField("El").SetValue(protectedHandle,fieldAdapter);Console.WriteLine("label="+fieldType.GetMethod("GetLabel").Invoke(fieldAdapter,null));Console.WriteLine("description="+stopType.GetMethod("Describe").Invoke(protectedHandle,null));Console.WriteLine("confirmation="+stopType.GetMethod("SetText").Invoke(protectedHandle,new object[]{"QA-only-second-sentinel"}));Console.WriteLine("nativeWriteVerified="+(protectedField.value=="QA-only-second-sentinel"));';
  const passwordResult=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),passwordCode);
  assert.match(passwordResult,/label=Password/);assert.match(passwordResult,/description=Password, password field/);
  assert.match(passwordResult,/confirmation=Password updated\./);assert.match(passwordResult,/nativeWriteVerified=True/);
  assert.doesNotMatch(passwordResult,/sentinel/);
  checks.push('Isolated native password field keeps its label, description and successful-write feedback free of its value; no authentication performed');

  let blocked=0;const before=failure();
  await page.route('**/activate?*',async route=>{controlled(route,'Deliberately aborted before native dispatch');blocked++;await route.abort('timedout');});
  await search().focus();await page.keyboard.press('Enter');await poll(()=>failure()>before,'uncertain blocked action feedback');await settled();
  assert.equal(blocked,1);assert.equal(await page.getByRole('textbox',{name:'Global search',exact:true}).count(),0);
  await page.unroute('**/activate?*');
  checks.push('Blocked native activation reports uncertainty without an uncaught rejection or automatic command replay');

  let executed=0;
  await page.route('**/activate?*',async route=>{controlled(route,'Deliberately lost response after one native dispatch');executed++;await route.fetch();await route.abort('timedout');});
  await search().focus();await page.keyboard.press('Enter');
  await poll(()=>page.getByRole('textbox',{name:'Global search',exact:true}).count(),'native search despite lost response');await settled();
  assert.equal(executed,1);await page.unroute('**/activate?*');
  await page.keyboard.press('Escape');await poll(()=>search().count(),'search origin');await settled();
  checks.push('Native search opens when its response is lost; command executes once and Escape returns to tactics');

  const prior=failure();await page.route('**/activate?*',route=>route.fulfill({status:503,body:'Controlled unavailable response'}));
  await search().focus();await page.keyboard.press('Enter');await poll(()=>failure()>prior,'HTTP failure feedback');await settled();
  await page.unroute('**/activate?*');assert.equal(await page.getByRole('textbox',{name:'Global search',exact:true}).count(),0);
  checks.push('HTTP errors are treated as unconfirmed actions rather than successful activations');

  // A real health read supplies the response headers; hold only its body in
  // the browser to exercise the deadline after fetch has already resolved.
  const deadline=await page.evaluate(async()=>{
   const nativeFetch=window.fetch;let headers=false;
   window.fetch=async(u,o)=>{const r=await nativeFetch(u,o);if(u!=='/health')return r;await r.text();headers=true;return {ok:r.ok,status:r.status,text:()=>new Promise((resolve,reject)=>{o.signal.addEventListener('abort',()=>reject(new DOMException('Controlled stalled body','AbortError')),{once:true});})};};
   try{await FM.core.requestText('/health',null,120);return {headers,aborted:false};}
   catch(e){return {headers,aborted:e.name==='AbortError'};}
   finally{window.fetch=nativeFetch;}
  });assert.deepEqual(deadline,{headers:true,aborted:true});
  checks.push('Controlled body stall remains bounded after real native response headers arrive');

  const refreshDeadline=await page.evaluate(async()=>{
   const nativeFetch=window.fetch,nativeTimer=window.setTimeout,nativeRefresh=FM.core.refresh;
   let headers=false,aborted=false,bodyRequests=0,abortRefresh,received;
   const gotHeaders=new Promise(resolve=>{received=resolve;});
   FM.core.refresh=function(...args){
    const previousTimer=window.setTimeout;
    window.setTimeout=(fn,ms,...rest)=>{if(ms===20000&&!abortRefresh)abortRefresh=fn;return previousTimer(fn,ms,...rest);};
    try{return nativeRefresh.apply(this,args);}finally{window.setTimeout=previousTimer;}
   };
   window.fetch=async(u,o)=>{
    const firstBody=u==='/body?c=1'&&++bodyRequests===1;
    const r=await nativeFetch(u,o);
    if(!firstBody)return r;
    await r.text();headers=true;
    nativeTimer(abortRefresh,100);received();
    return {ok:r.ok,status:r.status,headers:r.headers,text:()=>new Promise((resolve,reject)=>{
     const abort=()=>{aborted=true;reject(new DOMException('Controlled stalled refresh body','AbortError'));};
     if(o.signal.aborted)abort();else o.signal.addEventListener('abort',abort,{once:true});
    })};
   };
   try{
    FM.core.refresh(false,true);
    await Promise.race([gotHeaders,new Promise((resolve,reject)=>nativeTimer(()=>reject(Error('No native body headers')),10000))]);
    await new Promise(resolve=>nativeTimer(resolve,200));
    // A coalesced native update can legitimately start a new read immediately
    // after the failed one releases the gate. That is recovery, not a wedge.
    return {headers,aborted,released:!FM.core.refreshing||bodyRequests>1,retained:document.querySelectorAll('#fm-pitch-browse td').length===11};
   }finally{window.fetch=nativeFetch;FM.core.refresh=nativeRefresh;window.setTimeout=nativeTimer;}
  });assert.deepEqual(refreshDeadline,{headers:true,aborted:true,released:true,retained:true});
  await settled();
  checks.push('A controlled production-body stall releases the refresh gate and retains the last readable native DOM');

  // Delay one real diagnostic mirror while focus continues to change. Only
  // the newest subsequent snapshot should be sent after that request completes.
  let mirrors=0;const gate=new Promise(resolve=>{releaseMirror=resolve;});
  await page.route('**/rendered/report?*',async route=>{mirrors++;if(mirrors===1)await gate;await route.continue();});
  await page.evaluate(()=>FMReport.schedule());await poll(()=>mirrors===1,'first mirror');
  await page.locator('#bc-GK').focus();await search().focus();
  await new Promise(r=>setTimeout(r,800));assert.equal(mirrors,1);
  releaseMirror();await poll(()=>mirrors===2,'latest coalesced mirror');await page.unroute('**/rendered/report?*');
  checks.push('Diagnostic DOM reports keep one request in flight and coalesce newer focus snapshots');
  assert.ok(logs.some(t=>t&&t.includes('/activate response failed')));
  assert.deepEqual(c.errors,[]);
  const axe=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  const controlledFailures=c.requestFailures.filter(f=>controlledRequests.has(f.request?.id)).map(f=>({...f,controlled:controlledRequests.get(f.request.id)}));
  const unexpectedRequestFailures=c.requestFailures.filter(f=>!controlledRequests.has(f.request?.id));
  const report={ownedSave,mvid:module,occupants,arrow,checks,violations:axe.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)})),errors:c.errors,requestFailures:c.requestFailures,controlledFailures,unexpectedRequestFailures,networkFailures:c.network.failures,controlledTransportFailures:true,manualNVDA:false};
  const output=path.resolve(__dirname,'../../artifacts/tests/web/maintenance.json');fs.mkdirSync(path.dirname(output),{recursive:true});fs.writeFileSync(output,JSON.stringify(report,null,2));
  assert.deepEqual(report.violations,[]);assert.deepEqual(unexpectedRequestFailures,[]);assert.equal(controlledFailures.length,2);
  assert.deepEqual(c.network.failures.map(f=>({path:f.request?.path,error:f.error})),[{path:'/activate',error:'net::ERR_TIMED_OUT'},{path:'/activate',error:'net::ERR_TIMED_OUT'}]);console.log(JSON.stringify(report,null,2));
  const restore=page.getByRole('tab',{name:originalPhase,exact:true});
  if(await restore.getAttribute('aria-selected')!=='true')await c.activate(restore);
  await navigate(989379584,'Portal');
 }catch(error){
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/maintenance-failure.json'),JSON.stringify({error:error.stack,checks,errors:c.errors,requestFailures:c.requestFailures,network:c.network,transport:await c.transportTrace()},null,2));throw error;
 }finally{
  if(releaseMirror)releaseMirror();
  if(originalPhase){const phase=page.getByRole('tab',{name:originalPhase,exact:true});
   if(await phase.count()&&await phase.getAttribute('aria-selected')!=='true')await c.activate(phase);}
  await c.browser.close();
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
