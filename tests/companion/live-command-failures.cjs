// Controlled request refusal on the real production UI, followed by real
// navigation recovery. No Continue, save, bookmark or context action executes
// in the refusal cases. Not evidence that the native TCP fault is eliminated.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],spoken=[],controlled=new Set();
 page.on('request',r=>{if(new URL(r.url()).pathname==='/speak')spoken.push(new URL(r.url()).searchParams.get('text')||'');});
 async function refused(label,routePath,gesture,mode){
  await settled();const owner=await page.evaluate(()=>FM.core.screenIdentity(FMTOKEN)),before=c.requestFailures.length,start=spoken.length;
  let hits=0;const matches=url=>url.pathname===routePath,handler=async route=>{hits++;assert.equal(route.request().method(),'POST','Native actions use the non-idempotent transport method');if(mode==='HTTP503')await route.fulfill({status:503,contentType:'text/plain',body:'Controlled QA refusal'});else await route.abort('connectionfailed');};
  await page.route(matches,handler);
  try{
   await gesture();await poll(()=>hits===1,'one '+label+' request');
   await poll(()=>spoken.slice(start).some(t=>t.includes('The action could not be confirmed.')),'contextual '+label+' failure');await settled();
   assert.equal(hits,1,'A failed command is never replayed');
   assert.equal(await page.evaluate(()=>FM.core.screenIdentity(FMTOKEN)),owner,'Refused command leaves the actual native destination unchanged');
   assert.equal(await page.evaluate(()=>FM.core.dd),null,'Failure must not strand a dropdown gate');
   for(const failure of c.requestFailures.slice(before))if(failure.path===routePath&&failure.error==='net::ERR_CONNECTION_FAILED')controlled.add(failure);
   checks.push({label,route:routePath,mode,requests:hits,nativeDestinationUnchanged:true,uncertaintyAnnounced:true});console.log('Controlled refusal passed:',label);
  }finally{await page.unroute(matches,handler);}
 }
 try{
  if(!await page.getByRole('heading',{name:'Portal',exact:true}).count()){
   const portal=page.locator('#fmnav-tree [role=treeitem][data-uid="989379584"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
   await search.fill(await portal.locator(':scope > span').textContent());await portal.focus();const arrived=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');arrived.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await arrived).text(),/^Opening /);await poll(()=>page.getByRole('heading',{name:'Portal',exact:true}).count(),'owned Portal before controlled refusal');await search.fill('');await settled();
  }
  assert.equal(await page.getByRole('heading',{name:'Portal',exact:true}).count(),1);
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const filename=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>filename.count(),'owned QA identity');
  const ownedSave=await filename.inputValue();assert.match(ownedSave,/^FM27Access QA /);await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const menu=page.getByRole('combobox',{name:'FM Menu',exact:true});
  await refused('Alt+1 navigation','/matchchord',async()=>{await menu.focus();await page.keyboard.press('Alt+1');},'HTTP503');
  await refused('Continue shortcut','/continue/press',()=>page.keyboard.press('Alt+Enter'),'abort');
  await refused('Quick Save shortcut','/save',()=>page.keyboard.press('Alt+s'),'HTTP503');
  await refused('Bookmark shortcut','/bookmark',()=>page.keyboard.press('Control+1'),'abort');
  const search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  // Select the actual Tactics leaf from the native inventory rather than a
  // translated screen-label action dispatch.
  const target=page.locator('#fmnav-tree [role=treeitem][data-uid="195478016"]').first();
  assert.equal(await target.count(),1);
  await search.fill(await target.locator(':scope > span').textContent());
  await refused('Menu browser destination','/navigator/go',async()=>{await target.focus();await page.keyboard.press('Enter');},'HTTP503');
  await search.fill('');await settled();
  const context=page.locator('#fmroot button[aria-haspopup="menu"]:not([disabled])').first();assert.ok(await context.count()>0);
  await refused('Native context-menu open','/contextmenu',async()=>{await context.focus();await page.keyboard.press('Shift+F10');},'abort');
  assert.equal(await page.getByRole('menu').count(),0);
  // Genuine commands are restored: navigation must actually reach native
  // Messages and Portal, with no new transport refusal and one Messages title.
  await menu.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/matchchord');response.catch(()=>{});
  await page.keyboard.press('Alt+1');assert.equal((await response).status(),200);await poll(async()=>await page.title()==='Mensagens','native navigation after refusals');await settled();
  assert.equal(await page.getByRole('heading',{name:/^mensagens$/i}).count(),1);
  const portal=page.locator('#fmnav-tree [role=treeitem][data-uid="989379584"]').first();await search.fill(await portal.locator(':scope > span').textContent());await portal.focus();
  const returned=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');returned.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await returned).text(),/^Opening /);
  await poll(()=>page.getByRole('heading',{name:'Portal',exact:true}).count(),'native Portal recovery');await search.fill('');await settled();
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures.filter(f=>!controlled.has(f)),[]);
  const report={success:true,ownedSave,checks,realRecovery:['Messages','Portal'],errors:c.errors,controlledRequestFailures:[...controlled],unexpectedRequestFailures:[],saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/command-failures.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
 }catch(error){
  const report={success:false,error:error.stack||String(error),checks,errors:c.errors,requestFailures:c.requestFailures,network:c.network,transportTrace:await c.transportTrace()};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/command-failures-failure.json'),JSON.stringify(report,null,2));throw error;
 }finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
