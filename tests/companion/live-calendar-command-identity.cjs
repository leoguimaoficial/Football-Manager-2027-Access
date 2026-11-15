// Real DateGrid recycling across September/October. Withhold the genuine body
// BEFORE its server render, then exercise old week/session commands. Opening
// and canceling pickers only; no schedule/session write, clipboard or save.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],observations={},out=path.resolve(__dirname,'../../artifacts/tests/web');
 let baseline,week,session,owner,monthChanged=false,release,held=0;
 const snapshot=()=>page.locator('#fmroot table tbody tr').evaluateAll(rs=>rs.map(r=>({header:r.querySelector('th')?.innerText,cells:Array.from(r.querySelectorAll('td')).map(d=>d.innerText)})));
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 async function monthNative(){const native=await c.fetchText('/gui');return{native,month:native.match(/SIDropdown name="MonthDropdown"[\s\S]*?SIText name="StringValue" visible=True text="([^"]+)"/)?.[1]};}
 async function refused(id){const text=await c.fetchText('/activate?id='+encodeURIComponent(id)+'&screen='+encodeURIComponent(owner));assert.match(text,/calendar date changed|Unknown element|screen changed|no longer on screen/);return text;}
 try{
  observations.mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  assert.equal(await page.title(),'Treino','Begin at the real protected September training calendar');
  const trainingTab=page.getByRole('tab',{name:'Treino',exact:true});if(await trainingTab.getAttribute('aria-selected')!=='true')await activate(trainingTab);
  assert.equal(await trainingTab.getAttribute('aria-selected'),'true');
  const initial=await monthNative();assert.equal(initial.month,'Setembro de 2026');observations.initialNative=initial.native;
  baseline=await snapshot();assert.equal(baseline.length,5);assert.ok(baseline.every(r=>r.cells.length===7));
  week=await page.locator('#fmroot table tbody tr').evaluateAll(rs=>rs.flatMap((r,index)=>{const b=r.querySelector('th button:not(:disabled)');return b?[{id:b.id,name:b.textContent,index,date:r.querySelector('td')?.innerText.split('\n')[0]}]:[]})[0]);assert.equal(week.date,'21');
  session=await page.locator('#fmroot table tbody tr').nth(week.index).locator('td').first().locator('button').first().evaluate(e=>({id:e.id,name:e.textContent}));owner=await page.evaluate(()=>FMTOKEN);observations.original={week,session,owner};
  const gate=new Promise(resolve=>release=resolve);
  await page.route('**/body?*',async route=>{held++;await gate;await route.continue();});
  await page.getByRole('button',{name:'Next',exact:true}).focus();const next=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');next.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await next).text(),/^Activated /);monthChanged=true;
  await page.evaluate(()=>FM.core.refresh(true));await poll(()=>held>0,'real new-month body withheld before rendering');
  let actual;await poll(async()=>{actual=await monthNative();return actual.month==='Outubro de 2026';},'native October record rebind');observations.afterNativeRebind=actual;
  assert.deepEqual(await snapshot(),baseline,'Only native records changed; old September DOM is deliberately retained');
  observations.beforeBody={week:await refused(week.id),session:await refused(session.id),heldBodies:held};
  assert.equal(await page.title(),'Treino');
  checks.push('While the genuine replacement body is withheld before server rendering, native October record rebinding makes both old September week and session commands refuse without opening another picker');
  release();release=null;await page.unrouteAll({behavior:'wait'});await settled();await poll(async()=>JSON.stringify(await snapshot())!==JSON.stringify(baseline),'actual October DOM');
  observations.afterBody={week:await refused(week.id),session:await refused(session.id)};
  assert.equal(await page.locator('#'+week.id).count(),0);assert.equal(await page.locator('#'+session.id).count(),0);await audit('Actual October calendar with date-scoped command IDs');
  checks.push('After the actual October body arrives, old September IDs remain unknown instead of resolving to the same physical row in the new month');
  const october=await snapshot(),newWeek=page.locator('#fmroot table tbody tr').nth(week.index).locator('th button').first(),newId=await newWeek.getAttribute('id'),newLabel=await newWeek.textContent();assert.notEqual(newId,week.id);
  await activate(newWeek);await poll(()=>page.locator('[data-fm-native-overlay]').count(),'current October native picker');assert.equal(await page.locator('#fmsec0-h').textContent(),newLabel);await audit('Current October week remains normally operable');await page.keyboard.press('Escape');await settled();assert.equal(await page.evaluate(()=>document.activeElement.id),newId);assert.deepEqual(await snapshot(),october);
  checks.push('The new October week opens its own native preset, cancels with Escape and restores its exact date-scoped focus and unchanged calendar');
  await activate(page.getByRole('button',{name:'Prev',exact:true}));monthChanged=false;await poll(async()=>JSON.stringify(await snapshot())===JSON.stringify(baseline),'exact original September calendar');await settled();assert.equal((await monthNative()).month,'Setembro de 2026');
  assert.equal(await page.locator('#'+week.id).textContent(),week.name);assert.equal(await page.locator('#'+session.id).textContent(),session.name);
  await activate(page.locator('#'+week.id));await poll(()=>page.locator('[data-fm-native-overlay]').count(),'original September week');assert.equal(await page.locator('#fmsec0-h').textContent(),week.name);await page.keyboard.press('Escape');await settled();assert.equal(await page.evaluate(()=>document.activeElement.id),week.id);
  await activate(page.locator('#'+session.id));await poll(async()=>await page.title()==='Sessões de treino','original September session');assert.equal((await page.locator('#fmroot table').first().locator('tbody td').first().innerText()).split('\n')[0],'21');await activate(page.getByRole('button',{name:'Cancelar',exact:true}));await settled();assert.deepEqual(await snapshot(),baseline);assert.equal(await page.evaluate(()=>document.activeElement.id),session.id);await audit('Exact September date identities and session return');
  checks.push('Native Prev restores every original week/day and original date IDs; original week and September21 session still open and cancel with exact focus, without editing training');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  fs.writeFileSync(path.join(out,'calendar-command-identity.json'),JSON.stringify({success:true,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,network:c.network,trainingChanged:false,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));
 }catch(error){
  const before={title:await page.title(),html:await page.locator('#fmroot').innerHTML(),focus:await page.evaluate(()=>document.activeElement.id)},cleanup=[];
  if(release)release();try{await page.unrouteAll({behavior:'wait'});await settled();if(await page.getByRole('button',{name:'Close',exact:true}).count())await activate(page.getByRole('button',{name:'Close',exact:true}));if(monthChanged){await activate(page.getByRole('button',{name:'Prev',exact:true}));await settled();}cleanup.push({restored:JSON.stringify(await snapshot())===JSON.stringify(baseline)});}catch(e){cleanup.push({error:e.stack});}
  fs.writeFileSync(path.join(out,'calendar-command-identity-failure.json'),JSON.stringify({success:false,error:error.stack,checks,audits,observations,before,cleanup,errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));throw error;
 }finally{await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
