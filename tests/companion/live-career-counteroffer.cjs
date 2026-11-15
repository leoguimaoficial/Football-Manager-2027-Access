// Native business action in an explicitly owned, restored February checkpoint.
// One submission only; failure never retries an uncertain transfer command.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {default:AxeBuilder}=require('@axe-core/playwright');
const args=process.argv.slice(2),root=path.resolve(__dirname,'../..');
const checkpoint=args.find(x=>x.startsWith('--checkpoint='))?.slice(13);
assert.ok(args.includes('--commit'),'Explicit --commit is required for the native counteroffer');
const owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
assert.equal(checkpoint,owned.februaryWorking);assert.ok(owned.ownedWorkingFiles.includes(checkpoint+'.fm'));
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,settled,poll,activate}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,checks:[],audits:[],submission:'Not dispatched',saveSubmitted:false,manualNVDA:false};
 async function audit(stage){const r=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:r.violations.map(v=>v.id)});assert.equal(r.violations.length,0);}
 async function openMessage(){
  await page.keyboard.press('Alt+1');
  await poll(async()=>await page.title()==='Mensagens','actual native Inbox after shortcut');await settled();
  const all=page.getByRole('tab',{name:'Tudo',exact:true});
  if(await all.getAttribute('aria-selected')!=='true')await activate(all);
  await c.newestMessages();
  const row=page.locator('tr[data-fm-message-id="80448"]');assert.match(await row.innerText(),/London City.*Cerci/);
  await row.getByRole('button').first().focus();const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');
  await page.keyboard.press('Enter');assert.equal((await r).status(),200);await settled();
  assert.equal(await page.title(),'London City tenta empréstimo de Cerci');
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  if(await page.locator('[data-fm-message-view-id]').count()){
   await activate(page.getByRole('button',{name:'Close message',exact:true}));
   await poll(async()=>await page.locator('[data-fm-message-view-id]').count()===0,'preceding message closed before native filename guard');
  }
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await c.dropdown('FM Menu');await c.choose('Preferências');
  assert.equal(await page.getByRole('checkbox',{name:'Usar Gravação Automática',exact:true}).isChecked(),false);
  await activate(page.getByRole('button',{name:'Close',exact:true}));
  report.checks.push('Exact recorded owned native filename and autosave-off state verified without Save');
  await openMessage();assert.match(await page.locator('#fmroot').innerText(),/Deve Responder/);
  await activate(page.getByRole('button',{name:'Ver Prop.',exact:true}));
  assert.equal(await page.title(),'Prop. de Transferência');assert.match(await page.locator('#fmroot').innerText(),/Selina Cerci/);
  assert.equal(await page.getByRole('button',{name:'Undo',exact:true}).isDisabled(),true,'A restored, clean draft is required; never replay a previous submission');
  const term=page.getByRole('row').filter({has:page.getByRole('rowheader',{name:'Taxa mensal por utilização',exact:true})});
  assert.match(await term.innerText(),/8m\s*€/);await audit('Original pending loan terms');
  const step=page.waitForResponse(r=>new URL(r.url()).pathname==='/step');
  await term.getByRole('button',{name:'increase',exact:true}).focus();await page.keyboard.press('Enter');
  const stepped=await step;assert.equal(stepped.status(),200);assert.match(await stepped.text(),/8,25m/);await settled();
  assert.match(await term.innerText(),/8,25m/);assert.equal(await page.getByRole('button',{name:'Undo',exact:true}).isEnabled(),true);
  assert.equal(await page.getByRole('button',{name:'Aceitar Proposta',exact:true}).count(),0);await audit('Real changed draft and native negotiation commands');
  report.draft=await page.locator('#fmroot').innerText();report.checks.push('One actual native monetary step changes8m to8.25m and replaces acceptance with negotiation');
  await settled();await page.getByRole('button',{name:'Negociar Proposta',exact:true}).focus();
  const sent=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');
  report.submission='Outcome uncertain until response and reopened native message';await page.keyboard.press('Enter');
  const response=await sent;assert.equal(response.status(),200);report.reply=await response.text();assert.match(report.reply,/^Activated /);
  await settled();await openMessage();
  report.result=await page.locator('#fmroot').innerText();assert.match(report.result,/Tratado/);
  assert.match(report.result,/O Arsenal decidiu negociar a proposta por Cerci/);assert.match(report.result,/8,25m/);
  assert.equal(await page.getByRole('button',{name:'Ver Prop.',exact:true}).count(),0);
  assert.equal(await page.getByRole('button',{name:'Aceitar',exact:true}).count(),0);await audit('Reopened processed native offer message');
  report.submission='Confirmed in exact native message80448';report.checks.push('Exactly one counteroffer command results in native processed state, changed terms and removal of response controls');
  await page.keyboard.press('Escape');await poll(()=>page.evaluate(()=>document.activeElement?.closest('tr')?.dataset.fmMessageId==='80448'),'exact processed message row focus');
  report.checks.push('Escape returns to the exact processed message record');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  report.success=true;
 }catch(error){report.failure=error.stack;report.body=await page.locator('#fmroot').innerText().catch(()=>null);throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;
  fs.mkdirSync(path.join(root,'artifacts/tests/web'),{recursive:true});
  fs.writeFileSync(path.join(root,'artifacts/tests/web/career-counteroffer-'+(report.success?'passed':'failure')+'.json'),JSON.stringify(report,null,2));await c.browser.close();}
 console.log(JSON.stringify({success:report.success,checks:report.checks.length,cleanAxe:report.audits.length,submission:report.submission}));
})().catch(error=>{console.error(error);process.exitCode=1;});
