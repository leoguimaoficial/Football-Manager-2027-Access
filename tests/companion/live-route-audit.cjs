// Current native navigator discovery and semantic regression, not full business
// validation. Only real menu destinations are opened; no leaf action is submitted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const out=path.resolve(__dirname,'../../artifacts/tests/web'),captures=[],observations={};let current;
 const production=process.argv.includes('--production');
 const resumeIndex=process.argv.indexOf('--resume'),resume=resumeIndex<0?null:JSON.parse(fs.readFileSync(path.resolve(process.argv[resumeIndex+1]),'utf8').replace(/^\uFEFF/,''));
 const recheckIndex=process.argv.indexOf('--recheck-incoherent'),recheck=recheckIndex<0?null:JSON.parse(fs.readFileSync(path.resolve(process.argv[recheckIndex+1]),'utf8').replace(/^\uFEFF/,''));
 assert.ok(!(resume&&recheck),'Choose one explicit continuation boundary');
 const prefix=production?'production-route-audit':recheck?'route-audit-recheck':'route-audit';
 function write(name,extra={}){fs.writeFileSync(path.join(out,name.replace(/^route-audit/,prefix)+'.json'),JSON.stringify({observations,captures,current,errors:c.errors,requestFailures:c.requestFailures,network:c.network,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false,fullBusinessCoverage:false,...extra},null,2));}
 async function go(destination){
  const selector='#fmnav-tree [role=treeitem][data-uid="'+destination.uid+'"]'+(destination.obj===null?':not([data-obj])':'[data-obj="'+destination.obj+'"]');
  const entry=page.locator(selector).first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});
  await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await settled();
  if(await search.count()){await search.fill('');await settled();}
 }
 async function nativeHandles(controls){
  if(production)return{coherent:false,registrations:[],reason:'Developer registration introspection is absent in Release; this run validates native navigation/DOM semantics only'};
  const ids=[...new Set(controls.map(e=>e.id).filter(id=>/^[-\w:.]+$/.test(id)))];
  const snapshotState=()=>page.evaluate(()=>({token:FMTOKEN,refreshing:FM.core.refreshing,pending:FM.core.pending,
   ids:[...document.querySelectorAll('#fmroot button,#fmroot input,#fmroot select,#fmroot textarea,#fmroot a[href]')].map(e=>e.id).filter(Boolean).sort()}));
  const before=await snapshotState();
  // Read only the actual production registrations, adapter/native types and
  // command-context names. No validation/realization/activation, record values,
  // URLs, credentials or native command arguments are evaluated or emitted.
  const code=ids.length?require('./native-handle-inventory.cjs')(observations.mvid,ids):'';
  if(!ids.length)return{coherent:true,registrations:[]};
  const raw=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);
  assert.doesNotMatch(raw,/\[exception\]|\[compile\]/,'Read-only native action inventory');
  const after=await snapshotState(),registrations=raw.split(/\r?\n/).filter(l=>l.startsWith('HANDLE|')).map(line=>{const [,id,registration,handle,adapter,element,events,facts]=line.split('|');return{id,registration,handle,adapter,element,commandContexts:events?events.split(';'):[],nativeWidgetFacts:facts?facts.split(';'):[]};});
  assert.equal(registrations.length,ids.length,'Every requested registration has an explicit observation');
  const coherent=before.token===after.token&&!before.refreshing&&!after.refreshing&&!before.pending&&!after.pending
   &&JSON.stringify(before.ids)===JSON.stringify(after.ids)&&ids.every(id=>before.ids.includes(id));
  return{coherent,before,after,registrations,actionResults:'Not executed by read-only inventory; an overlapping compose/changed DOM is not coherent registration evidence, and empty event context does not imply an inert native callback'};
 }
 async function capture(destination,attempt=0){
  const native=production?'':await c.fetchText('/gui'),audit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  // Native GUI/AX inspection can overlap an ordinary SSE compose. Finish that
  // observed compose before correlating the DOM with native registrations.
  await settled();
  const snapshot=await page.locator('#fmroot').evaluate(root=>{
   const visible=e=>e.getClientRects().length>0&&!e.closest('[hidden],[aria-hidden=true]');
   // Record actual handlers and ownership attributes, never input values. This
   // inventories the rendered command path; it does not prove a native result.
   const controls=[...root.querySelectorAll('button,input,select,textarea,a[href]')].filter(visible).map(e=>({id:e.id,tag:e.tagName,role:e.getAttribute('role'),selected:e.getAttribute('aria-selected'),name:e.getAttribute('aria-label')||e.labels?.[0]?.textContent||e.textContent,type:e.getAttribute('type'),disabled:e.disabled,contextGuardPresent:e.hasAttribute('data-fm-command-context'),
    delegatedAttributeNames:[...e.attributes].map(a=>a.name).filter(n=>n.startsWith('data-')),
    events:Object.fromEntries(['onclick','onchange','oninput','onkeydown'].flatMap(n=>e.hasAttribute(n)?[[n,e.getAttribute(n)]]:[])),
    delegated:Object.fromEntries(['data-dd','data-fm-history','data-fm-menu-back','data-fm-menu-action','data-fm-nav','data-dialog-close','data-fm-page'].flatMap(n=>e.hasAttribute(n)?[[n,e.getAttribute(n)]]:[])),
    rowHeaders:e.closest('tr')?[...e.closest('tr').querySelectorAll('th')].map(x=>x.textContent):[],
    nativeActionValidation:'Not executed by this read-only route inspection'}));
   const headings=[...root.querySelectorAll('h1,h2,h3,h4,h5,h6')].filter(visible).map(e=>({id:e.id,tag:e.tagName,text:e.textContent}));
   // Names alone can satisfy axe while still exposing decorative asset noise.
   // Retain only name sources/context, never image URLs or proprietary bytes.
   // These hints require review; a generic caption is not proof of decoration.
   const media=[...document.querySelectorAll('img,[role=img],svg')].filter(visible).map(e=>{
    const labelledByText=(e.getAttribute('aria-labelledby')||'').split(/\s+/).filter(Boolean).map(id=>document.getElementById(id)?.textContent||'').join(' ').trim();
    const ariaLabel=e.getAttribute('aria-label')||'',alt=e.getAttribute('alt'),titleText=e.querySelector('title')?.textContent||e.getAttribute('title')||'';
    const nameHint=(ariaLabel||labelledByText||alt||titleText||'').trim();
    const section=e.closest('section,figure,tr');
    return{id:e.id,tag:e.tagName,role:e.getAttribute('role'),ariaLabel,labelledByText,alt,titleText,nameHint,
     genericNameCandidate:/^(?:club|clube|card[- ]frame|icon|ícone|image|imagem|frame|gauge|chart|gráfico)$/i.test(nameHint),
     nearbyHeading:section?.querySelector('h1,h2,h3,h4,h5,h6,figcaption,th')?.textContent||null,
     nearbyTablePresent:!!section?.querySelector('table')};
   });
   const ids=new Map();for(const e of document.querySelectorAll('[id]'))if(e.id)ids.set(e.id,(ids.get(e.id)||0)+1);
   const duplicateIds=[...ids].filter(([,count])=>count>1).map(([id,count])=>({id,count}));
   // Native ImageAdapter announcements use the inherited readonly paragraph,
   // not an HTML img. Inspect that surface too, without assuming every named
   // informative image should be hidden or treating text as asset bytes.
   const readonlyMediaText=[...root.querySelectorAll('p,td,th,li,button,h1,h2,h3,h4,h5,h6,span')].filter(e=>{
    if(!visible(e)||e.querySelector('p,td,th,li,button,h1,h2,h3,h4,h5,h6'))return false;
    if(e.tagName==='SPAN'&&e.parentElement?.closest('p,td,th,li,button,h1,h2,h3,h4,h5,h6'))return false;
    const text=e.textContent.trim();
    return /,\s*(?:image|imagem)\s*$/i.test(text)||/\bgauge\s*,\s*gauge\b/i.test(text)||/^(?:club|clube|card[- ]frame|icon|ícone|image|imagem|frame)$/i.test(text);
   }).map(e=>({id:e.id,tag:e.tagName,text:e.textContent.trim(),
    genericNameCandidate:/^(?:club|clube|card[- ]frame|icon|ícone|image|imagem|frame)(?:,\s*(?:image|imagem))?$/i.test(e.textContent.trim())||/\bgauge\s*,\s*gauge\b/i.test(e.textContent),
    technicalAssetNameCandidate:/^[A-Za-z][A-Za-z0-9_-]*(?:Icon|Image|Frame),\s*(?:image|imagem)$/i.test(e.textContent.trim()),
    nearbyHeading:e.closest('section,tr,figure')?.querySelector('h1,h2,h3,h4,h5,h6,th,figcaption')?.textContent||null}));
   const contentNotes=[...root.querySelectorAll('p')].filter(e=>visible(e)&&/chart, visual only|No readable text in this view|table not loaded|couldn't read all/i.test(e.textContent)).map(e=>{
    let h=e.previousElementSibling;while(h&&!/^H[1-6]$/.test(h.tagName))h=h.previousElementSibling;
    return{text:e.textContent,heading:h?.textContent||null};
   });
   return{language:document.documentElement.lang,headings,controls,media,readonlyMediaText,duplicateIds,contentNotes,tabs:[...root.querySelectorAll('[role=tab]')].filter(visible).map(e=>({text:e.textContent,selected:e.getAttribute('aria-selected')})),tables:[...root.querySelectorAll('table')].filter(visible).map(t=>({headers:[...t.querySelectorAll('thead th')].map(e=>e.textContent),rows:t.querySelectorAll('tbody tr').length})),focus:{id:document.activeElement.id,tag:document.activeElement.tagName}};
  });
  const handles=await nativeHandles(snapshot.controls);
  if(recheck&&!handles.coherent&&attempt===0){
   assert.deepEqual(audit.violations,[],'A semantic failure cannot be skipped as a metadata retry');
   (observations.registrationReadBoundaries??=[]).push({destination,attempt,handles,semanticViolations:[]});
   return capture(destination,1); // One additional read, never another native command.
  }
  if(handles.coherent){
   const registered=new Map(handles.registrations.map(r=>[r.id,r]));
   for(const control of snapshot.controls){
    const action=control.events.onclick?.match(/^act\('([^']+)'\)/);
    if(action&&!control.disabled)assert.equal(registered.get(action[1])?.registration,'present','Enabled actual button has no production command registration: '+control.name);
   }
  }
  const registrations=new Map(handles.registrations.map(r=>[r.id,r]));
  const availability=snapshot.controls.flatMap(control=>{
   const native=registrations.get(control.id),fact=native?.nativeWidgetFacts.find(x=>x.startsWith('VisualElement.enabledInHierarchy='));
   return fact?[{id:control.id,name:control.name,htmlDisabled:!!control.disabled,nativeEnabled:fact.endsWith('True'),registration:native.registration,adapter:native.adapter}]:[];
  });
  // This is a read-only observation, not an atomic native/DOM transaction.
  // A mismatch requires a stable native follow-up, never a forced HTML enable.
  const result={destination,title:await page.title(),token:await page.evaluate(()=>FMTOKEN),nativePanels:native.split(/\r?\n/).filter(l=>/^\[0\] Panel /.test(l)),...snapshot,nativeHandles:handles,availability,violations:audit.violations.map(v=>({id:v.id,impact:v.impact,help:v.help,nodes:v.nodes.map(n=>({target:n.target,html:n.html,summary:n.failureSummary}))}))};
  captures.push(result);write('route-audit-progress');
  assert.equal(result.language,observations.documentLanguage,'Destination preserves selected document language');
  assert.ok(result.title.trim(),'Actual page has a title');assert.ok(result.headings.length,'Actual content has a heading');
  assert.deepEqual(result.violations,[],'Actual destination semantic audit: '+destination.label);
  assert.deepEqual(result.duplicateIds,[],'All actual content IDs must be unique, including noninteractive headings and labels');
  assert.deepEqual(result.controls.filter(e=>/unknown control|unlabeled icon button/i.test(e.name||'')),[],'Unresolved actual native control names');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  console.log('PASS',captures.length,destination.uid,destination.obj,result.title,'controls='+result.controls.length,'tables='+result.tables.length);
 }
 try{
  observations.mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  observations.production=production;
  if(production)assert.equal(JSON.parse(await c.fetchText('/health')).developerTools,false);
  observations.controlInventoryVersion=10;
  observations.documentLanguage=await page.evaluate(()=>document.documentElement.lang);
  assert.equal(observations.documentLanguage,'pt','This native Portuguese route cohort starts in the restored selected language');
  if(production&&!await page.getByRole('heading',{name:'Portal',exact:true}).count()){
   const portal=await page.locator('#fmnav-tree > [role=treeitem][data-uid]').first().evaluate(e=>({uid:e.dataset.uid,obj:e.dataset.obj??null}));
   await go(portal);
  }
  observations.started=new Date().toISOString();
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const filename=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>filename.count(),'owned QA filename');observations.ownedSave=await filename.inputValue();assert.match(observations.ownedSave,/^FM27Access QA /);await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const nativeMenu=production?'Developer navigator introspection unavailable in Release':await c.fetchText('/navigator/list');fs.writeFileSync(path.join(out,prefix+'-native-menu.txt'),nativeMenu);
  observations.nativeUnavailable=nativeMenu.split(/\r?\n/).filter(line=>/DISABLED|CONDITION-OFF/.test(line));
  observations.entries=await page.locator('#fmnav-tree [role=treeitem][data-uid]').evaluateAll(es=>es.map(e=>({uid:e.dataset.uid,obj:e.dataset.obj??null,label:e.querySelector(':scope > span')?.textContent,depth:Number(e.getAttribute('aria-level')),disabled:e.getAttribute('aria-disabled')==='true',filter:e.dataset.filter})));
  fs.writeFileSync(path.join(out,prefix+'-navigator-tree.txt'),
   '# Actual native-derived companion navigator; private registration introspection is separate.\n'+observations.entries.map(e=>
    '  '.repeat(Math.max(0,e.depth-1))+e.label+' [uid='+e.uid+(e.obj?' obj='+e.obj:'')+']'+(e.disabled?' DISABLED':'')).join('\n')+'\n');
  const unique=new Map();for(const entry of observations.entries){const key=entry.uid+'|'+entry.obj,previous=unique.get(key);if(!previous||entry.depth>previous.depth)unique.set(key,entry);}
  observations.destinations=[...unique.values()].filter(e=>!e.disabled);observations.unavailable=[...unique.values()].filter(e=>e.disabled);
  const portal=observations.entries.find(e=>e.depth===1&&!e.disabled);assert.ok(portal);
  let targets=observations.destinations;
  if(recheck){
   assert.equal(recheck.success,true,'Recheck only a completed retained audit');
   assert.equal(recheck.observations.mvid,observations.mvid,'Never inherit another module');
   assert.equal(recheck.observations.controlInventoryVersion,observations.controlInventoryVersion);
   assert.equal(recheck.observations.ownedSave,observations.ownedSave);
   const keys=es=>es.map(e=>e.uid+'|'+e.obj);
   assert.deepEqual(keys(recheck.observations.destinations),keys(observations.destinations));
   assert.deepEqual(recheck.errors,[]);assert.deepEqual(recheck.requestFailures,[]);assert.deepEqual(recheck.network.failures,[]);
   assert.ok(recheck.captures.every(c=>c.violations.length===0));
   const pending=new Set(recheck.captures.filter(c=>!c.nativeHandles.coherent).map(c=>c.destination.uid+'|'+c.destination.obj));
   targets=observations.destinations.filter(d=>pending.has(d.uid+'|'+d.obj));
   observations.recheckedFrom={file:path.resolve(process.argv[recheckIndex+1]),destinations:targets,reason:'Explicit read-only follow-up for overlapping native composes; original observations retained, not one continuous run'};
  }
  if(resume){
   assert.equal(resume.observations.mvid,observations.mvid,'Never inherit another module qualification');
   assert.equal(resume.observations.controlInventoryVersion,observations.controlInventoryVersion,'Never inherit a capture without the current handler inventory');
   assert.equal(resume.observations.ownedSave,observations.ownedSave,'Exact owned QA checkpoint must match');
   const keys=es=>es.map(e=>e.uid+'|'+e.obj);
   assert.deepEqual(keys(resume.observations.destinations),keys(observations.destinations),'Actual native destination identities/order must remain unchanged');
   assert.deepEqual(resume.errors,[]);assert.deepEqual(resume.requestFailures,[]);assert.deepEqual(resume.network.failures,[]);
   assert.deepEqual(keys(resume.captures.map(c=>c.destination)),keys(observations.destinations.slice(0,resume.captures.length)));
   assert.ok(resume.captures.every(c=>c.violations.length===0),'An unresolved semantic failure cannot be skipped');
   captures.push(...resume.captures);observations.resumedFrom={file:path.resolve(process.argv[resumeIndex+1]),captures:resume.captures.length,reason:'Explicit agent investigation boundary; prior capture evidence retained, not a continuous-run claim'};
  }
  for(const destination of targets.slice(captures.length)){
   if(process.env.FM27_ROUTE_AUDIT_STOP&&fs.existsSync(process.env.FM27_ROUTE_AUDIT_STOP)){write('route-audit-interrupted',{success:false,reason:'Cooperative stop at a completed navigation boundary'});process.exitCode=2;return;}
   current=destination;await go(destination);await capture(destination);
   if(!await page.getByRole('textbox',{name:'Search all screens',exact:true}).count()){
    await page.keyboard.press('Escape');await poll(()=>page.getByRole('textbox',{name:'Search all screens',exact:true}).count(),'native modal Escape return');await settled();
   }
  }
  await go(portal);current=null;observations.finished=new Date().toISOString();write('route-audit',{success:true});
 }catch(error){
  const shot=production?'Native screenshot endpoint absent in Release':(await c.fetchText('/screenshot').catch(e=>e.message)).trim();
  observations.failureNativeScreenshot={reply:shot};
  if(fs.existsSync(shot)){const file=path.join(out,'route-audit-failure-native.png');fs.copyFileSync(shot,file);observations.failureNativeScreenshot.file=file;}
  await page.screenshot({path:path.join(out,'route-audit-failure-companion.png'),fullPage:true}).catch(e=>{observations.companionScreenshotError=e.message;});
  fs.writeFileSync(path.join(out,prefix+'-failure-native.txt'),production?'Native inspection endpoint absent in Release':await c.fetchText('/gui').catch(e=>e.message));
  fs.writeFileSync(path.join(out,'route-audit-failure.html'),await page.locator('#fmroot').innerHTML().catch(e=>e.message));
  write('route-audit-failure',{success:false,error:error.stack});throw error;
 }finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});


