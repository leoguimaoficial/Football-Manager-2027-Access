// Real editable native birthplace search in the separate owned QA profile.
// Cancels the biography without saving; never edits a user's profile.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],controlled=[],audits=[];let original,id;
 const place=()=>page.getByRole('combobox',{name:'Local de Nascimento',exact:true});
 const options=()=>page.locator('#'+id+'-lb [role=option]');
 async function edit(){
  await activate(page.getByRole('button',{name:'Perfis de Treinador',exact:true}));
  const index=await page.getByRole('radio').evaluateAll(rs=>rs.findIndex(r=>r.labels?.[0]?.textContent?.trim().startsWith('QA Portal, Access,')));assert.ok(index>=0,'Owned QA profile must exist');
  await activate(page.getByRole('button',{name:'Edit',exact:true}).nth(index));await activate(page.getByRole('button',{name:'DETALHES BIOGRÁFICOS',exact:true}));await activate(page.getByRole('tab',{name:'Nacionalidades',exact:true}));
  await poll(async()=>await place().inputValue()==='Porto Alegre (RS)','native saved QA birthplace');id=await place().getAttribute('id');
 }
 async function query(text){await place().fill(text);await poll(async()=>await place().getAttribute('data-search-query')===text&&await options().count()>0,'accepted native query '+text);}
 async function select(text){await query(text);await page.keyboard.press('ArrowDown');const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/searchdropdown/select');response.catch(()=>{});await page.keyboard.press('Enter');const t=await(await response).text();assert.match(t,/ selected$/);await settled();return t;}
 async function cancel(){await place().focus();await page.keyboard.press('Escape');await poll(()=>page.evaluate(()=>!FM.core.dd&&!FM.core.refreshing&&!FM.search.restoring),'native search cancellation');await settled();}
 async function nativeValue(text){const tree=await c.fetchText('/gui');assert.ok(tree.includes('text="'+text+'"'),'Native selected text '+text);}
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 try{
  await edit();original=await place().inputValue();
  await query('Porto');assert.ok((await options().allTextContents()).includes(original));
  await page.evaluate(()=>FM.core.refresh(false,true));await settled();assert.equal(await place().evaluate(e=>e===document.activeElement),true);assert.ok(await options().count()>0);
  await cancel();assert.equal(await place().inputValue(),'Porto');await nativeValue('Porto');assert.equal(await place().evaluate(e=>e===document.activeElement),true);
  checks.push('Native editable search retains input focus/listbox during neutral updates; Escape closes the list while preserving the native query, matching FM26');
  let release,held=false;const gate=new Promise(r=>release=r);
  await page.route('**/searchdropdown/filter?*',async route=>{
   if(new URL(route.request().url()).searchParams.get('q')!=='Porto'){await route.continue();return;}
   const r=await route.fetch();held=true;await gate;await route.fulfill({response:r});
  });
  await place().fill('Porto');await poll(()=>held,'actual old query response held');await place().fill('Harare');assert.equal(await options().count(),0,'New query removes obsolete native indices immediately');
  release();await poll(async()=>await place().getAttribute('data-search-query')==='Harare'&&await options().count()>0,'latest query after held old response');assert.deepEqual(await options().allTextContents(),['Harare']);await page.unroute('**/searchdropdown/filter?*');
  checks.push('A genuinely executed old native query held in transport cannot overwrite a later query; requests are serialized and obsolete indices disappear immediately');
  await page.keyboard.press('ArrowDown');let selected=page.waitForResponse(r=>new URL(r.url()).pathname==='/searchdropdown/select');selected.catch(()=>{});await page.keyboard.press('Enter');assert.equal(await(await selected).text(),'Harare selected');await settled();assert.equal(await place().inputValue(),'Harare');assert.equal(await place().getAttribute('value'),'Harare');await nativeValue('Harare');
  const beforeCancel=c.requests.filter(r=>r.path==='/searchdropdown/select').length;await query('Porto');await cancel();assert.equal(await place().inputValue(),'Porto');await nativeValue('Porto');assert.equal(c.requests.filter(r=>r.path==='/searchdropdown/select').length,beforeCancel,'Closing a query never commits a selection');
  await select('Porto Alegre');assert.equal(await place().inputValue(),original);await nativeValue(original);
  checks.push('Native Harare selection updates value/markup; another query/Escape closes without selecting, and explicit native inverse restores Porto Alegre');
  await place().fill('FM27AccessNoSuchBirthplace');await poll(async()=>await place().getAttribute('data-search-query')==='FM27AccessNoSuchBirthplace','native empty query result');assert.equal(await options().count(),0);await cancel();assert.equal(await place().inputValue(),'FM27AccessNoSuchBirthplace');
  const beforeSelect=c.requests.filter(r=>r.path==='/searchdropdown/select').length;await place().fill('Harare');await page.keyboard.press('Enter');await poll(async()=>await place().getAttribute('data-search-query')==='Harare'&&await options().count()>0,'Enter flushes current query');assert.equal(c.requests.filter(r=>r.path==='/searchdropdown/select').length,beforeSelect);await cancel();
  checks.push('No-result queries remain cancellable; Enter before the debounce filters the current query without selecting an old native index');
  await select('Porto Alegre');assert.equal(await place().inputValue(),original);
  for(const endpoint of ['open','filter']){
   let count=0;await page.route('**/searchdropdown/'+endpoint+'?*',async route=>{count++;await route.fulfill({status:503,body:'controlled refusal'});});
   await place().fill('Harare');await poll(()=>count===1,'controlled '+endpoint+' refusal');await poll(()=>page.evaluate(()=>!FM.core.dd&&!FM.core.refreshing),'failed search releases interaction');await settled();assert.equal(count,1);assert.equal(await place().inputValue(),original);await nativeValue(original);await page.unroute('**/searchdropdown/'+endpoint+'?*');controlled.push({endpoint,kind:'HTTP503 before execution',count});
  }
  checks.push('Refused open/filter responses run once, clear interaction state, restore the real value and permit another search without replay');
  await query('Harare');let executions=0,reply;
  await page.route('**/searchdropdown/select?*',async route=>{executions++;const r=await route.fetch();reply=await r.text();await route.abort('failed');});
  await page.keyboard.press('ArrowDown');await page.keyboard.press('Enter');await poll(()=>executions===1&&!!reply,'native selection with lost reply');assert.equal(reply,'Harare selected');await poll(()=>page.evaluate(()=>!FM.core.dd&&!FM.core.refreshing),'uncertain selection recovery');await settled();assert.equal(await place().inputValue(),'Harare');await nativeValue('Harare');await page.unroute('**/searchdropdown/select?*');controlled.push({endpoint:'select',kind:'native commit followed by lost reply',count:executions});await select('Porto Alegre');assert.equal(await place().inputValue(),original);
  checks.push('Native success with a lost selection reply executes once, observes Harare without false success or replay, and allows explicit inverse restoration');
  const stale=await c.fetchText('/searchdropdown/open?id='+id+'&screen=obsolete-owner');assert.match(stale,/screen changed/i);await audit('Owned QA biography after editable search regression');
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));await activate(page.getByRole('button',{name:'Menu Principal',exact:true}));await edit();assert.equal(await place().inputValue(),original);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));await activate(page.getByRole('button',{name:'Menu Principal',exact:true}));
  checks.push('Stale native opener is rejected; biography Cancel and reopening retain the saved QA value without saving the profile');
  assert.deepEqual(c.errors,[]);assert.equal(c.requestFailures.length,1);assert.equal(c.requestFailures[0].path,'/searchdropdown/select');assert.equal(c.network.failures.length,1);
  const result={success:true,mvid:JSON.parse(await c.fetchText('/health')).coreMvid,original,checks,audits,controlled,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,profileSaved:false,manualNVDA:false};fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/search-dropdown.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
 }catch(error){fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/search-dropdown-failure.json'),JSON.stringify({error:error.stack,original,id,checks,controlled,audits,errors:c.errors,requestFailures:c.requestFailures,trace:await c.transportTrace(),browser:await page.evaluate(()=>({focus:document.activeElement?.outerHTML,dd:FM.core.dd,intent:FM.search.intent,restore:FM.search.restoring,body:document.getElementById('fmroot')?.innerText}))},null,2));throw error;}
 finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
