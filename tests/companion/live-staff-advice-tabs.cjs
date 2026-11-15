// Native advice surface in the protected initial Arsenal QA checkpoint.
// No advice acknowledgement/application, Save, transfer or simulation is submitted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),checkpoint='FM27Access QA Squads 20261108-mw8cp1b3';
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const out=path.join(root,'artifacts/tests/web/staff-advice-tabs.json'),report={at:new Date().toISOString(),success:false,checkpoint,states:[],audits:[],checks:[],saveSubmitted:false,simulationAdvanced:false,adviceApplied:false,manualNVDA:false};
 const categories=['Plantel','Tácticas','Recrutamento','Treino','Clube'];
 async function capture(stage){
  await settled();const tabs=await page.getByRole('tab').allTextContents();
  assert.deepEqual(tabs.slice(0,5),categories);assert.equal(tabs.length,7,'Only the current native action/observation pair belongs beside the five categories');
  assert.match(tabs[5],/^Acções \(\d+\)$/);assert.match(tabs[6],/^Observações \(\d+\)$/);
  const text=await page.locator('#fmroot').innerText();assert.doesNotMatch(text,/unlabeled icon button|\[unreadable\]|unknown control/i);
  const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations});assert.equal(a.violations.length,0,stage);
  const tables=await page.getByRole('table').evaluateAll(es=>es.map(e=>({headers:[...e.querySelectorAll('thead th')].map(x=>x.textContent),rows:[...e.querySelectorAll('tbody tr')].map(x=>x.innerText)})));
  report.states.push({stage,tabs,text,tables,radioCount:await page.getByRole('radio').count()});
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const actual=await page.getByRole('textbox',{name:'File name',exact:true}).inputValue();await activate(page.getByRole('button',{name:'Cancelar',exact:true}));assert.equal(actual,checkpoint);
  await page.keyboard.press('Alt+1');await poll(async()=>await page.title()==='Mensagens','actual Inbox');await settled();
  await activate(page.getByRole('button',{name:'Advice',exact:true}));await poll(async()=>await page.title()==='Reunião da equipa técnica','native StaffMeetingCard');await settled();
  for(const category of categories){
   await activate(page.getByRole('tab',{name:category,exact:true}));
   await poll(()=>page.getByRole('tab',{name:category,exact:true}).getAttribute('aria-selected').then(v=>v==='true'),'actual category '+category);
   for(const kind of ['Acções','Observações']){
    const tab=page.getByRole('tab',{name:new RegExp('^'+kind+' \\(\\d+\\)$')});assert.equal(await tab.count(),1);
    const name=await tab.textContent();assert.equal(await tab.isEnabled(),true,'This native QA permits empty-category navigation too');
    if(await tab.getAttribute('aria-selected')!=='true')await activate(tab);
    await poll(()=>tab.getAttribute('aria-selected').then(v=>v==='true'),'actual category mode '+name);await capture(category+' / '+name);
    if(/\(0\)$/.test(name))assert.ok((await page.locator('#fmroot').innerText()).includes('Nenhum'),'Native empty category text');
   }
  }
  // Multiple cached content branches must not add outgoing technical tabs.
  for(const category of ['Recrutamento','Treino','Clube','Recrutamento']){await activate(page.getByRole('tab',{name:category,exact:true}));await capture('Repeated '+category);}
  await activate(page.getByRole('tab',{name:'Plantel',exact:true}));await activate(page.getByRole('tab',{name:/^Acções \(/}));
  const misa=page.getByRole('button',{name:'Misa',exact:true});assert.equal(await misa.count(),1);const origin=await misa.getAttribute('id');await activate(misa);await poll(async()=>await page.title()!=='Reunião da equipa técnica','native named player profile');assert.ok((await page.locator('#fmroot').innerText()).includes('Misa'));
  await page.locator('[data-fm-history="back"]').press('Enter');await poll(async()=>await page.title()==='Reunião da equipa técnica','actual advice origin');await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'exact native player link focus');await capture('Original squad-advice link return');
  const health=JSON.parse(await c.fetchText('/health'));assert.equal(health.pid,report.health.pid);assert.equal(health.coreMvid,report.health.coreMvid);
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  report.checks.push('All five actual categories and ten action/observation states retain exactly their current named native tabs and meaningful empty states','Repeated native category changes preserve data/tables and omit outgoing technical tab groups','Actual named player profile returns to the exact original advice action without applying any advice');report.success=true;
  console.log(JSON.stringify({success:true,module:health.coreMvid,groups:report.checks.length,audits:report.audits.length}));
 }catch(error){report.failure={error:error.stack,html:await page.content()};throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify(report,null,2));await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
