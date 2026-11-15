// Regression of actual native U21 availability, not a simulated disabled menu.
// No permitted availability leaf, Save or simulation is submitted. Explicit
// disabled requests exercise the production refusal after independent native proof.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'artifacts/tests/web/dropdown-availability.json');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,checks:[],audits:[],saveSubmitted:false,simulationAdvanced:false,permittedAvailabilityLeafSubmitted:false,manualNVDA:false};
 try{
  report.health=JSON.parse(await c.fetchText('/health'));assert.equal(report.health.developerTools,true);
  const referenceArg=process.argv.find(a=>a.startsWith('--core-reference='));
  report.coreReference=referenceArg?referenceArg.slice('--core-reference='.length):'artifacts/career-native-option-fixed-health.json';
  const qualified=JSON.parse(fs.readFileSync(path.resolve(root,report.coreReference),'utf8'));
  assert.equal(report.health.pid,qualified.health.pid);assert.equal(report.health.coreMvid,qualified.health.coreMvid);
  assert.notEqual(report.health.coreMvid,'0286f241-229a-493c-b4d1-1580bcf1a0ec','Never exercise defensive requests on the preceding unfixed native core');
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');report.checkpoint=await page.getByRole('textbox',{name:'File name',exact:true}).inputValue();
  assert.equal(report.checkpoint,'FM27Access QA Dialog Regression 20261110 Responsibilities Restored 20261110104840158');await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const destination=page.locator('#fmnav-tree [role=treeitem][data-uid="1381513472"][data-obj="2000797209"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await destination.locator(':scope > span').textContent());await destination.focus();const go=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');go.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await go).text(),/^Opening /);await settled();await search.fill('');await settled();assert.equal(await page.title(),'Arsenal Sub-21');
  const fields=page.getByRole('combobox',{name:'Definir Todos',exact:true});assert.equal(await fields.count(),2,'Observed two actual native Set All controls');
  const field=fields.first();report.owner=await field.getAttribute('id');await field.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');opened.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await opened).text(),/^</);await poll(()=>field.getAttribute('data-loaded'),'actual own native options');
  const read=()=>field.locator('option').evaluateAll(es=>es.map(e=>({value:e.value,text:e.textContent,disabled:e.disabled,folder:e.hasAttribute('data-sub')})));
  report.options=await read();report.native=await require('./native-dropdown-option-state.cjs')(c,report.health.coreMvid,report.owner);
  assert.equal(report.options.length,4);assert.deepEqual(report.native.guardDisabled.map(x=>x.disabled),[false,false,true,false]);
  assert.match(report.native.rows[2],/self=False\|hierarchy=False$/);assert.deepEqual(report.options.map(x=>x.disabled),[false,false,true,false]);
  for(let i=0;i<4;i++)assert.equal(report.options[i].text.replace(/, submenu$/,''),report.native.labels[i]);
  report.checks.push('Actual four native labels/order and disabled state exactly match ordinary semantic options; native-disabled entry remains readable');
  const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({name:'Native availability root',violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);
  report.defensiveRequests=[];
  const logBefore=await c.fetchText('/log'),cursor=/^cursor: (\d+)/.exec(logBefore)?.[1];assert.ok(cursor);
  for(const action of ['expand','select']){
   const response=await page.evaluate(async({action,owner})=>{const r=await fetch('/dropdown/'+action+'?path=&index=2&id='+encodeURIComponent(owner),{method:'POST'});return{status:r.status,text:await r.text()};},{action,owner:report.owner});
   assert.equal(response.status,200);assert.equal(response.text,'This option is not available right now.');report.defensiveRequests.push({action,...response});
   const after=await require('./native-dropdown-option-state.cjs')(c,report.health.coreMvid,report.owner);assert.deepEqual(after.guardDisabled,report.native.guardDisabled);assert.deepEqual(after.labels,report.native.labels);assert.equal(after.selectedIndex,report.native.selectedIndex);assert.deepEqual(await read(),report.options);
  }
  report.refusalLog=await c.fetchText('/log?since='+cursor);assert.doesNotMatch(report.refusalLog,/ConfirmSelection|ConfirmLeafSelection|TriggerChildOpen|TriggerSelectionEvent/,'Disabled requests must never enter native command dispatch');
  report.checks.push('Explicit disabled expand/select requests are refused by the current native availability guard, without closing or changing the actual menu');
  for(const index of [1,3]){
   await field.selectOption({value:String(index)});await field.focus();const expand=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/expand');expand.catch(()=>{});await page.keyboard.press('ArrowRight');assert.match(await(await expand).text(),/^</);
   const child=page.locator('select[data-chain][data-loaded]');await poll(()=>child.count().then(n=>n===1),'enabled actual submenu');const options=await child.locator('option').evaluateAll(es=>es.map(e=>({text:e.textContent,disabled:e.disabled})));assert.ok(options.length);(report.enabledFolders??=[]).push({index,options});
   await child.focus();const collapse=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/collapse');collapse.catch(()=>{});await page.keyboard.press('ArrowLeft');assert.equal(await(await collapse).text(),'Submenu closed.');await poll(()=>field.evaluate(e=>document.activeElement===e),'exact parent focus');assert.deepEqual(await read(),report.options);
  }
  await c.cancelDropdown();await settled();assert.equal(await page.evaluate(()=>document.activeElement.id),report.owner);report.checks.push('Both native-enabled folders open/cancel; parent/root focus and full native option state are preserved');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
 }catch(e){report.failure=e.stack;report.html=await page.locator('#fmroot').innerHTML().catch(()=>null);throw e;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify(report,null,2));console.log(JSON.stringify({success:report.success,checks:report.checks.length,audits:report.audits.length,failure:report.failure}));await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
