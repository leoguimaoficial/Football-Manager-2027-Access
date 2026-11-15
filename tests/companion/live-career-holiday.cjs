// Real production document and native holiday form. Default: edit/cancel only.
// Explicit --commit advances only the exact separately owned QA checkpoint.
// No private native eval, fixture server, save overwrite or automatic command retry.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {default:AxeBuilder}=require('@axe-core/playwright');
const args=process.argv.slice(2),checkpoint=args.find(a=>a.startsWith('--checkpoint='))?.slice(13);
assert.match(checkpoint||'',/^FM27Access QA Career (?:Coverage|Arrival) \d+$/,'Supply the exact isolated working checkpoint');
const commit=args.includes('--commit'),days=Number(args.find(a=>a.startsWith('--days='))?.slice(7)||3);
const quarter=args.includes('--career-quarter');
const month=args.includes('--career-month');
assert.ok(!(quarter&&month),'Choose one explicit progression boundary');
const requireSetPieceCategory=args.includes('--require-set-piece-category');
// Short native turns can finish without any processing-news rows being sampled.
// Calendar-only coverage never qualifies that separate message-content contract.
const calendarOnly=args.includes('--calendar-only');
assert.ok(!(calendarOnly&&requireSetPieceCategory),'Calendar-only coverage cannot require processing-category evidence');
assert.ok(Number.isInteger(days)&&days>=1&&days<=(quarter?120:month?31:7),'Supply an explicit bounded diagnostic duration');
if(quarter||month)assert.equal(commit,true,'Extended progression requires an explicit commit');
if(commit){
 const owned=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../../artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
 assert.equal(owned.success,true);assert.ok(owned.ownedWorkingFiles.includes(checkpoint+'.fm'),'Only an explicitly recorded working copy may be advanced');
 if(checkpoint.includes(' Career Arrival ')){
  assert.equal(owned.arrivalWorking,checkpoint);
  const proof=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../../artifacts/career-january-working-save.json'),'utf8').replace(/^\uFEFF/,''));
  assert.equal(proof.success,true);assert.equal(proof.name,checkpoint);
  assert.equal(proof.loadingCompleted,true);assert.equal(proof.verifiedCurrentNativeFilename,true);
 }else assert.equal(owned.currentWorking,checkpoint);
}
const output=path.resolve(__dirname,'../../artifacts/tests/web/career-holiday'+(quarter?'-quarter':month?'-month':'')+(commit?'-commit':'')+'.json');
const clean=s=>s.replace(/\s+/g,' ').trim();
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,activate,poll,settled}=c;
 const checks=[],audits=[],observations={checkpoint,commit,days,quarter,month,requireSetPieceCategory,calendarOnly},report={success:false,observations,checks,audits,manualNVDA:false,simulationAdvanced:false,confirmationSubmitted:false};
 let opened=false;
 async function audit(label){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({label,violations:a.violations.map(v=>v.id)});assert.equal(a.violations.length,0);}
 async function open(){
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/holiday');
  await page.keyboard.press('Alt+h');assert.equal(await(await response).text(),'');
  await poll(async()=>await page.title()==='Ir de férias','actual native holiday form');await settled();opened=true;
  assert.equal(await page.evaluate(()=>document.activeElement?.id),'fmsec0-h');
 }
 async function close(){await activate(page.getByRole('button',{name:'Cancelar',exact:true}));await poll(async()=>await page.title()!=='Ir de férias','native holiday cancellation');await settled();opened=false;}
 async function nativeDays(value){
  const field=page.getByRole('spinbutton',{name:'Days on vacation',exact:true});await poll(()=>field.isEnabled(),'native days mode enabled');
  const unchanged=await field.inputValue()===String(value);
  if(!unchanged){
   const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/setvalue');
   await field.fill(String(value));await field.press('Tab');assert.equal(await(await response).text(),'Set to '+value);
  }
  (observations.nativeDayValues??=[]).push({value,commitDispatched:!unchanged});
  await poll(()=>page.getByRole('radio',{name:'Voltar de férias depois de:, '+value+', dia(s)',exact:true}).isChecked(),'native return mode reflects committed days');await settled();
  assert.equal(await field.inputValue(),String(value));
 }
 async function snapshot(){return page.locator('input,select').evaluateAll(es=>es.map(e=>({type:e.type,label:e.getAttribute('aria-label')||[...e.labels||[]].map(l=>l.textContent).join(' '),value:e.value,checked:e.checked,disabled:e.disabled})));}
 try{
  observations.health=JSON.parse(await c.fetchText('/health'));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const filename=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>filename.count(),'native current checkpoint identity');assert.equal(await filename.inputValue(),checkpoint);
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const originalDate=clean(await page.locator('#fmroot button').nth(1).textContent());observations.originalDate=originalDate;
  const date=originalDate.match(/, (\d{1,2})\/(\d{1,2})\/(\d{4}),/);assert.ok(date,'Actual localized native calendar date');
  const expectedDate=new Date(Date.UTC(Number(date[3]),Number(date[2])-1,Number(date[1])+days));
  observations.expectedReturnDate=[expectedDate.getUTCDate(),expectedDate.getUTCMonth()+1,expectedDate.getUTCFullYear()].join('/');
  await open();observations.original=await snapshot();await audit('Native holiday initial form and heading focus');
  const dateMode=page.getByRole('radio',{name:/^Voltar de férias na data:/}),afterMode=page.getByRole('radio',{name:/^Voltar de férias depois de:/});
  const number=page.getByRole('spinbutton',{name:'Days on vacation',exact:true});assert.equal(await dateMode.isChecked(),true);assert.equal(await number.isDisabled(),true);
  await activate(afterMode,'Space');
  const draftDays=days===2?3:2;
  observations.cancelledDraftDays=draftDays;
  await nativeDays(draftDays);observations.edited=await snapshot();await audit('Native committed days and return radio label');
  await activate(dateMode,'Space');await poll(()=>number.isDisabled(),'date mode really disables day editor');assert.equal(await dateMode.isChecked(),true);
  await activate(page.getByRole('radio',{name:'Ir de férias indefinidamente',exact:true}),'Space');await poll(()=>page.getByRole('radio',{name:'Ir de férias indefinidamente',exact:true}).isChecked(),'native indefinite mode');
  assert.equal(await number.isDisabled(),true);await close();
  checks.push('Alt+H opens the native modal with heading focus; three native return modes, numeric commit and mode-specific availability work, then Cancel returns without simulation');
  await open();observations.reopened=await snapshot();assert.equal(await dateMode.isChecked(),true);await audit('Cancelled native holiday reopened without applying its draft');
  checks.push('Reopening resets the cancelled draft to native date mode; native radios remain distinct from navigation tabs');
  if(commit){
   // Never change global autosave as a hidden test side effect. The explicit
   // working-career owner must disable it through native Preferences first.
   await close();await c.dropdown('FM Menu');await c.choose('Preferências');
   const autosave=page.getByRole('checkbox',{name:'Usar Gravação Automática',exact:true});await poll(()=>autosave.count(),'native autosave safety state');assert.equal(await autosave.isChecked(),false,'Disable autosave explicitly before simulation; originals stay protected');
   await activate(page.getByRole('button',{name:'Close',exact:true}));await open();await activate(afterMode,'Space');await nativeDays(days);
   report.confirmationSubmitted='Outcome not yet observed';
   // Do not wait for a stable body after this command: an advancing simulation
   // can stay unstable until it has already finished. Start observing its
   // actual phase immediately after the one native dispatch acknowledgement.
   await settled();await page.getByRole('button',{name:'Confirmar férias',exact:true}).focus();
   const confirmResponse=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');
   // Preserve uncertainty before the sole native command, including if this
   // worker is interrupted before finally runs. Never inherit an older result.
   fs.writeFileSync(output,JSON.stringify(report,null,2));
   await page.keyboard.press('Enter');const confirmation=await confirmResponse;assert.equal(confirmation.status(),200);assert.match(await confirmation.text(),/^Activated /);
   opened=false;report.confirmationSubmitted=true;
   fs.writeFileSync(output,JSON.stringify(report,null,2));
   // An acknowledged command is not completion. Observe the game leave its
   // native processing surface and restore the actual interactive career.
   const simulationStarted=Date.now(),deadline=simulationStarted+((quarter||month)?900000:180000);observations.processingMessages=[];
   let readableMessages=false,processingAudit=false,lastSampleAt=0,processingSeen=false,lastProgressAt=0;
   observations.setPieceCategorySamples=0;
   while(Date.now()<deadline){
    const phase=await page.title();
    if(Date.now()-lastProgressAt>=15000){lastProgressAt=Date.now();console.log(JSON.stringify({phase,simulationSeconds:Math.round((Date.now()-simulationStarted)/1000),readableMessages,setPieceCategoryObservations:observations.setPieceCategorySamples}));fs.writeFileSync(output,JSON.stringify(report,null,2));}
    if(phase==='Processamento')processingSeen=true;
    if(processingSeen&&phase!=='Processamento'&&await page.getByRole('combobox',{name:'FM Menu',exact:true}).count())break;
    if(Date.now()-lastSampleAt>=750){
     lastSampleAt=Date.now();
     const snapshot=await page.evaluate(()=>({title:document.title,setPieceCategories:[...document.querySelectorAll('#fmroot p')].filter(e=>e.textContent.trim()==='Set-piece training').length,rows:[...document.querySelectorAll('#fmroot tr[data-fm-message-id]')].map(e=>({id:e.dataset.fmMessageId,cells:[...e.cells].map(c=>c.textContent.trim()),subjectControls:e.cells[1]?.querySelectorAll('button,a[href],input,[role=button]').length||0}))}));
     if(snapshot.title==='Processamento')observations.setPieceCategorySamples+=snapshot.setPieceCategories;
     const rows=snapshot.rows;
     if(snapshot.title==='Processamento'&&rows.length){
      const unreadable=rows.filter(r=>!r.cells[1]||r.cells[1]==="couldn't read").length;
      const sample={at:new Date().toISOString(),rows,unreadable};
      if(observations.processingMessages.length<12)observations.processingMessages.push(sample);
      if(!unreadable&&rows.every(r=>r.cells[2]&&r.cells[3])){
       assert.ok(rows.every(r=>r.subjectControls===0),'Native processing previews have no command/callback and must remain informative cells, not no-op buttons');
       readableMessages=true;observations.completeProcessingMessageSample=sample;
       if(!processingAudit){await audit('Actual processing messages: complete native subjects, senders and dates');processingAudit=true;}
      }
     }
    }
    await page.waitForTimeout(150);
   }
   assert.notEqual(await page.title(),'Processamento','Native processing must finish');await settled();
   assert.equal(processingSeen,true,'The real native processing phase must be observed, not inferred from dispatch');
   observations.returnDate=clean(await page.locator('#fmroot button').nth(1).textContent());
   assert.ok(observations.returnDate.includes(', '+observations.expectedReturnDate+','),'Native return must match the explicitly selected calendar duration');report.simulationAdvanced=true;
   observations.processingMessageCoverage=readableMessages?'Actual complete native subjects, senders and dates observed':'Not exercised: no complete native processing-message sample observed';
   assert.ok(readableMessages||(calendarOnly&&observations.processingMessages.length===0),'Actual processing messages must expose full subjects, senders and dates; strict coverage requires an observed sample');
   if(requireSetPieceCategory)assert.ok(observations.setPieceCategorySamples>0,'The actual native set-piece category must be observed through the production DOM, not inferred from compilation');
   await audit('Actual native holiday return');checks.push('Explicit native holiday confirmation advances the owned career and returns a changed native calendar date; delegated matches are not manual match evidence');
   if(readableMessages)checks.push('The actual in-progress native message list exposes complete subjects, senders and dates with record IDs, preserving its original four-column table');
   const origin=await page.locator('tr[data-fm-message-id]').evaluateAll(rs=>{
    const row=rs.find(r=>!r.cells[0]?.textContent.includes('response needed')&&r.cells[1]?.querySelector('button')&&r.cells[1].textContent.trim()!=="couldn't read");
    return row?{messageId:row.dataset.fmMessageId,id:row.cells[1].querySelector('button').id,subject:row.cells[1].textContent.trim()}:null;
   });
   assert.ok(origin,'Returning to the actual Portal retains actionable native inbox messages');observations.returnMessage=origin;
   const openedResponse=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');
   await page.locator('#'+origin.id).focus();await page.keyboard.press('Enter');assert.equal((await openedResponse).status(),200);
   await poll(async()=>await page.title()===origin.subject&&await page.getByRole('button',{name:'Close message',exact:true}).count()===1,'actual returning inbox message and native reading pane');await settled();
   await audit('Real actionable inbox after native read-only processing previews');
   await page.keyboard.press('Escape');await poll(()=>page.evaluate(id=>document.activeElement?.closest('tr')?.dataset.fmMessageId===id,origin.messageId),'original message record focus after closing');await settled();await audit('Inbox record focus after native message close');
   checks.push('After processing, a genuine native inbox message still opens its verified reading pane and Escape returns focus to the exact original message record');
  }else{
   await close();
   assert.equal(clean(await page.locator('#fmroot button').nth(1).textContent()),originalDate);
  }
  if(opened)await close();
  assert.equal(c.errors.length,0);assert.equal(c.requestFailures.length,0);assert.equal(c.network.failures.length,0);report.success=true;
 }catch(error){report.failure=error.stack;report.body=await page.locator('#fmroot').innerText().catch(()=>null);throw error;}
 finally{
  // Only a known unsubmitted draft is safe to dismiss after a harness failure.
  // An uncertain/completed native confirmation must never be cancelled/replayed.
  if(opened&&report.confirmationSubmitted===false){
   try{if(await page.title()==='Ir de férias'){await close();observations.unsubmittedDraftCancelled=true;}}
   catch(error){observations.draftCleanupError=error.message;}
  }
  report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;fs.mkdirSync(path.dirname(output),{recursive:true});fs.writeFileSync(output,JSON.stringify(report,null,2));await c.browser.close();
 }
 console.log(JSON.stringify({success:true,checks:checks.length,cleanAxe:audits.length,commit,days}));
})().catch(error=>{console.error(error);process.exitCode=1;});
