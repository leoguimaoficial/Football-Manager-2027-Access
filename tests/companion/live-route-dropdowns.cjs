// Actual native ordinary dropdowns across every current navigator destination.
// Open/cancel and nested-folder traversal only; never select a business/value
// leaf. Editable/searchable comboboxes have separate typed tests and inventory.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'artifacts/tests/web/route-dropdowns.json');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,browserLaunchProfile:c.browserLaunchProfile,routes:[],checks:[],businessLeafRequests:0,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 let current=null;
 const sharedArg=process.argv.find(a=>a.startsWith('--shared-navigation-reference='));let sharedTrees=new Map();
 const resumeArg=process.argv.find(a=>a.startsWith('--resume='));
 const reuseCurrent=process.argv.includes('--reuse-shared-navigation');
 function write(){fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify({...report,current,errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));}
 const selected=r=>{if(new URL(r.url()).pathname==='/dropdown/select')report.businessLeafRequests++;};
 async function go(d){const e=page.locator('#fmnav-tree [role=treeitem][data-uid="'+d.uid+'"]'+(d.obj===null?':not([data-obj])':'[data-obj="'+d.obj+'"]')).first(),q=page.getByRole('textbox',{name:'Search all screens',exact:true});await q.fill(await e.locator(':scope > span').textContent());await e.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await settled();if(await q.count()){await q.fill('');await settled();}}
 async function allOptions(field,observation){
  const read=()=>field.locator('option:not([data-more])').evaluateAll(es=>es.map(e=>({value:e.value,text:e.textContent,disabled:e.disabled,folder:e.hasAttribute('data-sub')})));
  observation.loadMore??=[];
  while(await field.locator('option[data-more]').count()){
   const before=await read(),more=field.locator('option[data-more]');assert.equal(await more.count(),1);const label=await more.textContent();
   await field.focus();await field.selectOption({label});assert.equal(await field.evaluate(e=>e.options[e.selectedIndex].hasAttribute('data-more')),true,'Enter is reserved for the actual native continuation, never a leaf');
   const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/more');response.catch(()=>{});await page.keyboard.press('Enter');const reply=await response;assert.equal(reply.status(),200);assert.match(await reply.text(),/^</);
   await poll(()=>read().then(options=>options.length>before.length),'native dropdown continuation actually appends records');const after=await read();assert.deepEqual(after.slice(0,before.length),before,'Continuation preserves all earlier native choices/order');observation.loadMore.push({label,before:before.length,after:after.length});assert.equal(report.businessLeafRequests,0);write();
  }
  const options=await read();assert.ok(options.length,'Actual native options exist');return options;
 }
 async function folders(field,trail,depth=0,ancestors=new Set(),observation={}){
  const snapshot=await allOptions(field,observation);
  const signature=JSON.stringify(snapshot);
  assert.ok(!ancestors.has(signature),'Repeated ancestor menu requires native identity investigation; do not silently skip or recurse indefinitely');
  const branch=new Set(ancestors);branch.add(signature);
  const children=snapshot.filter(e=>e.folder&&!e.disabled);
  for(const child of children){
   await field.focus();await field.selectOption({value:child.value});
   const expanded=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/expand');expanded.catch(()=>{});await page.keyboard.press('ArrowRight');assert.match(await(await expanded).text(),/^</);
   await poll(()=>page.locator('select[data-chain][data-loaded]').count().then(n=>n===depth+1),'actual native nested folder');
   const nested=page.locator('select[data-chain][data-loaded]').last(),item={folder:child.text,depth:depth+1};trail.push(item);write();
   await folders(nested,trail,depth+1,branch,item);
   item.options=await allOptions(nested,item);
   const collapsed=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/collapse');collapsed.catch(()=>{});await nested.focus();await page.keyboard.press('ArrowLeft');assert.equal(await(await collapsed).text(),'Submenu closed.');
   await poll(()=>field.evaluate(e=>document.activeElement===e),'exact parent focus after native submenu collapse');
  }
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));assert.equal(report.health.screen,'Live');
  const baseline=JSON.parse(fs.readFileSync(path.join(root,'artifacts/current-original-save-preservation-baseline.json'),'utf8').replace(/^\uFEFF/,''));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});report.checkpoint=await file.inputValue();assert.ok(baseline.protectedFiles.some(f=>f.name===report.checkpoint+'.fm'),'Read-only full route cohort requires an exact protected QA checkpoint');await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  if(sharedArg){const reference=JSON.parse(fs.readFileSync(path.resolve(root,sharedArg.slice('--shared-navigation-reference='.length)),'utf8'));sharedTrees=require('./shared-navigation-reference.cjs').build(reference,report.health,report.checkpoint);assert.ok(sharedTrees.size,'Two actually completed matching native contexts are required');report.sharedNavigationReference={source:sharedArg.slice('--shared-navigation-reference='.length),trees:[...sharedTrees.values()],boundary:'Shared navigation descendants are qualified by two retained identical native trees; each current root still opens, supplies its own exact options, closes and returns focus. Context-specific descendants are read on every screen. No claim that shared descendants were re-read in every destination.'};}
  const entries=await page.locator('#fmnav-tree [role=treeitem][data-uid]').evaluateAll(es=>es.map(e=>({uid:e.dataset.uid,obj:e.dataset.obj??null,label:e.querySelector(':scope > span')?.textContent,depth:Number(e.getAttribute('aria-level')),disabled:e.getAttribute('aria-disabled')==='true'})));
  const unique=new Map();for(const e of entries){const key=e.uid+'|'+e.obj;if(!unique.has(key)||unique.get(key).depth<e.depth)unique.set(key,e);}report.destinations=[...unique.values()].filter(e=>!e.disabled);report.unavailableDestinations=[...unique.values()].filter(e=>e.disabled);assert.ok(report.destinations.length>100);
  page.on('request',selected);
  if(resumeArg){const previous=JSON.parse(fs.readFileSync(path.resolve(root,resumeArg.slice(9)),'utf8'));assert.equal(previous.health.coreMvid,report.health.coreMvid);assert.equal(previous.health.pid,report.health.pid);assert.equal(previous.checkpoint,report.checkpoint);assert.deepEqual(previous.destinations,report.destinations);assert.deepEqual(previous.browserLaunchProfile,report.browserLaunchProfile);assert.deepEqual(previous.errors,[]);assert.deepEqual(previous.requestFailures,[]);assert.deepEqual(previous.network.failures,[]);assert.equal(previous.businessLeafRequests,0);assert.deepEqual(previous.routes.map(r=>r.destination),report.destinations.slice(0,previous.routes.length));assert.ok(previous.routes.every(r=>r.status.startsWith('Ordinary native dropdown opening/cancellation checked')&&r.controls.every(c=>c.status==='Native open/cancel and nested focus validated')));report.routes.push(...previous.routes);report.resumedFrom={source:resumeArg.slice(9),completedRoutes:previous.routes.length,boundary:'Explicit completed-prefix continuation after retained test investigation; not one uninterrupted sweep. No failed/in-progress control imported.'};}
  for(const destination of report.destinations.slice(report.routes.length)){
   current={destination,controls:[],status:'In analysis'};write();await go(destination);current.title=await page.title();
   const fields=await page.locator('#fmroot select[data-dd]').evaluateAll(es=>es.filter(e=>!e.disabled&&e.getClientRects().length).map(e=>{const content=document.querySelector('#fmsec1-h');return{id:e.id,name:e.getAttribute('aria-label'),context:e.getAttribute('data-fm-command-context'),before:e.textContent,navigationControl:!!content&&!!(e.compareDocumentPosition(content)&Node.DOCUMENT_POSITION_FOLLOWING)};}));
   current.disabledOrdinaryDropdowns=await page.locator('#fmroot select:disabled').count();current.searchableComboboxesNotCovered=await page.locator('#fmroot input[data-sdd]').count();assert.equal(new Set(fields.map(f=>f.id)).size,fields.length);
   for(const field of fields){
    assert.ok(field.name?.trim(),'Actual native dropdown has an accessible name');assert.match(field.id,/^[-\w:.]+$/);const element=page.locator('[id="'+field.id+'"]');assert.equal(await element.count(),1,'Native origin remains present');assert.equal(await element.isEnabled(),true);
    const item={...field,folders:[],status:'In analysis'};current.controls.push(item);write();
    await element.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');opened.catch(()=>{});await page.keyboard.press('Enter');const response=await opened;assert.equal(response.status(),200);const body=await response.text();assert.match(body,/^</,'Native dropdown must actually open, not merely acknowledge dispatch');await poll(()=>element.getAttribute('data-loaded'),'actual dropdown options delivered');
    item.options=await allOptions(element,item);const shared=field.navigationControl&&sharedTrees.get(require('./shared-navigation-reference.cjs').key(field,item.options));
    if(shared)item.sharedNavigationTree={...shared,descendantsReadInThisDestination:false};else await folders(element,item.folders,0,new Set(),item);
    await c.cancelDropdown();await settled();assert.equal(await page.evaluate(()=>document.activeElement.id),field.id,'Cancel restores exact still-present origin');item.after=await element.textContent();
    // Learning native menu size/cursor can legitimately change N-of-M. Keep
    // differences visible as observations; this test does not assert business
    // value persistence from menu text or silently discard those differences.
    item.closedTextChanged=item.after!==item.before;item.businessValuePreservationQualified=false;item.status='Native open/cancel and nested focus validated';assert.equal(report.businessLeafRequests,0);write();
   }
   if(!await page.getByRole('textbox',{name:'Search all screens',exact:true}).count()){await page.keyboard.press('Escape');await poll(()=>page.getByRole('textbox',{name:'Search all screens',exact:true}).count(),'native modal cancellation returns to the navigator');await settled();current.modalReturn={nativeEscapeSent:true,returnedTitle:await page.title()};}
   current.status='Ordinary native dropdown opening/cancellation checked; leaf and searchable behavior not covered';report.routes.push(current);current=null;
   if(reuseCurrent){
    const localReference={...report,errors:c.errors,requestFailures:c.requestFailures,network:c.network};
    const qualified=require('./shared-navigation-reference.cjs').build(localReference,report.health,report.checkpoint);
    for(const [key,tree] of qualified)sharedTrees.set(key,tree);
    report.sharedNavigationReference={source:'Two complete identical native trees retained in this same report before reuse',trees:[...sharedTrees.values()],boundary:'Only shared navigation descendants use two completed independent contexts. Every current root still opens, reads actual options, cancels and restores exact focus. Context-specific descendants are read on each destination; no claim of repeated shared descendants on every screen.'};
   }
   write();console.log(JSON.stringify({routes:report.routes.length,destination:destination.label,dropdowns:fields.length}));
  }
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);assert.equal(report.businessLeafRequests,0);report.checks.push('Every currently rendered enabled ordinary native dropdown opens with actual options, cancels and restores exact focus; contextual folders/continuations are read here, any shared navigation descendant reuse is explicitly referenced; no leaf selected');report.success=true;
 }catch(e){report.failure=e.stack||String(e);throw e;}
 finally{page.off('request',selected);if(await page.locator('select[data-dd][data-loaded]').count().catch(()=>0)){try{await c.cancelDropdown();report.observedOpenMenuCleanup='Closed once through its native owner';}catch(e){report.cleanupFailure=e.message;}}write();console.log(JSON.stringify({success:report.success,routes:report.routes.length,controls:report.routes.reduce((n,r)=>n+r.controls.length,0),failure:report.failure}));await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
