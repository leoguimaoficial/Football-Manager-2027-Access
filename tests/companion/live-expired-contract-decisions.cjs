// Public production UI over the explicitly owned July1 checkpoint. These native
// news choices are one-shot decisions, not draft preferences. --commit is required.
// This test does not infer a completed release/renewal from a processed decision.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..');
assert.ok(process.argv.includes('--commit'),'Explicitly authorize the owned-career decisions');
const owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
const checkpoint=owned.julyWorking,messageId='148239';
assert.ok(checkpoint&&owned.ownedWorkingFiles.includes(checkpoint+'.fm'));
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,activate,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,messageId,checks:[],audits:[],decisions:[],saveSubmitted:false,simulationAdvanced:false,completedReleaseOrRenewalClaimed:false,manualNVDA:false};
 const output=path.join(root,'artifacts/tests/web/expired-contract-decisions.json');
 const button=name=>page.getByRole('button',{name,exact:true});
 const row=name=>page.getByRole('table').locator('tbody tr').filter({has:button(name)});
 const persist=()=>fs.writeFileSync(output,JSON.stringify(report,null,2));
 async function audit(stage){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations});assert.equal(a.violations.length,0,stage);}
 async function openNews(){
  const item=page.locator('tr[data-fm-message-id="'+messageId+'"] button[onclick^="FM.msgview.open"]');assert.equal(await item.count(),1);
  await item.focus();const reply=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');reply.catch(()=>{});await page.keyboard.press('Enter');assert.equal(await(await reply).text(),'');
  await poll(()=>page.locator('[data-fm-message-view-id="'+messageId+'"]').count(),'exact expired-contract news');await settled();
 }
 async function decide(name,action,expected){
  const selected=row(name).getByRole('checkbox');assert.equal(await selected.isChecked(),false);
  await activate(selected,'Space');await poll(()=>selected.isChecked(),'actual selected '+name);assert.equal(await button(action).isEnabled(),true);
  const observation={name,action,before:await row(name).innerText(),submitted:'Outcome not yet observed'};report.decisions.push(observation);persist();
  observation.reply=await activate(button(action));observation.submitted=true;persist();
  await poll(async()=>await row(name).getByRole('combobox').count()===0,'native decision replaces selector for '+name);
  assert.match(await row(name).innerText(),expected);assert.equal(await selected.isChecked(),false);observation.after=await row(name).innerText();
  // The game consumes the decision and resets row checks, but can retain its
  // action-group availability until the report is reopened. Preserve native
  // state rather than demanding an immediate artificial disabled transition.
  observation.actionAvailabilityAfter=await page.locator('#fmroot button').evaluateAll(es=>es.slice(-4).map(e=>({name:e.innerText,disabled:e.disabled})));persist();
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await activate(button('Cancelar'));
  await page.keyboard.press('Alt+1');await poll(async()=>await page.title()==='Mensagens','actual Inbox');await settled();
  const all=page.getByRole('tab',{name:'Tudo',exact:true});if(await all.getAttribute('aria-selected')!=='true')await activate(all);await c.newestMessages();await openNews();
  assert.match(await page.locator('#fmroot').innerText(),/dezasseis jogadoras[\s\S]+numa base mensal/);
  assert.equal(await page.getByRole('table').locator('tbody tr').count(),16);assert.equal(await page.getByRole('table').getByRole('combobox').count(),16,'Refuse a previously processed checkpoint');
  report.headers=await page.getByRole('columnheader').allTextContents();assert.equal(report.headers.length,7);
  assert.equal(await button('Dispensar').isDisabled(),true);assert.equal(await button('Instruir DD a negociar renovação').isDisabled(),true);await audit('Sixteen real expired contracts and native no-selection availability');
  const kim=row('Kim Little').getByRole('combobox');await kim.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');opened.catch(()=>{});await page.keyboard.press('Enter');assert.equal((await opened).status(),200);await poll(()=>kim.getAttribute('data-loaded'),'actual recommendation options');
  report.recommendations=await kim.locator('option').allTextContents();assert.deepEqual(report.recommendations,['Propor Contrato','DD: Negociar Renovação de Contrato','Dispensar no final do contracto','Não oferecer contrato']);
  await c.cancelDropdown();assert.equal(await row('Kim Little').getByRole('combobox').count(),1);assert.equal(await page.evaluate(()=>document.activeElement?.getAttribute('aria-label')),'Acção Recomendada');
  report.checks.push('The actual native recommendation popup exposes four official choices; Escape cancels without consuming the decision and restores its origin');
  await row('Kim Little').getByRole('combobox').focus();const reopened=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');reopened.catch(()=>{});await page.keyboard.press('Enter');await reopened;
  const kimDecision={name:'Kim Little',action:'Não oferecer contrato',submitted:'Outcome not yet observed'};report.decisions.push(kimDecision);persist();kimDecision.reply=await c.choose('Não oferecer contrato');kimDecision.submitted=true;persist();
  await poll(async()=>await row('Kim Little').getByRole('combobox').count()===0,'native no-contract decision');assert.match(await row('Kim Little').innerText(),/Nenhuma decisão tomada/);kimDecision.after=await row('Kim Little').innerText();
  await decide('Katie Reid','Dispensar',/Dispensar no final do contracto/);
  await decide('Maddy Earl','Instruir DD a negociar renovação',/Delegar Negociações/);
  report.checks.push('Three distinct native decisions consume only their own selectors, expose their actual readonly result and reset row selection; actual native action availability is retained, not fabricated');
  const katieOrigin=await button('Katie Reid').getAttribute('id');await activate(button('Katie Reid'));await poll(async()=>await page.title()==='Relatório de jogador','actual Katie profile');await settled();report.profileBeforeProcessing=await page.locator('#fmroot').innerText();await audit('Actual player state before processing the decision');
  await activate(button('Back'));await poll(()=>page.locator('[data-fm-message-view-id="'+messageId+'"]').count(),'exact originating expiry message');await settled();assert.equal(await page.evaluate(()=>document.activeElement?.id),katieOrigin);
  report.checks.push('The processed decision still links the correct player; native Back restores the exact message/player opener, without inferring a club change');
  await page.keyboard.press('Escape');await poll(()=>page.evaluate(id=>document.activeElement?.closest('tr')?.dataset.fmMessageId===id,messageId),'exact Inbox message record focus');await settled();await openNews();
  for(const name of ['Kim Little','Katie Reid','Maddy Earl']){const decision=report.decisions.find(d=>d.name===name);assert.equal(await row(name).innerText(),decision.after);assert.equal(await row(name).getByRole('combobox').count(),0);}
  assert.equal(await page.getByRole('table').getByRole('combobox').count(),13);await audit('Reopened native news preserves the three independent decisions');
  report.checks.push('Escape restores the original Inbox record; reopening preserves all three consumed decisions and the thirteen untouched recommendation controls');
  await page.keyboard.press('Escape');await poll(()=>page.getByRole('combobox',{name:'FM Menu',exact:true}).count(),'career chrome restored');await settled();
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,report.health.coreMvid);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
  console.log(JSON.stringify({success:true,module:report.health.coreMvid,checks:report.checks.length,audits:report.audits.length,completedReleaseOrRenewalClaimed:false}));
 }catch(error){report.failure=error.stack;report.body=await page.locator('#fmroot').innerText().catch(()=>null);throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;persist();await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
