// Real native inbox disclosure, query, focus and cancellation regression.
// No fixture server, message action, simulation or save submission.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
async function run(c){
 const {page,poll}=c,checks=[],audits=[],observations=[];
 const linkedProfile=process.argv.find(a=>a.startsWith('--linked-profile='))?.slice(17);
 const linkedQuery=process.argv.find(a=>a.startsWith('--linked-query='))?.slice(15);
 const linkedMessage=process.argv.find(a=>a.startsWith('--linked-message='))?.slice(17);
 assert.equal(!!linkedQuery,!!linkedMessage,'A specific native result requires its actual query and NewsItem together');if(linkedMessage)assert.match(linkedMessage,/^\d+$/);
 const opener=()=>page.getByRole('button',{name:'Search messages',exact:true});
 const field=()=>page.getByRole('textbox',{name:'Procurar...',exact:true});
 const table=()=>page.getByRole('table').filter({has:page.getByRole('columnheader',{name:'Subject',exact:true})});
 const rows=()=>table().locator('tbody tr').allTextContents();
 const focus=()=>page.evaluate(()=>document.activeElement?.id);
 const audit=async name=>{const r=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:r.violations.map(v=>v.id)});assert.deepEqual(r.violations,[]);};
 let release,routeHandler,mvid;
 async function query(value){
  await field().fill(value);const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/settext');response.catch(()=>{});
  await page.keyboard.press('Tab');const r=await response;assert.equal(r.status(),200);
  const reply=await r.text();assert.ok(!/failed|unavailable|stale|changed\. Refresh/i.test(reply),reply);await c.settled();return reply;
 }
 async function close(){await c.activate(page.getByRole('button',{name:'Close',exact:true}));await poll(async()=>await field().count()===0,'native search closed');await poll(async()=>await focus()===await opener().getAttribute('id'),'exact search opener return');}
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  // A shared regression may enter from Calendar or another native page.
  // Establish the real Portal rather than requiring manual preparation.
  if(await page.getByRole('heading',{name:'Portal',exact:true}).count()!==1){
   const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="989379584"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
   await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();
   const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});
   await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);
   await poll(()=>page.getByRole('heading',{name:'Portal',exact:true}).count(),'actual native Portal');await c.settled();await search.fill('');await c.settled();
  }
  if(process.argv.includes('--require-open-search'))assert.equal(await field().count(),1,'Require an actual already-open native search before attaching this client');
  if(await field().count()){
   const existingQuery=await field().inputValue();observations.push({existingNativeQuery:existingQuery});
   if(existingQuery!=='')await query('');
   await close();
   checks.push('A native search already open when this fresh client attaches still closes to its actual paired opener');
  }
  assert.equal(await field().count(),0,'Start the new disclosure with native message search closed');
  assert.equal(await opener().count(),1);const date=(await page.locator('#fmroot').innerText()).match(/\b\d{1,2}\/\d{1,2}\/\d{4}[^\n]*/)?.[0]||null;
  await c.activate(opener());await poll(()=>field().evaluate(e=>e===document.activeElement),'new native search field focus');
  const initial=await rows();assert.ok(initial.length);
  const firstSubject=await table().locator('tbody tr').first().getByRole('button').first().innerText();
  // A later career can legitimately contain no conference invitations. Search
  // for a real word in a currently observed native subject instead of relying
  // on the earlier September fixture's news category.
  const nativeQuery=(firstSubject.match(/\p{L}[\p{L}\p{N}]{3,}/gu)||[]).sort((a,b)=>b.length-a.length)[0];assert.ok(nativeQuery,'An actual searchable native subject');
  observations.push({initialFirstSubject:firstSubject,initialWindowRows:initial,nativeQuery});
  await audit('Native message search disclosure');
  checks.push('Keyboard open lands directly on the actual newly revealed native field');
  await query(nativeQuery);await poll(async()=>{const r=await rows();return r.length>0&&r.some(t=>t.includes(firstSubject))&&initial.some(t=>!r.includes(t));},'native filtered messages');
  observations.push({filteredRows:await rows()});await field().focus();const caretEnd=Math.min(5,nativeQuery.length);await field().evaluate((e,end)=>e.setSelectionRange(2,end),caretEnd);
  await page.evaluate(()=>FM.core.refresh(false,true));await c.settled();assert.equal(await field().inputValue(),nativeQuery);
  assert.ok(await field().evaluate((e,end)=>e===document.activeElement&&e.selectionStart===2&&e.selectionEnd===end,caretEnd));
  checks.push('Native query filters actual subjects; neutral refresh retains field, value and caret');
  const resultQuery=linkedQuery||nativeQuery;
  if(linkedQuery&&linkedQuery!==nativeQuery){await query(linkedQuery);await poll(()=>page.locator('tr[data-fm-message-id="'+linkedMessage+'"] button[onclick^="FM.msgview.open"]').count(),'specific actual native query/result');}
  const results=await table().locator('tbody tr[data-fm-message-id]').evaluateAll(rs=>rs.map(r=>({message:r.dataset.fmMessageId,subject:r.querySelector('button[onclick^="FM.msgview.open"]')?.textContent})));
  const searchResult=linkedMessage?results.find(r=>r.message===linkedMessage):[...results].reverse().find(r=>!r.subject.toLocaleLowerCase().includes(resultQuery.toLocaleLowerCase()))||results.at(-1);
  assert.ok(searchResult?.message);observations.push({searchResult,query:resultQuery});
  // Native search also matches the message body, not just its subject. Read an
  // actual result and return to its original search, including an older record
  // outside the unfiltered Inbox's first native window. Test both dismiss paths.
  for(const escape of [false,true]){
   const action=page.locator('tr[data-fm-message-id="'+searchResult.message+'"] button[onclick^="FM.msgview.open"]');
   await action.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');
   await page.keyboard.press('Enter');assert.equal(await(await opened).text(),'');
   await poll(()=>page.locator('[data-fm-message-view-id="'+searchResult.message+'"]').count(),'exact native filtered result opened');await c.settled();
   const content=await page.locator('#fmroot').innerText();assert.ok(content.toLocaleLowerCase().includes(resultQuery.toLocaleLowerCase()),'The real returned message contains the query in its subject, sender or body');
   if(escape&&linkedProfile){
    await c.activate(page.getByRole('button',{name:linkedProfile,exact:true}));
    await poll(async()=>await page.locator('[data-fm-message-view-id]').count()===0,'actual linked native profile opened');
    assert.ok((await page.locator('#fmroot').innerText()).includes(linkedProfile));
    const returned=page.waitForResponse(r=>new URL(r.url()).pathname==='/history'&&new URL(r.url()).searchParams.get('dir')==='back');
    await page.keyboard.press('Alt+[');assert.equal(await(await returned).text(),'');
    await poll(()=>page.locator('[data-fm-message-view-id="'+searchResult.message+'"]').count(),'same native result restored after linked profile');await c.settled();
    await audit('Exact filtered message restored after linked native profile');
    checks.push('The real linked profile opens and native Back restores the exact filtered message without losing its search provenance');
   }
   if(escape){const closed=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/close');await page.keyboard.press('Escape');assert.equal(await(await closed).text(),'');}
   else await c.activate(page.getByRole('button',{name:'Close message',exact:true}));
   await poll(async()=>await field().count()>0&&await field().inputValue()===resultQuery,'actual original native search restored');
   await poll(()=>page.evaluate(id=>document.activeElement?.closest('tr')?.dataset.fmMessageId===id,searchResult.message),'exact filtered result row focus');
   await c.settled();await audit(escape?'Search result Escape return':'Search result Close return');
  }
  checks.push('Actual body-search result opens the exact NewsItem; Close and Escape restore its native query and exact older row');
  // Native filtering can reset the virtualised window/selection. Observe the
  // empty committed query and restored nonmatching subjects, not a fixed row
  // count or an assumed preservation of the previous window's first row.
  const clearReply=await query('');await poll(async()=>await field().inputValue()===''
   &&(await rows()).some(t=>!t.toLocaleLowerCase().includes(resultQuery.toLocaleLowerCase())),'cleared native search results');
  observations.push({clearReply,clearedWindowRows:await rows()});await close();
  checks.push('Clearing restores the native list and Close returns to the exact Search messages opener');
  // Hold an actual production body response; Tab while it is in flight must
  // cancel focus movement, while a later Close still remembers its origin.
  let heldResolve;const held=new Promise(r=>heldResolve=r),gate=new Promise(r=>release=r);let intercepted=false;
  routeHandler=async route=>{const response=await route.fetch();if(!intercepted){intercepted=true;heldResolve();await gate;}await route.fulfill({response});};
  await page.route(/\/body(?:\?|$)/,routeHandler);await opener().focus();
  const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');opened.catch(()=>{});await page.keyboard.press('Enter');assert.equal((await opened).status(),200);
  await Promise.race([held,new Promise((_,reject)=>setTimeout(()=>reject(Error('No actual body request was held')),10000))]);
  await page.keyboard.press('Tab');const userTarget=await focus();assert.notEqual(userTarget,await opener().getAttribute('id'));
  release();await c.settled();await poll(()=>field().count(),'delayed native field arrived');assert.equal(await focus(),userTarget,'Delayed field must not steal user Tab focus');
  await page.unroute(/\/body(?:\?|$)/,routeHandler);routeHandler=null;await close();await audit('Native inbox after cancelled focus handoff');
  checks.push('Tab cancels a held real-body focus handoff; later Close still returns to its original opener');
  const result={success:true,mvid,checks,audits,observations,date,jsErrors:c.errors,requestFailures:c.requestFailures,network:c.network,manualNVDA:false,saveSubmitted:false,simulationAdvanced:false};
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/message-search-focus.json'),JSON.stringify(result,null,2));return result;
 }catch(error){
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/message-search-focus-failure.json'),JSON.stringify({success:false,mvid,error:String(error.stack||error),checks,audits,observations,focus:await focus().catch(()=>null),jsErrors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));throw error;
 }finally{
  if(release)release();if(routeHandler)await page.unroute(/\/body(?:\?|$)/,routeHandler);
 }
}
module.exports=run;
if(require.main===module)(async()=>{const c=await require('./original-client.cjs')({networkDiagnostics:true});try{console.log(JSON.stringify(await run(c),null,2));}finally{await c.browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
