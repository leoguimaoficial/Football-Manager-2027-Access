// Real opponent report, protected September QA checkpoint only. No Save or simulation.
// Native tree evidence must come from Debug on this exact checkpoint. Release can
// compare the retained cohort, explicitly without claiming Debug endpoint access.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),args=process.argv.slice(2),option=n=>args.find(a=>a.startsWith('--'+n+'='))?.slice(n.length+3);
const expectedPath=option('native-expected'),retained=expectedPath?JSON.parse(fs.readFileSync(path.resolve(expectedPath),'utf8')):null;
const checkpoint='FM27Access QA Dialog Regression 20261110 Responsibilities Restored 20261110104840158';
function nativeShapes(tree){
 const shapes=[];let shape=null,indicator=null,stack=[];
 for(const line of tree.split('\n')){
  const m=/\[(\d+)\] (\w+)(?: name="([^"]*)")? visible=(True|False)/.exec(line);if(!m)continue;
  const depth=Number(m[1]);while(stack.length&&stack.at(-1).depth>=depth)stack.pop();
  const visible=m[4]==='True'&&stack.every(x=>x.visible);stack.push({depth,visible});
  if(shape&&depth<=shape.depth){shape=null;indicator=null;}
  if(m[2]==='FMTacticsPanel'&&visible){shape={depth,slots:[]};shapes.push(shape);}
  if(!shape)continue;
  if(indicator&&depth<=indicator.depth)indicator=null;
  if(m[2]==='FMPitchIndicator')indicator={depth,slot:m[3],added:false};
  if(indicator&&!indicator.added&&visible&&(m[3]==='any-player'||m[3]==='goal-keeper')){shape.slots.push(indicator.slot);indicator.added=true;}
 }
 return shapes.map(s=>s.slots).filter(s=>s.length);
}
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,observations:[],audits:[],checks:[],saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const output=path.join(root,'artifacts/tests/web/opponent-formation-'+(retained?'release':'native')+'.json');
 async function audit(stage){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations});if(a.violations.length)fs.writeFileSync(path.join(root,'artifacts/tests/web/opponent-formation-axe-failure.html'),await page.content());assert.equal(a.violations.length,0,stage);}
 async function capture(stage){
  await settled();const grids=page.locator('[data-pitch="browse"]');await poll(async()=>await grids.count()===2,'two native expected-formation diagrams');
  const slots=await grids.evaluateAll(es=>es.map(e=>[...e.querySelectorAll('[data-slot]')].map(x=>x.dataset.slot)));
  let native,nativeSummary;
  if(retained){const found=retained.observations.find(o=>o.stage===stage);assert.ok(found,'Retained exact-stage native data');native=found.native;nativeSummary=found.nativeSummary;}
  else{const tree=await c.fetchText('/gui');native=nativeShapes(tree);nativeSummary=[...tree.matchAll(/SIText name="(?:ip|oop)-(?:data|chance|formationused|usedfor)-text" visible=True text="([^"]+)"/g)].map(m=>m[1]);fs.writeFileSync(path.join(root,'artifacts/tests/web/opponent-formation-'+stage+'-native.txt'),tree);}
  assert.equal(native.length,2);for(let i=0;i<2;i++){assert.equal(native[i].length,11);assert.deepEqual([...slots[i]].sort(),[...native[i]].sort());}
  const html=await grids.evaluateAll(es=>es.map(e=>({label:e.getAttribute('aria-label'),text:e.innerText,actions:e.querySelectorAll('button,[data-role],[data-pi]').length})));
  for(const g of html){assert.match(g.label,/between positions/);assert.doesNotMatch(g.text,/unreadable|unavailable|number |undefined/i);assert.equal(g.actions,0);}
  // Each arrow must remain inside its own diagram, using the inherited geometry.
  for(let i=0;i<2;i++){
   const first=grids.nth(i).locator('[data-slot="GK"]');await first.focus();const target=await first.getAttribute('data-up');assert.ok(target);await page.keyboard.press('ArrowUp');assert.equal(await page.evaluate(()=>document.activeElement.id),target);assert.equal(await page.evaluate(()=>document.activeElement.closest('[data-pitch]').id),await grids.nth(i).getAttribute('id'));
  }
  const body=await page.locator('#fmroot').innerText();assert.equal(nativeSummary.length,6,'Both native counts, chance frequencies and duration summaries');for(const text of nativeSummary)assert.ok(body.includes(text),'Current native summary: '+text);report.observations.push({stage,native,nativeSummary,slots,html,body});await audit(stage);return slots;
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));assert.ok(report.health.developerTools||retained);if(retained){assert.equal(retained.success,true);assert.equal(retained.checkpoint,checkpoint);}
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const e=page.locator('#fmnav-tree [role="treeitem"][data-uid="242408448"][data-obj="2000209827"]').first(),q=page.getByRole('textbox',{name:'Search all screens',exact:true});assert.equal(await e.count(),1);await q.fill(await e.locator(':scope > span').textContent());await e.focus();const nav=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');assert.match(await(await nav).text(),/Opening/);await settled();await q.fill('');
  await activate(page.getByRole('tab',{name:'Análise da Formação',exact:true}));await poll(()=>page.getByRole('tab',{name:'Formação esperada',exact:true}).count(),'native nested analysis tabs');await activate(page.getByRole('tab',{name:'Formação esperada',exact:true}));
  const tables=page.locator('#fmroot table').filter({has:page.getByRole('columnheader',{name:'Frequência',exact:true})});assert.equal(await tables.count(),2);
  const groups=await tables.evaluateAll(es=>es.map(e=>[...new Set([...e.querySelectorAll('input[type=radio]')].map(r=>r.name))]));
  assert.equal(groups[0].length,1);assert.equal(groups[1].length,1);assert.notEqual(groups[0][0],groups[1][0],'Independent native selection tables must remain independent HTML radio groups');
  for(let i=0;i<2;i++)if(!await tables.nth(i).locator('tbody tr').nth(0).getByRole('radio').isChecked())await activate(tables.nth(i).locator('tbody tr').nth(0).getByRole('radio'),'Space');
  const before=await capture('used-first');
  for(let i=0;i<2;i++){
   const rows=tables.nth(i).locator('tbody tr');assert.equal(await rows.count(),2);const select=rows.nth(1).getByRole('radio',{name:'Select',exact:true});await activate(select,'Space');await poll(()=>rows.nth(1).getByRole('radio').isChecked(),'native second formation selected');const changed=await capture('used-second-'+i);assert.notDeepEqual(changed[i],before[i],'Selected native formation really changes');assert.deepEqual(changed[1-i],before[1-i],'Other native phase remains independent');assert.equal(await tables.nth(1-i).locator('tbody tr').nth(0).getByRole('radio').isChecked(),true,'Other phase retains its own selected row');
   await activate(rows.nth(0).getByRole('radio',{name:'Select',exact:true}),'Space');await poll(()=>rows.nth(0).getByRole('radio').isChecked(),'native first formation restored');
  }
  const faced=page.getByRole('radio',{name:'Formações Enfrentadas',exact:true});await activate(faced,'Space');await poll(()=>faced.isChecked(),'native faced-formation mode');await capture('faced-first');
  const used=page.getByRole('radio',{name:'Formações Utilizadas',exact:true});await activate(used,'Space');await poll(()=>used.isChecked(),'native used-formation mode restored');assert.deepEqual(await capture('used-restored'),before);
  for(const name of ['Táctica Esperada','Estilo Táctico Esperado']){await activate(page.getByRole('tab',{name,exact:true}));await poll(()=>page.getByRole('tab',{name,exact:true}).getAttribute('aria-selected').then(s=>s==='true'),'actual selected analysis subtab');await audit(name);report.observations.push({stage:name,body:await page.locator('#fmroot').innerText()});}
  await activate(page.getByRole('tab',{name:'Formação esperada',exact:true}));assert.deepEqual(await capture('used-reopened'),before);
  report.checks.push('Both anonymous native position diagrams retain all eleven actual slots, independent native data and no invented players/actions','Both selection tables, used/faced radio modes and three native subtabs operate with keyboard and restore initial state','Spatial arrows remain within each diagram; native repeated return preserves the original shape');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;console.log(JSON.stringify({success:true,module:report.health.coreMvid,groups:report.checks.length,audits:report.audits.length}));
 }finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;fs.mkdirSync(path.dirname(output),{recursive:true});fs.writeFileSync(output,JSON.stringify(report,null,2));await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
