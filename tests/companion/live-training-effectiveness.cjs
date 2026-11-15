// Actual Portuguese QA career, original HTML and live native training gauges.
// Read-only views and native navigation; no training-plan edit, save or simulation.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
async function run(c){
 const {page,poll,settled}=c,checks=[],audits=[],observations=[];
 const captions=['Nível geral','Entendimento táctico','Capacidade do jogador','Adesão do jogador','Informações sobre o próximo jogo'];
 const fullCaptions=['Eficácia global',...captions.slice(1)];
 const view=()=>page.getByRole('checkbox',{name:'Visão da equipa',exact:true});
 const focus=()=>page.evaluate(()=>document.activeElement?.id);
 const tables=()=>page.locator('#fmroot table').evaluateAll(ts=>ts.map(t=>({headers:[...t.querySelectorAll('thead th')].map(e=>e.textContent.trim()),rows:[...t.querySelectorAll('tbody tr')].map(tr=>[...tr.querySelectorAll('th,td')].map(e=>e.textContent.trim()))})));
 let mvid,summary,factors,playerView,developerTools,reference,checkpoint,checkpointSha256;
 const referenceArg=process.argv.find(a=>a.startsWith('--native-reference='));
 const checkpointArg=process.argv.find(a=>a.startsWith('--reference-checkpoint='));
 const root=path.resolve(__dirname,'../..'),readiness=[];
 const evidenceScope=()=>({developerTools,nativeReference:referenceArg?.slice(19),referenceCheckpoint:reference?checkpoint:null,checkpointSha256,
  nativeValuesObservedInCurrentProcess:developerTools===true,
  boundary:developerTools?'Current main-thread native field observations':'Current public native interactions/DOM compared with separately retained native measurements on the same protected immutable checkpoint',readiness});
 const evalNative=async code=>{const text=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);assert.doesNotMatch(text,/\[exception\]|\[compile\].*error/);return text;};
 const scope=code=>`foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}"){var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;${code}}`;
 // GetState reads native SIStepper/SIRadialGauge fields, independently of the
 // caption/announcement/HTML changes under test. Do not infer values from DOM.
 async function nativeSummary(ids){
  if(reference)return reference.summary;
  const text=await evalNative(scope(`var map=(System.Collections.IDictionary)a.GetType("FM27Access.Plugin.Server.ServerHandles").GetField("_handles",f).GetValue(null);foreach(string id in new[]{${ids.map(id=>JSON.stringify(id)).join(',')}}){var h=map[id];if(h==null)throw new System.Exception("Native summary handle absent");var e=h.GetType().GetField("El",f).GetValue(h);System.Console.WriteLine("STATE|"+e.GetType().GetMethod("GetState").Invoke(e,null));}`));
  return text.split(/\r?\n/).filter(s=>s.startsWith('STATE|')).map(s=>s.slice(6));
 }
 async function nativeDetails(expectedChecked=false,individualName=null){
  if(reference){
   const observed=individualName?reference.observations.find(o=>o.individual?.name===individualName)
    :expectedChecked?reference.observations.find(o=>o.nativePlayers):reference.observations.find(o=>o.native&&o.summary);
   assert.ok(observed,'Independent retained native measurements for this exact view/player');
   const native=observed.nativePlayers||observed.native;
   return {...native,source:'Retained native checkpoint reference; not current private introspection'};
  }
  const text=await evalNative(scope(`var root=a.GetType("FM27Access.Plugin.FM27PanelCatalog").GetMethod("GetForegroundPanel",f).Invoke(null,null);var cache=System.Activator.CreateInstance(a.GetType("FM27Access.Plugin.ClassificationCache"),true);var all=(System.Collections.IEnumerable)a.GetType("FM27Access.Plugin.TreeWalker").GetMethod("FindAllStopsInContainer",f).Invoke(null,new object[]{root,cache});string lastName="";foreach(var s in all){var t=s.GetType();var role=(string)t.GetProperty("Role").GetValue(s);if(role=="button"&&(bool)t.GetProperty("CanActivate").GetValue(s))lastName=(string)t.GetMethod("GetLabel").Invoke(s,null);if(t.Name=="StepperAdapter"&&t.GetMethod("Gauge",f).Invoke(s,null)!=null)System.Console.WriteLine("STEPPER|"+t.GetMethod("GetState").Invoke(s,null));if(t.Name=="RadialGaugeAdapter")System.Console.WriteLine("RADIAL|"+lastName+"|"+t.GetMethod("GetState").Invoke(s,null));if(t.Name=="CheckBoxAdapter")System.Console.WriteLine("CHECKBOX|"+t.GetMethod("GetState").Invoke(s,null));}`));
  return {raw:text,steppers:text.split(/\r?\n/).filter(s=>s.startsWith('STEPPER|')).map(s=>s.slice(8)),radials:text.split(/\r?\n/).filter(s=>s.startsWith('RADIAL|')).map(s=>{const p=s.split('|');return{name:p[1].replace(/<[^>]*>/g,''),state:p[2]};}),checked:/^CHECKBOX\|checked$/m.test(text)};
 }
 async function go(uid){
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);
  await poll(()=>page.evaluate(id=>FMTOKEN.split('\x1f').some(p=>p===id||p.startsWith(id+'#')),String(uid)),'native destination '+uid);await settled();await search.fill('');await settled();
 }
 async function audit(name){const r=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:r.violations.map(v=>v.id)});assert.deepEqual(r.violations,[]);}
 async function verifyDetails(){
  await poll(()=>view().count(),'native tactical effectiveness details');await settled();
  assert.equal(await view().isChecked(),false);
  const native=await nativeDetails();assert.equal(native.checked,false);assert.deepEqual(native.steppers,summary);
  const values=await page.locator('#fmroot p').filter({hasText:/ of 100$/}).evaluateAll(es=>es.map(e=>({text:e.textContent,onclick:e.getAttribute('onclick'),tabindex:e.getAttribute('tabindex')})));
  assert.deepEqual(values,fullCaptions.map((caption,i)=>({text:caption+': '+summary[i],onclick:null,tabindex:null})));
  assert.equal(await page.getByRole('button').filter({hasText:/ of 100$/}).count(),0,'Native disabled navigation gauges are read-only values');
  const rows=await tables();assert.equal(rows.length,5);assert.ok(rows.every(t=>JSON.stringify(t.headers)==='["Factor","Value"]'));
  const flat=rows.flatMap(t=>t.rows);assert.equal(flat.length,17);assert.ok(flat.every(r=>r.length===2&&r.every(Boolean)));
  assert.deepEqual(flat.map(r=>r[1]),native.radials.map(r=>r.state));assert.ok(flat.every(r=>!r[1].includes('Gauge')));
  if(factors)assert.deepEqual(rows,factors);else factors=rows;
  return {native,values,tables:rows};
 }
 async function toggle(checked){
  if(await view().isChecked()!==checked)await c.activate(view(),' ');
  await poll(async()=>await view().isChecked()===checked,'native team-view checkbox');
  await poll(async()=>{const t=await tables();return t.length===5&&t.reduce((n,t)=>n+t.rows.length,0)===(checked?55:17);},'actual native view tables');await settled();
  const native=await nativeDetails(checked);assert.equal(native.checked,checked);assert.equal(await view().evaluate(e=>e===document.activeElement),true);return native;
 }
 try{
  const health=JSON.parse(await c.fetchText('/health'));mvid=health.coreMvid;developerTools=health.developerTools!==false;
  if(!await page.getByRole('heading',{name:'Portal',exact:true}).count())await go(989379584);
  assert.equal(await page.getByRole('heading',{name:'Portal',exact:true}).count(),1);
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned checkpoint');checkpoint=await file.inputValue();assert.match(checkpoint,/^FM27Access QA /);await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  if(!developerTools){
   assert.ok(referenceArg&&checkpointArg,'Release requires explicit native-reference and its independently recorded checkpoint context');
   assert.equal(checkpoint,checkpointArg.slice(23));
   const baseline=JSON.parse(fs.readFileSync(path.join(root,'artifacts/current-original-save-preservation-baseline.json'),'utf8').replace(/^\uFEFF/,''));
   const original=baseline.protectedFiles.find(f=>f.name===checkpoint+'.fm');assert.ok(original,'Only an independently protected immutable checkpoint qualifies this retained oracle');
   checkpointSha256=crypto.createHash('sha256').update(fs.readFileSync(path.join(process.env.USERPROFILE,'Documents/Sports Interactive/Football Manager 27/games',original.name))).digest('hex');assert.equal(checkpointSha256,original.sha256);
   reference=JSON.parse(fs.readFileSync(path.resolve(root,referenceArg.slice(19)),'utf8'));assert.equal(reference.success,true);assert.equal(reference.summary.length,5);assert.ok(reference.observations.some(o=>o.nativePlayers?.radials.length===55));assert.deepEqual(reference.jsErrors,[]);assert.deepEqual(reference.requestFailures,[]);assert.deepEqual(reference.network.failures,[]);
  }
  await go(305880064);
  const buttons=page.getByRole('button').filter({hasText:/ of 100$/});const started=Date.now(),initialCount=await buttons.count();await poll(()=>buttons.count().then(n=>n===5),'five actually loaded native summary commands');readiness.push({initialCount,waitedMs:Date.now()-started,finalCount:await buttons.count()});assert.equal(await buttons.count(),5);
  const ids=await buttons.evaluateAll(es=>es.map(e=>e.id));summary=await nativeSummary(ids);assert.equal(summary.length,5);
  assert.deepEqual(await buttons.allTextContents(),captions.map((caption,i)=>caption+', '+summary[i]));assert.ok(await buttons.evaluateAll(es=>es.every(e=>!e.disabled)));
  await audit('Native training overview');checks.push('All five summary gauges retain actual native values, Portuguese captions and enabled detail commands');
  for(let i=0;i<captions.length;i++){
   const button=page.getByRole('button',{name:captions[i]+', '+summary[i],exact:true}),origin=await button.getAttribute('id');
   await c.activate(button);const observed=await verifyDetails();observations.push({summary:captions[i],origin,...observed});
   if(i===0)await audit('Native full tactical effectiveness factors');
   const back=page.locator('[data-fm-history="back"]');await c.activate(back);
   await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'exact summary origin '+captions[i]);assert.deepEqual(await buttons.allTextContents(),captions.map((caption,j)=>caption+', '+summary[j]));
  }
  checks.push('Each of five real commands opens native details; all read-only values and 17 factor rows agree with native fields; Back restores each exact opener');
  await c.activate(page.getByRole('button',{name:captions[0]+', '+summary[0],exact:true}));await verifyDetails();
  const nativePlayers=await toggle(true);playerView=await tables();assert.ok(playerView.every(t=>JSON.stringify(t.headers)==='["Esc","Nome","Valor"]'&&t.rows.length===11));
  const players=playerView.flatMap(t=>t.rows);assert.equal(nativePlayers.radials.length,55);
  assert.deepEqual(players.map(r=>({name:r[1],state:r[2]})),nativePlayers.radials);
  assert.equal(await page.locator('#fmroot table tbody').getByRole('button').count(),55);assert.ok(players.every(r=>r.length===3&&r.every(Boolean)&&!r[2].includes('Gauge')));
  await audit('Native tactical effectiveness player tables');observations.push({playerView,nativePlayers});
  checks.push('Keyboard team view produces five semantic native tables and all 55 player/value pairs match actual native gauges without technical Gauge noise');
  const player=page.locator('#fmroot table tbody tr').first().getByRole('button').first(),playerName=await player.textContent(),playerOrigin=await player.getAttribute('id');
  await c.activate(player);await poll(()=>page.getByRole('heading',{name:'Relatório de jogador',exact:true}).count(),'actual player report');await settled();assert.ok((await page.locator('#fmroot').innerText()).includes(playerName));
  await c.activate(page.locator('[data-fm-history="back"]'));await poll(()=>view().count(),'native detail return');await settled();
  assert.equal(await view().isChecked(),false,'The native card recreates its local display variable');assert.equal((await nativeDetails()).checked,false);
  assert.equal(await page.locator('#'+playerOrigin).count(),0);const destination=page.locator('#fmroot [id^="fmsec"][id$="-h"]').last();
  await poll(()=>destination.evaluate(e=>e===document.activeElement),'logical native parent heading when old row is absent');assert.ok(await page.evaluate(()=>FM.core.historyMove),'Later native body may still restore exact origin');
  await page.keyboard.press('Tab');const userFocus=await focus();assert.equal(await page.evaluate(()=>FM.core.historyMove),null);await page.evaluate(()=>FM.core.refresh(false,true));await settled();assert.equal(await focus(),userFocus);
  await audit('Native parent view recreation and logical focus');checks.push('Correct player report and native Back preserve the actual reset view, identify the parent heading and retain exact-target handoff until user Tab cancels it');
  await verifyDetails();await audit('Restored native factor view');
  const restoredPlayers=await toggle(true);assert.equal(restoredPlayers.checked,true);await toggle(false);await verifyDetails();
  checks.push('Inverse keyboard view change restores native unchecked state, all 17 original factors and focus without a plan edit');
  await go(195478016);
  const tacticTable=page.locator('#fmroot table').filter({has:page.getByRole('columnheader',{name:'Eficácia',exact:true})});assert.equal(await tacticTable.count(),1);
  const headers=await tacticTable.locator('thead th').allTextContents(),column=headers.indexOf('Eficácia'),nameColumn=headers.indexOf('Nome');assert.ok(column>=0&&nameColumn>=0);
  const rowMetrics=await tacticTable.locator('tbody tr').evaluateAll((rows,{column,nameColumn})=>rows.map(row=>({name:row.cells[nameColumn].textContent.trim(),metric:row.cells[column].textContent.trim(),buttons:row.cells[column].querySelectorAll('button:not(:disabled)').length})),{column,nameColumn});
  assert.ok(rowMetrics.filter(r=>r.buttons===1).length>=11);
  assert.ok(rowMetrics.every(r=>r.buttons===0&&r.metric===''||/^Eficácia, \d+ of 100$/.test(r.metric)&&r.buttons===1),'Native vacant bench rows have no player value/action');await audit('Native tactics table gauge column');
  for(const index of [0,rowMetrics.length-1]){
   const row=tacticTable.locator('tbody tr').nth(index),metric=row.locator('th,td').nth(column).getByRole('button'),origin=await metric.getAttribute('id');
   await c.activate(metric);await poll(()=>page.getByRole('heading',{name:'Eficácia táctica',exact:true}).count(),'individual native effectiveness');await settled();
   assert.equal(await view().count(),0);assert.ok((await page.locator('#fmroot').innerText()).includes(rowMetrics[index].name),'Correct native player context');
   const native=await nativeDetails(false,rowMetrics[index].name),individual=await tables();assert.equal(native.steppers.length,5);assert.equal('Eficácia, '+native.steppers[0],rowMetrics[index].metric);
   assert.equal(individual.flatMap(t=>t.rows).length,17);assert.deepEqual(individual.flatMap(t=>t.rows.map(r=>r[1])),native.radials.map(r=>r.state));
   assert.equal(await page.getByRole('button').filter({hasText:/ of 100$/}).count(),0);await audit('Individual effectiveness: '+rowMetrics[index].name);
   observations.push({individual:rowMetrics[index],native,tables:individual});await c.activate(page.locator('[data-fm-history="back"]'));
   await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'individual gauge exact origin');
  }
  checks.push('Tactics gauge commands use their real native column caption; first/last rows open the correct individual values/factors and Back restores each exact origin');
  await go(989379584);assert.equal(await page.getByRole('heading',{name:'Portal',exact:true}).count(),1);
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,mvid);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  const result={success:true,mvid,checkpoint,...evidenceScope(),checks,audits,summary,observations,jsErrors:c.errors,requestFailures:c.requestFailures,network:c.network,manualNVDA:false,saveSubmitted:false,simulationAdvanced:false};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/training-effectiveness.json'),JSON.stringify(result,null,2));return result;
 }catch(error){
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/training-effectiveness-failure.json'),JSON.stringify({success:false,error:error.stack,mvid,...evidenceScope(),checks,audits,summary,factors,playerView,observations,html:await page.locator('#fmroot').innerHTML().catch(()=>null),native:developerTools?await c.fetchText('/gui').catch(()=>null):null,jsErrors:c.errors,requestFailures:c.requestFailures,network:c.network,trace:developerTools?await c.transportTrace():null},null,2));throw error;
 }
}
module.exports=run;
if(require.main===module)(async()=>{const c=await require('./original-client.cjs')({networkDiagnostics:true});try{console.log(JSON.stringify(await run(c),null,2));}finally{await c.browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
