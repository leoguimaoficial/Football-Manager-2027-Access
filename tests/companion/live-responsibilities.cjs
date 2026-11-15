// Real reversible staff duty changes in the owned QA career. No simulation/save.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const diskPersistence=process.argv.includes('--disk-persistence');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],observations={};let baseline,original,originalChoice,originalTab,originalCategory;
 const title='Decidir que elementos da equipa técnica do Arsenal Sub-21 devem realizar formações';
 const row=()=>page.getByRole('row').filter({has:page.getByRole('rowheader',{name:title,exact:true})});
 const field=()=>row().getByRole('combobox',{name:'Assigned to',exact:true});
 const value=()=>field().locator('option').first().textContent();
 const footer=name=>page.getByRole('button',{name,exact:true});
 const table=()=>page.getByRole('table').filter({has:page.getByRole('columnheader',{name:'Responsibility',exact:true})});
 async function snapshot(){return table().locator('tbody tr').evaluateAll(rs=>rs.map(r=>({group:r.cells[0].textContent,duty:r.querySelector('th[scope=row]')?.textContent,assignee:r.querySelector('select')?.options[0]?.text||r.cells[3]?.textContent})));}
 async function counts(){const text=await page.locator('#fmroot').innerText(),m=/(\d+) Controlado por si, (\d+) Delegado à Equipa Técnica/.exec(text);assert.ok(m,'Native responsibility totals');return m.slice(1).map(Number);}
 async function focused(control){return control.evaluate(e=>e===document.activeElement);}
 async function go(uid){const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');r.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await r).text(),/^Opening /);await poll(()=>page.evaluate(id=>FMTOKEN.split('\x1f').some(p=>p===id||p.startsWith(id+'#')),String(uid)),'native destination');await settled();await search.fill('');await settled();}
 async function staff(){if(await page.getByRole('tab',{name:'Delegado',exact:true}).getAttribute('aria-selected')!=='true')await activate(page.getByRole('tab',{name:'Delegado',exact:true}));const staff=page.getByRole('radio',{name:'Equipa Técnica',exact:true});if(!await staff.isChecked())await activate(staff,'Space');await poll(()=>row().count(),'native staff duties');await settled();}
 async function reopen(){await go(989379584);await go(3128627712);await staff();}
 async function openField(){await field().focus();const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');r.catch(()=>{});await page.keyboard.press('Enter');assert.ok((await(await r).text()).startsWith('<'));await poll(()=>field().getAttribute('data-loaded'),'native staff choices');}
 async function restoreChoice(){await openField();assert.ok((await field().locator('option').allTextContents()).includes(originalChoice));await field().selectOption({label:originalChoice});const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/select');r.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await r).text(),/ selected$/);await settled();await poll(async()=>await value()===original,'native original assignee');}
 async function unchangedOthers(){const current=await snapshot();assert.deepEqual(current.filter(r=>r.duty!==title),baseline.filter(r=>r.duty!==title));}
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 try{
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const filename=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>filename.count(),'owned QA name');observations.ownedSave=await filename.inputValue();assert.match(observations.ownedSave,/^FM27Access QA /);await activate(footer('Cancelar'));
  await go(3128627712);originalTab=await page.getByRole('tab',{selected:true}).textContent();originalCategory=await page.getByRole('radio',{checked:true}).evaluate(e=>e.labels[0].textContent.trim());await staff();baseline=await snapshot();original=await value();const totals=await counts();observations.baseline={totals,rows:baseline};assert.equal(await footer('Confirmar').isDisabled(),true);
  await openField();originalChoice=await field().evaluate(e=>e.options[e.selectedIndex].text);observations.availableStaff=await field().locator('option').allTextContents();await c.cancelDropdown();assert.equal(await value(),original);await audit('Native staff responsibilities baseline');
  checks.push('Real native delegation table retains headers, duties/descriptions/assignees, actual eligible staff options, native disabled footer and exact dropdown cancellation');
  await activate(row().getByRole('button',{name:'Assumir controlo',exact:true}));await poll(async()=>await value()!==original,'native draft self assignment');const self=await value();assert.match(self,/Teste FM27 Acesso/);assert.deepEqual(await counts(),[totals[0]+1,totals[1]-1]);assert.equal(await focused(field()),true,'Vanishing Take Control returns to the same row value');await unchangedOthers();
  await page.evaluate(()=>FM.core.refresh(false,true));await settled();assert.equal(await focused(field()),true,'Neutral refresh preserves repaired focus');
  await activate(footer('Anular'));assert.equal(await value(),original);assert.equal(await focused(footer('Refazer')),true,'Disabled Undo returns to available Redo');
  await activate(footer('Refazer'));assert.equal(await value(),self);assert.equal(await focused(footer('Repor')),true);
  await activate(footer('Repor'));assert.equal(await value(),original);assert.equal(await focused(field()),true,'Reset with disabled footer returns to the edited row');assert.deepEqual(await snapshot(),baseline);assert.deepEqual(await counts(),totals);
  checks.push('Actual Take Control, Undo, Redo and Reset update native assignment/counts, preserve every other duty, and restore deterministic same-row/footer focus without timers');
  await activate(row().getByRole('button',{name:'Assumir controlo',exact:true}));await activate(footer('Confirmar'));assert.equal(await footer('Confirmar').isDisabled(),true);assert.equal(await footer('Anular').isDisabled(),true);assert.equal(await focused(field()),true);await reopen();assert.equal(await value(),self);assert.deepEqual(await counts(),[totals[0]+1,totals[1]-1]);await unchangedOthers();
  await restoreChoice();await activate(footer('Confirmar'));assert.equal(await focused(field()),true);await reopen();assert.equal(await value(),original);assert.deepEqual(await snapshot(),baseline);assert.deepEqual(await counts(),totals);
  checks.push('Native Confirm clears the dirty/undo state; leaving/reopening retains the committed responsible person; native original choice/Confirm/reopen restores the complete baseline');
  await activate(row().getByRole('button',{name:'Assumir controlo',exact:true}));const confirmId=await footer('Confirmar').getAttribute('id');let refused=0;
  await page.route('**/activate?*',async route=>{if(new URL(route.request().url()).searchParams.get('id')===confirmId){refused++;await route.fulfill({status:503,body:'controlled refusal'});}else await route.continue();});
  await footer('Confirmar').focus();const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');r.catch(()=>{});await page.keyboard.press('Enter');assert.equal((await r).status(),503);await poll(()=>page.evaluate(()=>!FM.core.refreshing),'refused commit recovery');await settled();await page.unroute('**/activate?*');assert.equal(refused,1);assert.equal(await value(),self);assert.equal(await footer('Confirmar').isEnabled(),true,'Refused transport cannot clear the native pending draft');await activate(footer('Anular'));await reopen();assert.deepEqual(await snapshot(),baseline);observations.controlledCommitRefusal=refused;
  checks.push('A refused Confirm request runs once, retains the real pending draft instead of claiming commit, and native Undo/reopen restores the saved assignment');
  const group=page.getByRole('row').filter({has:page.getByRole('rowheader',{name:'Cursos de Treinador',exact:true})}).filter({has:page.getByRole('button',{name:'Controlar tudo',exact:true})});assert.equal(await group.count(),1);
  const courseRows=baseline.filter(r=>r.group==='Cursos de Treinador');assert.ok(courseRows.length>=2);await activate(group.getByRole('button',{name:'Controlar tudo',exact:true}));
  const allControlled=await snapshot();assert.ok(allControlled.filter(r=>r.group==='Cursos de Treinador').every(r=>r.assignee===self));assert.deepEqual(allControlled.filter(r=>r.group!=='Cursos de Treinador'),baseline.filter(r=>r.group!=='Cursos de Treinador'));
  assert.equal(await focused(group.getByRole('button',{name:'Delegar tudo às pessoas recomendadas',exact:true})),true,'Newly enabled opposite group control retains its stable native slot ID');
  await activate(group.getByRole('button',{name:'Delegar tudo às pessoas recomendadas',exact:true}));observations.recommended=await snapshot();assert.ok(observations.recommended.filter(r=>r.group==='Cursos de Treinador').every(r=>r.assignee!==self));
  if(await footer('Repor').isEnabled())await activate(footer('Repor'));assert.deepEqual(await snapshot(),baseline);if(await footer('Confirmar').isEnabled())await activate(footer('Confirmar'));await reopen();assert.deepEqual(await snapshot(),baseline);assert.deepEqual(await counts(),totals);await audit('Restored native staff duties after committed/inverse and bulk draft tests');
  checks.push('Actual group Control All and Delegate Recommended change only both native course duties; Reset/reopen restores every original duty and total without bulk confirmation');
  // The same native list/panel is reused across categories. Hold body delivery
  // before it reaches the server: the old registered handles must independently
  // reject a newly bound native duty, not rely only on the next render's IDs.
  // Drain an already-started real body before holding subsequent requests.
  // Otherwise its late response can replace the old document after the native
  // category changes, bypassing a route installed after that request started.
  await page.evaluate(()=>{window.qaResponsibilityRefreshGate={original:FM.core.refresh,held:[]};FM.core.refresh=function(){qaResponsibilityRefreshGate.held.push(Array.from(arguments));};});
  try{await poll(()=>page.evaluate(()=>!FM.core.refreshing&&!FM.core.pending),'preceding real body drained');}
  catch(error){await page.evaluate(()=>{FM.core.refresh=qaResponsibilityRefreshGate.original;delete window.qaResponsibilityRefreshGate;});throw error;}
  const stale=await field().getAttribute('id'),oldFirst=await page.getByRole('combobox',{name:'Assigned to',exact:true}).first().getAttribute('id');
  const oldBulk=await group.getByRole('button',{name:'Controlar tudo',exact:true}).getAttribute('id');
  const scouting=await page.getByRole('radio',{name:'Observação',exact:true}).getAttribute('id'),owner=await page.evaluate(()=>FMTOKEN);
  let releaseBodies,heldBodies=0;const bodiesGate=new Promise(resolve=>{releaseBodies=resolve;});
  await page.route('**/body?*',async route=>{heldBodies++;await bodiesGate;try{await route.continue();}catch{}});
  try{
   observations.refreshBoundary=await page.evaluate(()=>{const held=qaResponsibilityRefreshGate.held;FM.core.refresh=qaResponsibilityRefreshGate.original;delete window.qaResponsibilityRefreshGate;return{precedingBodyDrained:true,heldRefreshCalls:held.length};});
   assert.match(await c.fetchText('/activate?id='+scouting+'&screen='+encodeURIComponent(owner)),/^Activated /);
   await poll(async()=>(await c.fetchText('/nodeprobe?type=ResponsibilityGroupName&depth=1')).includes('Responsabilidade de Observação'),'native category rebound while old DOM remains');
   await page.evaluate(()=>FM.core.refresh(true));await poll(()=>heldBodies>0,'new body request held before server rendering');
   observations.retainedCommand={id:stale,present:await page.locator('#'+stale).count(),heldBodies};
   assert.equal(observations.retainedCommand.present,1,'Exact old command remains before replacement HTML; native values/captions may update independently');
   observations.recycledCategory={oldFirst,heldBodies,openReply:await c.fetchText('/dropdown/open?id='+oldFirst),bulkReply:await c.fetchText('/activate?id='+oldBulk+'&screen='+encodeURIComponent(owner))};
   assert.match(observations.recycledCategory.openReply,/no longer available|Unknown element|no longer on screen/);
   assert.match(observations.recycledCategory.bulkReply,/no longer available|Unknown element|no longer on screen/);
  }finally{releaseBodies();await page.unrouteAll({behavior:'wait'});}
  await page.evaluate(()=>FM.core.refresh(true));await poll(()=>page.getByRole('rowheader',{name:'Atribuir Olheiros',exact:true}).count(),'actual scouting duties');await settled();
  const newFirst=await page.getByRole('combobox',{name:'Assigned to',exact:true}).first().getAttribute('id');assert.notEqual(oldFirst,newFirst,'Different native duties cannot inherit the same command ID');observations.recycledCategory.newFirst=newFirst;
  assert.match(await c.fetchText('/dropdown/open?id='+oldFirst),/Unknown element/);assert.equal(await footer('Confirmar').isDisabled(),true);assert.deepEqual(await counts(),totals);
  await staff();assert.deepEqual(await snapshot(),baseline);assert.deepEqual(await counts(),totals);
  checks.push('Before new HTML arrives, old individual/group handles refuse a different native category; after rendering, native identities produce distinct IDs and old IDs remain rejected without any dirty assignment');
  if(diskPersistence){
   const crypto=require('node:crypto'),games=path.join(process.env.USERPROFILE,'Documents','Sports Interactive','Football Manager 27','games');
   const fileState=name=>{const file=path.join(games,name),s=fs.statSync(file,{bigint:true});return{size:String(s.size),mtimeNs:String(s.mtimeNs),sha256:crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')};};
   const existing=Object.fromEntries(fs.readdirSync(games).filter(n=>n.endsWith('.fm')).map(n=>[n,fileState(n)]));observations.diskPersistence={existing,created:[]};
   async function waitNative(read,label){const deadline=Date.now()+60000;do{if(await read())return;await page.waitForTimeout(150);}while(Date.now()<deadline);throw Error('Timeout: '+label);}
   async function saveNew(label){
    const suffix=new Date().toISOString().replace(/[^0-9]/g,''),name='FM27Access QA Dialog Regression 20261110 Responsibilities '+label+' '+suffix,file=name+'.fm';
    assert.equal(fs.existsSync(path.join(games,file)),false,'Never overwrite a checkpoint');
    await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const filename=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>filename.count(),'native Save As');
    const update=page.waitForResponse(r=>new URL(r.url()).pathname==='/settext');update.catch(()=>{});await filename.fill(name);await filename.press('Tab');assert.equal(await(await update).text(),'Set to '+name);await settled();assert.equal(await filename.inputValue(),name);
    assert.equal(fs.existsSync(path.join(games,file)),false);await activate(footer('Gravar'));
    await waitNative(async()=>fs.existsSync(path.join(games,file))&&await page.getByRole('heading',{name:'Portal',exact:true}).count()===1&&!await filename.count(),'actual new native save complete');await settled();const state=fileState(file);assert.ok(Number(state.size)>1000000);observations.diskPersistence.created.push({name:file,...state});return name;
   }
   async function loadOwned(name){
    assert.ok(observations.diskPersistence.created.some(f=>f.name===name+'.fm'),'Load only a checkpoint created by this test');
    await c.dropdown('FM Menu');await c.choose('Carregar Jogo');const no=footer('Não'),choice=page.getByRole('row').filter({hasText:name}).getByRole('radio');
    await poll(async()=>await no.count()||await choice.count(),'native unsaved prompt or exact owned save');
    if(await no.count()){assert.match(await page.locator('#fmroot').innerText(),/Deseja gravar o jogo antes de carregar outro jogo\?/);await activate(no);}
    await poll(()=>choice.count(),'exact new checkpoint row');assert.equal(await choice.count(),1);await activate(choice,'Space');await activate(footer('Carregar'));
    if(await no.count()){assert.match(await page.locator('#fmroot').innerText(),/Deseja gravar o jogo antes de carregar outro jogo\?/);await activate(no);}
    await waitNative(()=>page.getByRole('heading',{name:'Portal',exact:true}).count(),'actual owned checkpoint loaded');await settled();
   }
   await activate(row().getByRole('button',{name:'Assumir controlo',exact:true}));const savedPerson=await value();await activate(footer('Confirmar'));await go(989379584);const committed=await saveNew('Committed');
   await go(3128627712);await staff();assert.equal(await value(),savedPerson);await restoreChoice();await activate(footer('Confirmar'));assert.equal(await value(),original);await go(989379584);
   await loadOwned(committed);await go(3128627712);await staff();assert.equal(await value(),savedPerson,'Native Load restores the person from disk, replacing the deliberate in-memory inverse');assert.deepEqual(await counts(),[totals[0]+1,totals[1]-1]);await unchangedOthers();
   checks.push('A new owned Save As persists the confirmed native person; after an in-memory inverse, Load without saving restores the committed disk person/counts and all other duties');
   await restoreChoice();await activate(footer('Confirmar'));await go(989379584);const restored=await saveNew('Restored');await loadOwned(restored);await go(3128627712);await staff();assert.deepEqual(await snapshot(),baseline);assert.deepEqual(await counts(),totals);
   for(const[name,state]of Object.entries(existing))assert.deepEqual(fileState(name),state,'Existing save preserved: '+name);
   for(const created of observations.diskPersistence.created){const{name,...state}=created;assert.deepEqual(fileState(name),state,'New checkpoint unchanged by loading: '+name);}
   observations.diskPersistence.allExistingSavesUnchanged=true;observations.diskPersistence.currentOwnedSave=restored;
   checks.push('The inverse native person is confirmed, saved to a second new owned file and loaded back; every original duty/total is restored and all pre-existing/new checkpoints retain exact bytes and metadata');
  }
  if(originalCategory!=='Equipa Técnica')await activate(page.getByRole('radio',{name:originalCategory,exact:true}),'Space');if(originalTab!=='Delegado')await activate(page.getByRole('tab',{name:originalTab,exact:true}));await go(989379584);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  const result={success:true,mvid:JSON.parse(await c.fetchText('/health')).coreMvid,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,simulationAdvanced:false,saveSubmitted:diskPersistence,persistenceScope:diskPersistence?'Native Confirm, two NEW owned Save As files and Load without saving; no game restart':'Native Confirm and leave/reopen; not a save/reload or game restart',manualNVDA:false};fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/responsibilities'+(diskPersistence?'-persistence':'')+'.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
 }catch(error){fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/responsibilities-failure.json'),JSON.stringify({error:error.stack,checks,audits,observations,baseline,original,originalChoice,errors:c.errors,requestFailures:c.requestFailures,trace:await c.transportTrace(),browser:await page.evaluate(()=>({focus:document.activeElement?.outerHTML,actionFocus:FM.core.actionFocus,actionRow:FM.core.actionRow,token:FMTOKEN,body:document.getElementById('fmroot')?.innerText}))},null,2));throw error;}
 finally{await page.evaluate(()=>{if(window.qaResponsibilityRefreshGate){FM.core.refresh=qaResponsibilityRefreshGate.original;delete window.qaResponsibilityRefreshGate;FM.core.refresh();}}).catch(()=>{});await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
