const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const c=await require('./original-client.cjs')(),{page,poll,settled,activate,dropdown,cancelDropdown}=c;
 let release;
 try{
  assert.equal(await page.getByRole('button',{name:'Preferências',exact:true}).count(),1,'Start at the main menu');
  await activate(page.getByRole('button',{name:'Preferências',exact:true}));
  await poll(()=>page.getByRole('tab',{name:'Detalhado',exact:true}).count(),'preferences');
  if(await page.getByRole('tab',{name:'Detalhado',exact:true}).getAttribute('aria-selected')!=='true')await activate(page.getByRole('tab',{name:'Detalhado',exact:true}));
  await activate(page.getByRole('tab',{name:'Ecrã',exact:true}));await settled();
  const quality=page.getByRole('combobox',{name:'Qualidade Gráfica',exact:true});
  const original=await quality.locator('option:checked').textContent();
  let intercepted=false,ready=false;
  const gate=new Promise(resolve=>{release=resolve;});
  await page.route('**/body?c=1',async route=>{
   if(intercepted){await route.continue();return;}intercepted=true;
   const response=await route.fetch(),headers=response.headers();
   const token=decodeURIComponent(headers['x-fm-token'].replace(/\+/g,' '));
   // The body is real; only simulate the content revision emitted by an
   // asynchronous native entity-name callback while that body is in flight.
   const revision=Number(/\x1Fnames:(\d+)$/.exec(token)?.[1]||0)+1;
   headers['x-fm-token']=encodeURIComponent(token.replace(/\x1Fnames:\d+$/,'')+'\x1Fnames:'+revision);
   ready=true;await gate;await route.fulfill({response,headers});
  });
  await page.evaluate(()=>FM.core.refresh());await poll(()=>ready,'captured production body');
  // Reproduce the race with a real native dropdown while the older body is held.
  await quality.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');
  await page.keyboard.press('Enter');assert.equal((await opened).status(),200);
  await poll(()=>quality.getAttribute('data-loaded'),'populated choices');
  const options=await quality.locator('option').allTextContents();assert.ok(options.length>1);
  release();await poll(()=>page.evaluate(()=>!FM.core.refreshing),'deferred old body');
  assert.equal(await quality.getAttribute('data-loaded'),'1');
  assert.deepEqual(await quality.locator('option').allTextContents(),options);
  assert.equal(await page.evaluate(()=>FM.core.dd),await quality.getAttribute('id'));
  await cancelDropdown();await settled();await page.unroute('**/body?c=1');
  assert.equal(await quality.locator('option:checked').textContent(),original);
  await dropdown('Qualidade Gráfica');
  const nameOptions=await quality.locator('option').allTextContents(),owner=await quality.getAttribute('id');
  await page.evaluate(()=>{
   const revision=Number(/\x1Fnames:(\d+)$/.exec(FMTOKEN)?.[1]||0)+1;
   FMES.dispatchEvent(new MessageEvent('screen',{data:FMTOKEN.replace(/\x1Fnames:\d+$/,'')+'\x1Fnames:'+revision}));
  });
  assert.equal(await page.evaluate(()=>FM.core.dd),owner,'A name-only revision is not a screen departure');
  assert.equal(await quality.getAttribute('data-loaded'),'1');
  assert.deepEqual(await quality.locator('option').allTextContents(),nameOptions);
  await cancelDropdown();await settled();
  assert.equal(await quality.locator('option:checked').textContent(),original);
  assert.equal(await page.evaluate(()=>document.activeElement?.id),await quality.getAttribute('id'));
  await dropdown('Qualidade Gráfica');await page.evaluate(()=>FM.core.refresh());
  assert.equal(await quality.getAttribute('data-loaded'),'1');
  await cancelDropdown();await settled();
  assert.equal(await quality.locator('option:checked').textContent(),original);
  await dropdown('Qualidade Gráfica');
  const failedOwner=await quality.getAttribute('id');let failedCommits=0;
  await page.route('**/dropdown/select?*',async route=>{failedCommits++;await route.abort('connectionfailed');});
  const other=(await quality.locator('option').allTextContents()).find(label=>label!==original);
  assert.ok(other);await quality.selectOption({label:other});
  const failed=page.waitForEvent('requestfailed',r=>new URL(r.url()).pathname==='/dropdown/select');
  const feedback=page.waitForRequest(r=>new URL(r.url()).pathname==='/speak'
   &&new URL(r.url()).searchParams.get('text')==='The dropdown selection could not be confirmed.');
  await page.keyboard.press('Enter');await failed;await feedback;
  assert.equal(await page.evaluate(()=>FM.core.dd),failedOwner,'A lost commit response must leave Escape available');
  assert.equal(await quality.getAttribute('data-loaded'),'1');
  await cancelDropdown();await settled();await page.unroute('**/dropdown/select?*');
  assert.equal(failedCommits,1,'Never replay a native command after a transport error');
  assert.equal(await quality.locator('option:checked').textContent(),original,'Blocked request must not change the native value');
  assert.equal(await page.evaluate(()=>document.activeElement?.id),failedOwner);
  await dropdown('Qualidade Gráfica');
  assert.equal(await quality.locator('option:checked').textContent(),original,'Reopening must read the unchanged native selection');
  await cancelDropdown();await settled();
  await dropdown('Qualidade Gráfica');let appliedCommits=0;
  await page.route('**/dropdown/select?*',async route=>{appliedCommits++;const response=await route.fetch();assert.equal(response.status(),200);await route.abort('connectionfailed');});
  await quality.selectOption({label:other});
  const lostReply=page.waitForEvent('requestfailed',r=>new URL(r.url()).pathname==='/dropdown/select');
  const lostFeedback=page.waitForRequest(r=>new URL(r.url()).pathname==='/speak'
   &&new URL(r.url()).searchParams.get('text')==='The dropdown selection could not be confirmed.');
  await page.keyboard.press('Enter');await lostReply;await lostFeedback;
  await cancelDropdown();await page.unroute('**/dropdown/select?*');
  await poll(async()=>await quality.locator('option:checked').textContent()===other,'Escape re-reads a native commit whose response was lost');
  assert.equal(appliedCommits,1,'A lost success response must not replay the command');
  assert.equal(await page.evaluate(()=>document.activeElement?.id),failedOwner);
  await dropdown('Qualidade Gráfica');await c.choose(original);
  await poll(async()=>await quality.locator('option:checked').textContent()===original,'native preference restored after lost-response check');
  await activate(page.getByRole('button',{name:'Close',exact:true}));
  await poll(()=>page.getByRole('button',{name:'Preferências',exact:true}).count(),'returned main menu');
  assert.deepEqual(c.errors,[]);
  const report={checks:['An in-flight real body with a name revision cannot strip an open dropdown session','Name-only screen events preserve the native open dropdown','Refresh while browsing defers until Escape','Native value and origin focus survive cancellation','A blocked native commit reports uncertainty without an uncaught error or automatic retry; Escape restores the unchanged value and origin focus','A lost native success response is not replayed; Escape reads the actually changed value and restores focus; original preference restored'],errors:c.errors,controlledFailedCommits:failedCommits,controlledAppliedCommits:appliedCommits,requestFailures:c.requestFailures,gameStateChanged:false,preferenceTemporarilyChangedAndRestored:true};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/live-dropdown-refresh.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
 }finally{if(release)release();await page.evaluate(()=>fetch('/dropdown/close').catch(()=>{})).catch(()=>{});await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
