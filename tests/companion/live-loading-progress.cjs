// Native reload of the CURRENT owned QA checkpoint. Never writes a save.
// Observe genuine live values and calculated accessibility attributes, not a
// fabricated progress event or a DOM-only rendering assertion.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')(),{page,poll,activate}=c;
 let success=false,failure=null,ownedSave,updates=[],audit;
 try{
  assert.equal(await page.getByRole('heading',{name:'Portal',exact:true}).count(),1);
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const name=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>name.count(),'current save identity');
  ownedSave=await name.inputValue();assert.match(ownedSave,/^FM27Access QA /);
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await page.evaluate(()=>{
   window.qaProgressUpdates=[];
   FMES.addEventListener('values',event=>{
    const values=JSON.parse(event.data);
    for(const id in values){
     const el=document.getElementById(id);if(el?.getAttribute('role')!=='progressbar')continue;
     let payload;try{payload=JSON.parse(values[id]);}catch{continue;}
     qaProgressUpdates.push({id,payload,dom:{label:el.getAttribute('aria-label'),state:el.getAttribute('aria-valuetext'),text:el.textContent}});
    }
   });
  });
  await c.dropdown('FM Menu');await c.choose('Carregar Jogo');
  // A dirty career asks BEFORE the native file chooser; some native paths ask
  // after selection. Refuse saving in either position, only for this actual
  // localized unsaved-changes prompt. Never press Yes/overwrite a checkpoint.
  const no=page.getByRole('button',{name:'Não',exact:true});
  await poll(async()=>await no.count()>0||await page.getByRole('row').filter({hasText:ownedSave}).getByRole('radio').count()>0,'native unsaved prompt or owned file');
  if(await no.count()){
   assert.match(await page.locator('#fmroot').innerText(),/Deseja gravar o jogo antes de carregar outro jogo\?/);
   await activate(no);
  }
  await activate(page.getByRole('row').filter({hasText:ownedSave}).getByRole('radio'),'Space');
  await activate(page.getByRole('button',{name:'Carregar',exact:true}));
  if(await no.count()){
   assert.match(await page.locator('#fmroot').innerText(),/Deseja gravar o jogo antes de carregar outro jogo\?/);await activate(no);
  }
  const deadline=Date.now()+60000;
  while(Date.now()<deadline){
   updates=await page.evaluate(()=>qaProgressUpdates);
   if(updates.length&&await page.getByRole('heading',{name:'Portal',exact:true}).count())break;
   await page.waitForTimeout(100);
  }
  assert.equal(await page.getByRole('heading',{name:'Portal',exact:true}).count(),1,'Native loading returns to the saved Portal');
  assert.ok(new Set(updates.map(u=>u.payload.state)).size>=2,'Observe at least two genuine native progress states');
  for(const update of updates){assert.equal(update.dom.label,update.payload.label);assert.equal(update.dom.state,update.payload.state);assert.equal(update.dom.text,update.payload.text);}
  audit=(await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations.map(v=>v.id);
  assert.deepEqual(audit,[]);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);success=true;
 }catch(error){failure=error.stack||String(error);throw error;
 }finally{
  const dir=path.resolve(__dirname,'../../artifacts/tests/web');fs.mkdirSync(dir,{recursive:true});
  const report={success,failure,ownedSave,updates,audit,jsErrors:c.errors,requestFailures:c.requestFailures,manualNVDA:false};
  fs.writeFileSync(path.join(dir,'loading-progress.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));await c.browser.close();
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
