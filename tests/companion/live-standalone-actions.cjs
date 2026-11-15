// Public production routes only. Empty expired-loan menu versus a real available
// selected-player menu. No menu business leaf, Save or simulation is submitted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
const checkpoint=owned.julyWorking,messageId='149572';
assert.ok(checkpoint&&owned.ownedWorkingFiles.includes(checkpoint+'.fm'));
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,activate,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,checks:[],audits:[],businessLeafInvoked:false,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const output=path.join(root,'artifacts/tests/web/standalone-actions.json');let selectedByTest=false;
 const action=()=>page.getByRole('button',{name:'Acções',exact:true});
 const irene=()=>page.getByRole('table').locator('tbody tr').filter({has:page.getByRole('button',{name:'Irene Paredes',exact:true})}).getByRole('checkbox');
 async function audit(stage){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations});assert.equal(a.violations.length,0,stage);}
 async function goSquad(){
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="1381513472"][data-obj="2000104296"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});
  await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);
  await poll(()=>page.evaluate(()=>FMTOKEN.split('\x1f').some(p=>p.startsWith('1381513472#'))),'actual senior squad');await settled();await search.fill('');await settled();
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await page.keyboard.press('Alt+1');await poll(async()=>await page.title()==='Mensagens','actual July Inbox');await settled();
  const all=page.getByRole('tab',{name:'Tudo',exact:true});if(await all.getAttribute('aria-selected')!=='true')await activate(all);await c.newestMessages();
  const news=page.locator('tr[data-fm-message-id="'+messageId+'"] button[onclick^="FM.msgview.open"]');assert.equal(await news.count(),1);
  await news.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');opened.catch(()=>{});await page.keyboard.press('Enter');assert.equal(await(await opened).text(),'');await poll(()=>page.locator('[data-fm-message-view-id="'+messageId+'"]').count(),'exact returned-loan news');await settled();
  await activate(page.getByRole('table').locator('tbody tr').filter({hasText:'Selina Cerci'}).getByRole('button',{name:'Relatório de Empréstimo',exact:true}));await poll(async()=>await page.title()==='Relatório de Empréstimo','expired native loan detail');await settled();
  assert.match(await page.locator('#fmroot').innerText(),/Expirou\s+30\/6\/2027/);
  const emptyOrigin=await action().getAttribute('id'),emptyOwner=await page.evaluate(()=>FMTOKEN);report.expiredOrigin={id:emptyOrigin,owner:emptyOwner};
  await action().focus();const emptyReply=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');emptyReply.catch(()=>{});await page.keyboard.press('Enter');report.emptyReply=await(await emptyReply).text();
  assert.equal(report.emptyReply,'That menu is unavailable in the current context.');await settled();assert.equal(await page.getByRole('menu').count(),0);assert.equal(await page.title(),'Relatório de Empréstimo');assert.equal(await page.evaluate(id=>document.activeElement?.id===id,emptyOrigin),true);await audit('Native empty loan menu reports unavailability without fake success or focus loss');
  report.checks.push('An empty expired-loan action group reports current unavailability; it never claims Activated or leaves an empty popup');
  await goSquad();
  report.staleReply=await page.evaluate(async({id,owner})=>await(await fetch('/activate?id='+encodeURIComponent(id)+'&screen='+encodeURIComponent(owner),{method:'POST'})).text(),{id:emptyOrigin,owner:emptyOwner});assert.match(report.staleReply,/screen changed|no longer on screen|Unknown element/);assert.equal(await page.getByRole('menu').count(),0);
  report.checks.push('An old standalone-menu command is refused after changing native screens');
  assert.equal(await irene().count(),1);assert.equal(await irene().isChecked(),false);assert.equal(await action().isDisabled(),true);
  await activate(irene(),'Space');selectedByTest=true;await poll(()=>irene().isChecked(),'actual Irene selection');await poll(()=>action().isEnabled(),'native group becomes available');
  const origin=await action().getAttribute('id');report.availableReply=await activate(action());assert.equal(report.availableReply,'Opened menu');assert.equal(await page.getByRole('menu').count(),1);report.menu=await page.getByRole('menu').innerText();assert.match(report.menu,/Irene Paredes/);await audit('Confirmed native selected-player action menu');
  await page.getByRole('menuitem',{name:'Mover entre equipas',exact:true}).focus();await page.keyboard.press('ArrowRight');await settled();report.submenu=await page.getByRole('menu').innerText();assert.match(report.submenu,/Mover Para S-21/);await page.keyboard.press('ArrowLeft');await settled();await page.keyboard.press('Escape');await poll(async()=>await page.title()==='Arsenal','native squad restored');await settled();assert.equal(await page.evaluate(id=>document.activeElement?.id===id,origin),true);
  report.checks.push('A real available menu confirms its native entries, supports folder arrows and Escape, and restores exact Actions focus');
  assert.equal(await irene().isChecked(),true);await activate(irene(),'Space');selectedByTest=false;await poll(async()=>await irene().isChecked()===false&&await action().isDisabled(),'original native selection and group availability restored');await audit('Original squad state after exact selection inverse');
  report.checks.push('Only the test row selection is inverted; its original unchecked/disabled-menu state is restored');
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,report.health.coreMvid);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
  console.log(JSON.stringify({success:true,module:report.health.coreMvid,checks:report.checks.length,audits:report.audits.length}));
 }catch(error){report.failure=error.stack;throw error;}
 finally{
  if(selectedByTest){if(await page.getByRole('menu').count())await page.keyboard.press('Escape');if(await irene().count()===1&&await irene().isChecked())await activate(irene(),'Space');}
  report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;fs.writeFileSync(output,JSON.stringify(report,null,2));await c.browser.close();
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
