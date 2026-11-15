// Read-only proof of actual completed loan and independent native player state.
// No acceptance, simulation, Save, private evaluator or inferred completion.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),args=process.argv.slice(2),checkpoint=args.find(a=>a.startsWith('--checkpoint='))?.slice(13);
const owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
assert.ok([owned.releaseAcceptedLoanWorking,owned.releaseLoanProfileWorking].includes(checkpoint));assert.equal(checkpoint,owned.currentWorking);assert.ok(owned.ownedWorkingFiles.includes(checkpoint+'.fm'));
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,checks:[],audits:[],saveSubmitted:false,simulationAdvanced:false,businessCommandSubmitted:false,manualNVDA:false};
 const field=()=>page.getByRole('textbox',{name:'Procurar...',exact:true});
 async function audit(stage){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations.map(v=>v.id)});assert.equal(a.violations.length,0);}
 async function query(value){if(await field().inputValue()===value)return;await field().fill(value);const changed=page.waitForResponse(r=>new URL(r.url()).pathname==='/settext');await field().press('Tab');const r=await changed;assert.equal(r.status(),200);assert.match(await r.text(),/^Set to /);await settled();}
 async function observe(stage){(report.transitions??=[]).push(await page.evaluate(stage=>({stage,title:document.title,token:FMTOKEN,history:FM.core.historyIndex,focus:document.activeElement?.id,query:document.querySelector('[data-fm-message-query]')?.value,returnMessageId:FM.msgview.returnMessageId,viewObserved:FM.msgview.viewObserved,refreshAfterClose:FM.msgview.refreshAfterClose,searchReturn:FM.msgview.searchReturn}),stage));}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  if(await page.locator('[data-fm-message-view-id]').count())await activate(page.getByRole('button',{name:'Close message',exact:true}));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  report.date=await page.locator('#fmroot button').nth(1).textContent();assert.match(report.date,/6\/2\/2027/);
  await page.keyboard.press('Alt+1');await poll(async()=>await page.title()==='Mensagens','actual native Inbox');await settled();const all=page.getByRole('tab',{name:'Tudo',exact:true});if(await all.getAttribute('aria-selected')!=='true')await activate(all);
  report.originalSearchOpen=await field().count()===1;report.originalQuery=report.originalSearchOpen?await field().inputValue():null;
  if(!report.originalSearchOpen)await activate(page.getByRole('button',{name:'Search messages',exact:true}));await query('Cerci');await c.newestMessages();
  const completed=page.getByRole('button',{name:'Cerci emprestada à London City',exact:true});await poll(()=>completed.count(),'actual completed-loan news, not acceptance/contract-stage inference');assert.equal(await completed.count(),1);
  report.messageId=await completed.evaluate(e=>e.closest('tr').dataset.fmMessageId);report.beforeRow=await completed.locator('xpath=ancestor::tr').innerText();await completed.focus();
  const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');await page.keyboard.press('Enter');assert.equal(await(await opened).text(),'');await poll(()=>page.locator('[data-fm-message-view-id="'+report.messageId+'"]').count(),'exact actual loan-completed reading pane');await settled();
  report.news=await page.locator('#fmroot').innerText();assert.equal(await page.title(),'Cerci emprestada à London City');assert.match(report.news,/Selina Cerci/);assert.match(report.news,/London City/);await audit('Actual completed-loan news and details');report.checks.push('Actual completed-loan NewsItem is discovered through native search and opened by its current record identity; acceptance alone is not completion');
  await observe('Opened completed news before player link');await activate(page.getByRole('button',{name:'Selina Cerci',exact:true}).first());await poll(async()=>await page.locator('[data-fm-message-view-id]').count()===0&&/Por empréstimo:\s+London City/.test(await page.locator('#fmroot').innerText()),'independent native player loan club');await settled();await observe('Native linked player profile');
  report.profile=await page.locator('#fmroot').innerText();assert.match(report.profile,/Emprestado a outro clube/);assert.match(report.profile,/26-27[\s\S]*London City/);
  const parentRow=page.getByRole('row').filter({has:page.getByRole('rowheader',{name:'Clube-mãe',exact:true})}),loanRow=page.getByRole('row').filter({has:page.getByRole('rowheader',{name:'Por empréstimo:',exact:true})});
  assert.equal(await parentRow.count(),1,'Official parent-club caption labels its own native table row');assert.match(await parentRow.getByRole('cell').innerText(),/Arsenal/);assert.equal(await loanRow.count(),1,'Official loan caption labels its own native table row');assert.match(await loanRow.getByRole('cell').innerText(),/London City/);
  const playingTimeRow=page.getByRole('row').filter({has:page.getByRole('rowheader',{name:'Playing Time',exact:true})});assert.equal(await playingTimeRow.count(),1,'The actual FullContract.AgreedPlayingTime property is not a scouting requirement');assert.match(await playingTimeRow.getByRole('cell').innerText(),/Emprestado a outro clube/);
  await audit('Independent native Cerci profile confirms loan status, correctly associated clubs and career row');report.checks.push('Independent profile confirms the loan and career club; native Clube-mãe/Arsenal and Por empréstimo:/London City have correctly associated table rows');
  if(args.includes('--club-links')){
   for(const [caption,club,destination] of [['Clube-mãe','Arsenal','Arsenal'],['Por empréstimo:','London City','London City Lionesses']]){
    const row=page.getByRole('row').filter({has:page.getByRole('rowheader',{name:caption,exact:true})}),link=row.getByRole('button',{name:club,exact:true});
    assert.equal(await link.count(),1,'The original linked club remains an actual guarded native action');const origin=await link.getAttribute('id');
    await activate(link);await poll(async()=>await page.title()===destination,'actual full-name club destination '+destination);await settled();
    if(club==='London City')assert.match(await page.locator('#fmroot').innerText(),/Copperjax Community Stadium/);
    (report.nativeClubVisits??=[]).push({caption,linkName:club,destination:await page.title()});await audit('Banner native '+club+' link destination');
    await page.locator('[data-fm-history="back"]').press('Enter');await poll(async()=>await page.title()==='Relatório de jogador','actual Cerci profile restored from '+club);await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'exact original banner club-link focus');await settled();
    assert.equal(await page.getByRole('row').filter({has:page.getByRole('rowheader',{name:caption,exact:true})}).getByRole('button',{name:club,exact:true}).getAttribute('id'),origin);
   }
   report.checks.push('Both retained banner club links open their actual Arsenal/London City destinations and native Back restores the exact original link focus');
  }
  await activate(page.locator('[data-fm-history="back"]'));await poll(()=>page.locator('[data-fm-message-view-id="'+report.messageId+'"]').count(),'native Back restores the exact completed-loan news');await settled();await observe('Exact native news restored by Back');
  await page.keyboard.press('Escape');await observe('Message Escape dispatched');await poll(()=>page.evaluate(id=>document.activeElement?.closest('tr[data-fm-message-id]')?.dataset.fmMessageId===id,report.messageId),'completed-news exact original row focus');assert.equal(await field().inputValue(),'Cerci');await audit('Exact news record/query return after linked native profile');report.checks.push('Native profile Back and message Escape preserve the exact actual news record and native search query');
  if(report.originalSearchOpen)await query(report.originalQuery);else{await activate(page.getByRole('button',{name:'Close',exact:true}));await poll(async()=>await field().count()===0,'native search closed');assert.equal(await page.evaluate(()=>document.activeElement.id),await page.getByRole('button',{name:'Search messages',exact:true}).getAttribute('id'));}
  await audit('Original native search disclosure state restored');report.checks.push('Original native query/disclosure state is restored, including exact current opener focus when closing');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,report.health.coreMvid);report.success=true;
 }catch(error){report.failure=error.stack;report.body=await page.locator('#fmroot').innerText().catch(()=>null);throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;fs.writeFileSync(path.join(root,'artifacts/tests/web/career-loan-completion-'+(report.success?'passed':'failure')+'.json'),JSON.stringify(report,null,2));await c.browser.close();}
 console.log(JSON.stringify({success:report.success,message:report.messageId,checks:report.checks.length,cleanAxe:report.audits.length}));
})().catch(error=>{console.error(error);process.exitCode=1;});
