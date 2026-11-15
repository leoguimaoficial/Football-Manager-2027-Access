// Actual navigation leaves from the independently retained native World tree.
// Representative native entity families, not all database records. No business
// command, save, simulation, hidden-attribute query or licensing/name override.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'artifacts/tests/web/world-destinations.json');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,cases:[],audits:[],browserLaunchProfile:c.browserLaunchProfile,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false,allDatabaseRecordsQualified:false};
 const sourceArg=process.argv.find(a=>a.startsWith('--native-menu-reference='));
 function write(){fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify({...report,errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));}
 async function audit(stage){const unresolved=await page.locator('#fmroot button,#fmroot input,#fmroot select,#fmroot [role="button"]').evaluateAll(es=>es.filter(e=>e.getClientRects().length&&/unknown control|unlabeled icon button/i.test(e.getAttribute('aria-label')||e.textContent)).map(e=>({tag:e.tagName,id:e.id,name:e.getAttribute('aria-label')||e.textContent})));assert.deepEqual(unresolved,[],'Actual discovered destination has no unresolved native control-name marker');const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations.map(v=>v.id),unresolved});assert.deepEqual(a.violations,[]);}
 async function portal(){const e=page.locator('#fmnav-tree [role=treeitem][data-uid="989379584"]:not([data-obj])').first(),q=page.getByRole('textbox',{name:'Search all screens',exact:true});await q.fill(await e.locator(':scope > span').textContent());await e.focus();const reply=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');reply.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await reply).text(),/^Opening /);await settled();await q.fill('');await settled();assert.equal(await page.title(),'Portal');}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));assert.ok(sourceArg,'Explicit independently retained native menu tree is required');
  report.nativeMenuReference=sourceArg.slice('--native-menu-reference='.length);const reference=JSON.parse(fs.readFileSync(path.resolve(root,report.nativeMenuReference),'utf8'));
  assert.equal(reference.health.pid,report.health.pid);assert.equal(reference.businessLeafRequests,0);assert.deepEqual(reference.errors,[]);assert.deepEqual(reference.requestFailures,[]);assert.deepEqual(reference.network.failures,[]);
  if(reference.health.coreMvid!==report.health.coreMvid){
   assert.ok(process.argv.includes('--allow-discovery-reference'),'A different core can supply candidate labels only with an explicit discovery-only reference; no old native qualification is inherited');
   report.discoveryReferenceBoundary={coreMvid:reference.health.coreMvid,sameNativeProcess:true,wholeTreeObservedInCurrentCore:false,scope:'Retained tree discovers candidate paths only. Every chosen branch/option is opened and verified against actual current options; current navigation/result/return evidence is collected anew. No shared-tree or business-action pass is inherited.'};
  }
  const world=reference.routes[0].controls.find(c=>c.name==='World'&&c.status==='Native open/cancel and nested focus validated');assert.ok(world?.folders.length);
  const paths=[],stack=[];for(const folder of world.folders){stack.length=folder.depth-1;stack.push(folder.folder);for(const option of folder.options)if(!option.folder&&!option.disabled)paths.push([...stack,option.text]);}
  const select=(name,predicate)=>{const matching=paths.filter(predicate);assert.equal(matching.length,1,'Exactly one independently observed native path for '+name);return{name,path:matching[0]};};
  const cases=[
   select('English women club',p=>p[0]==='Clubes, submenu'&&p.includes('Mulheres, submenu')&&p.at(-1)==='Chelsea'),
   select('Brazil men club using its official game abbreviation',p=>p[0]==='Clubes, submenu'&&p.includes('Brasil, submenu')&&p.at(-1)==='FLA'),
   select('Women competition',p=>p[0]==='Competições, submenu'&&p.includes('Mulheres, submenu')&&p.at(-1)==="Barclays Women's Super League"),
   select('Continent',p=>p[0]==='Continentes, submenu'&&p.at(-1)==='Europa'),
   select('Nation overview',p=>p[0]==='Nações, submenu'&&p.length===4&&p.at(-1)==='Portugal'),
   select('Women national team',p=>p[0]==='Nações, submenu'&&p.includes('Selecção Nacional feminina, submenu')&&p.at(-1)==='Portugal')
  ];
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');report.checkpoint=await page.getByRole('textbox',{name:'File name',exact:true}).inputValue();assert.equal(report.checkpoint,reference.checkpoint);await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const baseline=JSON.parse(fs.readFileSync(path.join(root,'artifacts/current-original-save-preservation-baseline.json'),'utf8').replace(/^\uFEFF/,'')),file=baseline.protectedFiles.find(f=>f.name===report.checkpoint+'.fm');assert.ok(file);report.checkpointSha256=crypto.createHash('sha256').update(fs.readFileSync(path.join(process.env.USERPROFILE,'Documents/Sports Interactive/Football Manager 27/games',file.name))).digest('hex');assert.equal(report.checkpointSha256,file.sha256);
  const leagueName=await page.locator('#fmnav-tree [role=treeitem][data-uid="420575744"][data-obj="2000101095"] > span').first().textContent();assert.ok(leagueName.trim());
  for(const entry of cases){
   await portal();const origin=await page.getByRole('combobox',{name:'World',exact:true}).getAttribute('id'),beforeToken=await page.evaluate(()=>FMTOKEN);const observed={...entry,origin,originToken:beforeToken,navigationSubmissions:0};report.cases.push(observed);write();
   await c.dropdown('World');
   for(const [depth,label] of entry.path.slice(0,-1).entries()){
    const field=page.locator('select[data-dd][data-loaded]').last();assert.equal(await field.locator('option').filter({hasText:new RegExp('^'+label.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')+'$')}).count(),1);
    await field.selectOption({label});assert.equal(await field.evaluate(e=>e.options[e.selectedIndex].getAttribute('data-sub')),'1');const expanded=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/expand');expanded.catch(()=>{});await field.focus();await page.keyboard.press('ArrowRight');assert.match(await(await expanded).text(),/^</);await poll(()=>page.locator('select[data-chain][data-loaded]').count().then(n=>n===depth+1),'actual World submenu');
   }
   const leaf=entry.path.at(-1),field=page.locator('select[data-dd][data-loaded]').last();await field.selectOption({label:leaf});assert.equal(await field.evaluate(e=>e.options[e.selectedIndex].hasAttribute('data-sub')||e.options[e.selectedIndex].hasAttribute('data-more')),false);
   observed.uncertainNavigation=true;observed.navigationSubmissions=1;write();await c.choose(leaf);await settled();
   await poll(()=>page.evaluate(token=>FMTOKEN!==token,beforeToken),'actual native entity destination differs from Portal');observed.destinationTitle=await page.title();observed.destinationToken=await page.evaluate(()=>FMTOKEN);assert.notEqual(observed.destinationTitle,'Portal');
   if(['English women club','Brazil men club using its official game abbreviation','Continent','Nation overview'].includes(entry.name))assert.equal(observed.destinationTitle,leaf,'Site title identifies its actual bound entity rather than an unrelated reused asset caption');
   if(entry.name==='Women competition')assert.ok([leaf,leagueName].includes(observed.destinationTitle),'Competition site identifies its actual native competition');
   const expected=[leaf,...(entry.name==='Women competition'?[leagueName]:[])];observed.identityEvidence=await page.locator('#fmroot').evaluate((root,names)=>[...root.querySelectorAll('h1,h2,h3,h4,p,th,td')].filter(e=>!e.closest('#fmnav-tree')&&!e.querySelector('select,input,ul[role=tree]')).map(e=>e.textContent.trim()).filter(text=>names.some(name=>text===name||text.includes(name))),expected);assert.ok(observed.identityEvidence.length,'Actual native destination identifies its selected entity outside the menu/tree');await audit(entry.name+' actual destination');
   observed.uncertainNavigation=false;observed.resultBoundary='Observed native panel/title/entity caption and original return, not an independent current engine-record-ID query';write();
   await c.activate(page.getByRole('button',{name:'Back',exact:true}));await poll(()=>page.title().then(t=>t==='Portal'),'native original Portal return');await settled();assert.equal(await page.evaluate(()=>document.activeElement.id),origin);await audit(entry.name+' exact World origin return');observed.returned=true;write();
  }
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
 }catch(e){report.failure=e.stack||String(e);report.failureBody=await page.locator('#fmroot').innerText().catch(()=>null);throw e;}
 finally{write();console.log(JSON.stringify({success:report.success,cases:report.cases.length,audits:report.audits.length,failure:report.failure}));await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
