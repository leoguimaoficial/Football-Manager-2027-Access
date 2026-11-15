// Actual accepted-contract QA profile: keep all original native banner values
// and controls, while self-labelled actions do not become unnamed data fields.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'artifacts/tests/web');
const referenceArg=process.argv.find(a=>a.startsWith('--before='));
assert.ok(referenceArg,'Retain an actual pre-correction profile, not invented expected player data');
const before=JSON.parse(fs.readFileSync(path.resolve(root,referenceArg.slice(9)),'utf8'));
const owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
assert.equal(before.checkpoint,owned.acceptedContractWorking);assert.equal(before.profile,'Maddy Earl');
assert.ok(owned.ownedWorkingFiles.includes(before.checkpoint+'.fm'));
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint:before.checkpoint,checks:[],audits:[],saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const banner=()=>page.locator('#fmroot table').filter({has:page.getByRole('rowheader',{name:'Contract',exact:true})});
 async function rows(){return banner().locator(':scope > tbody > tr').evaluateAll(rs=>rs.map(r=>({label:r.querySelector(':scope > th')?.textContent||'',value:r.querySelector(':scope > td')?.textContent,controls:[...r.querySelectorAll('button,input,select')].map(e=>({name:e.textContent,disabled:e.disabled}))})));}
 const expected=before.banner[0].rows.map(r=>({label:r.label,value:r.value,controls:r.controls.map(c=>({name:c.name,disabled:c.disabled}))}));
 async function audit(stage){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 async function verify(){
  assert.equal(await banner().count(),1);report.rows=await rows();assert.deepEqual(report.rows,expected,'All original native field/value/control text and order remain');
  assert.equal(await banner().locator('th').filter({hasText:/^\s*$/}).count(),0,'Self-labelled actions have no empty field header');
  const actionRows=banner().locator(':scope > tbody > tr').filter({has:page.locator('td[colspan="2"]')});
  assert.equal(await actionRows.count(),2);assert.equal(await actionRows.locator('button').count(),2);
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),before.checkpoint);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await page.keyboard.press('Alt+1');await poll(()=>page.getByRole('heading',{name:'Mensagens',exact:true}).count(),'actual Inbox');await c.newestMessages();await settled();
  const news=page.locator('tr[data-fm-message-id="154686"] button[onclick^="FM.msgview.open"]');assert.equal(await news.count(),1);
  const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');opened.catch(()=>{});await news.focus();await page.keyboard.press('Enter');assert.equal(await(await opened).text(),'');await poll(()=>page.locator('[data-fm-message-view-id="154686"]').count(),'actual signed renewal');await settled();
  await activate(page.locator('[data-fm-msg-body]').getByRole('button',{name:'Maddy Earl',exact:true}));await poll(async()=>await page.title()==='Relatório de jogador','actual player profile');await settled();
  await verify();await audit('Native profile with action rows and all original values');report.checks.push('All fifteen native banner rows retain their exact order, field/value pairs and control names; two self-labelled actions have full-width cells and no empty headers');
  const actions=banner().getByRole('button',{name:'Acções',exact:true}),actionOrigin=await actions.getAttribute('id');
  await activate(actions);await poll(()=>page.getByRole('menu').count(),'actual Actions menu');assert.equal(await page.getByRole('menuitem',{name:'Maddy Earl',exact:true}).count(),1);assert.equal(await page.getByRole('menuitem',{name:'Contrato',exact:true}).count(),1);report.menu=await page.getByRole('menuitem').allTextContents();await audit('Native player actions menu');
  await page.keyboard.press('Escape');await poll(async()=>!await page.getByRole('menu').count(),'actual menu closure');await poll(()=>page.evaluate(id=>document.activeElement.id===id,actionOrigin),'exact Actions origin');report.checks.push('Original Actions command opens the real player-specific menu; Escape closes it and restores exact origin without a business leaf');
  const ability=banner().getByRole('button',{name:/^Capacidade:.*Potencial:/}),abilityOrigin=await ability.getAttribute('id');assert.equal(await ability.count(),1);
  await activate(ability);await poll(async()=>await page.title()==='Relatório de preparador','actual native coach report');await settled();assert.match(await page.locator('#fmroot').innerText(),/Maddy Earl/);await audit('Actual ability/potential action destination');
  await activate(page.locator('[data-fm-history="back"]'));await poll(async()=>await page.title()==='Relatório de jogador','profile return');await settled();await poll(()=>page.evaluate(id=>document.activeElement.id===id,abilityOrigin),'exact rating action origin');await verify();await audit('Returned profile values and origin');report.checks.push('Compound ability/potential command opens the actual coach report and native Back preserves the exact opener and every original profile field');
  if(report.health.developerTools){
   report.nativeActionInventory=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),require('./native-handle-inventory.cjs')(report.health.coreMvid,[actionOrigin,abilityOrigin]));
   assert.doesNotMatch(report.nativeActionInventory,/\[exception\]|\[compile\]/);assert.match(report.nativeActionInventory,/ContextMenuButtonAdapter/);assert.match(report.nativeActionInventory,/StarRatingAbilityAdapter/);
  }
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
 }catch(error){report.failure=error.stack||String(error);throw error;}
 finally{report.jsErrors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;fs.mkdirSync(out,{recursive:true});fs.writeFileSync(path.join(out,'profile-banner-actions.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
