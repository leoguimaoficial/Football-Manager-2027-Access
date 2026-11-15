// Shared native confirmation callback readiness. Cancel every tested exit;
// never save, leave the career, quit the game or synthesize a native callback.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const cycles=[],bodies=[],audits=[];let success=false,error;
 const saves=path.join(process.env.USERPROFILE,'Documents','Sports Interactive','Football Manager 27','games');
 const saveState=()=>fs.readdirSync(saves).filter(n=>n.endsWith('.fm')).map(name=>{const s=fs.statSync(path.join(saves,name),{bigint:true});return{name,size:String(s.size),mtime:String(s.mtimeNs)};});
 const baseline=saveState();
 page.on('response',async r=>{if(new URL(r.url()).pathname!=='/body')return;try{const b=await r.text();if(b.includes('data-fm-native-dialog'))bodies.push({at:Date.now(),buttons:(b.match(/<button\b/g)||[]).length,provisional:b.includes('fm-provisional'),html:b});}catch{}});
 try{
  assert.equal(await page.getByRole('dialog').count(),0,'Begin on the real career page');
  for(const option of ['Sair para o Ecrã Inicial','Deixar Jogo e Sair'])for(let pass=0;pass<3;pass++){
   await c.dropdown('FM Menu');const origin=await page.getByRole('combobox',{name:'FM Menu',exact:true}).getAttribute('id');
   const started=Date.now(),firstBody=bodies.length;
   await c.choose(option);
   const dialog=page.getByRole('dialog');await poll(()=>dialog.count(),'native exit confirmation');
   await poll(async()=>await dialog.getByRole('button').count()===3,'all three native responses arrive automatically');
   const title=await dialog.getAttribute('aria-labelledby');assert.match(await dialog.innerText(),/Gravar|gravado/);
   const buttons=await dialog.getByRole('button').allTextContents();assert.deepEqual(buttons,['Cancelar','Não','Sim']);
   assert.equal(await dialog.getByRole('button',{name:'Cancelar',exact:true}).isEnabled(),true);
   assert.equal(await dialog.getByRole('button',{name:'Não',exact:true}).isEnabled(),true);
   assert.equal(await dialog.getByRole('button',{name:'Sim',exact:true}).isEnabled(),true);
   assert.equal(await page.evaluate(()=>document.activeElement?.id),title,'Initial focus stays on the prompt while callbacks settle');
   assert.equal(await page.getByRole('combobox',{name:'FM Menu',exact:true}).count(),0,'Native background is suppressed');
   await page.keyboard.press('Tab');assert.equal(await dialog.getByRole('button',{name:'Cancelar',exact:true}).evaluate(e=>e===document.activeElement),true);
   await page.keyboard.press('Shift+Tab');assert.equal(await dialog.getByRole('button',{name:'Sim',exact:true}).evaluate(e=>e===document.activeElement),true);
   await page.keyboard.press('Tab');assert.equal(await dialog.getByRole('button',{name:'Cancelar',exact:true}).evaluate(e=>e===document.activeElement),true);
   const readyMs=Date.now()-started;
   if(pass===0){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({option,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
   const close=pass===0?'Escape':pass===1?'Enter':'Space';
   if(close==='Escape')await page.keyboard.press('Escape');else await c.activate(dialog.getByRole('button',{name:'Cancelar',exact:true}),close);
   await poll(async()=>await page.getByRole('dialog').count()===0,'native cancellation');await settled();
   await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'exact FM Menu origin focus');
   assert.deepEqual(saveState(),baseline,'No checkpoint written or removed');
   cycles.push({option,pass,buttons,readyMs,close,bodies:bodies.slice(firstBody).map(({html,...b})=>b),originRestored:true});
   console.log('PASS',option,pass+1,close,readyMs+'ms');
  }
  // Leave the user's originally reported prompt open for inspection.
  await c.dropdown('FM Menu');await c.choose('Sair para o Ecrã Inicial');
  await poll(async()=>await page.getByRole('dialog').getByRole('button').count()===3,'final first-entry buttons');
  assert.deepEqual(saveState(),baseline);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  success=true;
 }catch(e){error=e.stack;throw e;}
 finally{
  const report={success,error,health:JSON.parse(await c.fetchText('/health')),cycles,bodies,audits,saveFilesUnchanged:JSON.stringify(saveState())===JSON.stringify(baseline),errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,manualRefreshRequested:false,saveOrExitConfirmed:false,manualNVDA:false};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/generic-dialog-readiness-live.json'),JSON.stringify(report,null,2));await c.browser.close();
 }
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
