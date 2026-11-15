// Production app.js/native game regression. Run with the normal Companion
// closed, so this is the sole live UI client. No save, simulation or business edit.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true});
 const {page,poll,settled}=c,checks=[],observations=[],spoken=[];let success=false,failure;
 page.on('request',r=>{const u=new URL(r.url());if(u.pathname==='/speak')spoken.push(u.searchParams.get('text'));});
 const button=name=>page.getByRole('button',{name,exact:true});
 const index=()=>page.evaluate(()=>FM.core.historyIndex);
 async function go(uid,title){
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first();
  const query=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await query.fill(await entry.locator(':scope > span').textContent());await entry.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');
  await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);
  await poll(async()=>await page.title()===title,'actual '+title);await settled();await query.fill('');await settled();
 }
 try{
  if(await page.title()!=='Plantel')await go('1355920640','Plantel');
  for(const kind of ['button','escape','history','escape']){
   const before=await index(),origin=await button('Pesquisar').getAttribute('id');
   await c.activate(button('Pesquisar'));
   await poll(()=>page.locator('[data-fm-native-overlay]').count(),'actual native search overlay');await settled();
   assert.equal(await page.getByRole('textbox',{name:'Global search',exact:true}).count(),1);
   assert.equal(await index(),before,'Search opening does not move menu history');
   const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/history'&&new URL(r.url()).searchParams.get('dir')==='back');
   const started=Date.now();
   if(kind==='button'){await button('Back').focus();await page.keyboard.press('Enter');}
   else{await page.getByRole('textbox',{name:'Global search',exact:true}).focus();await page.keyboard.press(kind==='escape'?'Escape':'Alt+[');}
   const reply=await(await response).text();assert.equal(reply,'','A confirmed overlay close must have no error reply');
   const elapsedMs=Date.now()-started;
   await poll(async()=>await page.locator('[data-fm-native-overlay]').count()===0,'search actually closed');await settled();
   assert.equal(await page.title(),'Plantel');assert.equal(await index(),before,'Close keeps native history index');
   await poll(()=>page.evaluate(id=>document.activeElement.id===id,origin),'exact global search opener focus');
   assert.doesNotMatch(await page.locator('body').innerText(),/Navigation did not reach another history entry in time/);
   observations.push({kind,elapsedMs,reply,index:before,origin,focus:await page.evaluate(()=>document.activeElement.id)});
  }
  checks.push('Four open/close cycles: native Back button, Escape and Alt+[ close search without a history move/error and restore exact opener focus');
  await go('989379584','Portal');
  const portalIndex=await index(),portalOrigin=await button('Pesquisar').getAttribute('id');
  await c.activate(button('Pesquisar'));await poll(()=>page.locator('[data-fm-native-overlay]').count(),'search from Portal');await settled();
  const closed=page.waitForResponse(r=>new URL(r.url()).pathname==='/history');
  await page.keyboard.press('Escape');assert.equal(await(await closed).text(),'');await settled();
  await poll(()=>page.evaluate(id=>document.activeElement.id===id,portalOrigin),'Portal exact opener return');assert.equal(await index(),portalIndex);
  checks.push('Search from a second actual native page returns to its own history entry/opener');
  await go('1355920640','Plantel');const source=await index();
  await c.activate(button('Equipa Principal'));await poll(async()=>await page.title()==='FC Porto','actual senior squad destination');await settled();
  assert.ok(await index()>source,'A real native page command moves history');
  const returned=page.waitForResponse(r=>new URL(r.url()).pathname==='/history');await page.keyboard.press('Alt+[');assert.equal(await(await returned).text(),'');
  await poll(async()=>await page.title()==='Plantel'&&await index()===source,'actual history Back');await settled();
  checks.push('Ordinary native page navigation still waits for and confirms a real history landing');
  assert.ok(!spoken.some(t=>t&&/Navigation did not reach another history entry in time/.test(t)),'No failed-history speech request');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(e){failure=e.stack;throw e;}
 finally{
  const report={at:new Date().toISOString(),success,failure,mvid:JSON.parse(await c.fetchText('/health')).coreMvid,checks,observations,spoken,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,manualNVDA:false};
  fs.mkdirSync(path.resolve(__dirname,'../../artifacts/tests/web'),{recursive:true});
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/global-search-close.json'),JSON.stringify(report,null,2));
  await c.browser.close();
 }
 console.log(JSON.stringify({success,checks:checks.length,closes:observations.length,closeMs:observations.map(x=>x.elapsedMs)}));
})().catch(e=>{console.error(e.stack);process.exitCode=1});
