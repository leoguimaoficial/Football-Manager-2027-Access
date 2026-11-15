// Explicit native business commands on immutable, freshly reloaded owned QA
// branches. A placed-ad dialog is not saved publication proof; qualify that
// separately with live-staff-advert-persistence.cjs after native Save As/load.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),artifacts=path.join(root,'artifacts');
const action=process.argv.find(a=>a.startsWith('--action='))?.slice(9);
assert.ok(['publish','withdraw'].includes(action),'Explicit business action required');
const manifest=JSON.parse(fs.readFileSync(path.join(artifacts,'career-coverage-working-save.json'),'utf8'));
const checkpoint=manifest[action==='publish'?'staffAdvertWorking':'publishedStaffAdvertWorking'];
assert.ok(checkpoint&&manifest.ownedWorkingFiles.includes(checkpoint+'.fm'));
const load=manifest.lastVerifiedWorkingLoad;
assert.equal(load?.name,checkpoint,'Natively reload the exact immutable branch with --activate-working first');
const loadProof=JSON.parse(fs.readFileSync(path.join(artifacts,load.evidence),'utf8'));
assert.ok(loadProof.success&&loadProof.observations.loadedCheckpoint===checkpoint);
const attemptFile=path.join(artifacts,'tests/web/staff-advert-'+action+'-attempt.json');
if(fs.existsSync(attemptFile)){const previous=JSON.parse(fs.readFileSync(attemptFile,'utf8'));assert.ok(Date.parse(load.at)>Date.parse(previous.at),'Never replay an uncertain/completed command without a separately verified native reload');}
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,action,checkpoint,nativeReload:load.evidence,checks:[],audits:[],businessDispatches:0,savedOutcomeValidated:false,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const baseline=JSON.parse(fs.readFileSync(path.join(artifacts,'career-staff-adverts-before-rows.json'),'utf8')).rows;
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 async function go(uid,obj){const e=page.locator(`#fmnav-tree [role=treeitem][data-uid="${uid}"][data-obj="${obj}"]`).first(),q=page.getByRole('textbox',{name:'Search all screens',exact:true});await q.fill(await e.locator(':scope > span').textContent());await e.focus();const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');r.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await r).text(),/^Opening /);await settled();await q.fill('');await settled();}
 const rows=()=>page.locator('#fmroot table tbody tr').evaluateAll(rs=>rs.map(r=>[...r.cells].map(c=>c.textContent.trim().replace(/\s+/g,' '))));
 function beforeDispatch(){assert.equal(report.businessDispatches,0);report.businessDispatches=1;fs.mkdirSync(path.dirname(attemptFile),{recursive:true});fs.writeFileSync(attemptFile,JSON.stringify({at:report.at,checkpoint,nativeReload:load,action,outcome:'Uncertain until native state is observed; do not replay'},null,2));}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));assert.equal(loadProof.observations.mvid,report.health.coreMvid);
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await go('3400157696','2000104296');const own=action==='publish'?0:1,total=43+own;
  assert.equal(await page.locator('#fmroot').getByText(own?'1 anúncio de emprego criado':'0 anúncios de emprego criados',{exact:true}).count(),1);
  assert.equal(await page.locator('#fmroot').getByText(total+' anúncios de emprego no total',{exact:true}).count(),1);
  await c.activate(page.getByRole('button',{name:'ANÚNCIOS PARA ELEMENTOS DA EQUIPA TÉCNICA',exact:true}));await settled();assert.equal(await page.title(),'Anúncios para Elementos da Equipa Técnica');report.beforeRows=await rows();assert.equal(report.beforeRows.length,total);assert.deepEqual(report.beforeRows.filter(r=>!r[1].includes('Arsenal')),baseline);await audit('Actual immutable native vacancy baseline');
  if(action==='publish'){
   await go('1204137728','2000104296');assert.equal(await page.title(),'Equipa Técnica');
   // Resolve the actual headed department, never the second identically named
   // select by position. Debug additionally proves its native owner/Pointer.
   const ids=await page.getByRole('combobox',{name:'Colocar Anúncio',exact:true}).evaluateAll(es=>es.flatMap(e=>{let h=e.closest('p');while(h&&h.tagName!=='H3')h=h.previousElementSibling;return /^\d+ Elementos da Equipa de Recrutamento$/.test(h?.textContent||'')?[e.id]:[];}));assert.equal(ids.length,1);
   const id=ids[0];report.departmentControl=id;
   if(report.health.developerTools){const all=await page.getByRole('combobox',{name:/^(Fazer Pedido à Direcção|Colocar Anúncio)$/}).evaluateAll(es=>es.map(e=>e.id));report.nativeDepartment=await require('./native-staff-department-controls.cjs')(c,report.health.coreMvid,all);const command=report.nativeDepartment.commands.find(c=>c.id===id),native=report.nativeDepartment.native.find(n=>n.pointer===command?.pointer);assert.equal(native?.department,'ClubRecruitmentStaff');assert.equal(native.enabled,true);}
   const field=page.locator('#'+id);await field.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');opened.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await opened).text(),/^</);await poll(()=>field.getAttribute('data-loaded'),'actual recruitment advert options');
   await field.selectOption({label:'Observação, submenu'});const expanded=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/expand');expanded.catch(()=>{});await page.keyboard.press('ArrowRight');assert.match(await(await expanded).text(),/^</);await settled();const child=page.locator('select[data-chain][data-loaded]').last();report.options=await child.locator('option').allTextContents();assert.ok(report.options.includes('Olheiro'));await audit('Actual recruitment scout publication options');
   beforeDispatch();report.nativeReply=await c.choose('Olheiro');await poll(()=>page.title().then(t=>t==='Colocar Anúncio'),'native placed-advert confirmation');report.confirmation=await page.locator('#fmroot').innerText();assert.match(report.confirmation,/Colocou um anúncio para o cargo de olheiro no Arsenal\./);const preference=page.getByRole('checkbox',{name:'Não mostrar esta mensagem de novo.',exact:true});assert.equal(await preference.isChecked(),false);await audit('Native placed-advert confirmation');await c.activate(page.getByRole('button',{name:'Ok',exact:true}));report.dontShowPreferenceChanged=false;
   report.checks.push('One actual recruitment-department scout command reaches native placed-advert dialog; Dont-show preference unchanged. Saved publication is not inferred from this acknowledgement');
   await go('3400157696','2000104296');report.immediateSummary=(await page.locator('#fmroot').innerText()).split(/\r?\n/).filter(l=>/anúncios? de emprego/.test(l));if(report.health.developerTools)report.immediateNativeSummary=(await c.fetchText('/gui')).split(/\r?\n/).filter(l=>/SIText visible=True text="\d+ anúncios? de emprego/.test(l));
  }else{
   const ownRow=page.getByRole('row').filter({has:page.getByRole('button',{name:'Arsenal',exact:true})}).filter({hasText:'Olheiro'});assert.equal(await ownRow.count(),1);const radio=ownRow.getByRole('radio'),oldId=await radio.getAttribute('id');await c.activate(radio,'Space');assert.equal(await radio.isChecked(),true);await c.dropdown('Colocar Anúncio');report.options=await page.locator('select[data-dd][data-loaded]').last().locator('option').allTextContents();assert.ok(report.options.includes('Remover Anúncio'));await audit('Actual own-advert management menu');
   beforeDispatch();report.nativeReply=await c.choose('Remover Anúncio');await settled();report.afterRows=await rows();assert.deepEqual(report.afterRows,baseline);const stale=await page.evaluate(async id=>(await fetch('/activate?id='+encodeURIComponent(id)+'&screen='+encodeURIComponent(FMTOKEN),{method:'POST'})).text(),oldId);assert.match(stale,/Unknown element|no longer available|stale|Refresh the screen/i);report.staleReply=stale;assert.deepEqual(await rows(),baseline);await audit('Only own advertisement removed');report.checks.push('One native withdrawal removes only own scout record; all43other vacancies remain exact and deleted-row command is refused without retargeting');
  }
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
 }catch(e){report.failure=e.stack||String(e);throw e;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;const dest=path.join(artifacts,'tests/web/staff-advert-'+action+'-command.json');fs.mkdirSync(path.dirname(dest),{recursive:true});fs.writeFileSync(dest,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
