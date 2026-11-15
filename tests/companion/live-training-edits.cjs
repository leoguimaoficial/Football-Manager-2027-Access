// Actual native future-week/session writes and their complete inverse. No plan
// creation, file save, career progression or clipboard operation. The native
// Clear action is required after a session inverse to remove its override flag.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],observations={},out=path.resolve(__dirname,'../../artifacts/tests/web');
 let baseline,week,session,dirty=false;
 const rows=()=>page.locator('#fmroot table tbody tr');
 const snapshot=()=>rows().evaluateAll(rs=>rs.map(r=>({header:r.querySelector('th')?.innerText,cells:Array.from(r.querySelectorAll('td')).map(e=>e.innerText)})));
 const row=()=>rows().nth(week.index);
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 async function calendar(){await poll(async()=>await page.title()==='Treino','native training calendar');await settled();}
 async function preset(name){await activate(row().locator('th button').first());await poll(()=>page.locator('[data-fm-native-overlay]').count(),'actual week picker');await activate(page.getByRole('button',{name:/^Compreensão táctica(?:, selected)?$/}));await activate(page.getByRole('button',{name,exact:true}));await calendar();}
 async function selectSession(name){await activate(page.locator('#'+session.id));await poll(async()=>await page.title()==='Sessões de treino','actual native session picker');await settled();const choice=page.getByRole('radio',{name,exact:true});assert.equal(await choice.count(),1);if(!await choice.isChecked())await activate(choice,'Space');assert.equal(await choice.isChecked(),true);await audit('Actual native session selection '+name);await activate(page.getByRole('button',{name:'Confirmar',exact:true}));await calendar();}
 async function clearOverride(){const clear=row().getByRole('button',{name:'Limpar',exact:true});if(await clear.isEnabled())await activate(clear);await calendar();}
 try{
  observations.mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="1689642752"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  if(!await page.evaluate(()=>FMTOKEN.split('\x1f').some(p=>p.startsWith('1689642752#')))){await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await poll(()=>page.evaluate(()=>FMTOKEN.includes('1689642752')),'native training destination');await settled();await search.fill('');}
  const tab=page.getByRole('tab',{name:'Treino',exact:true});if(await tab.getAttribute('aria-selected')!=='true')await activate(tab);await calendar();
  baseline=await snapshot();assert.equal(baseline.length,5);assert.ok(baseline.every(r=>r.cells.length===7));
  week=await rows().evaluateAll(rs=>rs.flatMap((r,index)=>{const b=r.querySelector('th button:not(:disabled)');return b?[{index,id:b.id,name:b.textContent,date:r.querySelector('td')?.innerText.split('\n')[0]}]:[]})[0]);
  assert.ok(week);assert.equal(week.date,'21','Use the protected QA future week, never a current/locked week');assert.equal(week.name,'Foco táctico - Em posse da bola');
  assert.equal(await row().getByRole('button',{name:'Limpar',exact:true}).isEnabled(),false,'Original week has no native override');observations.week=week;observations.baseline=baseline;
  dirty=true;await preset('Foco táctico - Equilibrado');observations.changedWeek=await snapshot();observations.weekNative=await c.fetchText('/gui');assert.notDeepEqual(observations.changedWeek,baseline);assert.match(observations.changedWeek[week.index].header,/Foco táctico - Equilibrado/);await audit('Native future week changed to its real balanced preset');
  checks.push('The actual weekly schedule choice changes the native training calendar and its displayed seven-day sessions');
  await preset(week.name);await clearOverride();assert.deepEqual(await snapshot(),baseline);assert.equal(await row().getByRole('button',{name:'Limpar',exact:true}).isEnabled(),false);await audit('Original complete weekly schedule restored');
  checks.push('Selecting the original native preset restores all five weeks and all thirty-five day/session values without a file save');
  session=await row().locator('td').first().locator('button').first().evaluate(e=>({id:e.id,name:e.textContent}));assert.equal(session.name,'Estilo de passe');observations.session=session;
  await selectSession('Construção');observations.changedSession=await snapshot();observations.sessionNative=await c.fetchText('/gui');assert.notDeepEqual(observations.changedSession[week.index].cells[0],baseline[week.index].cells[0]);
  for(let w=0;w<baseline.length;w++)for(let d=0;d<7;d++)if(w!==week.index||d!==0)assert.equal(observations.changedSession[w].cells[d],baseline[w].cells[d]);
  assert.match(observations.changedSession[week.index].cells[0].replace(/\n+/g,'\n'),/^21\nConstrução\nConstrução\nDesenvolvimento individual$/);await audit('Native confirmed individual session changes only its intended day');
  checks.push('Native radio selection and Confirm change exactly the intended future session, with every other day preserved');
  await selectSession(session.name);observations.inverseBeforeClear=await snapshot();assert.deepEqual(observations.inverseBeforeClear.map(r=>r.cells),baseline.map(r=>r.cells));assert.equal(await row().getByRole('button',{name:'Limpar',exact:true}).isEnabled(),true,'Native inverse retains an explicit modified override');assert.match(observations.inverseBeforeClear[week.index].header,/Modificado/);
  await clearOverride();assert.deepEqual(await snapshot(),baseline);assert.equal(await row().getByRole('button',{name:'Limpar',exact:true}).isEnabled(),false);observations.restoredNative=await c.fetchText('/gui');dirty=false;await audit('Native Clear removes the override and restores every original week');
  checks.push('The inverse restores the original session; native Clear then removes Modified and disables itself, restoring the complete original calendar state');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  fs.writeFileSync(path.join(out,'training-edits.json'),JSON.stringify({success:true,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,network:c.network,trainingRestored:true,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));
 }catch(error){
  const before={title:await page.title(),html:await page.locator('#fmroot').innerHTML(),focus:await page.evaluate(()=>document.activeElement.id)},cleanup=[];
  // Preserve failure before attempting the real native inverse. Never replace
  // it with a post-cleanup snapshot or issue a duplicate uncertain command.
  if(dirty&&baseline&&week)try{if(await page.title()!=='Treino')await activate(page.getByRole('button',{name:'Close',exact:true}));await calendar();await preset(week.name);await clearOverride();cleanup.push({restored:JSON.stringify(await snapshot())===JSON.stringify(baseline)});}catch(e){cleanup.push({error:e.stack});}
  fs.writeFileSync(path.join(out,'training-edits-failure.json'),JSON.stringify({success:false,error:error.stack,checks,audits,observations,before,cleanup,errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));throw error;
 }finally{await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
