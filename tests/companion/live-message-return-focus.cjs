// Actual FM27 NewsItem return across Portal and Messages. No simulated rows,
// game Save or calendar progression; reading changes only owned QA read state.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],observations={},out=path.resolve(__dirname,'../../artifacts/tests/web');let mvid,developerTools;
 const productionReadiness=process.argv.includes('--production-readiness');
 const filterCycles=Number(process.argv.find(a=>a.startsWith('--filter-cycles='))?.slice(16)||0);assert.ok(Number.isInteger(filterCycles)&&filterCycles>=0&&filterCycles<=20);
 const rows=()=>page.locator('tr[data-fm-message-id]');
 const subject=id=>page.locator('tr[data-fm-message-id="'+id+'"] button[onclick^="FM.msgview.open"]');
 const focused=()=>page.evaluate(()=>document.activeElement?.closest('tr[data-fm-message-id]')?.dataset.fmMessageId);
 async function audit(name){const r=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:r.violations.map(v=>v.id)});assert.equal(r.violations.length,0);}
 async function portal(){const e=page.locator('#fmnav-tree [role=treeitem][data-uid="989379584"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});await search.fill(await e.locator(':scope > span').textContent());await e.focus();const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');r.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await r).text(),/^Opening /);await poll(async()=>await page.title()==='Portal','actual native Portal');await search.fill('');await settled();}
 async function nativePane(){
  if(!developerTools||productionReadiness){
   const pane=page.locator('[data-fm-message-view-id]');assert.equal(await pane.count(),1);
   return{id:await pane.getAttribute('data-fm-message-view-id'),subject:await page.title(),source:'Production HTML: native CurrentMessageId and reading-pane subject; no independent private probe'};
  }
  const raw=await c.fetchText('/nodeprobe?type=MessageTitle&depth=1'),ids=[...raw.matchAll(/NewsItem = FM\.UI\.NewsItemReference[^\n]*\{UniqueId=(\d+)\}/g)].map(m=>m[1]);assert.ok(ids.length,'Actual native reading pane identity exists');assert.equal(new Set(ids).size,1);return{raw,id:ids[0]};
 }
 async function nativeRowsReady(){
  const before=await rows().evaluateAll(rs=>rs.map(r=>({message:r.dataset.fmMessageId,id:r.querySelector('button[onclick^="FM.msgview.open"]')?.id})));if(!before.length||before.some(r=>!r.id))return false;
  if(!developerTools||productionReadiness){
   const state=await page.evaluate(()=>({token:FMTOKEN,pending:FM.core.pending,refreshing:FM.core.refreshing,rows:[...document.querySelectorAll('tr[data-fm-message-id]')].map(r=>({message:r.dataset.fmMessageId,id:r.querySelector('button[onclick^="FM.msgview.open"]')?.id})),enabled:[...document.querySelectorAll('tr[data-fm-message-id] button[onclick^="FM.msgview.open"]')].every(b=>!b.disabled)}));
   const valid=!state.pending&&!state.refreshing&&state.enabled&&JSON.stringify(before)===JSON.stringify(state.rows);
   (observations.readiness??=[]).push({source:'Production DOM coherence only; private handle probe unavailable',rows:before.length,token:state.token,valid});return valid;
  }
  const token=await page.evaluate(()=>FMTOKEN),code='foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'"){var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;var hs=(System.Collections.IDictionary)a.GetType("FM27Access.Plugin.Server.ServerHandles").GetField("_handles",f).GetValue(null);foreach(string id in new[]{'+before.map(r=>JSON.stringify(r.id)).join(',')+'}){var h=hs[id];if(h==null){System.Console.WriteLine("ROW|"+id+"|False||");continue;}var args=new object[]{null};var valid=h.GetType().GetMethod("TryValidate").Invoke(h,args);var ad=h.GetType().GetField("El",f).GetValue(h);System.Console.WriteLine("ROW|"+id+"|"+valid+"|"+h.GetType().GetField("MessageId",f).GetValue(h)+"|"+ad.GetType().GetProperty("MessageId",f).GetValue(ad,null));}}';
  const raw=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);assert.doesNotMatch(raw,/\[exception\]|\[compile\]/);const native=raw.split(/\r?\n/).filter(l=>l.startsWith('ROW|')).map(l=>{const[,id,valid,registered,current]=l.split('|');return{id,valid:valid==='True',registered,current};});
  const after=await rows().evaluateAll(rs=>rs.map(r=>({message:r.dataset.fmMessageId,id:r.querySelector('button[onclick^="FM.msgview.open"]')?.id})));const coherent=token===await page.evaluate(()=>FMTOKEN)&&JSON.stringify(before)===JSON.stringify(after);const valid=coherent&&native.length===before.length&&native.every((r,i)=>r.valid&&r.id===before[i].id&&r.registered===before[i].message&&r.current===before[i].message);(observations.readiness??=[]).push({coherent,rows:before.length,valid,invalid:native.filter(r=>!r.valid).slice(0,3)});return valid;
 }
 async function ownerDiagnostics(){
  if(!developerTools)return{unavailable:'Private native owner probes are excluded from Release'};
  const id=observations.failedHandle||observations.opens?.at(-1)?.id;if(!id)return null;
  const code='foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'"){var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;var hs=(System.Collections.IDictionary)a.GetType("FM27Access.Plugin.Server.ServerHandles").GetField("_handles",f).GetValue(null);var h=hs['+JSON.stringify(id)+'];if(h==null){System.Console.WriteLine("Missing handle");continue;}var args=new object[]{null};System.Console.WriteLine("VALID|"+h.GetType().GetMethod("TryValidate").Invoke(h,args)+"|"+args[0]);var ad=h.GetType().GetField("El",f).GetValue(h);var e=(UnityEngine.UIElements.VisualElement)ad.GetType().GetProperty("BackingElement",f).GetValue(ad,null);for(var p=e;p!=null;p=p.hierarchy.parent)System.Console.WriteLine("ANCESTOR|"+p.name+"|"+p.GetIl2CppType().Name+"|"+p.Pointer+"|"+p.resolvedStyle.display+"|"+p.resolvedStyle.visibility+"|"+p.visible+"|"+(p.panel!=null));}';
  try{return await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);}catch(error){return{diagnosticError:error.message};}
 }
 async function open(id,{hold=false,cancel=false,holdRequest=false}={}){
  await poll(nativeRowsReady,developerTools&&!productionReadiness?'actual current native NewsItem command owners':'current actionable production message rows');
  const button=subject(id),expectedSubject=(await button.textContent()).trim(),domId=await button.getAttribute('id'),handler=await button.getAttribute('onclick');
  assert.equal(handler,'FM.msgview.open(\''+domId+'\')');const observation={message:id,id:domId,handler,heldBody:hold,heldRequest:holdRequest,cancel};(observations.opens??=[]).push(observation);
  let release=()=>{},releaseCommand=()=>{},held=0,heldCommands=0,opens=0,bodies=0;const gate=new Promise(resolve=>release=resolve),commandGate=new Promise(resolve=>releaseCommand=resolve);
  const observe=r=>{const route=new URL(r.url()).pathname;if(route==='/message/open')opens++;if(route==='/body')bodies++;};
  const holdBody=async route=>{const response=await route.fetch();if(!held){held++;await gate;}await route.fulfill({response});};
  const holdCommand=async route=>{heldCommands++;await commandGate;await route.continue();};
  try{
   if(hold||holdRequest)page.on('request',observe);
   if(holdRequest)await page.route('**/message/open?*',holdCommand);
   if(hold){await page.route('**/body?*',holdBody);await page.evaluate(()=>FM.core.refresh(false,true));await poll(()=>held===1,'one complete genuine native body held');}
   await button.focus();const r=cancel?null:page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');if(r)r.catch(()=>{});await page.keyboard.press('Enter');
   if(hold){
    assert.equal(opens,0,'No message command before the pending genuine body commits');assert.equal(await page.evaluate(()=>FM.msgview.pendingOpen?.message),id);
    if(cancel){await page.keyboard.press('Tab');const chosen=await page.evaluate(()=>document.activeElement.id);assert.equal(await page.evaluate(()=>FM.msgview.pendingOpen),null);release();await settled();assert.equal(opens,0);assert.equal(await page.evaluate(()=>document.activeElement.id),chosen);checks.push('With a complete genuine body held, Tab cancels the undispatched message intent and its newer focus survives; no message command is sent');return;}
    release();
   }
   if(holdRequest){
    await poll(()=>heldCommands===1,'one actual message request held before native dispatch');const beforeBodies=bodies;
    await page.evaluate(()=>FM.core.refresh(false,true));
    const state=await page.evaluate(()=>({opening:FM.msgview.opening,refreshing:FM.core.refreshing,pending:FM.core.pending}));
    assert.deepEqual(state,{opening:1,refreshing:false,pending:true});assert.equal(bodies,beforeBodies,'A late read must not replace handles while the message command is in flight');observation.coalescedState=state;releaseCommand();
   }
   const response=await r,reply=await response.text();observation.requestId=new URL(response.request().url()).searchParams.get('id');observation.reply=reply;
   assert.equal(response.status(),200);assert.equal(reply,'');await poll(()=>page.locator('[data-fm-message-view-id="'+id+'"]').count(),'exact native message view');await poll(()=>page.locator('[data-fm-msg-body]').first().evaluate(e=>document.activeElement===e),'message-body focus');await settled();
   const pane=await nativePane();assert.equal(pane.id,id);assert.equal(await page.title(),expectedSubject);observation.pane=pane;
   if(hold){assert.equal(opens,1);checks.push('A complete genuine body held during Enter commits before one message dispatch; native record, subject and body focus agree, without command replay');}
   if(holdRequest){assert.equal(opens,1);assert.equal(await page.evaluate(()=>FM.msgview.opening),0);checks.push('A genuine message request held before native dispatch queues a later body read without sending it; one released native command opens the exact record and then releases the coalesced read');}
  }finally{release();releaseCommand();if(hold)await page.unroute('**/body?*',holdBody);if(holdRequest)await page.unroute('**/message/open?*',holdCommand);if(hold||holdRequest)page.off('request',observe);}
 }
 async function close(id,escape=false){if(escape)await page.keyboard.press('Escape');else await activate(page.getByRole('button',{name:'Close message',exact:true}));await poll(()=>page.locator('[data-fm-message-view-id]').count().then(n=>n===0),'native message closed');await poll(async()=>await focused()===id,'exact native message return');await settled();assert.equal(await focused(),id);assert.equal(await page.evaluate(()=>FM.core.msgStash),null);}
 try{
  const health=JSON.parse(await c.fetchText('/health'));mvid=health.coreMvid;developerTools=health.developerTools===true;observations.verification={developerTools,privateNativeProbes:developerTools&&!productionReadiness,productionNativeIdentityAndSubject:true};
  if(await page.locator('[data-fm-message-view-id]').count()){
   await activate(page.getByRole('button',{name:'Close message',exact:true}));
   await poll(async()=>await page.locator('[data-fm-message-view-id]').count()===0,'close preceding regression message before native filename guard');
  }
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned QA');observations.ownedSave=await file.inputValue();assert.match(observations.ownedSave,/^FM27Access QA /);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await portal();const originalFilter=await page.getByRole('tab',{selected:true}).textContent();await activate(page.getByRole('tab',{name:'Tudo',exact:true}));await c.newestMessages();await poll(async()=>await rows().count()>=3,'actual multiple native messages');const before=await rows().evaluateAll(rs=>rs.map(r=>({id:r.dataset.fmMessageId,subject:r.cells[1].textContent})));assert.equal(new Set(before.map(r=>r.id)).size,before.length);
  const id=before[1].id,origin=await subject(id).getAttribute('id');observations.portalOrigin={id,origin,token:await page.evaluate(()=>FMTOKEN),row:before[1]};await open(id,{hold:true,cancel:true});await open(id,{hold:true});await close(id);assert.equal(await page.title(),'Mensagens');const returned=await subject(id).getAttribute('id');assert.notEqual(returned,origin,'This native Portal-to-Messages case must exercise changed DOM ownership');observations.returned={id:returned,...(developerTools&&!productionReadiness?{native:await nativePane()}:{}),focus:await focused()};await audit('Actual Portal non-first message Close return');checks.push('Native Portal to Messages changes DOM owner/id but exact NewsItem and native pane agree; Close returns to that non-first item and releases old stash');
  await page.evaluate(()=>FM.core.refresh(false,true));await settled();assert.equal(await focused(),id);await page.keyboard.press('Tab');const chosen=await page.evaluate(()=>document.activeElement.id);await page.evaluate(()=>FM.core.refresh(false,true));await settled();assert.equal(await page.evaluate(()=>document.activeElement.id),chosen);checks.push('Neutral refresh retains the same message; a newer actual Tab choice survives a later refresh');
  await portal();await poll(()=>subject(id).count(),'same retained Portal record');await open(id,{holdRequest:true});await close(id,true);checks.push('A second actual Portal open and Escape independently restore the same native record');
  for(let cycle=0;cycle<filterCycles;cycle++){
   for(const filter of ['all','unread']){
    await activate(page.getByRole('tab',filter==='all'?{name:'Tudo',exact:true}:{name:/^Por Ler \(/}));await settled();
    const current=await rows().evaluateAll(rs=>rs.map(r=>({message:r.dataset.fmMessageId,id:r.querySelector('button[onclick^="FM.msgview.open"]')?.id})));assert.ok(current.length);
    for(const row of current){
     assert.ok(row.id);const reply=await page.evaluate(async id=>{const r=await fetch('/describe?id='+encodeURIComponent(id),{method:'POST'});return{status:r.status,text:await r.text()};},row.id);
     (observations.filterOwnership??=[]).push({cycle:cycle+1,filter,...row,...reply});
     if(reply.status!==200||/no longer on screen|message changed|identity is still loading|Unknown element|Description unavailable/.test(reply.text)){observations.failedHandle=row.id;assert.fail('Current '+filter+' row '+row.message+' has no valid native owner: '+reply.text);}
    }
   }
  }
  if(filterCycles)checks.push(filterCycles+' actual All/unread filter cycles retain valid native owners on every rendered subject; read-only /describe validates once without waiting/retrying native handles');
  await activate(page.getByRole('tab',{name:/^Por Ler \(/}));await poll(async()=>await page.getByRole('tab',{name:/^Por Ler \(/}).getAttribute('aria-selected')==='true'&&await page.locator('tr[data-fm-message-id="'+id+'"]').count()===0&&await rows().count()>=3,'actual unread rows after read item exclusion');const unread=await rows().evaluateAll(rs=>rs.map(r=>r.dataset.fmMessageId));observations.unreadBefore=unread;await open(unread[1]);await close(unread[2],true);assert.equal(await page.locator('tr[data-fm-message-id="'+unread[1]+'"]').count(),0);observations.unreadAfter=await rows().evaluateAll(rs=>rs.map(r=>r.dataset.fmMessageId));await audit('Actual unread middle removal and next native row');checks.push('Unread removes the actual middle record and preserves inherited next-row focus, without selecting its departed native identity');
  await portal();const base=originalFilter.replace(/\s*\(\d+\)$/,'').replace(/[.*+?^${}()|[\]\\]/g,'\\$&');await activate(page.getByRole('tab',{name:new RegExp('^'+base+'(?:\\s*\\(\\d+\\))?$')}));await settled();await audit('Original native filter restored');assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,mvid);assert.equal(c.errors.length+c.requestFailures.length+c.network.failures.length,0);
  fs.writeFileSync(path.join(out,'message-return-focus.json'),JSON.stringify({success:true,mvid,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,network:c.network,messageReadStateChanged:true,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));console.log(JSON.stringify({success:true,mvid,checks,audits},null,2));
 }catch(error){fs.writeFileSync(path.join(out,'message-return-focus-failure.json'),JSON.stringify({success:false,error:error.stack,mvid,checks,audits,observations,ownerDiagnostics:await ownerDiagnostics(),html:await page.locator('#fmroot').innerHTML(),native:await c.fetchText('/gui').catch(diagnosticError=>({unavailable:diagnosticError.message})),errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));throw error;}
 finally{await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
