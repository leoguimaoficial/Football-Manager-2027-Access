// Two actual weeks have the same preset/title/content. Old popup commands must
// not reach the new week. Only category navigation/cancel; no training write.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],observations={},out=path.resolve(__dirname,'../../artifacts/tests/web');let baseline,popup=false;
 const snapshot=()=>page.locator('#fmroot table tbody tr').evaluateAll(rs=>rs.map(r=>({header:r.querySelector('th')?.innerText,cells:Array.from(r.querySelectorAll('td')).map(d=>d.innerText)})));
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 async function state(week){const button=page.getByRole('button',{name:'Preparação do Jogo',exact:true});return{week,token:await page.evaluate(()=>FMTOKEN),category:await button.getAttribute('id'),buttons:await page.getByRole('button').allTextContents(),html:await page.locator('#fmroot').innerHTML()};}
 async function close(){await activate(page.getByRole('button',{name:'Close',exact:true}));popup=false;await poll(async()=>await page.title()==='Treino','native calendar return');await settled();}
 try{
  observations.mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  assert.equal(await page.title(),'Treino');const tab=page.getByRole('tab',{name:'Treino',exact:true});if(await tab.getAttribute('aria-selected')!=='true')await activate(tab);
  baseline=await snapshot();const weeks=await page.locator('#fmroot table tbody tr').evaluateAll(rs=>rs.flatMap((r,index)=>{const b=r.querySelector('th button:not(:disabled)');return b?[{id:b.id,index,label:b.textContent,date:r.querySelector('td')?.innerText.split('\n')[0]}]:[]}));assert.equal(weeks.length,2);assert.equal(weeks[0].label,weeks[1].label);assert.notEqual(weeks[0].date,weeks[1].date);observations.weeks=weeks;
  await activate(page.locator('#'+weeks[0].id));popup=true;await poll(()=>page.locator('[data-fm-native-overlay]').count(),'first native weekly popup');await settled();observations.first=await state(weeks[0]);assert.ok(observations.first.buttons.includes('Compreensão táctica, selected'));await audit('First real equal-caption weekly popup');await close();
  assert.equal(await page.evaluate(()=>document.activeElement.id),weeks[0].id);
  await activate(page.locator('#'+weeks[1].id));popup=true;await poll(()=>page.locator('[data-fm-native-overlay]').count(),'second native weekly popup');await settled();observations.second=await state(weeks[1]);assert.deepEqual(observations.first.buttons,observations.second.buttons,'Real menu content is deliberately identical');assert.notEqual(observations.first.token,observations.second.token);assert.notEqual(observations.first.category,observations.second.category);await audit('Second equal-caption popup has its own native week identity');
  checks.push('Two real equal-caption weeks retain identical native menu choices but have distinct action ownership and option IDs');
  observations.oldReply=await c.fetchText('/activate?id='+observations.first.category+'&screen='+encodeURIComponent(observations.first.token));assert.match(observations.oldReply,/screen changed|Unknown element|calendar date changed|no longer on screen/);
  observations.oldIdCurrentScreen=await c.fetchText('/activate?id='+observations.first.category+'&screen='+encodeURIComponent(observations.second.token));assert.match(observations.oldIdCurrentScreen,/Unknown element|calendar date changed|no longer on screen/);
  assert.ok((await page.getByRole('button').allTextContents()).includes('Compreensão táctica, selected'));
  checks.push('The old week category refuses with both its old screen token and the current screen token, leaving the new native week category unchanged');
  const target=page.getByRole('button',{name:'Preparação do Jogo',exact:true}),origin=await target.getAttribute('id');await activate(target);assert.equal(await page.evaluate(()=>document.activeElement.id),origin);assert.ok((await page.getByRole('button').allTextContents()).includes('Preparação do Jogo, selected'));observations.selectedNative=await c.fetchText('/gui');await audit('Current week category still executes normally');
  await activate(page.getByRole('button',{name:'Compreensão táctica',exact:true}));await close();assert.equal(await page.evaluate(()=>document.activeElement.id),weeks[1].id);assert.deepEqual(await snapshot(),baseline);await audit('All original calendar values and second week origin restored');
  checks.push('The current week category works, restores its original category and closes to the exact second-week origin with all five weeks/thirty-five days unchanged');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  fs.writeFileSync(path.join(out,'calendar-popup-owner.json'),JSON.stringify({success:true,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,network:c.network,trainingChanged:false,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));
 }catch(error){
  const before={title:await page.title(),html:await page.locator('#fmroot').innerHTML(),focus:await page.evaluate(()=>document.activeElement.id)},cleanup=[];if(popup)try{const original=page.getByRole('button',{name:'Compreensão táctica',exact:true});if(await original.count())await activate(original);await close();cleanup.push({restored:JSON.stringify(await snapshot())===JSON.stringify(baseline)});}catch(e){cleanup.push({error:e.stack});}
  fs.writeFileSync(path.join(out,'calendar-popup-owner-failure.json'),JSON.stringify({success:false,error:error.stack,checks,audits,observations,before,cleanup,errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));throw error;
 }finally{await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
