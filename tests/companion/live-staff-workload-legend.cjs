// Native coach-assignment legend: preserve actual counts/categories, not fixed
// decorative meter percentages. Read-only; no assignments, Save or simulation.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
module.exports=async function run(c){
 const {page,poll,settled}=c,root=path.resolve(__dirname,'../..');
 const report={at:new Date().toISOString(),success:false,checks:[],audits:[],saveSubmitted:false,simulationAdvanced:false,assignmentsChanged:false,manualNVDA:false};
 const referenceArg=process.argv.find(a=>a.startsWith('--native-reference='));
 function retainedNative(){
  assert.ok(referenceArg,'Release requires independent native evidence from this exact immutable checkpoint');
  const reference=JSON.parse(fs.readFileSync(path.resolve(root,referenceArg.slice(19)),'utf8'));
  assert.ok(reference.success&&reference.checkpoint===report.checkpoint&&reference.initial?.native);
  assert.equal(reference.checkpointSha256,report.checkpointSha256,'Native reference belongs to these exact saved career bytes');
  return reference;
 }
 async function audit(name){const r=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({name,violations:r.violations.map(v=>v.id)});assert.deepEqual(r.violations,[]);}
 async function readLegend(){
  // Independent visible native tree, not the production pairing helper.
  let gui='',native=[];
  if(!report.health.developerTools){
   const reference=retainedNative();
   native=reference.initial.native;report.nativeReferenceSource=referenceArg.slice(19);
  }else gui=await c.fetchText('/gui');
  const lines=gui.split(/\r?\n/);
  for(let i=0;i<lines.length;i++)if(/VisualElement name="[^\"]+_chart" visible=True/.test(lines[i])){
   const depth=Number(lines[i].match(/\[(\d+)\]/)[1]),parts=[];
   for(let j=i+1;j<lines.length&&Number(lines[j].match(/\[(\d+)\]/)?.[1])>depth;j++){
    const m=lines[j].match(/SIText name="(count|text)" visible=True text="([^\"]*)"/);if(m)parts.push({kind:m[1],text:m[2]});
   }
   if(parts.length===2&&parts[0].kind==='count'&&parts[1].kind==='text')native.push({caption:parts[1].text,value:parts[0].text});
  }
  assert.equal(native.length,4,'Exactly four observed native workload category rows');
  const dom=await page.locator('#fmroot p').evaluateAll((ps,pairs)=>ps.flatMap(p=>pairs.some(x=>p.textContent===x.caption+': '+x.value)?[{caption:p.textContent.split(': ')[0],value:p.querySelector('span')?.textContent,id:p.querySelector('span')?.id}]:[]),native);
  assert.deepEqual(dom.map(({caption,value})=>({caption,value})),native);
  assert.ok(dom.every(x=>x.id));
  assert.equal(await page.locator('#fmroot').getByText(/^\d+ x: \d+ of 0 to 100$/).count(),0);
  return{native,dom,gui};
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));assert.equal(await page.title(),'Equipa Técnica');
  const beforeArg=process.argv.find(a=>a.startsWith('--before='));
  report.beforeSource=beforeArg?beforeArg.slice(9):'artifacts/career-staff-native-complete-overview-baseline.json';
  const before=JSON.parse(fs.readFileSync(path.resolve(root,report.beforeSource),'utf8'));
  assert.equal(before.working,JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8')).staffRecruitmentWorking);
  report.checkpoint=before.working;
  report.checkpointSha256=crypto.createHash('sha256').update(fs.readFileSync(path.join(process.env.USERPROFILE,'Documents/Sports Interactive/Football Manager 27/games',report.checkpoint+'.fm'))).digest('hex');
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),report.checkpoint);await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));await settled();
  const selectors=await page.locator('#fmroot select').evaluateAll(es=>{const h=document.getElementById('fmsec1-h');return es.filter(e=>h.compareDocumentPosition(e)&Node.DOCUMENT_POSITION_FOLLOWING).map(e=>({id:e.id,label:e.getAttribute('aria-label')}));});
  // Opening a native action menu learns its item count and remembers the last
  // browsed index. That orientation is not a business/form value. Verify the
  // actual production adapter kind before excluding ONLY bare index/count text
  // from the historical comparison; all names, controls and real values remain.
  if(report.health.developerTools){
   const code=`var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${report.health.coreMvid}"&&a.GetType("FM27Access.Plugin.CoreEntry").GetField("_bridge",f).GetValue(null)!=null){var map=(System.Collections.IDictionary)a.GetType("FM27Access.Plugin.Server.ServerHandles").GetField("_handles",f).GetValue(null);var sources=(System.Collections.IDictionary)a.GetType("FM27Access.Plugin.Server.LiveValues").GetField("_sources",f).GetValue(null);foreach(var id in new string[]{${selectors.map(s=>'"'+s.id+'"').join(',')}}){var h=map[id];var ad=h==null?sources[id]:h.GetType().GetField("El",f).GetValue(h);if(ad==null)throw new System.Exception("Actual dropdown source missing");var p=ad.GetType().GetProperty("IsActionMenu",f);var c=ad.GetType().GetProperty("IsCardOptions",f);bool menu=(p!=null&&(bool)p.GetValue(ad,null))||(c!=null&&(bool)c.GetValue(ad,null));System.Console.WriteLine("MENU_KIND|"+id+"|"+menu);}}`;
   const raw=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);assert.doesNotMatch(raw,/\[compile\]|\[exception\]|error CS/i);report.nativeMenuKindEvidence=raw;
   report.nativeMenuKinds=selectors.map(s=>({...s,action:raw.split(/\r?\n/).includes('MENU_KIND|'+s.id+'|True')}));assert.equal(raw.split(/\r?\n/).filter(l=>l.startsWith('MENU_KIND|')).length,selectors.length);
  }else{report.nativeMenuKinds=retainedNative().nativeMenuKinds;assert.ok(Array.isArray(report.nativeMenuKinds));assert.deepEqual(report.nativeMenuKinds.map(s=>s.label),selectors.map(s=>s.label));}
  const menuLabels=[...new Set(report.nativeMenuKinds.filter(s=>s.action&&report.nativeMenuKinds.filter(x=>x.label===s.label).every(x=>x.action)).map(s=>s.label))];report.menuNavigationMetadataLabels=menuLabels;
  const currentHtml=await page.locator('#fmroot').innerHTML();
  report.preservation=await page.evaluate(({before,after,menuLabels})=>{
   const project=html=>{const d=new DOMParser().parseFromString(html,'text/html'),h=d.querySelector('#fmsec1-h');if(!h)throw Error('Native content heading missing');for(const e of d.querySelectorAll('select'))if(menuLabels.includes(e.getAttribute('aria-label'))&&/^(?:\d+ of \d+)?$/.test(e.textContent.trim()))e.textContent='';const after=e=>!!(h.compareDocumentPosition(e)&Node.DOCUMENT_POSITION_FOLLOWING);const result={controls:[...d.querySelectorAll('button,input,select,textarea')].filter(after).map(e=>({tag:e.tagName,type:e.getAttribute('type'),label:e.getAttribute('aria-label'),text:e.tagName==='INPUT'?e.value:e.textContent,disabled:e.disabled})),tables:[...d.querySelectorAll('table')].filter(after).map(e=>e.textContent)};const legend=[...d.querySelectorAll('h3')].find(e=>e.textContent==='TAREFAS DE PREPARADORES');if(!legend)throw Error('Actual workload legend heading missing');const ps=[];for(let p=legend.nextElementSibling;p?.tagName==='P';p=p.nextElementSibling)ps.push(p);if(![4,12].includes(ps.length))throw Error('Unexpected native legend presentation');ps.forEach(p=>p.remove());const walker=d.createTreeWalker(d.body,NodeFilter.SHOW_TEXT),texts=[];while(walker.nextNode())if(after(walker.currentNode))texts.push(walker.currentNode.textContent);result.otherContentText=texts.join(' ').replace(/\s+/g,' ').trim();return result;};return{before:project(before),after:project(after)};
  },{before:before.html,after:currentHtml,menuLabels});
  assert.deepEqual(report.preservation.after,report.preservation.before,'All other real content controls and tables retain order, values and availability');
  const first=await readLegend();report.initial={native:first.native,dom:first.dom};
  const code=`var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${report.health.coreMvid}"){var values=(System.Collections.IDictionary)a.GetType("FM27Access.Plugin.Server.LiveValues").GetMethod("Snapshot",f).Invoke(null,null);foreach(var id in new string[]{${first.dom.map(x=>'"'+x.id+'"').join(',')}})System.Console.WriteLine("VALUE|"+id+"|"+values[id]);}`;
  if(report.health.developerTools){const raw=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);assert.doesNotMatch(raw,/\[compile\]|\[exception\]|error CS/i);report.valueSnapshot=raw;for(const x of first.dom)assert.ok(raw.split(/\r?\n/).includes('VALUE|'+x.id+'|'+x.value));}
  report.checks.push('Four exact native category/count pairs; all other content controls and tables preserved. Debug additionally confirms existing readonly LiveValues snapshot; Release uses explicitly retained exact-checkpoint native reference');await audit('Actual native staff workload legend');
  const card=page.getByRole('button',{name:'TAREFAS DE PREPARADORES',exact:true}),origin=await card.getAttribute('id');
  await c.activate(card);await poll(()=>page.title().then(t=>t==='Tarefas de Preparadores'),'actual coach assignments destination');await settled();
  const table=page.locator('#fmroot table').filter({has:page.getByRole('columnheader',{name:'Carga de Trabalho do Treinador',exact:true})});assert.equal(await table.count(),1);
  report.nativeDetailRows=await table.locator('tbody tr').evaluateAll(rows=>rows.map(r=>[...r.cells].map(e=>e.textContent.trim())));assert.equal(report.nativeDetailRows.filter(r=>r[1]==='Leve').length,6);
  await audit('Actual native coach assignment detail');await c.activate(page.getByRole('button',{name:'Back',exact:true}));await settled();assert.equal(await page.title(),'Equipa Técnica');assert.equal(await page.evaluate(()=>document.activeElement.id),origin);
  const reopened=await readLegend();assert.deepEqual(reopened.native,first.native);await audit('Returned native workload legend');
  report.checks.push('Real native card opens coach tasks; six light training-area rows corroborate count; Back restores exact origin and all legend pairs');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
 }catch(error){report.failure=error.stack||String(error);throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;const dest=path.join(root,'artifacts/tests/web/staff-workload-legend.json');fs.mkdirSync(path.dirname(dest),{recursive:true});fs.writeFileSync(dest,JSON.stringify(report,null,2));}
 return report;
};
if(require.main===module)(async()=>{const c=await require('./original-client.cjs')({networkDiagnostics:true});try{console.log(JSON.stringify(await module.exports(c),null,2));}finally{await c.browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
