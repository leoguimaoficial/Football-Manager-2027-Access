// Real native defender swap/restoration, plus stale/refused/lost-reply cases.
// Owned QA career only. Native occupants are read independently through the
// production main-thread inspector; no fixture model or game-state injection.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],observations={},controlled=new Set(),spoken=[];let baseline=null,initialPhase=null;
 page.on('request',r=>{if(new URL(r.url()).pathname==='/speak')spoken.push(new URL(r.url()).searchParams.get('text')||'');});
 const mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
 const nativeCode='var a=System.AppDomain.CurrentDomain.GetAssemblies()[0];foreach(var item in System.AppDomain.CurrentDomain.GetAssemblies())if(item.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'")a=item;var e=a.GetType("FM27Access.Plugin.Tactics.PitchMoveEngine");var pitch=e.GetMethod("FindPitch",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,null);var t=a.GetType("FM27Access.Plugin.Tactics.PitchGrid");var rows=(System.Collections.Generic.Dictionary<string,string>)t.GetMethod("ReadOccupants",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{pitch});foreach(var row in rows)System.Console.WriteLine(row.Key+"="+row.Value);';
 async function native(){const result=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),nativeCode);assert.doesNotMatch(result,/Exception|error CS/);return Object.fromEntries(result.trim().split(/\r?\n/).map(line=>{const at=line.indexOf('=');assert.ok(at>0);return[line.slice(0,at),line.slice(at+1)];}));}
 const dom=()=>page.locator('#fm-pitch-move [data-slot][data-occ]').evaluateAll(es=>Object.fromEntries(es.map(e=>[e.getAttribute('data-slot'),e.getAttribute('data-name')])));
 async function go(uid){
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});
  await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await settled();await search.fill('');await settled();
 }
 async function place(lost=false){
  await page.locator('#bc-DCL').focus();await page.keyboard.press('Enter');await page.keyboard.press('ArrowDown');await page.keyboard.press('ArrowDown');await page.keyboard.press('Enter');
  assert.equal(await page.locator('#fm-pitch-move').isVisible(),true);
  for(let step=0;step<5&&await page.evaluate(()=>document.activeElement?.getAttribute('data-slot'))!=='DCR';step++)await page.keyboard.press('ArrowRight');
  assert.equal(await page.evaluate(()=>document.activeElement?.getAttribute('data-slot')),'DCR');
  const waiting=lost?page.waitForEvent('requestfailed',r=>new URL(r.url()).pathname==='/tactics/move'):page.waitForResponse(r=>new URL(r.url()).pathname==='/tactics/move');waiting.catch(()=>{});
  await page.keyboard.press('Enter');const response=await waiting;const reply=lost?{failure:response.failure()?.errorText}:{status:response.status(),text:await response.text()};
  await settled();return{...reply,focus:await page.evaluate(()=>document.activeElement?.id),source:await page.evaluate(()=>FM.pitch.src)};
 }
 try{
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const name=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>name.count(),'owned QA identity');observations.ownedSave=await name.inputValue();assert.match(observations.ownedSave,/^FM27Access QA /);await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await go(195478016);initialPhase=await page.getByRole('tab',{selected:true}).textContent();await c.activate(page.getByRole('tab',{name:'Com Posse de Bola',exact:true}));
  await poll(()=>page.locator('#fm-pitch-browse [data-role]').count(),'actual editable native pitch');baseline=await native();assert.equal(Object.keys(baseline).length,11);assert.ok(baseline.DCL&&baseline.DCR&&baseline.DCL!==baseline.DCR);assert.deepEqual(await dom(),baseline);
  observations.baseline=baseline;
  const stale=async route=>{const url=new URL(route.request().url());url.searchParams.set('screen','obsolete-native-pitch-snapshot');await route.continue({url:url.href});};
  await page.route('**/tactics/move?*',stale);try{observations.stale=await place();assert.match(observations.stale.text,/This screen changed/);assert.equal(observations.stale.focus,'bc-DCL');assert.deepEqual(await native(),baseline);}finally{await page.unroute('**/tactics/move?*',stale);}
  checks.push('Actual backend main-thread stale-screen guard refuses the keyboard move before native execution and retains all eleven occupants/source focus');
  const swapped={...baseline,DCL:baseline.DCR,DCR:baseline.DCL};observations.forward=await place();assert.match(observations.forward.text,/^Swapped /);assert.equal(observations.forward.focus,'bc-DCR');assert.deepEqual(await native(),swapped);assert.deepEqual(await dom(),swapped);
  observations.reverse=await place();assert.match(observations.reverse.text,/^Swapped /);assert.deepEqual(await native(),baseline);assert.deepEqual(await dom(),baseline);
  checks.push('Keyboard-only actual native defender swap and inverse: independent native occupants and DOM agree; destination focus follows the moved player; all eleven original positions restored');
  let blocked=0;const refusal=async route=>{blocked++;await route.fulfill({status:503,contentType:'text/plain',body:'Controlled QA refusal'});};const speechBefore=spoken.length;
  await page.route('**/tactics/move?*',refusal);try{observations.refused=await place();assert.equal(observations.refused.status,503);assert.equal(observations.refused.focus,'bc-DCL');assert.equal(observations.refused.source,null);assert.equal(blocked,1);assert.deepEqual(await native(),baseline);assert.ok(spoken.slice(speechBefore).some(t=>t.includes('could not be confirmed')));}finally{await page.unroute('**/tactics/move?*',refusal);}
  checks.push('Controlled HTTP refusal executes no native move, reports uncertainty once, clears placement state and restores source focus without replay');
  let applied=0;const failuresBefore=c.requestFailures.length,start=spoken.length;
  const lose=async route=>{applied++;const response=await route.fetch();assert.equal(response.status(),200);observations.nativeAppliedReply=await response.text();assert.match(observations.nativeAppliedReply,/^Swapped /);await route.abort('connectionfailed');};
  await page.route('**/tactics/move?*',lose);try{observations.lost=await place(true);assert.equal(applied,1);assert.equal(observations.lost.focus,'bc-DCL');assert.equal(observations.lost.source,null);assert.deepEqual(await native(),swapped);assert.deepEqual(await dom(),swapped);assert.ok(spoken.slice(start).some(t=>t.includes('could not be confirmed')));assert.ok(!spoken.slice(start).some(t=>t.startsWith('Swapped ')));for(const f of c.requestFailures.slice(failuresBefore))if(f.path==='/tactics/move'&&f.error==='net::ERR_CONNECTION_FAILED')controlled.add(f);}finally{await page.unroute('**/tactics/move?*',lose);}
  observations.restoredAfterLost=await place();assert.match(observations.restoredAfterLost.text,/^Swapped /);assert.deepEqual(await native(),baseline);assert.deepEqual(await dom(),baseline);
  checks.push('A real native success whose reply is lost is never replayed or falsely acknowledged; native/DOM changed state is observed, then an explicit inverse restores every original occupant');
  const axe=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();assert.deepEqual(axe.violations,[]);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures.filter(f=>!controlled.has(f)),[]);
  await c.activate(page.getByRole('tab',{name:initialPhase,exact:true}));await go(989379584);
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/pitch-move.json'),JSON.stringify({success:true,mvid,checks,observations,violations:[],controlledRequestFailures:[...controlled],unexpectedRequestFailures:[],errors:c.errors,phaseRestored:true,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));console.log(JSON.stringify({success:true,checks,errors:c.errors,controlledFailures:controlled.size},null,2));
 }catch(error){
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/pitch-move-failure.json'),JSON.stringify({success:false,error:error.stack||String(error),mvid,checks,observations,errors:c.errors,requestFailures:c.requestFailures,network:c.network,transportTrace:await c.transportTrace()},null,2));throw error;
 }finally{
  // Observe the actual outcome even if the HTTP reply was lost. Never blindly
  // reverse a boolean recorded before native execution was known.
  if(baseline&&await page.locator('#fm-pitch-move').count()){
   const current=await native().catch(()=>null),swapped={...baseline,DCL:baseline.DCR,DCR:baseline.DCL};
   if(current&&JSON.stringify(current)===JSON.stringify(swapped))await place().catch(e=>console.error('Native lineup restoration requires inspection:',e.message));
  }
  await c.browser.close();
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
