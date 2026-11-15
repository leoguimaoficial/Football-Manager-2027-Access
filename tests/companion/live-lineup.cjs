// Production-client regression for the empty lineup in the owned Arsenal QA
// Squads career. Selects one goalkeeper and removes only that test selection.
// No simulation, tactic reset, Save or overwrite is performed.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],timings=[],speech=[];
 const cycleArg=process.argv.find(a=>a.startsWith('--open-cycles=')),openCycles=cycleArg?Number(cycleArg.split('=')[1]):1;
 assert.ok(Number.isInteger(openCycles)&&openCycles>=1&&openCycles<=20);
 page.on('request',r=>{const u=new URL(r.url());if(u.pathname==='/speak')speech.push(u.searchParams.get('text'));});
 const table=()=>page.locator('table:not(#fm-pitch-browse)').last();
 let nameColumn=-1,originalPhase;
 const slot=()=>table().locator('tbody tr').first().locator(':scope > td, :scope > th').nth(nameColumn).getByRole('combobox');
 async function open(){
  await settled();const control=slot();await control.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');
  await page.keyboard.press('Enter');const body=await(await response).text();assert.ok(body.startsWith('<'),body);
  await poll(()=>control.getAttribute('data-loaded'),'native goalkeeper options');return control;
 }
 async function complete(label,expected){
  const at=Date.now();await page.evaluate(()=>FM.core.refresh());await settled();
  assert.equal(await table().locator('tbody tr').count(),expected);
  assert.doesNotMatch(await page.locator('#fmroot').innerText(),/couldn't read all|table not loaded|not responding|mod is busy/);
  timings.push({label,milliseconds:Date.now()-at,rows:expected});
 }
 let changed=false;
 try{
  const health=JSON.parse(await c.fetchText('/health'));
  // The preceding regression returns to Portal. Establish the actual native
  // destination rather than requiring a manual navigation before this suite.
  if(await page.getByRole('heading',{name:'Tácticas',exact:true}).count()!==1){
   const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="195478016"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
   await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();
   const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});
   await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);
   await poll(()=>page.getByRole('heading',{name:'Tácticas',exact:true}).count(),'actual native Tactics destination');
   await settled();await search.fill('');await settled();
  }
  assert.equal(await page.getByRole('heading',{name:'Tácticas',exact:true}).count(),1);
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned save identity');
  const ownedSave=await file.inputValue();assert.match(ownedSave,/^FM27Access QA Squads /);
  await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  // The game persists this view outside the save. The broad squad regression
  // deliberately tests Panoramic; that mode has an additional role column and
  // intentionally no compact pitch. Establish the native pitch view explicitly
  // and resolve the player cell by its actual header, never a stored index.
  const phase=page.getByRole('tab',{name:'Com Posse de Bola',exact:true});
  originalPhase=await phase.locator('..').getByRole('tab',{selected:true}).textContent();
  if(await phase.getAttribute('aria-selected')!=='true')await c.activate(phase);
  nameColumn=(await table().locator('thead th').allTextContents()).indexOf('Nome');
  assert.ok(nameColumn>=0,'Native player-name column');
  assert.equal(await slot().getAttribute('aria-label'),'Sem Escolhas','Use the dedicated empty-lineup QA state');
  assert.equal(await page.locator('#fm-pitch-browse td').count(),0);
  for(let pass=0;pass<3;pass++)await complete('Empty lineup refresh '+(pass+1),50);
  checks.push('All 26 native empty selection slots and 24 available-player rows survive repeated full harvests');
  const origin=await slot().getAttribute('id');
  for(let cycle=0;cycle<openCycles;cycle++){
   if(cycle)await complete('Virtualized reopen refresh '+cycle,50);
   await open();await c.cancelDropdown();assert.equal(await page.evaluate(()=>document.activeElement.id),origin);
  }
  checks.push('Opening and cancelling the real goalkeeper selector preserves the empty position and origin focus');

  // Controlled HTTP failure: no native selection is dispatched.
  const before=speech.length;
  await page.route('**/dropdown/open?*',route=>route.fulfill({status:503,body:'Controlled unavailable response'}));
  await slot().focus();await page.keyboard.press('Enter');
  await poll(()=>speech.slice(before).some(t=>t?.includes('could not be confirmed')),'dropdown failure feedback');
  await page.unroute('**/dropdown/open?*');
  await poll(()=>page.evaluate(()=>FM.dropdown.pending===null),'released failed open');
  await complete('After controlled HTTP failure',50);
  checks.push('HTTP failure releases pending dropdown state and does not leave an uncommittable options list');

  // Keep real native headers, but stall the open reply body. Exercise the
  // production deadline callback early rather than waiting 20s in this test.
  await page.evaluate(()=>{
   const nativeFetch=window.fetch,nativeTimer=window.setTimeout,nativeRequestText=FM.core.requestText;
   window.qaDropdownStall={headers:false,aborted:false,deadlineCaptured:false};let abortRequest;
   // Scope deadline observation to the actual dropdown request's synchronous
   // setup. A global timer override can accidentally capture a concurrent body
   // refresh deadline and abort the wrong read. Deferred requests re-enter this
   // wrapper through the production requestText method once that body finishes.
   FM.core.requestText=function(u,o,timeout){
    if(!String(u).startsWith('/dropdown/open?'))return nativeRequestText.call(this,u,o,timeout);
    const previousTimer=window.setTimeout;
    window.setTimeout=(fn,ms,...args)=>{if(ms===(timeout||20000)){abortRequest=fn;qaDropdownStall.deadlineCaptured=true;}return previousTimer(fn,ms,...args);};
    try{return nativeRequestText.call(this,u,o,timeout);}finally{window.setTimeout=previousTimer;}
   };
   window.fetch=async(u,o)=>{
    const r=await nativeFetch(u,o);if(!String(u).startsWith('/dropdown/open?'))return r;
    await r.text();qaDropdownStall.headers=true;
    if(typeof abortRequest!=='function')throw Error('Controlled dropdown deadline was not captured');
    nativeTimer(()=>abortRequest(),100);
    return {ok:r.ok,status:r.status,text:()=>new Promise((resolve,reject)=>{
     o.signal.addEventListener('abort',()=>{qaDropdownStall.aborted=true;reject(new DOMException('Controlled stalled dropdown body','AbortError'));},{once:true});
    })};
   };
   window.qaRestoreDropdownTransport=()=>{window.fetch=nativeFetch;FM.core.requestText=nativeRequestText;window.setTimeout=nativeTimer;};
   window.qaAbortDropdownStall=()=>{if(typeof abortRequest==='function')abortRequest();};
  });
  const beforeStall=speech.length;await slot().focus();await page.keyboard.press('Enter');
  await poll(()=>page.evaluate(()=>qaDropdownStall.aborted&&FM.dropdown.pending===null),'bounded dropdown body and released gate');
  assert.deepEqual(await page.evaluate(()=>qaDropdownStall),{headers:true,aborted:true,deadlineCaptured:true});
  await page.evaluate(()=>qaRestoreDropdownTransport());
  await poll(()=>speech.slice(beforeStall).some(t=>t?.includes('could not be confirmed')),'stalled-open feedback');
  await settled();
  checks.push('A real native popup whose reply body stalls is cancelled by its owner; no unseen popup or pending gate strands navigation');

  await open();changed=true;const selected=await c.choose('DISP, Misa, GR, Fér, 3 stars');
  assert.match(selected,/selected$/);
  await poll(async()=>await slot().getAttribute('aria-label')==='Misa','observed native row selection');
  await poll(async()=>/Goalkeeper, Misa(?:,|$)/.test(await page.locator('#fm-pitch-browse').innerText()),'native shirt occupant');
  // Suitability mode places role/stars in the interactive cell; the inherited
  // formation overview still carries the actual shirt number. Check both
  // representations without requiring a different native display mode.
  assert.match(await page.getByRole('region',{name:'Formation overview',exact:true}).innerText(),/Goalkeeper: 16 Misa\./);
  await complete('Selected goalkeeper',49);
  await open();await c.cancelDropdown();assert.equal(await slot().getAttribute('aria-label'),'Misa');
  checks.push('Misa is selected once, observed in the goalkeeper row and native-bound compact shirt, and persists after reopening/cancelling');
  await open();await c.choose('Retirar da Posição');changed=false;
  await poll(async()=>await slot().getAttribute('aria-label')==='Sem Escolhas','restored empty goalkeeper slot');
  await complete('Restored empty lineup',50);
  assert.equal(await page.locator('#fm-pitch-browse td').count(),0);
  checks.push('Native Retirar da Posição restores only the test selection and all 50 original rows');
  const audit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  const report={ownedSave,mvid:health.coreMvid,checks,timings,openCycles,violations:audit.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)})),errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,controlledTransportFaults:true,manualNVDA:false};
  const output=path.resolve(__dirname,'../../artifacts/tests/web/lineup.json');fs.mkdirSync(path.dirname(output),{recursive:true});fs.writeFileSync(output,JSON.stringify(report,null,2));
  assert.deepEqual(report.violations,[]);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);console.log(JSON.stringify(report,null,2));
 }catch(error){
  const output=path.resolve(__dirname,'../../artifacts/tests/web/lineup-failure.json');
  fs.writeFileSync(output,JSON.stringify({error:error.stack,checks,timings,stall:await page.evaluate(()=>window.qaDropdownStall||null),pending:await page.evaluate(()=>({dropdown:!!FM.dropdown.pending,owner:FM.core.dd})),errors:c.errors,requestFailures:c.requestFailures,network:c.network,transport:await c.transportTrace()},null,2));throw error;
 }finally{
  await page.evaluate(()=>window.qaAbortDropdownStall?.()).catch(()=>{});
  await page.evaluate(()=>window.qaRestoreDropdownTransport?.()).catch(()=>{});
  if(changed&&await slot().getAttribute('aria-label')==='Misa'){
   if(await page.evaluate(()=>!!FM.core.dd))await c.cancelDropdown();
   await open();await c.choose('Retirar da Posição');
  }
  if(originalPhase){
   const phase=page.getByRole('tab',{name:originalPhase,exact:true});
   if(await phase.count()&&await phase.getAttribute('aria-selected')!=='true')await c.activate(phase);
  }
  await c.browser.close();
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
