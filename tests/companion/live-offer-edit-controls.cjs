// Actual pending Cerci offer, owned February QA only. Native edits are undone;
// no offer response, calendar progression or game Save is submitted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const{default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const root=path.resolve(__dirname,'../..'),checks=[],audits=[],observations={};
 let success=false,health;
 async function audit(stage){const r=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({stage,violations:r.violations.map(v=>v.id)});assert.equal(r.violations.length,0);}
 async function capture(stage){await settled();return{stage,title:await page.title(),text:await page.locator('#fmroot').innerText(),focus:await page.evaluate(()=>document.activeElement.id)};}
 async function readNative(deadlineId,globalId,repeats){
  if(!health.developerTools)return{source:'Production DOM/native command observations; evaluator is absent in Release'};
  for(const id of[deadlineId,globalId])assert.match(id,/^e[\da-f]+$/);
  const code='foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+health.coreMvid+'"){var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;var live=a.GetType("FM27Access.Plugin.Server.LiveValues");var dt=a.GetType("FM27Access.Plugin.Adapters.DropdownAdapter");var d=new object[]{'+JSON.stringify(deadlineId)+',null};var g=new object[]{'+JSON.stringify(globalId)+',null};live.GetMethod("TryGetSource",f).Invoke(null,d);live.GetMethod("TryGetSource",f).Invoke(null,g);if(d[1]==null||g[1]==null)throw new System.Exception("Actual current native dropdown source unavailable");System.Console.WriteLine("DEADLINE_VISIBLE_BEFORE="+dt.GetMethod("IsPopupVisible",f).Invoke(d[1],null));System.Console.WriteLine("GLOBAL_VISIBLE_BEFORE="+dt.GetMethod("IsPopupVisible",f).Invoke(g[1],null));for(int i=0;i<'+repeats+';i++)System.Console.WriteLine("STATE="+g[1].GetType().GetMethod("GetState",f).Invoke(g[1],null));System.Console.WriteLine("DEADLINE_SELECTED="+dt.GetMethod("GetSelectedIndex",f).Invoke(d[1],null));System.Console.WriteLine("DEADLINE_VISIBLE_AFTER="+dt.GetMethod("IsPopupVisible",f).Invoke(d[1],null));System.Console.WriteLine("GLOBAL_VISIBLE_AFTER="+dt.GetMethod("IsPopupVisible",f).Invoke(g[1],null));}';
  const result=await page.evaluate(async code=>await(await fetch('/eval',{method:'POST',body:code})).text(),code);
  assert.doesNotMatch(result,/\[exception\]|\[compile\]/);return{source:'Actual production adapter on Unity main thread',result};
 }
 async function nativeSelectorCache(id){
  if(!health.developerTools)return{source:'Private raw-cache probe unavailable in Release; actual popup/caption/dependent state still exercised'};
  assert.match(id,/^e[\da-f]+$/);
  const code='foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+health.coreMvid+'"){var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;var ht=a.GetType("FM27Access.Plugin.Server.ServerHandles");var hs=(System.Collections.IDictionary)ht.GetField("_handles",f).GetValue(null);var h=hs['+JSON.stringify(id)+'];if(h==null)throw new System.Exception("Missing current selector handle");var ad=h.GetType().GetField("El",f).GetValue(h);var dt=a.GetType("FM27Access.Plugin.Adapters.DropdownAdapter");var d=dt.GetMethod("GetDropdown",f).Invoke(ad,null);System.Console.WriteLine("RAW_NATIVE_INDEX="+d.GetType().GetProperty("SelectedIndex",f).GetValue(d,null));System.Console.WriteLine("CANONICAL_INDEX="+dt.GetMethod("GetSelectedIndex",f).Invoke(ad,null));}';
  const result=await page.evaluate(async code=>await(await fetch('/eval',{method:'POST',body:code})).text(),code);assert.doesNotMatch(result,/\[exception\]|\[compile\].*error/);return{source:'Actual SIDropdown.SelectedIndex and canonical native clause index, read on Unity',result};
 }
 try{
  health=JSON.parse(await c.fetchText('/health'));assert.equal(await page.title(),'Prop. de Transferência');
  assert.match(await page.locator('#fmroot').innerText(),/Selina Cerci/);
  const owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const file=await page.getByRole('textbox',{name:'File name',exact:true}).inputValue();assert.equal(file,owned.februaryWorking);assert.ok(owned.ownedWorkingFiles.includes(file+'.fm'));
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));await settled();
  assert.equal(await page.title(),'Prop. de Transferência');assert.equal(await page.getByRole('button',{name:'Undo',exact:true}).isDisabled(),true);
  const receipt=JSON.parse(fs.readFileSync(fs.readFileSync(path.join(root,'artifacts/install/active-mod.txt'),'utf8').trim(),'utf8').replace(/^\uFEFF/,''));
  const log=path.join(receipt.root,'BepInEx/LogOutput.log');
  const deadline=page.getByRole('combobox',{name:'Amanhã',exact:true}),global=page.getByRole('combobox',{name:'Proposta completa',exact:true});
  const deadlineId=await deadline.getAttribute('id'),globalId=await global.getAttribute('id'),globalBefore=(await global.textContent()).trim();
  observations.initial=await capture('clean-native-editor');await audit('clean-native-editor');
  const opened=await c.dropdown('Amanhã');
  observations.deadlineOptions=await opened.locator('option').evaluateAll(xs=>xs.map(x=>({text:x.textContent,value:x.value,selected:x.selected,disabled:x.disabled})));
  assert.equal(observations.deadlineOptions.filter(x=>x.selected)[0].text,'Amanhã');
  const start=fs.statSync(log).size;observations.openPassive=await readNative(deadlineId,globalId,5);
  if(health.developerTools){assert.match(observations.openPassive.result,/DEADLINE_VISIBLE_BEFORE=True/);assert.match(observations.openPassive.result,/DEADLINE_VISIBLE_AFTER=True/);assert.equal((observations.openPassive.result.match(/STATE=/g)||[]).length,5);}
  assert.equal(await page.evaluate(()=>FM.core.dd),deadlineId);assert.equal((await global.textContent()).trim(),globalBefore);await audit('deadline-popup');
  await c.cancelDropdown();assert.equal(await page.evaluate(()=>document.activeElement.id),deadlineId);
  observations.closedPassive=await readNative(deadlineId,globalId,5);
  if(health.developerTools){assert.match(observations.closedPassive.result,/DEADLINE_VISIBLE_BEFORE=False/);assert.match(observations.closedPassive.result,/DEADLINE_VISIBLE_AFTER=False/);assert.match(observations.closedPassive.result,/GLOBAL_VISIBLE_BEFORE=False/);assert.match(observations.closedPassive.result,/GLOBAL_VISIBLE_AFTER=False/);}
  const newLog=fs.readFileSync(log).subarray(start).toString('utf8');observations.passiveEmptyRowWarnings=(newLog.match(/\[Dropdown\] none of \d+ rendered rows carried a label/g)||[]).length;assert.equal(observations.passiveEmptyRowWarnings,0);
  checks.push('Passive whole-offer value reads preserve the real deadline popup, selection, global caption and cancel focus, with zero new empty-row warnings');
  const globalPopup=await c.dropdown('Proposta completa');observations.globalOptions=await globalPopup.locator('option').allTextContents();assert.ok(observations.globalOptions.length);await audit('whole-offer-popup');await c.cancelDropdown();assert.equal((await global.textContent()).trim(),globalBefore);
  checks.push('Whole-offer negotiation menu still supplies its real native options and cancels without changing terms');
  await c.dropdown('Amanhã');await c.choose('Hoje');await poll(()=>page.getByRole('combobox',{name:'Hoje',exact:true}).count(),'actual changed native deadline');
  observations.today=await capture('native-deadline-today');observations.todayNative=await readNative(deadlineId,globalId,1);
  if(health.developerTools)assert.match(observations.todayNative.result,/DEADLINE_SELECTED=1/);
  if(await page.getByRole('button',{name:'Undo',exact:true}).isEnabled()){observations.deadlineInverse='Native Undo';await activate(page.getByRole('button',{name:'Undo',exact:true}));}
  else{observations.deadlineInverse='Actual native inverse selection';await c.dropdown('Hoje');await c.choose('Amanhã');}
  await poll(()=>page.getByRole('combobox',{name:'Amanhã',exact:true}).count(),'native deadline restored');observations.restoredNative=await readNative(deadlineId,globalId,1);if(health.developerTools)assert.match(observations.restoredNative.result,/DEADLINE_SELECTED=2/);
  checks.push('Real deadline selection changes the native selected index and its observed inverse restores Amanhã');
  for(const phase of ['com posse da bola','sem posse da bola']){
  const positionName='Posição preferida '+phase,roleName='Função preferida '+phase;
  const position=page.getByRole('combobox',{name:positionName,exact:true}),role=page.getByRole('combobox',{name:roleName,exact:true});
  const originalPosition=(await position.textContent()).trim();assert.equal(await role.isDisabled(),true);
  const positions=await c.dropdown(positionName);const positionOptions=await positions.locator('option').evaluateAll(xs=>xs.map(x=>({text:x.textContent,selected:x.selected,disabled:x.disabled})));
  (observations.phases??=[]).push({phase,positionOptions,originalPosition});const phaseObservation=observations.phases.at(-1);
  const permitted=positionOptions.filter(x=>!x.selected&&!x.disabled&&x.text),chosen=permitted.find(x=>/ponta|avan.ad|striker|PL\s*\(/i.test(x.text))||permitted[0];assert.ok(chosen);phaseObservation.chosenPosition=chosen.text;await c.choose(chosen.text);
  await poll(()=>role.isEnabled(),'actual native dependent role enabled');assert.equal(await page.getByRole('combobox',{name:roleName+' negotiability',exact:true}).isEnabled(),true);
  const roles=await c.dropdown(roleName);phaseObservation.roles=await roles.locator('option').allTextContents();assert.ok(phaseObservation.roles.length>1);const roleId=await role.getAttribute('id');await audit('native-role-options-'+phase);
  phaseObservation.roleOptions=await roles.locator('option').evaluateAll(xs=>xs.map(x=>({text:x.textContent,selected:x.selected,disabled:x.disabled})));
  const originalRole=phaseObservation.roleOptions.find(x=>x.selected),chosenRole=phaseObservation.roleOptions.find(x=>!x.selected&&!x.disabled&&x.text);
  assert.ok(originalRole);assert.ok(chosenRole);phaseObservation.chosenRole=chosenRole.text;
  await c.choose(chosenRole.text);await poll(async()=>(await role.textContent()).trim().startsWith(chosenRole.text),'actual chosen native dependent role');
  phaseObservation.roleChanged=await capture('native-role-changed-'+phase);await audit('native-role-changed-'+phase);
  phaseObservation.roleValueRevisionBeforeUndo=await role.getAttribute('data-fm-value-at');
  await activate(page.getByRole('button',{name:'Undo',exact:true}));
  await poll(async()=>(await role.textContent()).trim().startsWith(originalRole.text),'native role Undo restores the prior role');
  phaseObservation.roleValueRevisionAfterUndo=await role.getAttribute('data-fm-value-at');
  assert.ok(Number(phaseObservation.roleValueRevisionAfterUndo)>0,'Composed term values retain the normal live-value revision contract');
  assert.ok((await position.textContent()).trim().startsWith(chosen.text),'Role Undo must retain the earlier position edit');
  assert.equal(await page.getByRole('button',{name:'Undo',exact:true}).isEnabled(),true,'The position edit remains pending after undoing only the role');
  phaseObservation.roleCacheBeforeReopen=await nativeSelectorCache(roleId);
  const restoredRole=await c.dropdown(roleName);phaseObservation.roleRestoredPopup=await restoredRole.locator('option').evaluateAll(xs=>xs.map(x=>({text:x.textContent,selected:x.selected})));
  assert.deepEqual(phaseObservation.roleRestoredPopup.filter(x=>x.selected).map(x=>x.text),[originalRole.text],'Actual native role popup must follow the restored role, not the old selected cache');
  phaseObservation.roleCacheAfterReopen=await nativeSelectorCache(roleId);await c.cancelDropdown();assert.equal(await page.evaluate(()=>document.activeElement.id),roleId);
  await activate(page.getByRole('button',{name:'Undo',exact:true}));
  const originalCaption=positionOptions.find(x=>x.selected).text;
  await poll(async()=>(await position.textContent()).trim().startsWith(originalCaption)&&await role.isDisabled(),'native Undo restores original caption and dependent unavailable role');
  assert.equal(await page.getByRole('combobox',{name:roleName+' negotiability',exact:true}).isDisabled(),true);assert.equal(await page.getByRole('button',{name:'Undo',exact:true}).isDisabled(),true);
  const positionId=await position.getAttribute('id');phaseObservation.beforeReopen=await capture('undo-before-reopen-'+phase);phaseObservation.cacheBeforeReopen=await nativeSelectorCache(positionId);
  const restored=await c.dropdown(positionName);phaseObservation.restoredPopup=await restored.locator('option').evaluateAll(xs=>xs.map(x=>({text:x.textContent,selected:x.selected})));
  assert.deepEqual(phaseObservation.restoredPopup.filter(x=>x.selected).map(x=>x.text),[originalCaption],'Actual reopened native popup must select the restored value, not its pre-Undo cache');phaseObservation.cacheAfterReopen=await nativeSelectorCache(positionId);if(health.developerTools){assert.match(phaseObservation.cacheBeforeReopen.result,/CANONICAL_INDEX=0/);assert.match(phaseObservation.cacheAfterReopen.result,/RAW_NATIVE_INDEX=0/);}await c.cancelDropdown();
  assert.equal(await page.getByRole('button',{name:'Undo',exact:true}).isDisabled(),true);phaseObservation.afterUndo=await capture('native-dependent-terms-undone-'+phase);await audit('native-dependent-terms-undone-'+phase);
  checks.push('Actual position and dependent role '+phase+' both change natively; successive Undo operations restore the role then position, actual popup selections, dependent state and clean draft');
  }
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,health.coreMvid);success=true;
 }catch(error){observations.failure=error.stack;throw error;}
 finally{fs.mkdirSync(path.join(root,'artifacts/tests/web'),{recursive:true});fs.writeFileSync(path.join(root,'artifacts/tests/web/offer-edit-controls-'+(success?'passed':'failure')+'.json'),JSON.stringify({at:new Date().toISOString(),success,health,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,offerSubmitted:false,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));await c.browser.close();}
 console.log({checks:checks.length,audits:audits.length});
})().catch(e=>{console.error(e);process.exitCode=1;});
