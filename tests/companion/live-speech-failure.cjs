// Actual production UI/Alt+I and native SpeechPipeline, with isolated transport
// faults. No game command, save or simulation is submitted by these injections.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],audits=[],observations={},controlled=new Set(),releases=[];
 const notice=()=>page.locator('#fm_speech_notice');
 async function audit(name){const result=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:result.violations.map(v=>v.id)});assert.deepEqual(result.violations,[]);}
 async function goPortal(){const e=page.locator('#fmnav-tree [role=treeitem][data-uid="989379584"]').first(),q=page.getByRole('textbox',{name:'Search all screens',exact:true});await q.fill(await e.locator(':scope > span').textContent());await e.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await poll(()=>page.getByRole('heading',{name:'Portal',exact:true}).count(),'actual Portal');await q.fill('');await settled();}
 async function recovered(){const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/speak');await page.evaluate(()=>FM.core.say('QA speech transport recovered'));assert.equal((await response).status(),200);await poll(async()=>await notice().count()===0,'actual speech acknowledgement clears error');assert.ok((await c.fetchText('/speech')).includes('QA speech transport recovered'));}
 try{
  observations.mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  assert.equal(await page.evaluate(()=>typeof FM.core.speechNotice),'function','Fresh client must contain deployed speech feedback');
  await goPortal();assert.equal(await notice().count(),0);
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned QA identity');assert.match(await file.inputValue(),/^FM27Access QA /);await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const info=page.getByRole('button',{name:/^Edit bookmarks/});await info.focus();await settled();const origin=await info.getAttribute('id'),description=await c.fetchText('/describe?id='+encodeURIComponent(origin)),html=await page.locator('#fmroot').innerHTML();assert.ok(description.length>0);observations.nativeDescriptionLength=description.length;
  // Observe real request settlement without changing its result or replaying it.
  await page.evaluate(()=>{const original=FM.core.requestText;window.qaSpeechSettled={};window.qaSpeechCompletions=0;window.qaRestoreSpeechObserver=()=>{FM.core.requestText=original;};FM.core.requestText=function(u,o,t){const result=original.call(this,u,o,t);if(!String(u).startsWith('/speak?'))return result;const text=new URL(u,location.href).searchParams.get('text');return result.then(value=>{qaSpeechSettled[text]='success';qaSpeechCompletions++;return value;},error=>{qaSpeechSettled[text]='failed';qaSpeechCompletions++;throw error;});};});
  let failures=0;
  await page.route('**/speak?*',async route=>{failures++;if(failures===1)await route.fulfill({status:503,body:'Controlled unavailable speech'});else{controlled.add(c.requestInfo(route.request()).id);await route.abort('failed');}});
  await page.keyboard.press('Alt+i');await poll(()=>notice().count(),'readable HTTP speech failure');assert.equal(await notice().getAttribute('role'),'alert');assert.equal(await notice().innerText(),'Speech could not be confirmed.');
  await page.evaluate(()=>window.qaFirstSpeechNotice=document.getElementById('fm_speech_notice'));
  await page.keyboard.press('Alt+i');await poll(()=>failures===2,'one speech request per actual Alt+I');await poll(()=>page.evaluate(()=>qaSpeechCompletions>=2),'both actual failure handlers settled');assert.equal(await page.evaluate(()=>document.getElementById('fm_speech_notice')===qaFirstSpeechNotice),true);
  assert.equal(await page.locator('#fmroot').innerHTML(),html);assert.equal(await page.evaluate(()=>document.activeElement.id),origin);assert.ok(!(await page.locator('body').innerText()).includes(description));await audit('Error-only speech notice and unchanged actual Portal');
  await page.unroute('**/speak?*');await recovered();assert.equal(await page.evaluate(()=>document.activeElement.id),origin);
  checks.push('Real Alt+I/describe reaches HTTP and connection failure paths once; one generic alert preserves focus/DOM, never exposes its description, and real native speech acknowledgement clears it');

  let executed=0,releaseSuccess;const successGate=new Promise(r=>releaseSuccess=r);releases.push(releaseSuccess);
  await page.route('**/speak?*',async route=>{const text=new URL(route.request().url()).searchParams.get('text');if(text==='QA delayed speech acknowledgement'){const response=await route.fetch();assert.equal(response.status(),200);executed++;await successGate;await route.fulfill({response});}else await route.fulfill({status:503,body:'Controlled newer speech failure'});});
  await page.evaluate(()=>FM.core.say('QA delayed speech acknowledgement'));await poll(()=>executed===1,'actual older native speech execution');await page.evaluate(()=>FM.core.say('QA newer unavailable speech'));await poll(()=>notice().count(),'newer speech failure');releaseSuccess();await poll(()=>page.evaluate(()=>qaSpeechSettled['QA delayed speech acknowledgement']==='success'),'older actual acknowledgement settled');assert.equal(await notice().count(),1);assert.equal(executed,1);
  await page.unroute('**/speak?*');await recovered();checks.push('An older real native success cannot clear a newer failure; no speech retry or duplicate native execution');

  let held=false,releaseStale;const staleGate=new Promise(r=>releaseStale=r);releases.push(releaseStale);
  await page.route('**/speak?*',async route=>{if(new URL(route.request().url()).searchParams.get('text')!=='QA old screen speech'){await route.continue();return;}held=true;await staleGate;await route.fulfill({status:503,body:'Controlled stale speech failure'});});
  await page.evaluate(()=>FM.core.say('QA old screen speech'));await poll(()=>held,'old-screen speech request held');await page.keyboard.press('Alt+1');await poll(()=>page.getByRole('heading',{name:'Mensagens',exact:true}).count(),'real native Messages transition');await settled();releaseStale();await poll(()=>page.evaluate(()=>qaSpeechSettled['QA old screen speech']==='failed'),'stale failure settled');assert.equal(await notice().count(),0);await page.unroute('**/speak?*');await goPortal();await audit('Actual Portal after speech recovery and native context return');
  checks.push('A late failure from another native screen cannot add a stale alert after Alt+1; original Portal returns without a notice');
  observations.nativeDelayedSpeechExecutions=executed;
  const expected=c.requestFailures.filter(f=>controlled.has(f.request?.id)),unexpected=c.requestFailures.filter(f=>!controlled.has(f.request?.id));assert.equal(expected.length,1);assert.deepEqual(unexpected,[]);assert.deepEqual(c.errors,[]);assert.deepEqual(c.network.failures.map(f=>({path:f.request?.path,error:f.error})),[{path:'/speak',error:'net::ERR_FAILED'}]);
  const report={success:true,checks,audits,observations,controlledFailures:expected,unexpectedRequestFailures:unexpected,errors:c.errors,networkFailures:c.network.failures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/speech-failure.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
 }catch(error){fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/speech-failure-error.json'),JSON.stringify({error:error.stack,checks,audits,observations,html:await page.locator('#fmroot').innerHTML(),notice:await notice().allTextContents(),errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));throw error;}
 finally{for(const release of releases)release();await page.unrouteAll({behavior:'wait'});await page.evaluate(()=>window.qaRestoreSpeechObserver?.()).catch(()=>{});await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
