// Actual men's squad tutorial and ordinary holiday card. Requires either an
// explicitly attached sole Companion or a sole original-client test session.
// Never overlaps a second live game document with the normal Companion.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
assert.ok(process.env.FM27_COMPANION_CDP||process.env.FM27_SOLE_AUXILIARY_CLIENT==='1',
 'Close the normal Companion before opting into the sole auxiliary client');
(async()=>{
 const c=await require('./original-client.cjs')({initScript:()=>{
  window.qaPopupFocus=[];window.qaPopupPhase='initial';
  const focus=HTMLElement.prototype.focus;
  HTMLElement.prototype.focus=function(...args){
   qaPopupFocus.push({at:Date.now(),phase:qaPopupPhase,id:this.id,tag:this.tagName,text:this.textContent.slice(0,200),
    connected:this.isConnected,insidePopup:!!this.closest('[data-fm-native-dialog],[data-fm-native-overlay]')});
   return focus.apply(this,args);
  };
 }}),{page,settled,poll,activate}=c;
 const out=path.resolve(__dirname,'../../artifacts/popup-transition-focus');fs.mkdirSync(out,{recursive:true});
 const report={at:new Date().toISOString(),success:false,steps:[],bodies:[],checks:[],manualNVDA:false,
  validationSurface:process.env.FM27_COMPANION_CDP?'sole attached Companion':'sole original production client in headless Edge',
  save:false,simulation:false,tutorialFinished:false};
 const captures=[];
 page.on('response',response=>{
  if(new URL(response.url()).pathname!=='/body')return;
  captures.push((async()=>{const html=await response.text();report.bodies.push({at:Date.now(),phase:await page.evaluate(()=>qaPopupPhase),
   token:response.headers()['x-fm-token'],popup:html.includes('data-fm-native-overlay')||html.includes('data-fm-native-dialog'),
   headings:(html.match(/<h[1-6]\b[^>]*>[\s\S]*?<\/h[1-6]>/g)||[]).map(h=>h.replace(/<[^>]*>/g,''))});})().catch(error=>report.captureError=String(error)));
 });
 const overlay=()=>page.locator('[data-fm-native-overlay],[data-fm-native-dialog]');
 async function phase(value){await page.evaluate(value=>qaPopupPhase=value,value);}
 async function counter(){return page.locator('#fmroot p').evaluateAll(ps=>ps.map(p=>p.textContent).filter(t=>/^\d+\/\d+$/.test(t)));}
 async function step(name,expected){
  await phase('tutorial-step');await activate(page.getByRole('button',{name,exact:true}));
  await poll(async()=>JSON.stringify(await counter())===JSON.stringify([expected+'/7']),'actual tutorial step '+expected);await settled();
  assert.equal(await overlay().count(),1);assert.equal(await page.locator('#fmroot #fmnav-tree').count(),0);
  assert.equal(await page.evaluate(()=>!!document.activeElement.closest('[data-fm-native-overlay],[data-fm-native-dialog]')),true);
  report.steps.push({name,step:expected,focus:await page.evaluate(()=>document.activeElement.id)});
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));assert.equal(report.health.pid,2676);
  await settled();assert.equal(await page.locator('h1').textContent(),'Apresentação do Plantel');
  report.initialCounter=await counter();
  assert.equal(report.initialCounter.length,1);const initialStep=Number(report.initialCounter[0].split('/')[0]);
  assert.ok(initialStep>=1&&initialStep<=7);
  if(initialStep<7){
   for(let n=initialStep-1;n>=1;n--)await step('Anterior',n);
   // Native summary7 has no Previous action. Preserve the starting checkpoint
   // by stopping at6; an already-open summary is observed without completing it.
   for(let n=2;n<=6;n++)await step('Continuar',n);
   for(let n=5;n>=initialStep;n--)await step('Anterior',n);
  }else assert.equal(await page.getByRole('button',{name:'Anterior',exact:true}).count(),0);
  await phase('tutorial-neutral');const chosen=page.getByRole('button',{name:initialStep===7?'Close':'Continuar',exact:true});
  await chosen.focus();const chosenId=await chosen.getAttribute('id');await page.evaluate(()=>FM.core.refresh(false,true));await settled();
  assert.equal(await page.evaluate(()=>document.activeElement.id),chosenId);
  report.checks.push(initialStep===7?'Actual native summary7 and neutral refresh retain popup content/focus; no completion submitted':
   'Actual tutorial steps1–6, both directions and neutral refresh retain popup content/focus; original checkpoint restored');
  await phase('actual-close');await activate(page.getByRole('button',{name:'Close',exact:true}));
  await poll(async()=>await overlay().count()===0,'actual tutorial closure');await settled();
  assert.equal(await page.locator('#fmnav-tree').count(),1);
  report.checks.push('Actual native Close releases the popup and restores the real underlying screen');
  await phase('ordinary-dialog');
  const holiday=page.locator('#fmnav-tree [role=treeitem][data-uid="135301632"]').first();assert.equal(await holiday.count(),1);
  const search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await holiday.locator(':scope>span').textContent());await holiday.focus();const holidayId=await holiday.getAttribute('id');
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');
  assert.match(await(await response).text(),/^Opening /);await poll(async()=>await overlay().count()===1,'holiday popup');await settled();
  assert.equal(await page.locator('#fmnav-tree').count(),0);assert.equal(await page.evaluate(()=>!!document.activeElement.closest('[data-fm-native-overlay],[data-fm-native-dialog]')),true);
  const cancel=page.getByRole('button',{name:'Cancelar',exact:true});assert.equal(await cancel.count(),1);
  await phase('ordinary-close');await activate(cancel);await poll(async()=>await overlay().count()===0,'holiday Cancel');await settled();
  assert.equal(await page.evaluate(()=>document.activeElement.id),holidayId);await search.fill('');
  report.checks.push('Ordinary native holiday popup suppresses its background; Cancel restores the exact navigator opener without starting a holiday');
  await phase('restore-tutorial');
  const row=page.locator('tr[data-fm-message-id="8"] button[onclick^="FM.msgview.open"]');assert.equal(await row.count(),1);
  const opening=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');await row.focus();await page.keyboard.press('Enter');
  assert.equal(await(await opening).text(),'');await poll(()=>page.locator('[data-fm-message-view-id="8"]').count(),'original squad message');await settled();
  await activate(page.getByRole('button',{name:'Continuar introdução',exact:true}));
  await poll(async()=>await overlay().count()===1,'reopened original tutorial');await settled();report.reopenedCounter=await counter();
  assert.deepEqual(report.reopenedCounter,report.initialCounter);
  report.checks.push('Original native squad tutorial reopens at its retained checkpoint, without completion, skip, save or simulation');
  await Promise.all(captures);
  report.focus=await page.evaluate(()=>qaPopupFocus);
  assert.deepEqual(report.focus.filter(f=>f.phase==='tutorial-step'&&!f.insidePopup),[],'No background focus during any tutorial transition');
  assert.deepEqual(report.bodies.filter(b=>b.phase==='tutorial-step'&&!b.popup),[],'No background HTML during tutorial transitions');
  assert.equal(report.captureError,undefined);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);report.success=true;
 }catch(error){report.failure=error.stack;throw error;}
 finally{report.lastHtml=await page.locator('#fmroot').innerHTML().catch(()=>null);report.focus=await page.evaluate(()=>qaPopupFocus).catch(()=>null);
  fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));await c.browser.close();}
 console.log(JSON.stringify({success:report.success,steps:report.steps.length,checks:report.checks,bodies:report.bodies.length,
  backgroundFocus:report.focus.filter(f=>f.phase==='tutorial-step'&&!f.insidePopup).length}));
})().catch(error=>{console.error(error);process.exitCode=1;});
