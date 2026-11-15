// An explicit proposal on the pristine, separately owned July2 contract QA.
// Native pending-offer evidence is not player acceptance. No Save or simulation.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'artifacts/tests/web');
const owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
const checkpoint=owned.contractWorking;
assert.ok(process.argv.includes('--propose'),'Explicit --propose is required for this real owned-QA contract action');
assert.ok(checkpoint&&owned.ownedWorkingFiles.includes(checkpoint+'.fm'));
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,checks:[],audits:[],commands:[],offerSubmitted:false,playerAccepted:false,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const output=path.join(out,'contract-renewal.json');
 function persist(){fs.writeFileSync(output,JSON.stringify(report,null,2));}
 async function action(control,key='Enter',expected=/^Activated /){
  assert.equal(await control.count(),1);assert.ok(await control.isEnabled());
  const item={at:new Date().toISOString(),id:await control.getAttribute('id'),name:await control.textContent(),uncertainBeforeAwait:true};
  report.commands.push(item);persist();await control.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');response.catch(()=>{});
  await page.keyboard.press(key);const actual=await response;item.reply=await actual.text();item.uncertainBeforeAwait=false;persist();
  assert.equal(actual.status(),200);assert.match(item.reply,expected);await settled();return actual;
 }
 async function audit(stage){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 const button=name=>page.getByRole('button',{name,exact:true});
 const field=name=>page.getByRole('combobox',{name,exact:true});
 async function contractMenu(){await action(button('Acções'),'Enter',/^Opened menu$/);assert.equal(await page.getByRole('menu').count(),1);await page.getByRole('menuitem',{name:'Contrato',exact:true}).focus();await page.keyboard.press('ArrowRight');await settled();}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await action(button('Cancelar'));
  await c.dropdown('FM Menu');await c.choose('Preferências');assert.equal(await page.getByRole('checkbox',{name:'Usar Gravação Automática',exact:true}).isChecked(),false);await action(button('Close'));
  await page.keyboard.press('Alt+1');await poll(()=>page.getByRole('heading',{name:'Mensagens',exact:true}).count(),'native Inbox');await c.newestMessages();await settled();
  const news=page.locator('tr[data-fm-message-id="150740"] button[onclick^="FM.msgview.open"]');assert.equal(await news.count(),1);
  const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');opened.catch(()=>{});await news.focus();await page.keyboard.press('Enter');assert.equal(await(await opened).text(),'');await poll(()=>page.locator('[data-fm-message-view-id="150740"]').count(),'actual failed-delegation message');await settled();
  assert.match(await page.locator('#fmroot').innerText(),/Maddy Earl[\s\S]*cancelou as negociações/);
  await action(button('Maddy Earl').first());await poll(async()=>await page.title()==='Relatório de jogador','actual Maddy profile');await settled();
  report.originalProfile=await page.locator('#fmroot').innerText();assert.match(report.originalProfile,/Maddy Earl/);await contractMenu();
  assert.equal(await page.getByRole('menuitem',{name:'Alterar Proposta de Contrato',exact:true}).count(),0,'Never resubmit an already pending offer');
  await action(page.getByRole('menuitem',{name:'Iniciar negociações contratuais',exact:true}));
  assert.equal(await page.title(),'Iniciar negociações contratuais?');assert.equal(await page.getByRole('checkbox',{name:'Não mostrar esta mensagem de novo.',exact:true}).isChecked(),false);await audit('Native negotiation confirmation');await action(button('Sim'));
  await poll(()=>button('Finalizar Promessas').count(),'actual preliminary promises');await settled();
  assert.match(await page.locator('#fmroot').innerText(),/Liam Clark/);report.promisedPlayingTime=await field('Tempo de Jogo').textContent();report.promisedStartDate=await field('Data de início do contrato').textContent();
  const origin=await field('Tempo de Jogo').getAttribute('id');await c.dropdown('Tempo de Jogo');
  const requests=(await page.requests()).filter(r=>new URL(r.url()).pathname==='/dropdown/open');report.playingTimeOptionsHtml=await(await requests.at(-1).response()).text();
  await c.cancelDropdown();await settled();assert.equal(await field('Tempo de Jogo').textContent(),report.promisedPlayingTime);assert.equal(await page.evaluate(()=>document.activeElement?.id),origin);await audit('Native promise selector cancel and exact focus');
  report.checks.push('Actual preliminary promise values and native playing-time options are readable; cancel preserves value and exact focus without changing preferences');
  const finalized=await action(button('Finalizar Promessas'));await poll(()=>button('Negociar Contrato').count(),'actual agent preliminary agreement');await settled();
  const stale=new URL(finalized.url());assert.equal(await page.locator('#'+stale.searchParams.get('id')).count(),0);const before=await page.locator('#fmroot').innerText();
  report.stalePhaseReply=await page.evaluate(async url=>await(await fetch(url,{method:'POST'})).text(),stale.pathname+stale.search);assert.doesNotMatch(report.stalePhaseReply,/^Activated /);assert.equal(await page.locator('#fmroot').innerText(),before);
  report.checks.push('A retained completed-phase command is refused rather than retargeted to the new negotiation action');
  await action(button('Negociar Contrato'));await poll(()=>button('Finalizar Acordo').count(),'actual native financial demand');await settled();
  report.agentDemand={salary:await field('Ordenado').textContent(),loyalty:await field('Prémio de lealdade').textContent(),agent:await field('Comissão de agente').textContent(),duration:await field('Duração do contrato').textContent()};
  assert.ok(Object.values(report.agentDemand).every(Boolean));report.financialText=await page.locator('#fmroot').innerText();await audit('Actual requested salary, duration, bonuses and negotiability');
  await action(button('Finalizar Acordo'));await poll(()=>button('Sair').count(),'actual agent final-decision message');await settled();report.pendingText=await page.locator('#fmroot').innerText();
  assert.match(report.pendingText,/decisão final do meu cliente/);report.offerSubmitted=true;persist();await audit('Native pending player decision');await action(button('Sair'));
  await poll(async()=>await page.title()==='Relatório de jogador','native negotiation exit');await settled();await contractMenu();
  assert.equal(await page.getByRole('menuitem',{name:'Alterar Proposta de Contrato',exact:true}).count(),1);assert.equal(await page.getByRole('menuitem',{name:'Retirar a Oferta de Contrato',exact:true}).count(),1);report.nativePendingMenu=await page.getByRole('menu').innerText();await page.keyboard.press('Escape');await settled();await audit('Native player profile retains pending offer actions');
  report.checks.push('One accepted-by-agent proposal creates actual native Change/Withdraw Contract Offer actions; final player acceptance remains a separate required observation');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
  console.log(JSON.stringify({success:true,module:report.health.coreMvid,groups:report.checks.length,audits:report.audits.length,offerSubmitted:true,playerAccepted:false}));
 }catch(error){report.failure=error.stack;report.body=await page.locator('#fmroot').innerText().catch(()=>null);throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;persist();await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
