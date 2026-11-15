// Actual open-window registration on an explicitly owned QA career only.
// Confirm a changed compliant set, reopen, then confirm its exact native inverse.
// No Save, simulated success, eligibility override or uncertain command replay.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),args=process.argv.slice(2);
const checkpoint=args.find(a=>a.startsWith('--checkpoint='))?.slice(13);
const owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
assert.equal(args.includes('--commit'),true,'Explicit native registration commit is required');
assert.equal(checkpoint,owned.registrationWorking,'Use the exact recorded open-window QA checkpoint');
assert.ok(owned.ownedWorkingFiles.includes(checkpoint+'.fm'));
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,activate,settled}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,checks:[],audits:[],observations:[],commands:[],saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const out=()=>fs.writeFileSync(path.join(root,'artifacts/tests/web/career-registration-'+(report.success?'passed':'failure')+'.json'),JSON.stringify(report,null,2));
 async function audit(stage){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations.map(v=>v.id)});assert.equal(a.violations.length,0);}
 async function go(uid,obj){
  const target=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]'+(obj?'[data-obj="'+obj+'"]':'')).first();
  const search=page.getByRole('textbox',{name:'Search all screens',exact:true});await search.fill(await target.locator(':scope > span').textContent());await target.focus();
  const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');assert.match(await(await r).text(),/^Opening /);await settled();await search.fill('');await settled();
 }
 async function snapshot(stage){
  const value=await page.locator('#fmroot tr').evaluateAll(rs=>rs.filter(r=>r.querySelector('input[type=checkbox]')).map(r=>({name:r.cells[3]?.textContent.trim(),fields:[...r.querySelectorAll('input[type=checkbox]')].map(e=>({label:e.getAttribute('aria-label'),checked:e.checked,disabled:e.disabled}))})));
  assert.ok(value.length>20,'Actual populated registration records');report.observations.push({stage,records:value,body:(await page.locator('#fmroot').innerText()).slice(-1800)});return value;
 }
 async function confirm(stage){
  const origin=await page.getByRole('button',{name:'Confirmar',exact:true}).getAttribute('id');await activate(page.getByRole('button',{name:'Confirmar',exact:true}));
  assert.equal(await page.title(),'Por Favor Confirme');assert.match(await page.locator('#fmroot').innerText(),/Enviar lista completa do plantel\?/);
  assert.equal(await page.locator('#fmroot button').count(),2);report.commands.push({stage,outcome:'Not yet observed'});out();
  await activate(page.getByRole('button',{name:'Confirmar',exact:true}));
  await poll(async()=>await page.title()==='Inscrição'&&await page.getByRole('button',{name:'Anular',exact:true}).isDisabled(),'native accepted registration with clean edit state');
  assert.equal(await page.evaluate(()=>document.activeElement.id),origin);report.commands.at(-1).outcome='Native clean edit state and exact confirmation-origin focus observed';out();
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await c.dropdown('FM Menu');await c.choose('Preferências');assert.equal(await page.getByRole('checkbox',{name:'Usar Gravação Automática',exact:true}).isChecked(),false);await activate(page.getByRole('button',{name:'Close',exact:true}));
  await go('2254266112','2000104296');assert.equal(await page.title(),'Inscrição');assert.match(await page.locator('#fmroot').innerText(),/Encerra hoje/);assert.match(await page.locator('#fmroot button').nth(1).textContent(),/4\/2\/2027/);
  const misa=page.getByRole('checkbox',{name:'Misa, 27, Reg',exact:true});assert.equal(await misa.isChecked(),true);assert.equal(await misa.isEnabled(),true);
  const baseline=await snapshot('initial-native25');assert.match(await page.locator('#fmroot').innerText(),/25\/25/);await audit('Actual open-window registration');
  await activate(misa,'Space');await poll(async()=>!(await misa.isChecked())&&/24\/25/.test(await page.locator('#fmroot').innerText()),'actual native draft row and rule count');assert.equal(await page.getByRole('button',{name:'Anular',exact:true}).isEnabled(),true);
  await activate(page.getByRole('button',{name:'Anular',exact:true}));assert.equal(JSON.stringify(await snapshot('native-undo25')),JSON.stringify(baseline));
  report.checks.push('Keyboard toggles the exact native registration record and24/25 count; native Anular restores the original full set and availability');
  const origin=await page.getByRole('button',{name:'Confirmar',exact:true}).getAttribute('id');await activate(page.getByRole('button',{name:'Confirmar',exact:true}));assert.match(await page.locator('#fmroot').innerText(),/Enviar lista completa do plantel\?/);await audit('Native full-squad confirmation');
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));assert.equal(await page.evaluate(()=>document.activeElement.id),origin);assert.equal(JSON.stringify(await snapshot('cancel-original25')),JSON.stringify(baseline));
  report.checks.push('Native confirmation suppresses background and Cancel restores the exact action without changing the original registration');
  await activate(misa,'Space');await confirm('Submit actual24-player compliant set');assert.equal(await misa.isChecked(),false);await go('989379584');await go('2254266112','2000104296');assert.equal(await misa.isChecked(),false);assert.match(await page.locator('#fmroot').innerText(),/24\/25/);assert.equal(await page.getByRole('button',{name:'Anular',exact:true}).isDisabled(),true);await snapshot('native24-persists-after-reopen');await audit('Actual changed registration persists after reopening');
  report.checks.push('Changed compliant registration is confirmed once, has clean native edit state and persists after leaving/reopening');
  await activate(misa,'Space');await confirm('Restore actual original25-player set');await go('989379584');await go('2254266112','2000104296');assert.equal(JSON.stringify(await snapshot('original25-restored-and-reopened')),JSON.stringify(baseline));assert.match(await page.locator('#fmroot').innerText(),/25\/25/);await audit('Original native registration fully restored');
  report.checks.push('The native inverse restores every original selection/registration value and availability after another reopen');
  report.statusLimit='Official native green-check Inscrição pendente remains unchanged; result proof uses actual changed records, rule counts, clean edit state and persistence, not that caption';
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,report.health.coreMvid);report.success=true;
 }catch(error){report.failure=error.stack;report.body=await page.locator('#fmroot').innerText().catch(()=>null);throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;out();await c.browser.close();}
 console.log(JSON.stringify({success:report.success,checks:report.checks.length,cleanAxe:report.audits.length}));
})().catch(error=>{console.error(error);process.exitCode=1;});
