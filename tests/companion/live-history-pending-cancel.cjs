// A new keyboard choice cancels an undispatched history request. One real
// parent response is delayed; no native state, response content or clock mocked.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const observations={},checks=[],audits=[];let release,holding=false;
 async function go(uid,obj){const item=page.locator('#fmnav-tree [data-uid="'+uid+'"]'+(obj?'[data-obj="'+obj+'"]':'')).first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});await search.fill(await item.locator(':scope > span').textContent());await item.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await settled();await search.fill('');await settled();}
 try{
  observations.mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});observations.ownedSave=await file.inputValue();assert.match(observations.ownedSave,/^FM27Access QA Dialog Regression /);await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await go('989379584');await go('837367296','2000104296');await settled();observations.before=await page.evaluate(()=>({index:FM.core.historyIndex,ready:FM.core.historyReady}));assert.ok(observations.before.ready);
  let held=0,forward=0;const gate=new Promise(resolve=>release=resolve);
  page.on('request',r=>{const u=new URL(r.url());if(u.pathname==='/history'&&u.searchParams.get('dir')==='forward')forward++;});
  await page.route('**/body?*',async route=>{const response=await route.fetch(),title=decodeURIComponent((response.headers()['x-fm-title']||'').replace(/\+/g,' '));if(title==='Portal'&&!held){held++;observations.held={title,index:response.headers()['x-fm-history-index']??null};await gate;}await route.fulfill({response});});holding=true;
  await page.keyboard.press('Alt+[');await poll(()=>held===1,'actual native Portal response held');
  await page.keyboard.press('Alt+]');assert.equal(forward,0);assert.equal(await page.evaluate(()=>FM.core.historyMove?.waiting),true);
  await page.getByRole('textbox',{name:'Search all screens',exact:true}).focus();await page.keyboard.press('Tab');observations.choice=await page.evaluate(()=>({focus:document.activeElement.id,move:FM.core.historyMove}));assert.ok(observations.choice.focus);assert.equal(observations.choice.move,null);
  release();await page.unrouteAll({behavior:'wait'});holding=false;
  await poll(()=>page.evaluate(index=>document.title==='Portal'&&FM.core.historyReady&&FM.core.historyIndex<index,observations.before.index),'actual parent history body delivered');await settled();
  observations.after=await page.evaluate(()=>({index:FM.core.historyIndex,focus:document.activeElement.id,move:FM.core.historyMove}));observations.forwardRequests=forward;observations.nativeHistory=await c.fetchText('/history');
  assert.equal(forward,0,'Do not send a canceled deferred native command');assert.equal(observations.after.focus,observations.choice.focus,'Actual new keyboard focus survives later parent delivery');assert.match(observations.nativeHistory,new RegExp('^index='+observations.after.index+' '));
  const audit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name:'Actual parent after canceled deferred Forward',violations:audit.violations.map(v=>v.id)});assert.deepEqual(audit.violations,[]);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  checks.push('Tab while a genuine Portal body is held cancels queued Forward before dispatch; real native Portal index and exact new navigator focus survive delivery, with zero Forward requests');
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/history-pending-cancel.json'),JSON.stringify({success:true,mvid:observations.mvid,checks,audits,observations,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false,errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));console.log(JSON.stringify({success:true,mvid:observations.mvid,checks,audits},null,2));
 }catch(error){if(holding){release();await page.unrouteAll({behavior:'wait'});holding=false;}fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/history-pending-cancel-failure.json'),JSON.stringify({success:false,error:error.stack,checks,audits,observations,title:await page.title(),html:await page.locator('#fmroot').innerHTML(),errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));throw error;}
 finally{if(holding){release();await page.unrouteAll({behavior:'wait'});}await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
