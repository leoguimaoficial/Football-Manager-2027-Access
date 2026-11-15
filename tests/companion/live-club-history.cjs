// Owned Portuguese Arsenal QA history. Native profile/role views only; no
// contracts, staff employment changes, career simulation or save submission.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],screens=[],audits=[];let success=false,failure=null,mvid;
 async function go(uid){
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});
  await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);
  await poll(()=>page.evaluate(id=>FMTOKEN.split('\x1f').some(p=>p.startsWith(id+'#')),String(uid)),'native history destination');
  await settled();await search.fill('');await settled();
 }
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 const managers=()=>page.locator('table').filter({has:page.getByRole('columnheader',{name:'Manager',exact:true})});
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  await go(356773888);assert.ok((await page.locator('#fmroot').innerText()).includes('Teste FM27 Acesso'));
  assert.deepEqual(await managers().locator('tbody tr').evaluateAll(es=>es.map(e=>[...e.querySelectorAll('th,td')].map(c=>c.textContent))),[
   ['Teste FM27 Acesso','6/7/2026','Presente'],['Renée Slegers','5/9/2023','6/7/2026']]);
  assert.equal(await managers().locator('tbody th[scope=row] button').count(),2);
  checks.push('Two actual manager tenures retain native names, start/end dates, Present state and linked row headers');
  const manager=managers().getByRole('button',{name:'Renée Slegers',exact:true}),origin=await manager.getAttribute('id');
  await activate(manager);await poll(()=>page.getByRole('heading',{name:'Relatório da Equipa Técnica',exact:true}).count(),'actual linked staff report');
  assert.ok((await page.locator('#fmroot').innerText()).includes('Renée Slegers'));
  assert.equal(await page.getByText('Reputação: 7000 of 0 to 9000',{exact:true}).count(),1,'The explicit native reputation caption is preserved');
  const roles=page.locator('table').filter({has:page.getByRole('columnheader',{name:'Cargos preferidos',exact:true})});
  assert.deepEqual(await roles.locator('tbody tr').allTextContents(),[
   'Treinador: 100 of 0 to 100','Adjunto: 100 of 0 to 100','Treinador Adjunto Principal: 72 of 0 to 100']);
  const radios=roles.getByRole('radio'),initial=await radios.evaluateAll(es=>es.findIndex(e=>e.checked));assert.ok(initial>=0);
  const other=(initial+1)%await radios.count();await activate(radios.nth(other),'Space');
  await poll(()=>radios.nth(other).isChecked(),'actual native preferred role selection');
  await activate(radios.nth(initial),'Space');await poll(()=>radios.nth(initial).isChecked(),'original role view restored');
  await audit('Linked staff report and native contextual role gauges');
  checks.push('Actual Renée report; three role gauges name their own native captions once, keep full scales, native role selection and restoration work');
  await activate(page.getByRole('button',{name:'Back',exact:true}));await poll(()=>managers().count(),'native history parent');
  await poll(()=>page.evaluate(id=>document.activeElement.id===id,origin),'exact manager row origin');
  checks.push('Native history Back restores the exact linked manager row');
  for(const [caption,uid] of [['TÍTULO(S)',3755238400],['HISTÓRICO DO TREINADOR',387845632],['HISTÓRICO DO CLUBE',2658229504],['MELHOR 11',1836008022]]){
   const tile=page.getByRole('button',{name:caption,exact:true}),tileOrigin=await tile.getAttribute('id');
   await activate(tile);await poll(()=>page.evaluate(id=>FMTOKEN.split('\x1f').some(p=>p.startsWith(id+'#')),String(uid)),'native history tile destination');
   await activate(page.getByRole('button',{name:'Back',exact:true}));await poll(()=>managers().count(),'history tile parent');
   await poll(()=>page.evaluate(id=>document.activeElement.id===id,tileOrigin),'history tile origin');
  }
  checks.push('All four native overview card buttons open their corresponding full history destinations; Back restores each exact card origin');
  for(const [label,uid] of [['Overview',356773888],['Honours',3755238400],['Club records',2658229504],['Best eleven',1836008022],['Milestones',2635223808],['Manager history',387845632]]){
   await go(uid);const text=await page.locator('#fmroot').innerText();assert.ok(text.trim());assert.equal(await page.locator('h1').count(),1);
   assert.doesNotMatch(text,/unknown control|unlabeled icon button|No navigable content|couldn't read/);await audit(label);
   screens.push({label,uid,headings:await page.locator('h1,h2,h3').allTextContents(),tables:await page.locator('table').count(),rows:await page.locator('table tbody tr').count(),buttons:await page.getByRole('button').allTextContents()});
   console.log('history destination checked:',label);
  }
  checks.push('Six native history destinations, actual contents/empty states and clean accessibility scans');
  await go(989379584);assert.equal(await page.getByRole('heading',{name:'Portal',exact:true}).count(),1);
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,mvid);
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(error){failure=error.stack||String(error);throw error;
 }finally{
  const dir=path.resolve(__dirname,'../../artifacts/tests/web');fs.mkdirSync(dir,{recursive:true});
  let transportTrace=null;
  if(c.requestFailures.length)transportTrace=await page.evaluate(async()=>{
   const ac=new AbortController(),timer=setTimeout(()=>ac.abort(),3000);
   try{return await(await fetch('/httptrace',{signal:ac.signal})).json();}catch(error){return{diagnosticError:error.message};}finally{clearTimeout(timer);}
  }).catch(error=>({diagnosticError:error.message}));
  const report={success,failure,mvid,checks,screens,audits,network:c.network,transportTrace,jsErrors:c.errors,requestFailures:c.requestFailures,manualNVDA:false};
  fs.writeFileSync(path.join(dir,'club-history.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));await c.browser.close();
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
