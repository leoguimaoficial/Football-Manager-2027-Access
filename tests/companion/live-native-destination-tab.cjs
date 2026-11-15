// Actual native local-tab selection when its navigator destination is already
// open. No schedule edit, file save or simulation. The original FM26 menu/tree
// focus contract is retained; this does not claim manual NVDA or whole-screen parity.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,settled,activate,poll}=c;
 const out=path.resolve(__dirname,'../../artifacts/tests/web'),checks=[],audits=[],observations=[];
 let mvid,baseline;
 const rows=()=>page.locator('#fmroot table tbody tr').evaluateAll(rs=>rs.map(r=>({header:r.querySelector('th')?.innerText,cells:[...r.querySelectorAll('td')].map(x=>x.innerText)})));
 async function native(){
  const tab=page.getByRole('tab',{name:'Treino',exact:true}),id=await tab.getAttribute('id');
  const code='foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'"){var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;var sh=a.GetType("FM27Access.Plugin.Server.ServerHandles");var hs=(System.Collections.IDictionary)sh.GetField("_handles",f).GetValue(null);var handle=hs["'+id+'"];var ad=handle.GetType().GetField("El",f).GetValue(handle);var el=(UnityEngine.UIElements.VisualElement)sh.GetMethod("NativeElement",f).Invoke(null,new object[]{ad});for(var up=el;up!=null;up=up.parent){var group=up.TryCast<SI.Bindable.ButtonGroup>();if(group!=null){System.Console.WriteLine("SELECTED_INDEX|"+group.GetSelectedIndex());System.Console.WriteLine("SELECTION_PATH|"+group.SelectionBinding.m_path);break;}}var pending=(System.Collections.IDictionary)a.GetType("FM27Access.Plugin.Server.NativeDestinationTab").GetField("Pending",f).GetValue(null);System.Console.WriteLine("PENDING_SUBSCRIPTIONS|"+pending.Count);}';
  const raw=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);
  assert.doesNotMatch(raw,/\[exception\]|\[compile\].*error/);assert.match(raw,/PENDING_SUBSCRIPTIONS\|0/);return raw;
 }
 async function go(uid,obj){
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"][data-obj="'+obj+'"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();
  const focus=await entry.getAttribute('id'),response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});
  await page.keyboard.press('Enter');const reply=await(await response).text();assert.match(reply,/^Opening /);await settled();
  return {reply,focus,actualFocus:await page.evaluate(()=>document.activeElement.id)};
 }
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 async function local(name){const tab=page.getByRole('tab',{name,exact:true});if(await tab.getAttribute('aria-selected')!=='true')await activate(tab);assert.equal(await tab.getAttribute('aria-selected'),'true');}
 async function expectRows(expected,label){await poll(async()=>JSON.stringify(await rows())===JSON.stringify(expected),label);assert.deepEqual(await rows(),expected);}
 async function trainingReady(){await poll(async()=>{const r=await rows();return r.length===5&&r.every(w=>w.cells.length===7&&w.cells.every(c=>c.includes('\n')));},'all actual native training days have their session text');}
 async function verify(name,index){assert.equal(await page.getByRole('tab',{name,exact:true}).getAttribute('aria-selected'),'true');const raw=await native();assert.match(raw,new RegExp('SELECTED_INDEX\\|'+index+'(?:\\r?\\n|$)'));return raw;}
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  await go('1689642752','2000104296');await poll(()=>page.getByRole('tab',{name:'Treino',exact:true}).count(),'actual senior calendar tabs');await local('Treino');await trainingReady();baseline=await rows();assert.equal(baseline.length,5);
  await local('Geral');const beforeGeneral=await verify('Geral',0),historyBefore=await c.fetchText('/history');
  const fromGeneral=await go('1689642752','2000104296');const afterGeneral=await verify('Treino',1);assert.equal(fromGeneral.actualFocus,fromGeneral.focus,'Inherited FM26 menu selection remains a predictable focus origin');assert.equal(await c.fetchText('/history'),historyBefore,'Selecting the local destination adds no synthetic history entry');await expectRows(baseline,'actual original senior sessions after native destination');await audit('Actual same Training destination after local General');observations.push({case:'General to same Training',beforeGeneral,afterGeneral,...fromGeneral});
  checks.push('Same senior Training destination selects native index1 after local General, updates aria-selected, retains FM26 tree focus and native history, and leaves all35days unchanged');
  await local('Calendário');const beforeFixtures=await verify('Calendário',2),fromFixtures=await go('1689642752','2000104296'),afterFixtures=await verify('Treino',1);await expectRows(baseline,'actual original senior sessions after native destination');await audit('Actual same Training destination after local Fixtures');observations.push({case:'Fixtures to same Training',beforeFixtures,afterFixtures,...fromFixtures});
  checks.push('Same Training destination also selects its real native tab from local Fixtures, without hardcoding the preceding tab or changing training data');
  const already=await go('1689642752','2000104296');await verify('Treino',1);await expectRows(baseline,'actual original senior sessions after native destination');observations.push({case:'Already selected Training',...already});
  checks.push('Selecting the already correct local tab remains an observed native no-change and releases the temporary binding');
  await go('1208069888','2000104296');await poll(()=>page.getByRole('tab',{name:'Geral',exact:true}).count(),'actual General calendar destination');await settled();await verify('Geral',0);await local('Treino');await verify('Treino',1);const general=await go('1208069888','2000104296'),nativeGeneral=await verify('Geral',0);await audit('Actual same General destination after local Training');observations.push({case:'Training to same General',nativeGeneral,...general});
  checks.push('The inverse General destination resolves its own native argument and selects index0 after local Training; no per-caption Training exception');
  await go('837367296','2000104296');await settled();await verify('Calendário',2);const fixturesBaseline=await rows();await local('Geral');
  const shortcutResponse=page.waitForResponse(r=>new URL(r.url()).pathname==='/matchchord');shortcutResponse.catch(()=>{});await page.keyboard.press('Alt+6');assert.equal(await(await shortcutResponse).text(),'');await settled();const shortcutNative=await verify('Calendário',2);await expectRows(fixturesBaseline,'actual original fixture rows after native destination');await audit('Actual repeated Agenda shortcut after local General');observations.push({case:'Alt+6 restores same Agenda local tab',shortcutNative});
  checks.push('Repeated inherited Alt+6 resolves the live senior object and native Agenda argument, restores index2 after local General and retains its complete original fixture calendar');
  await go('1689642752','2000797209');await poll(()=>page.evaluate(()=>FMTOKEN.includes('1689642752')),'actual youth Training destination');await settled();await verify('Treino',1);await trainingReady();const youthBaseline=await rows();assert.notDeepEqual(youthBaseline,baseline,'The youth destination exposes its own actual sessions and fixtures');assert.ok(youthBaseline.some(r=>r.cells.some(v=>v.includes('Sub-21'))),'Actual youth fixture captions identify the separate team');await local('Geral');const youth=await go('1689642752','2000797209'),nativeYouth=await verify('Treino',1);await expectRows(youthBaseline,'actual original youth sessions after native destination');await audit('Actual same youth Training destination');observations.push({case:'Youth General to same Training',nativeYouth,youthBaseline,...youth});
  checks.push('The separately object-bound youth destination retains its own local-tab behavior rather than reusing the senior context');
  await go('1689642752','2000104296');await settled();await verify('Treino',1);await expectRows(baseline,'actual original senior sessions after native destination');await audit('Original senior calendar restored');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  fs.writeFileSync(path.join(out,'native-destination-tab.json'),JSON.stringify({success:true,mvid,checks,audits,observations,seniorCalendarRestored:true,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false,errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));
 }catch(error){fs.writeFileSync(path.join(out,'native-destination-tab-failure.json'),JSON.stringify({success:false,error:error.stack,mvid,checks,audits,observations,title:await page.title(),html:await page.locator('#fmroot').innerHTML(),errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));throw error;}
 finally{await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
