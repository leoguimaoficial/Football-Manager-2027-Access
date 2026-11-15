// Actual Arrange Friendly draft: never chooses an opponent or submits a fixture.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],controlled=[];let original,id,origin;
 async function audit(name){const a=await new AxeBuilder({page}).analyze();audits.push({name,violations:a.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});assert.deepEqual(a.violations,[]);}
 const date=()=>page.locator('button[onclick^="fmdate("]');
 async function open(){await date().focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/date/cal');response.catch(()=>{});await page.keyboard.press('Enter');const r=await response;assert.equal(r.status(),200);assert.ok((await r.text()).startsWith('<'));await poll(()=>page.locator('[data-cal-grid]').count(),'actual calendar');}
 async function pickDay(day){await page.locator('[data-day="'+day+'"]:not([data-dis])').focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/date/set');response.catch(()=>{});await page.keyboard.press('Enter');const t=await(await response).text();assert.match(t,/^Set to /);await settled();await poll(()=>page.evaluate(id=>document.activeElement?.id===id,id),'date origin after apply');return t;}
 async function observe(value){await poll(async()=>await date().innerText()===value,'observed date '+value);const native=await c.fetchText('/gui');const [d,m,y]=value.split('/').map(Number);assert.match(native,new RegExp('name="dateSelectedText"[^\\n]*text="'+d+'/'+m+'/'+y+'"'));}
 try{
  await page.keyboard.press('Alt+6');await poll(()=>page.getByRole('button',{name:'Marcar Amigável',exact:true}).count(),'native Agenda');await settled();
  origin=await page.getByRole('button',{name:'Marcar Amigável',exact:true}).getAttribute('id');await activate(page.locator('#'+origin));
  await poll(()=>date().count(),'native friendly draft');await settled();id=await date().getAttribute('id');original=await date().innerText();
  assert.equal(await page.getByRole('heading',{level:1,name:'Marcar amigável',exact:true}).evaluate(e=>e===document.activeElement),true);
  assert.equal(await page.locator('main').count(),1);assert.equal(await page.getByRole('dialog').count(),0,'An ordinary native card is not an answer dialog');
  assert.equal(await date().getAttribute('aria-label'),'Data: '+original);assert.equal(await page.getByRole('button',{name:'Confirmar',exact:true}).isDisabled(),true,'No opponent is selected');
  await open();const selected=await page.evaluate(()=>document.activeElement?.getAttribute('data-day'));
  await page.evaluate(()=>FM.core.refresh(false,true));await settled();assert.equal(await page.evaluate(()=>document.activeElement?.getAttribute('data-day')),selected);
  assert.equal(await page.evaluate(()=>FM.core.dd),id);await audit('Native friendly date calendar');
  const monthBefore=await page.locator('[data-cal]').evaluate(e=>[Number(e.dataset.cy),Number(e.dataset.cm)]);
  const nextTarget=(await page.locator('[data-cal-nav]').getAttribute('data-nm')).split('/').map(Number);
  assert.equal(nextTarget.length,2,'Actual friendly controller exposes another available month');
  for(const suffix of ['nm','pm']){
   const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/date/cal');r.catch(()=>{});await page.locator('#'+id+'-'+suffix).focus();await page.keyboard.press('Enter');assert.equal((await r).status(),200);
   const expected=suffix==='nm'?nextTarget:monthBefore;await poll(async()=>JSON.stringify(await page.locator('[data-cal]').evaluate(e=>[Number(e.dataset.cy),Number(e.dataset.cm)]))===JSON.stringify(expected),'controller month '+suffix);
   assert.ok(await page.locator('[data-day]:not([data-dis])').count()>0,'Navigation lands only in a native available month');
  }
  checks.push('Native controller-provided next/previous month targets are navigable by keyboard and retain real enabled days; returns to the original month without applying a date');
  await page.keyboard.press('Escape');await settled();assert.equal(await page.evaluate(()=>document.activeElement?.id),id);assert.equal(await page.locator('[data-cal-grid]').count(),0);
  checks.push('Actual card title/landmark, native date label, selected day and neutral refresh; Escape restores the disclosure and releases deferred updates');
  await open();const days=await page.locator('[data-day]:not([data-dis])').evaluateAll(es=>es.map(e=>Number(e.getAttribute('data-day'))));
  const alternative=days.find(d=>String(d)!==selected);assert.ok(alternative,'This draft needs two actual available dates');
  const [od,om,oy]=original.split('/').map(Number),changed=String(alternative).padStart(2,'0')+'/'+String(om).padStart(2,'0')+'/'+oy;
  await pickDay(alternative);await observe(changed);await open();assert.equal(await page.evaluate(()=>document.activeElement?.getAttribute('data-day')),String(alternative));await pickDay(od);await observe(original);
  checks.push('Keyboard native date commit changes the real dateSelectedText, reopens selected, and inverse commit restores the exact original value');
  const refusal=await c.fetchText('/date/set?id='+id+'&value='+encodeURIComponent(changed)+'&screen=obsolete-owner');assert.match(refusal,/screen changed/i);await observe(original);
  checks.push('Obsolete native screen token refuses the date operation before changing the draft');
  let failed=0;await page.route('**/date/cal?*',async route=>{failed++;await route.fulfill({status:503,body:'controlled refusal'});});
  await date().focus();await page.keyboard.press('Enter');await poll(()=>failed===1&&!page.isClosed(),'single refused month request');await poll(()=>page.evaluate(()=>!FM.core.dd&&!FM.date.openId&&!FM.core.refreshing),'calendar refusal releases interaction');
  assert.equal(await page.locator('[data-cal-grid]').count(),0);assert.equal(await page.evaluate(()=>document.activeElement?.id),id);await page.unroute('**/date/cal?*');controlled.push({route:'/date/cal',kind:'HTTP503 before execution',count:failed});await observe(original);
  checks.push('A refused calendar read releases the shared interaction gate, retains native date and returns focus without retrying');
  let releaseCalendar,heldCalendar=false,deliveredCalendar=false;const calendarGate=new Promise(r=>releaseCalendar=r);
  await page.route('**/date/cal?*',async route=>{const r=await route.fetch();heldCalendar=true;await calendarGate;await route.fulfill({response:r});deliveredCalendar=true;});
  await date().focus();await page.keyboard.press('Enter');await poll(()=>heldCalendar,'actual calendar response held');await page.keyboard.press('Escape');releaseCalendar();
  await poll(()=>deliveredCalendar,'late cancelled calendar delivered');await page.unroute('**/date/cal?*');await settled();assert.equal(await page.locator('[data-cal-grid]').count(),0);assert.equal(await page.evaluate(()=>document.activeElement?.id),id);assert.equal(await page.getByRole('heading',{level:1,name:'Marcar amigável',exact:true}).count(),1);
  checks.push('Escape during a held real calendar response cancels only the disclosure; its late reply cannot reopen the grid or close the native form');
  await open();let executed=0,reply,releaseApply;const applyGate=new Promise(r=>releaseApply=r);
  await page.route('**/date/set?*',async route=>{executed++;const r=await route.fetch();reply=await r.text();await applyGate;await route.abort('failed');});
  await page.locator('[data-day="'+alternative+'"]:not([data-dis])').focus();await page.keyboard.press('Enter');await poll(()=>executed===1&&!!reply,'native date executed with lost reply');assert.match(reply,/^Set to /);await page.keyboard.press('Enter');assert.equal(executed,1,'Repeated Enter while apply is in flight must not issue another command');releaseApply();
  await poll(()=>page.evaluate(()=>!FM.core.dd&&!FM.date.openId&&!FM.core.refreshing),'lost date response recovery');await observe(changed);assert.equal(await page.evaluate(()=>document.activeElement?.id),id);
  await page.unroute('**/date/set?*');controlled.push({route:'/date/set',kind:'native execution then lost reply',count:executed});await open();await pickDay(od);await observe(original);
  checks.push('A date execution with lost reply runs once, reports uncertainty, observes the actual changed date, and permits explicit inverse restoration');
  await open();await activate(page.getByRole('button',{name:'Cancelar',exact:true}));await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'exact Agenda origin after cancellation');
  assert.equal(await page.evaluate(()=>FM.core.dd),null);assert.equal(await date().count(),0);checks.push('Native Cancel while the calendar is open closes the draft and restores the exact Agenda opener; no fixture submitted');
  const portal=page.locator('#fmnav-tree [role=treeitem][data-uid="989379584"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await portal.locator(':scope > span').textContent());await portal.focus();const navigation=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');navigation.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await navigation).text(),/^Opening /);await poll(()=>page.getByRole('heading',{name:'Portal',exact:true}).count(),'native Portal restoration');await search.fill('');await settled();
  assert.deepEqual(c.errors,[]);assert.equal(c.requestFailures.length,1,'Only the explicitly lost date response is aborted');assert.equal(c.requestFailures[0].path,'/date/set');assert.equal(c.network.failures.length,1);
  const result={success:true,mvid:JSON.parse(await c.fetchText('/health')).coreMvid,original,checks,audits,controlled,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,fixtureSubmitted:false,manualNVDA:false};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/date-field.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
 }catch(error){fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/date-field-failure.json'),JSON.stringify({error:error.stack,original,id,checks,audits,controlled,errors:c.errors,requestFailures:c.requestFailures,nativeTrace:await c.transportTrace(),browser:await page.evaluate(()=>({focus:document.activeElement?.outerHTML,date:FM.date,dd:FM.core.dd,stack:FM.dialog.stack,body:document.getElementById('fmroot')?.innerText}))},null,2));throw error;}
 finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
