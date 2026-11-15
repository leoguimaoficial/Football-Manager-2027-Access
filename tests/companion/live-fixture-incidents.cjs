// Real career fixture selection with actual concurrent native body harvesting.
// Every incident stays bound to the selected native fixture. No match playback,
// career simulation, save, result edit or command retry is performed.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),artifacts=path.join(root,'artifacts');
const checkpoint='FM27Access QA Dialog Regression 20261110 Responsibilities Restored 20261110104840158';
const referenceArg=process.argv.find(a=>a.startsWith('--native-reference='));
const reference=referenceArg?JSON.parse(fs.readFileSync(path.resolve(root,referenceArg.slice(19)),'utf8')):null;
const profileReferenceArg=process.argv.find(a=>a.startsWith('--profile-reference='));
const profileReference=profileReferenceArg?JSON.parse(fs.readFileSync(path.resolve(root,profileReferenceArg.slice(20)),'utf8')):null;
if(profileReference)assert.equal(profileReference.checkpoint,checkpoint);
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,checks:[],audits:[],cases:[],responses:[],saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const output=path.join(artifacts,'tests/web/fixture-incidents.json');
 function persist(){fs.writeFileSync(output,JSON.stringify(report,null,2));}
 const radio=label=>page.getByRole('radio',{name:label,exact:true});
 const fixtureTables=()=>page.locator('#fmroot table').filter({has:page.locator('input[type=radio]')});
 const fixtureData=()=>fixtureTables().evaluateAll(ts=>ts.map(t=>({headers:[...t.querySelectorAll('thead th')].map(e=>e.innerText),rows:[...t.querySelectorAll('tbody tr')].map(r=>[...r.cells].map(e=>e.innerText.replace(/\s+/g,' ').trim()))})));
 async function audit(stage){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 async function profileBanner(name){
  const table=page.locator('#fmroot table').filter({has:page.getByRole('rowheader',{name:'Contract',exact:true})});
  assert.equal(await table.count(),1,'Actual '+name+' native profile keeps its inherited field/value table');
  const rows=await table.locator(':scope > tbody > tr').evaluateAll(rs=>rs.map(r=>({label:r.querySelector(':scope > th')?.textContent??null,value:r.querySelector(':scope > td')?.textContent,actionSpan:r.querySelector(':scope > td')?.getAttribute('colspan'),buttons:[...r.querySelectorAll('button')].map(b=>b.textContent)})));
  assert.equal(rows.filter(r=>r.label!==null&&!r.label.trim()).length,0,'Actual '+name+' profile has no empty scalar-field headers');
  if(profileReference?.profile===name){
   const missing=profileReference.rows.filter(r=>r.label===''&&r.value==='1'&&r.buttons.length===0);assert.equal(missing.length,1,'Actual retained GKKit number row is unique');
   const expected=profileReference.rows.map(r=>r===missing[0]?{...r,label:'Squad Number'}:r);assert.deepEqual(rows,expected,'Only the actual missing goalkeeper shirt-number label changes; all other native values, controls and order remain');report.profileReference=profileReferenceArg.slice(20);
  }
  assert.ok(rows.some(r=>r.actionSpan==='2'&&r.buttons.includes('Acções')),'Actual Actions menu retains its native position as an action');
  (report.bannerProfiles??=[]).push({name,rows});
 }
 async function select(label,overlap){
  await settled();const control=radio(label);if(await control.isChecked())return{alreadySelected:true};await control.focus();
  report.inflight={label,overlap,uncertainBeforeAwait:true};persist();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');response.catch(()=>{});
  let bodyStarted=false,bodyFinishedBeforeCommand=null,bodyData;
  if(overlap){
   const started=page.waitForRequest(r=>new URL(r.url()).pathname==='/body');started.catch(()=>{});
   await page.evaluate(()=>{window.qaFixtureOverlapDone=false;window.qaFixtureOverlap=fetch('/body?c=1').then(async r=>({status:r.status,html:await r.text()})).finally(()=>{window.qaFixtureOverlapDone=true;});});
   await started;bodyStarted=true;bodyFinishedBeforeCommand=await page.evaluate(()=>qaFixtureOverlapDone);
  }
  await page.keyboard.press('Space');const result=await response,reply=await result.text();report.responses.push({label,status:result.status(),reply,bodyStarted,bodyFinishedBeforeCommand});report.inflight.uncertainBeforeAwait=false;persist();assert.equal(result.status(),200);assert.match(reply,/^Selected /,'Require an actual selection acknowledgement, never an availability error');
  if(overlap){bodyData=await page.evaluate(()=>qaFixtureOverlap);assert.equal(bodyData.status,200);assert.ok(bodyData.html.includes('<table'));}
  await poll(()=>radio(label).isChecked(),'actual native selected fixture reflected in DOM');await settled();assert.equal(await fixtureTables().locator('input[type=radio]:checked').count(),1);assert.equal(await page.evaluate(()=>document.activeElement?.getAttribute('aria-label')),label);report.inflight=null;persist();return{reply,bodyStarted,bodyFinishedBeforeCommand};
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="837367296"][data-obj="2000104296"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const nav=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');nav.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await nav).text(),/^Opening /);await settled();
  report.initialTab=await page.getByRole('tablist').filter({has:page.getByRole('tab',{name:'Geral',exact:true})}).getByRole('tab',{selected:true}).textContent();if(report.initialTab!=='Calendário')await activate(page.getByRole('tab',{name:'Calendário',exact:true}));await poll(async()=>await fixtureTables().locator('tbody tr').count()===36,'actual complete fixture season');await settled();report.originalSelection=await fixtureTables().getByRole('radio',{checked:true}).getAttribute('aria-label');
  const originalData=await fixtureData(),played=page.locator('#fmroot table').filter({has:page.getByRole('columnheader',{name:'Resultado',exact:true})});report.fixtureRows=await played.locator('tbody tr').evaluateAll(es=>es.map(r=>({label:r.querySelector('input[type=radio]').getAttribute('aria-label'),score:[...r.querySelectorAll('button')].find(e=>/^\d+ - \d+$/.test(e.textContent))?.textContent})));assert.equal(report.fixtureRows.length,7);
  for(let i=0;i<report.fixtureRows.length;i++){
   const row=report.fixtureRows[i],selection=await select(row.label,i>0);const id=await radio(row.label).getAttribute('id');let native;
   if(report.health.developerTools)native=await require('./native-fixture-incidents-probe.cjs')(c,report.health.coreMvid,id);
   else{assert.ok(reference?.success&&reference.checkpoint===checkpoint,'Release requires the retained actual native dataset for this immutable QA');const retained=reference.cases.find(item=>item.label===row.label);assert.ok(retained&&retained.score===row.score);native=retained.native;report.nativeReferenceSource=referenceArg.slice(19);}
   report.pendingCase={...row,selection,native};persist();
   const labels={goal:'Goal','goal down':'Own goal','penalty scored':'Penalty scored',penelities:'Penalty kick','yellow card':'Yellow card','red card':'Red card'};let goals=0;
   for(const event of native.events){const family=event.sprite.replace(/_(?:small|medium|large)_\d+x$/,'');assert.ok(labels[family],'Investigate a newly encountered actual incident family: '+event.sprite);const isGoal=['goal','goal down','penalty scored','penelities'].includes(family);if(family==='penelities'){assert.equal(event.kind,2);assert.match(event.time,/^pen /);}if(isGoal){const minutes=event.time.split(/\s*,\s*/);assert.ok(minutes.every(m=>/^(?:pen )?\d+(?:\+\d+)?'$/.test(m)),'Cross-check only actual official goal-time strings, including grouped scorer minutes');goals+=minutes.length;}for(const text of event.texts)assert.ok((await page.locator('#fmroot').innerText()).includes(text),'Actual incident player/time retained: '+text);}
   const expectedGoals=row.score.split(' - ').reduce((sum,n)=>sum+Number(n),0);assert.equal(goals,expectedGoals,'Native reported goal minutes agree with the actual selected fixture score; a grouped scorer row can contain multiple minutes');
   for(const [family,label] of Object.entries(labels)){const expected=native.events.filter(e=>e.sprite.startsWith(family+'_')).length;assert.equal(await page.locator('#fmroot table').filter({has:page.getByRole('columnheader',{name:'Min',exact:true})}).getByText(label,{exact:true}).count(),expected,'Every actual incident glyph is named once: '+row.label+' '+label);}
   const eventTables=page.locator('#fmroot table').filter({has:page.getByRole('columnheader',{name:'Min',exact:true})});
   assert.equal(await eventTables.count(),native.events.length?1:0,'Native goalless list remains empty; populated groups have one semantic table');
   if(native.events.length){
    assert.deepEqual(await eventTables.locator('thead th').allTextContents(),['Min','Team','Event','Player']);
    const actual=await eventTables.locator('tbody tr').evaluateAll(rows=>rows.map(r=>[...r.cells].map(c=>c.innerText.replace(/\s+/g,' ').trim())));
    assert.deepEqual(actual,native.events.map(e=>[e.time,e.team,labels[e.sprite.replace(/_(?:small|medium|large)_\d+x$/,'')],e.player]),'Exact native side/team/player/time association and native group order');
    assert.equal(await eventTables.locator('tbody th[scope=row]').count(),native.events.length);
    for(const event of native.events)assert.ok(await eventTables.getByRole('button',{name:event.player,exact:true}).count(),'Real native scorer links survive the live-cell table');
   }
   assert.ok(Array.isArray(native.ratings),'Require retained actual native rating data, including production reference runs');
   const ratingTables=page.locator('#fmroot table').filter({has:page.getByRole('columnheader',{name:'Rating',exact:true})});assert.equal(await ratingTables.count(),native.ratings.length?1:0);
   if(native.ratings.length){
    const eventNames={'sub on':'Substitution on','sub off':'Substitution off','yellow card':'Yellow card','red card':'Red card','sub on and off':'Substituted on and off','sub on and injured':'Substitute injured','sub on and sent off':'Substitute sent off','injured':'Injury','goal':'Goal'};
    const expected=native.ratings.map(r=>[r.player,r.sprites.map(s=>{const family=s.replace(/_(?:small|medium|large)_\d+x$/,'');assert.ok(eventNames[family],'Investigate actual rating event asset: '+s);return eventNames[family];}).join(' '),r.display]);
    assert.deepEqual(await ratingTables.locator('thead th').allTextContents(),['Player','Event','Rating']);
    assert.deepEqual(await ratingTables.locator('tbody tr').evaluateAll(rows=>rows.map(r=>[...r.cells].map(c=>c.innerText.replace(/\s+/g,' ').trim()))),expected,'Every actual player, localized native rating and numeric event record stays in its own semantic row');
    assert.equal(await ratingTables.locator('tbody th[scope=row]').count(),native.ratings.length);
    for(const rating of native.ratings)assert.equal(await ratingTables.getByRole('button',{name:rating.player,exact:true}).count(),1,'Keep each real native rating-player link');
   }
   assert.doesNotMatch(await page.locator('#fmroot').innerText(),/EventType, image|\bPenelities\b/);assert.deepEqual(await fixtureData(),originalData);report.cases.push({...row,selection,native});report.pendingCase=null;await audit('Actual selected fixture incidents '+row.label);persist();
   if(i===0){const scorer=page.getByRole('button',{name:'Poppy Pritchard',exact:true}),origin=await scorer.getAttribute('id');await activate(scorer);await poll(async()=>await page.title()==='Relatório de jogador','actual scorer player profile');await settled();assert.match(await page.locator('#fmroot').innerText(),/Poppy Pritchard/);await profileBanner('Poppy Pritchard');await audit('Preserved actual scorer profile link');await activate(page.getByRole('button',{name:'Back',exact:true}));await poll(()=>page.locator('#'+origin).count(),'original incident scorer button');await settled();assert.equal(await page.evaluate(()=>document.activeElement?.id),origin);assert.ok(await radio(row.label).isChecked());report.checks.push('Original scorer button opens the actual player profile and native Back restores its exact focus and selected past fixture'); const rated=ratingTables.getByRole('button',{name:'Daphne van Domselaar',exact:true}),ratingOrigin=await rated.getAttribute('id');await activate(rated);await poll(async()=>await page.title()==='Relatório de jogador','actual rated player profile');await settled();assert.match(await page.locator('#fmroot').innerText(),/Daphne van Domselaar/);await profileBanner('Daphne van Domselaar');await audit('Preserved actual rating-player profile link');await activate(page.getByRole('button',{name:'Back',exact:true}));await poll(()=>page.locator('#'+ratingOrigin).count(),'original rated-player button');await settled();assert.equal(await page.evaluate(()=>document.activeElement?.id),ratingOrigin);assert.ok(await radio(row.label).isChecked());report.checks.push('Rating-player link opens actual Daphne profile and native Back restores exact row focus and selected fixture');}
  }
  report.checks.push('All native rating players, localized values and icon-only player events retain semantic row association, original links and exact real numeric event counts');report.checks.push('Actual native home/away groups retain exact team, official minute strings, incident and live scorer links in the inherited Min/Team/Event/Player table; the true goalless case has no fabricated table');report.checks.push('All seven actual past fixtures preserve native player/time/incident glyph data, including grouped scorer minutes and the genuine goalless case; no technical EventType names or fabricated events');report.checks.push('Consecutive fixture changes overlap real native body harvests; each command is sent once, every reply is explicitly Selected and actual selected-fixture records/DOM agree without row retargeting');
  await select(report.originalSelection,true);assert.deepEqual(await fixtureData(),originalData);if(report.initialTab!=='Calendário')await activate(page.getByRole('tab',{name:report.initialTab,exact:true}));await settled();await audit('Original fixture selection and calendar tab restored');report.checks.push('Original native fixture/tab and complete36-fixture data restored; no playback, Save or career simulation');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;console.log(JSON.stringify({success:true,module:report.health.coreMvid,groups:report.checks.length,audits:report.audits.length,fixtures:report.cases.length,events:report.cases.reduce((n,c)=>n+c.native.events.length,0),overlaps:report.responses.filter(r=>r.bodyStarted&&!r.bodyFinishedBeforeCommand).length}));
 }catch(error){report.failure=error.stack;report.body=await page.locator('#fmroot').innerText().catch(()=>null);throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;persist();await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
