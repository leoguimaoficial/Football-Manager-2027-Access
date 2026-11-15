// Real compiled client/native message commands under controlled transport loss.
// No native state or response is fabricated, no command is replayed, no Save.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const args=process.argv.slice(2),checkpoint=args.find(a=>a.startsWith('--checkpoint='))?.slice(13);
const subject=args.find(a=>a.startsWith('--subject='))?.slice(10)||'Steph Catley lesionada';
const owned=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../../artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
assert.equal(owned.success,true);assert.ok(owned.ownedWorkingFiles.includes(checkpoint+'.fm'),'Use an explicitly recorded separate working copy');
const output=path.resolve(__dirname,'../../artifacts/tests/web/message-response-recovery.json');fs.mkdirSync(path.dirname(output),{recursive:true});
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const report={success:false,at:new Date().toISOString(),checkpoint,checks:[],audits:[],cases:[],saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 let release=()=>{},handler;
 const route='**/message/open?*';
 async function audit(label){const r=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({label,violations:r.violations.map(v=>v.id)});assert.equal(r.violations.length,0);}
 async function snapshot(){return page.evaluate(()=>({title:document.title,opening:FM.msgview.opening,wantBody:FM.msgview.wantBody,stash:!!FM.core.msgStash,refreshing:FM.core.refreshing,pending:FM.core.pending,view:document.querySelector('[data-fm-message-view-id]')?.getAttribute('data-fm-message-view-id'),focus:document.activeElement?.id}));}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  if(await page.locator('[data-fm-message-view-id]').count()){
   await activate(page.getByRole('button',{name:'Close message',exact:true}));
   await poll(async()=>await page.locator('[data-fm-message-view-id]').count()===0,'preceding message closed before native filename guard');
  }
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned checkpoint');assert.equal(await file.inputValue(),checkpoint);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await page.keyboard.press('Alt+1');await poll(()=>page.getByRole('heading',{name:'Mensagens',exact:true}).count(),'actual native Messages');await settled();
  const all=page.getByRole('tab',{name:'Tudo',exact:true});if(await all.getAttribute('aria-selected')!=='true')await activate(all);await c.newestMessages();await settled();
  for(const forwarded of [false,true]){
   const button=page.getByRole('button',{name:subject,exact:true});assert.equal(await button.count(),1);
   const identity=await button.evaluate(b=>({message:b.closest('tr').dataset.fmMessageId,id:b.id,status:b.closest('tr').cells[0].textContent}));
   const sample={forwarded,identity,requests:0,nativeCalls:0,nativeReply:null,routeCleanup:[]};report.cases.push(sample);
   const gate=new Promise(resolve=>release=resolve);let held=false;
   handler=async intercepted=>{
    sample.requests++;assert.equal(intercepted.request().method(),'POST');held=true;
    let response;
    if(forwarded){response=await intercepted.fetch();sample.nativeCalls++;sample.nativeReply={status:response.status(),body:await response.text()};assert.deepEqual(sample.nativeReply,{status:200,body:''});}
    await gate;
    try{if(response)await intercepted.fulfill({response});else await intercepted.abort('aborted');}
    catch(error){sample.routeCleanup.push(error.message);}
   };
   await page.route(route,handler);await button.focus();await page.keyboard.press('Enter');await poll(()=>held,'genuine message request held');
   if(forwarded)await poll(()=>sample.nativeReply!==null,'genuine native open completed before its reply is lost');
   assert.equal((await snapshot()).opening,1);
   // Use the production20s complete-response deadline, not a forged error or
   // a shorter test timer. The real browser aborts its own withheld response.
   await page.waitForFunction(()=>FM.msgview.opening===0&&!FM.core.refreshing&&!FM.core.pending,{},{timeout:35000});await settled();
   sample.result=await snapshot();release();await page.unroute(route,handler);handler=null;
   assert.equal(sample.requests,1,'The browser sends one message command only');assert.equal(sample.result.opening,0);
   if(!forwarded){
    assert.equal(sample.nativeCalls,0);assert.equal(sample.result.view,undefined);assert.equal(sample.result.wantBody,false,'An authoritative native inbox must retire a failed modal intent');assert.equal(sample.result.stash,false,'A failed pre-HTTP open must not retain an old inbox stash');
    assert.equal(await button.locator('..').locator('..').locator('td').first().textContent(),identity.status);
    report.checks.push('Withheld pre-HTTP request executes no native command; real timeout releases the pending gate and retires modal intent/stash after native inbox observation');
   }else{
    assert.equal(sample.nativeCalls,1);assert.equal(sample.result.view,identity.message);assert.equal(await page.title(),subject);
    assert.equal(await page.locator('[data-fm-msg-body]').first().evaluate(e=>document.activeElement===e),true);
    await page.keyboard.press('Escape');await poll(()=>page.evaluate(id=>document.activeElement?.closest('tr')?.dataset.fmMessageId===id,identity.message),'exact native message return after lost reply');await settled();
    report.checks.push('Native open executes once before its actual reply is withheld; timeout observes the real matching pane/body focus without replay, and Escape restores the exact original record');
   }
   await audit(forwarded?'Actual opened message after lost reply and native return':'Native inbox after pre-HTTP timeout');
  }
  assert.equal(c.errors.length,0);
  assert.equal(c.requestFailures.length,2);assert.ok(c.requestFailures.every(r=>r.path==='/message/open'&&/ABORTED/.test(r.error)),'Only the two controlled request cancellations are allowed');
  assert.ok(c.network.failures.every(r=>r.request?.path==='/message/open'&&r.canceled),'Only controlled message cancellations are allowed in CDP');
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,report.health.coreMvid);report.success=true;
 }catch(error){report.failure=error.stack;report.state=await snapshot().catch(()=>null);report.html=await page.locator('#fmroot').innerHTML().catch(()=>null);throw error;}
 finally{
  release();if(handler)await page.unroute(route,handler).catch(()=>{});
  report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;
  try{fs.writeFileSync(output,JSON.stringify(report,null,2));}finally{await c.browser.close();}
 }
 console.log(JSON.stringify({success:true,checks:report.checks.length,cleanAxe:report.audits.length,controlledTransportLosses:2}));
})().catch(error=>{console.error(error);process.exitCode=1;});
