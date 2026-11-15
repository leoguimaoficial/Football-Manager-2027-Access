// Actual paused Arsenal/Man Utd historical replay, opened from the owned QA
// September 19 fixture. Native timeline/transport only; no career simulation,
// match-result edits, playback-preference edits or save submission.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll}=c;
 const checks=[],audits=[],observations=[];let success=false,failure=null,mvid,diagnostic;
 const slider=()=>page.getByRole('slider',{name:'Calendário',exact:true});
 async function action(button){
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');response.catch(()=>{});
  await button.focus();await page.keyboard.press('Enter');const r=await response;
  assert.equal(r.status(),200);const body=await r.text();assert.match(body,/^Activated /);return body;
 }
 async function adjust(key){
  await slider().focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/setvalue');response.catch(()=>{});
  await page.keyboard.press(key);const r=await response;assert.equal(r.status(),200);
  assert.equal(await r.text(),'Playback position selected.');
  assert.ok(await slider().evaluate(e=>e===document.activeElement));
 }
 async function audit(name){const r=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:r.violations.map(v=>v.id)});assert.deepEqual(r.violations,[]);}
 const snapshot=()=>slider().evaluate(e=>({min:e.min,max:e.max,step:e.step,value:e.value,text:e.getAttribute('aria-valuetext'),label:e.labels[0].textContent.trim()}));
 const eventTable=()=>page.locator('table').filter({has:page.getByRole('columnheader',{name:'Event',exact:true})}).filter({has:page.getByRole('columnheader',{name:'Team',exact:true})});
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  assert.equal(await page.getByRole('button',{name:'Play (match paused)',exact:true}).count(),1,'Pause the actual historical playback first');
  assert.match(await page.locator('h1').innerText(),/ARS.*MUN/);
  assert.equal(await slider().count(),1);const before=await snapshot();observations.push({before});
  assert.ok(+before.max>+before.min&&+before.step>0);assert.match(before.text,/^\d+(?:\.\d+)?% selected; played \d+:\d{2}$/);
  assert.equal(before.label,'Calendário');await audit('Paused native historical timeline');
  checks.push('Official native timeline caption, actual selected percentage and separately identified played clock; original range/step, no raw time-slice or invented minute announcement');
  await adjust('ArrowRight');await poll(async()=>(await snapshot()).text!==before.text,'native selection clock');
  const selected=await snapshot();assert.ok(+selected.value>+before.value);observations.push({selected});
  await slider().focus();const sliderId=await slider().getAttribute('id');
  const description=page.waitForResponse(r=>new URL(r.url()).pathname==='/describe');description.catch(()=>{});
  const speech=page.waitForResponse(r=>new URL(r.url()).pathname==='/speak');speech.catch(()=>{});
  await page.keyboard.press('Alt+i');const help=await(await description).text();assert.match(help,/selected passage when resumed/);
  assert.equal((await speech).status(),200);
  // Other native timeline markers can bind during speech. Check this control
  // and absence of injected help, rather than freezing the whole live page.
  assert.equal(await slider().getAttribute('id'),sliderId);assert.ok(await slider().evaluate(e=>e===document.activeElement));
  assert.equal(await page.getByText(help,{exact:true}).count(),0);
  checks.push('Arrow selection changes the native-backed selected position while retaining focus; original Alt+I explains the separate played clock and resume semantics without DOM insertion');
  // Entry may be paused at any passage. Select the actual native start and
  // observe its engine reset before waiting for the first played goal. A
  // pre-existing score/marker must not masquerade as a new resume result.
  await adjust('Home');await poll(async()=>+await slider().inputValue()===+before.min,'native first passage selected');
  await action(page.getByRole('button',{name:'Play (match paused)',exact:true}));
  await poll(async()=>/ARS 0 - 0 MUN/.test(await page.locator('h1').innerText()),'native first-passage lead-in after resume');
  // The native highlight includes its lead-in. Observe the actual first goal,
  // not an assumed equality between raw engine slices and the official minute.
  const until=Date.now()+90000;let marker;
  while(Date.now()<until){
   marker=page.getByRole('button',{name:"5', Goal, Ella Toone, assisted by Elisabeth Terland",exact:true});
   if(await marker.count()&&/ARS 0 - 1 MUN/.test(await page.locator('h1').innerText()))break;
   await page.waitForTimeout(250);
  }
  assert.equal(await marker.count(),1,'Native played goal exposes official minute/scorer/assist');
  assert.match(await page.locator('h1').innerText(),/ARS 0 - 1 MUN/,'Actual playback engine reached the first goal');
  await action(page.getByRole('button',{name:'Pause (match playing)',exact:true}));
  await poll(()=>page.getByRole('button',{name:'Play (match paused)',exact:true}).count(),'native pause');
  observations.push({playedHeading:await page.locator('h1').innerText(),marker:await marker.innerText(),range:await snapshot()});
  assert.equal(await slider().count(),1,'Commentary cannot replace the timeline caption');
  await audit('Played native goal marker and paused transport');
  checks.push('Native resume reaches the goal; timeline marker retains actual TimeString, named Player/Player2 and verified assist meaning');
  const beforeEnd=await snapshot();
  await adjust('End');await poll(async()=>+await slider().inputValue()>+before.max-+before.step&&(await snapshot()).text!==beforeEnd.text,'native final passage selection');
  observations.push({endSelection:await snapshot()});
  await action(page.getByRole('button',{name:'Play (match paused)',exact:true}));
  // End selects the last native passage, which still plays its actual lead-in
  // and stoppage-time tail. A normal 20-second UI-action deadline is too short.
  const endDeadline=Date.now()+90000;
  while(Date.now()<endDeadline&&!await page.getByRole('heading',{name:'Relatório no final do jogo',exact:true}).count())await page.waitForTimeout(250);
  assert.equal(await page.getByRole('heading',{name:'Relatório no final do jogo',exact:true}).count(),1,'native replay-end report');
  await poll(async()=>await eventTable().count()===1&&await eventTable().locator('tbody tr').count()===5
   &&(await eventTable().locator('tbody tr').allTextContents()).every(t=>t.includes('Goal')&&t.includes('assisted by')),'cold native incident sprites heal without F5');
  observations.push({fulltimeHeadings:await page.locator('h1,h2').allTextContents(),events:await eventTable().locator('tbody tr').allTextContents()});
  assert.equal(await page.locator('h1').count(),1);assert.equal(await page.locator('h2').filter({hasText:/^Navigation$/}).count(),1);
  await audit('Cold native fulltime report');
  checks.push('Native replay-end transition recovers all five Goal/assist rows without manual refresh; one score h1 and subordinate Navigation/report headings');
  await action(page.getByRole('button',{name:'Continuar',exact:true}));
  await poll(()=>page.getByRole('heading',{name:'Portal',exact:true}).count(),'actual career parent');
  assert.ok((await page.locator('#fmroot').innerText()).includes('SÁB, 19/9/2026'));
  await audit('Career Portal after native replay');
  checks.push('Native Continue returns to Portal on the same career day');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(error){failure=error.stack||String(error);diagnostic={html:await page.locator('#fmroot').innerHTML(),native:await c.fetchText('/gui'),transport:await c.transportTrace()};throw error;
 }finally{
  const dir=path.resolve(__dirname,'../../artifacts/tests/web');fs.mkdirSync(dir,{recursive:true});
  const report={success,mvid,failure,diagnostic,checks,audits,observations,network:c.network,jsErrors:c.errors,requestFailures:c.requestFailures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
  fs.writeFileSync(path.join(dir,'historical-replay.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));await c.browser.close();
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
