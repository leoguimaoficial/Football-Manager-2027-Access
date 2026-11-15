// Real pre-match manual opposition editor transaction. Requires an owned QA
// Team News screen; changes one native instruction and restores it exactly.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
async function run(c){
 const {page,poll}=c,checks=[],audits=[],observations=[];
 const opener=()=>page.getByRole('button',{name:'Eu resolvo',exact:true});
 const cancel=()=>page.getByRole('button',{name:'Cancelar',exact:true});
 const confirm=()=>page.getByRole('button',{name:'Confirmar Alterações',exact:true});
 const rows=()=>page.locator('table tbody tr').evaluateAll(rs=>rs.filter(r=>r.querySelector('select[data-dd]')).map(r=>({
  name:r.querySelector('td:nth-child(3) button')?.textContent,
  values:[...r.querySelectorAll('select')].map(s=>({label:s.getAttribute('aria-label'),text:s.selectedOptions[0]?.textContent}))
 })));
 const row=()=>page.getByRole('row').filter({has:page.getByRole('button',{name:'Sheila García',exact:true})});
 const pressure=()=>row().getByRole('combobox',{name:'Fazer Pressão',exact:true});
 const value=()=>pressure().locator('option:checked').textContent();
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 async function open(){await c.activate(opener());await poll(async()=>await pressure().count()===1,'actual opponent rows');await c.settled();}
 async function returned(){await poll(async()=>await opener().count()===1&&await cancel().count()===0,'actual Team News destination');await poll(()=>opener().evaluate(e=>e===document.activeElement),'exact manual editor origin focus');}
 async function select(label){
  await pressure().focus();const reply=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');reply.catch(()=>{});
  await page.keyboard.press('Enter');assert.equal((await reply).status(),200);
  await poll(()=>pressure().getAttribute('data-loaded'),'native instruction options');
  observations.push({options:await pressure().locator('option').allTextContents()});
  await c.choose(label);await poll(async()=>await value()===label,'native instruction value');
 }
 let baseline;
 try{
  assert.equal(await opener().count(),1,'Start at pre-match Team News');await open();baseline=await rows();assert.equal(baseline.length,22);
  assert.equal(await value(),'-','QA original instruction is unset');await audit('Native manual opposition editor');
  await c.activate(cancel());await returned();checks.push('Clean Cancel closes the native editor and returns to its exact opener');
  await open();assert.deepEqual(await rows(),baseline);await select('Nunca');
  await c.activate(cancel());await poll(()=>page.getByRole('button',{name:'Sim',exact:true}).count(),'native discard confirmation');
  assert.match(await page.locator('#fmroot').innerText(),/alterações feitas sejam ignoradas/);
  await c.activate(page.getByRole('button',{name:'Não',exact:true}));await poll(()=>cancel().count(),'No retains native draft');
  assert.equal(await value(),'Nunca');checks.push('Draft Cancel opens native discard dialog; No retains the pending instruction');
  await c.activate(cancel());await poll(()=>page.getByRole('button',{name:'Sim',exact:true}).count(),'discard reopened');
  await c.activate(page.getByRole('button',{name:'Sim',exact:true}));await returned();await open();assert.deepEqual(await rows(),baseline);
  checks.push('Yes discards the draft; reopening restores all 22 original rows');
  await select('Nunca');await c.activate(confirm());await returned();await open();assert.equal(await value(),'Nunca');
  const applied=await rows(),expected=structuredClone(baseline);expected.find(r=>r.name==='Sheila García').values.find(v=>v.label==='Fazer Pressão').text='Nunca';
  assert.deepEqual(applied,expected);checks.push('Confirm persists exactly the intended native instruction across close/reopen');
  await select('-');await c.activate(confirm());await returned();await open();assert.deepEqual(await rows(),baseline);await audit('Restored native opposition instructions');
  await c.activate(cancel());await returned();checks.push('Confirmed inverse and reopening restore every original row and exact origin focus');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);
  const result={success:true,checks,audits,observations,baseline,jsErrors:c.errors,requestFailures:c.requestFailures,nativeDraftAppliedAndRestored:true,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/opposition-lifecycle.json'),JSON.stringify(result,null,2));return result;
 }catch(error){fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/opposition-lifecycle-failure.json'),JSON.stringify({success:false,error:String(error.stack||error),checks,audits,observations,baseline,body:await page.locator('#fmroot').innerText().catch(()=>null),jsErrors:c.errors,requestFailures:c.requestFailures,restorationMustBeChecked:true},null,2));throw error;}
}
module.exports=run;
if(require.main===module)(async()=>{const c=await require('./original-client.cjs')({networkDiagnostics:true});try{console.log(JSON.stringify(await run(c),null,2));}finally{await c.browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
