// Native confirmations in the separate owned Dialog Regression checkpoint.
// Never confirms an overwrite or changes the native "do not show" preference.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true,initScript:()=>{
  window.qaHoldScreens=false;window.qaHeldScreens=[];
  const add=EventSource.prototype.addEventListener;
  EventSource.prototype.addEventListener=function(type,listener,options){
   if(type!=='screen')return add.call(this,type,listener,options);
   return add.call(this,type,function(event){if(qaHoldScreens){qaHeldScreens.push(()=>listener.call(this,event));return;}listener.call(this,event);},options);
  };
 }}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],passes=[];const mvid=JSON.parse(await c.fetchText('/health')).coreMvid;let ownedSave=null,saveFile=null,before=null,newMedicalReport=null;
 await page.evaluate(()=>{
  window.qaDialogTrace=[];
  const before=FM.dialog.beforeRefresh;
  FM.dialog.beforeRefresh=function(h,f,t){before(h,f,t);qaDialogTrace.push({kind:'before',focus:f,state:FM.dialog.state,returning:FM.dialog.returning,next:t});if(qaDialogTrace.length>70)qaDialogTrace.shift();};
  FM.core.afterRefresh.push(()=>{qaDialogTrace.push({kind:'after',focus:document.activeElement?.id,tag:document.activeElement?.tagName,state:FM.dialog.state,returning:FM.dialog.returning});if(qaDialogTrace.length>70)qaDialogTrace.shift();});
 });
 const snapshot=file=>{const s=fs.statSync(file,{bigint:true});return{size:String(s.size),mtimeNs:String(s.mtimeNs)};};
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});assert.deepEqual(a.violations,[]);}
 async function go(uid){
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});
  await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);
  await poll(()=>page.evaluate(id=>FMTOKEN.split('\x1f').some(p=>p===id||p.startsWith(id+'#')),String(uid)),'native destination');
  await settled();await search.fill('');await settled();
 }
 async function dialog(title){
  const d=page.getByRole('dialog',{name:title,exact:true});await poll(()=>d.count(),'actual native confirmation');await settled();
  assert.equal(await d.getAttribute('aria-modal'),'true');
  assert.equal(await page.evaluate(()=>document.activeElement?.id),await d.getAttribute('aria-labelledby'),'Native dialog arrival focuses its prompt');
  assert.equal(await page.getByRole('textbox',{name:'Search all screens',exact:true}).count(),0,'Native modal background is suppressed');
  return d;
 }
 try{
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const filename=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>filename.count(),'owned save form');
  ownedSave=await filename.inputValue();assert.match(ownedSave,/^FM27Access QA Dialog Regression /,'Use only the separate scratch checkpoint');
  saveFile=path.join(process.env.USERPROFILE,'Documents','Sports Interactive','Football Manager 27','games',ownedSave+'.fm');before=snapshot(saveFile);
  for(let pass=0;pass<3;pass++){
   passes.push({pass,stage:'opening'});
   const origin=await page.getByRole('button',{name:'Gravar',exact:true}).getAttribute('id');
   if(pass===1)await page.evaluate(()=>{qaHoldScreens=true;});
   await activate(page.getByRole('button',{name:'Gravar',exact:true}));
   const d=await dialog('Substituir o Ficheiro Actual?');assert.match(await d.innerText(),new RegExp(ownedSave));
   assert.equal(await filename.count(),0,'Save form is behind the actual modal');
   await page.keyboard.press('Tab');assert.equal(await d.getByRole('button',{name:'Cancelar',exact:true}).evaluate(e=>e===document.activeElement),true);
   await page.keyboard.press('Shift+Tab');assert.equal(await d.getByRole('button',{name:'Confirmar',exact:true}).evaluate(e=>e===document.activeElement),true);
   await page.evaluate(()=>FM.core.refresh(false,true));await settled();
   assert.equal(await d.getByRole('button',{name:'Confirmar',exact:true}).evaluate(e=>e===document.activeElement),true,'Neutral refresh retains the browsed choice');
   if(pass===0)await audit('Actual overwrite confirmation');
   await page.keyboard.press('Escape');await poll(()=>filename.count(),'native Save As return');
   passes.at(-1).stage='returning';
   await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'exact Save button origin after cancellation');
   if(pass===1){await page.evaluate(()=>{qaHoldScreens=false;qaHeldScreens.splice(0).forEach(deliver=>deliver());});await settled();assert.equal(await page.evaluate(()=>document.activeElement?.id),origin);}
   assert.deepEqual(snapshot(saveFile),before,'Overwrite cancellation must never write the checkpoint');
   passes.at(-1).stage='passed';
   console.log('Native overwrite/cancel cycle passed:',pass+1);
  }
  checks.push('Three actual overwrite dialogs identify their prompt, contain Tab/Shift+Tab, preserve neutral focus, cancel with Escape and restore exact Save origin; one genuine SSE delivery is held/released; file unchanged');
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));await go(989379584);
  const all=page.getByRole('tab',{name:'Tudo',exact:true});if(await all.getAttribute('aria-selected')!=='true')await activate(all);
  await c.newestMessages();
  const medicalTitle='Risco de lesão do Arsenal actualizado';
  const medicalRows=()=>page.locator('tr[data-fm-message-id]').filter({has:page.getByRole('button',{name:medicalTitle,exact:true})});
  const priorReports=await medicalRows().evaluateAll(rs=>rs.map(r=>r.dataset.fmMessageId));
  newMedicalReport={previous:priorReports,delivered:null};
  await go(3979388416);
  const selected=await page.getByRole('tab',{selected:true}).textContent();await activate(page.getByRole('tab',{name:'Elevado',exact:true}));
  await poll(()=>page.getByRole('combobox',{name:'Acções',exact:true}).count(),'populated medical actions');
  const actionOrigin=await page.getByRole('combobox',{name:'Acções',exact:true}).getAttribute('id');
  await c.dropdown('Acções');await c.choose('Obter Actualização sobre Jogadores em Risco de Lesão no Arsenal');
  const d=await dialog('Actualização do Risco de Lesões'),remember=d.getByRole('checkbox');assert.equal(await remember.isChecked(),false);
  await page.keyboard.press('Tab');assert.equal(await d.getByRole('button',{name:'Ok',exact:true}).evaluate(e=>e===document.activeElement),true);
  await page.keyboard.press('Shift+Tab');assert.equal(await remember.evaluate(e=>e===document.activeElement),true);
  await page.evaluate(()=>FM.core.refresh(false,true));await settled();assert.equal(await remember.evaluate(e=>e===document.activeElement),true);assert.equal(await remember.isChecked(),false);
  await audit('Actual medical update confirmation');await activate(d.getByRole('button',{name:'Ok',exact:true}));
  await poll(()=>page.evaluate(id=>document.activeElement?.id===id,actionOrigin),'exact medical Actions origin');
  await activate(page.getByRole('tab',{name:selected,exact:true}));
  checks.push('Native medical update confirmation focuses its title, contains keyboard focus, preserves unchecked reminder preference and neutral focus, acknowledges once and returns to exact Actions origin');
  await go(989379584);
  if(await all.getAttribute('aria-selected')!=='true')await activate(all);await c.newestMessages();
  let delivered=[];await poll(async()=>{delivered=(await medicalRows().evaluateAll(rs=>rs.map(r=>r.dataset.fmMessageId))).filter(id=>!priorReports.includes(id));return delivered.length>0;},'new actual native medical report identity');
  assert.equal(delivered.length,1,'Exactly one new native medical report per explicit request');newMedicalReport={previous:priorReports,delivered:delivered[0]};
  const report=page.locator('tr[data-fm-message-id="'+delivered[0]+'"]').getByRole('button',{name:medicalTitle,exact:true});
  await report.focus();await page.keyboard.press('Enter');await poll(()=>page.getByRole('button',{name:'Close message',exact:true}).count(),'medical message');await settled();
  assert.equal(await page.locator('[data-fm-message-view-id]').getAttribute('data-fm-message-view-id'),delivered[0]);
  const body=page.locator('[data-fm-msg-body]');assert.match(await body.innerText(),/Gary Lewin|risco de lesão/i);
  // The native body anchor is its introductory paragraph; the medical table
  // is the actual sibling attachment, not a child of that paragraph.
  const attachment=page.locator('table').filter({has:page.getByRole('columnheader',{name:'Principal factor de risco',exact:true})});
  assert.equal(await attachment.count(),1);assert.ok(await attachment.locator('tbody tr').count()>0);
  await poll(()=>body.evaluate(e=>e===document.activeElement),'medical message body receives focus');await audit('Delivered native medical report');
  await page.keyboard.press('Escape');await poll(async()=>await page.getByRole('button',{name:'Close message',exact:true}).count()===0,'message return');await settled();
  await poll(()=>page.evaluate(()=>!!document.activeElement?.getAttribute('onclick')?.includes('msgview.open')),'message returns to a real inbox row');
  checks.push('Actual medical update is delivered with native risk table; message body receives focus and Escape returns to an inbox row');
  await go(989379584);assert.deepEqual(snapshot(saveFile),before);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);
  assert.deepEqual(c.network.failures,[]);
  const result={success:true,mvid,ownedSave,newMedicalReport,checks,audits,saveUnchanged:true,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,manualNVDA:false};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/native-dialogs.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
 }catch(error){
  const result={error:error.stack||String(error),ownedSave,checks,passes,audits,saveUnchanged:saveFile&&before?JSON.stringify(snapshot(saveFile))===JSON.stringify(before):null,errors:c.errors,requestFailures:c.requestFailures,network:c.network,transportTrace:await c.transportTrace(),browser:await page.evaluate(()=>({title:document.title,focus:document.activeElement?.id,dialog:FM.dialog?.state,returning:FM.dialog?.returning,trace:qaDialogTrace,inputTrace:qaInputTrace,position:FM.core.pos}))};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/native-dialogs-failure.json'),JSON.stringify(result,null,2));throw error;
 }finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
