// Actual reply to the recorded QA counteroffer. One permitted native acceptance.
// No simulation, Save, invented reply, hidden provider writes or command replay.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),args=process.argv.slice(2);
const checkpoint=args.find(a=>a.startsWith('--checkpoint='))?.slice(13);
const owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
assert.equal(args.includes('--commit'),true,'Explicit native acceptance is required');
assert.equal(checkpoint,owned.releaseCounterofferWorking);assert.equal(checkpoint,owned.currentWorking);assert.ok(owned.ownedWorkingFiles.includes(checkpoint+'.fm'));
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,checks:[],audits:[],submission:'Not dispatched',saveSubmitted:false,simulationAdvanced:false,loanCompletionInferred:false,manualNVDA:false};
 const output=()=>fs.writeFileSync(path.join(root,'artifacts/tests/web/career-loan-reply-'+(report.success?'passed':'failure')+'.json'),JSON.stringify(report,null,2));
 async function audit(stage){const r=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:r.violations.map(v=>v.id)});assert.equal(r.violations.length,0);}
 async function inbox(){
  if(await page.locator('[data-fm-message-view-id]').count())await activate(page.getByRole('button',{name:'Close message',exact:true}));
  await page.keyboard.press('Alt+1');await poll(async()=>await page.title()==='Mensagens','actual native Inbox');await settled();
  const all=page.getByRole('tab',{name:'Tudo',exact:true});if(await all.getAttribute('aria-selected')!=='true')await activate(all);await c.newestMessages();await settled();
 }
 async function open(id){
  const action=page.locator('tr[data-fm-message-id="'+id+'"] button[onclick^="FM.msgview.open"]');assert.equal(await action.count(),1);await action.focus();
  const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');await page.keyboard.press('Enter');assert.equal(await(await r).text(),'');
  await poll(()=>page.locator('[data-fm-message-view-id="'+id+'"]').count(),'exact native reply reading pane');await settled();assert.equal(await page.title(),'London City tenta empréstimo de Cerci');
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  const saveProof=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-release-f18-counteroffer-working-save.json'),'utf8'));
  assert.equal(saveProof.success,true);assert.equal(saveProof.name,checkpoint);assert.equal(saveProof.health.coreMvid,report.health.coreMvid);
  if(await page.locator('[data-fm-message-view-id]').count())await activate(page.getByRole('button',{name:'Close message',exact:true}));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await c.dropdown('FM Menu');await c.choose('Preferências');assert.equal(await page.getByRole('checkbox',{name:'Usar Gravação Automática',exact:true}).isChecked(),false);await activate(page.getByRole('button',{name:'Close',exact:true}));
  report.date=await page.locator('#fmroot button').nth(1).textContent();assert.match(report.date,/3\/2\/2027/);await inbox();
  const replies=await page.locator('tr[data-fm-message-id]').evaluateAll(rs=>rs.filter(r=>r.querySelector('button[onclick^="FM.msgview.open"]')?.textContent==='London City tenta empréstimo de Cerci'&&r.dataset.fmMessageId!=='80448').map(r=>({id:r.dataset.fmMessageId,status:r.cells[0].textContent,sent:r.cells[3].textContent})));
  report.actualReplies=replies;assert.equal(replies.length,1,'One actual reply from the intended club, not the earlier processed proposal');report.messageId=replies[0].id;
  await open(report.messageId);report.before=await page.locator('#fmroot').innerText();assert.match(report.before,/Selina Cerci/);assert.match(report.before,/London City/);assert.match(report.before,/Deve Responder/);assert.match(report.before,/8,25m/);
  assert.equal(await page.getByRole('button',{name:'Aceitar',exact:true}).isEnabled(),true);report.pendingVerifiedThroughNativeContentAndEnabledAction=true;report.listStateBoundary='A native priority transfer reply need not carry the blocking response-needed flag; preserve both distinct states';await audit('Actual pending native counterparty reply and financial terms');
  report.checks.push('Native date, owned filename, autosave-off state and unique actual pending club/player reply verified; real returned terms retained');
  await page.getByRole('button',{name:'Aceitar',exact:true}).focus();const accepted=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');report.submission='Outcome not yet observed';output();
  await page.keyboard.press('Enter');const r=await accepted;assert.equal(r.status(),200);report.nativeReply=await r.text();assert.match(report.nativeReply,/^Activated /);await settled();
  await inbox();await open(report.messageId);report.after=await page.locator('#fmroot').innerText();assert.match(report.after,/Tratado/);assert.match(report.after,/O Arsenal aceitou a proposta por Cerci/);assert.equal(await page.getByRole('button',{name:'Aceitar',exact:true}).count(),0);assert.equal(await page.getByRole('button',{name:'Ver Prop.',exact:true}).count(),0);await audit('Reopened exact native reply confirms actual acceptance');
  report.submission='One native acceptance confirmed in reopened exact NewsItem';report.checks.push('Exactly one native Aceitar changes the real reply to processed/Arsenal accepted and removes response controls; loan completion is not inferred');
  await page.keyboard.press('Escape');await poll(()=>page.evaluate(id=>document.activeElement?.closest('tr[data-fm-message-id]')?.dataset.fmMessageId===id,report.messageId),'exact processed reply row focus');await audit('Actual accepted reply row return');report.checks.push('Escape returns to the exact accepted reply record');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,report.health.coreMvid);report.success=true;
 }catch(error){report.failure=error.stack;report.body=await page.locator('#fmroot').innerText().catch(()=>null);throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;output();await c.browser.close();}
 console.log(JSON.stringify({success:report.success,message:report.messageId,checks:report.checks.length,cleanAxe:report.audits.length,submission:report.submission}));
})().catch(error=>{console.error(error);process.exitCode=1;});
