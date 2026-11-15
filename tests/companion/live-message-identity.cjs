// Actual duplicate medical reports in the owned QA career. No synthetic inbox.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],observations={},routeErrors=[];let releaseBodies;
 const view=()=>page.locator('[data-fm-message-view-id]');
 async function nativePane(){const probe=await c.fetchText('/nodeprobe?type=MessageTitle&depth=1');const ids=[...probe.matchAll(/NewsItem = FM\.UI\.NewsItemReference[^\n]*\{UniqueId=(\d+)\}/g)].map(m=>m[1]);assert.ok(ids.length,'Actual pane NewsItemReference must be readable');assert.equal(new Set(ids).size,1,'Native reading-pane aliases agree');return ids[0];}
 async function open(id){const button=page.locator('tr[data-fm-message-id="'+id+'"] button[onclick*="msgview.open"]');await button.focus();const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');r.catch(()=>{});await page.keyboard.press('Enter');assert.equal(await(await r).text(),'');await poll(async()=>await view().count()===1&&await view().getAttribute('data-fm-message-view-id')===id,'exact message '+id);await settled();assert.equal(await nativePane(),id);await poll(()=>page.locator('[data-fm-msg-body]').first().evaluate(e=>e===document.activeElement),'message body focus');}
 async function close(){await page.keyboard.press('Escape');await poll(async()=>await view().count()===0,'native inbox return');await settled();}
 async function all(){await activate(page.getByRole('tab',{name:'Tudo',exact:true}));await settled();await newest();}
 async function newest(){
  // The inherited inbox preserves its native virtualized-list window. Tudo
  // changes the filter, not the scroll position. Reach actual recent records
  // through the original paging controls before checking report prerequisites.
  observations.newerWindows??=[];observations.newerWindows.push(...await c.newestMessages());
 }
 async function back(id){await page.keyboard.press('Alt+[');await poll(async()=>await view().count()===1&&await view().getAttribute('data-fm-message-view-id')===id,'exact history message '+id);await settled();assert.equal(await nativePane(),id);}
 async function portal(){const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="989379584"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');r.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await r).text(),/^Opening /);await poll(()=>page.getByRole('heading',{name:'Portal',exact:true}).count(),'Portal');await search.fill('');await settled();}
 try{
  if(await page.getByRole('button',{name:'Close message',exact:true}).count())await close();
  await page.keyboard.press('Alt+1');await poll(()=>page.getByRole('heading',{name:'Mensagens',exact:true}).count(),'Messages');await all();
  const rows=await page.locator('tr[data-fm-message-id]').evaluateAll(rs=>rs.map(r=>({id:r.dataset.fmMessageId,key:JSON.stringify([r.cells[1].textContent,r.cells[2].textContent,r.cells[3].textContent])})));
  assert.equal(rows.length,await page.locator('button[onclick*="msgview.open"]').count(),'Every currently rendered native message has its real identifier');
  const group=rows.filter(r=>r.key===rows.find(r=>rows.filter(x=>x.key===r.key).length>=3)?.key);assert.ok(group.length>=3,'Requires three actual same-title/sender/time QA reports');assert.equal(new Set(group.map(r=>r.id)).size,group.length);observations.duplicates=group;
  const [a,b,d]=group.map(r=>r.id);
  await open(a);const focused=await page.evaluate(()=>document.activeElement?.id);await page.evaluate(()=>FM.core.refresh(false,true));await settled();assert.equal(await page.evaluate(()=>document.activeElement?.id),focused);assert.equal(await view().getAttribute('data-fm-message-view-id'),a);
  const audit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name:'Identified duplicate medical report',violations:audit.violations.map(v=>v.id)});assert.deepEqual(audit.violations,[]);
  checks.push('Actual identical-title/sender/time reports retain distinct native UniqueId values; selected view and independent native pane reference agree, with body/neutral focus and clean axe');
  await activate(page.getByRole('button',{name:'Arsenal',exact:true}));await page.keyboard.press('Alt+1');await poll(()=>page.getByRole('heading',{name:'Mensagens',exact:true}).count(),'second native inbox visit');await all();await open(b);
  await activate(page.getByRole('button',{name:'Arsenal',exact:true}));await back(b);await close();await page.keyboard.press('Alt+[');await poll(async()=>await page.title()==='Arsenal','intermediate native club');await page.keyboard.press('Alt+[');await poll(async()=>await view().count()===1&&await view().getAttribute('data-fm-message-view-id')===a,'original duplicate after other pane');await settled();assert.equal(await nativePane(),a);
  checks.push('Two separate native Messages/Arsenal history branches restore their own duplicate report; returning to the older branch selects its real native identity instead of the other same-subject pane');
  await close();await all();const beforeStale=await nativePane();
  await page.route('**/message/open?*',async route=>{try{const u=new URL(route.request().url());u.searchParams.set('screen','obsolete-native-owner');await route.continue({url:u.href});}catch(e){routeErrors.push(e.message);}});
  const staleResponse=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');staleResponse.catch(()=>{});await page.locator('tr[data-fm-message-id="'+d+'"] button[onclick*="msgview.open"]').focus();await page.keyboard.press('Enter');observations.staleScreen=await(await staleResponse).text();assert.match(observations.staleScreen,/screen changed/i);await page.unroute('**/message/open?*');await settled();assert.equal(await view().count(),0);assert.equal(await nativePane(),beforeStale);
  checks.push('Keyboard open with obsolete rendered owner is refused before changing the selected native message or opening a modal');
  let executions=0,lostReply=false;
  await page.route('**/message/open?*',async route=>{try{executions++;const r=await route.fetch();assert.equal(await r.text(),'');await route.abort('failed');lostReply=true;}catch(e){routeErrors.push(e.message);}});
  await page.locator('tr[data-fm-message-id="'+d+'"] button[onclick*="msgview.open"]').focus();await page.keyboard.press('Enter');await poll(async()=>lostReply&&await view().count()===1&&await view().getAttribute('data-fm-message-view-id')===d,'native open with lost reply');await settled();assert.equal(executions,1);assert.equal(await nativePane(),d);await page.unroute('**/message/open?*');await close();await all();observations.lostReplyExecutions=executions;
  checks.push('An actual open whose response is lost runs once; authoritative DOM and native identity establish the correct report without command replay');
  const oldRow=page.locator('tr[data-fm-message-id]').first(),oldId=await oldRow.getAttribute('data-fm-message-id'),oldButton=await oldRow.locator('button[onclick*="msgview.open"]').getAttribute('id');
  let held=0,delivered=0;const gate=new Promise(r=>releaseBodies=r);
  await page.route('**/body?c=1',async route=>{held++;try{await gate;await route.continue();}catch(e){routeErrors.push(e.message);}finally{delivered++;}});
  const filterReply=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');filterReply.catch(()=>{});await page.getByRole('tab',{name:/^Por Ler \(/}).focus();await page.keyboard.press('Enter');assert.equal((await filterReply).status(),200);await poll(()=>held>0,'real native filter result body held');
  observations.changedRows=await c.fetchText('/nodeprobe?type=NewsItemButton&depth=2');const paneAfterFilter=await nativePane();
  const changedReply=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');changedReply.catch(()=>{});await page.locator('#'+oldButton).focus();await page.keyboard.press('Enter');observations.recycled=await(await changedReply).text();observations.oldId=oldId;assert.match(observations.recycled,/message changed|no longer on screen|screen changed|Unknown element/i);assert.equal(await nativePane(),paneAfterFilter);
  releaseBodies();await poll(()=>delivered===held,'all held read handlers released');await settled();await page.unroute('**/body?c=1');assert.equal(await view().count(),0);await all();
  checks.push('An actual native filter changes/recycles rows while its DOM refresh is held; the old visible row command is refused and cannot select another message');
  await portal();assert.deepEqual(c.errors,[]);assert.deepEqual(routeErrors,[]);assert.equal(c.requestFailures.length,1);assert.equal(c.requestFailures[0].path,'/message/open');assert.equal(c.network.failures.length,1);
  const result={success:true,mvid:JSON.parse(await c.fetchText('/health')).coreMvid,checks,audits,observations,routeErrors,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,simulationAdvanced:false,saveSubmitted:false,manualNVDA:false};fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/message-identity.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
 }catch(error){fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/message-identity-failure.json'),JSON.stringify({error:error.stack,checks,audits,observations,routeErrors,errors:c.errors,requestFailures:c.requestFailures,trace:await c.transportTrace(),browser:await page.evaluate(()=>({focus:document.activeElement?.outerHTML,token:FMTOKEN,body:document.getElementById('fmroot')?.innerText}))},null,2));throw error;}
 finally{if(releaseBodies)releaseBodies();await page.unrouteAll({behavior:'wait'});await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
