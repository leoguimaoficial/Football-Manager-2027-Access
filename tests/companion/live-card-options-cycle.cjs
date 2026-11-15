// Exercise each actual native option of one owned Portal slot, then restore it.
// Never invent tile choices, navigate a card leaf, Save or advance simulation.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const{default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const out=path.resolve(__dirname,'../../artifacts/tests/web'),checks=[],audits=[],observations={choices:[]};
 let success=false,changed=false,id,original,mvid;
 async function open(){
  await settled();const control=page.locator('#'+id);await control.focus();
  const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');r.catch(()=>{});
  await page.keyboard.press('Enter');const response=await r;assert.equal(response.status(),200);assert.ok((await response.text()).startsWith('<'));
  await poll(()=>control.getAttribute('data-loaded'),'actual options of this native slot');return control;
 }
 async function native(){
  const code='foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'"){var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;var sh=a.GetType("FM27Access.Plugin.Server.ServerHandles");var hs=(System.Collections.IDictionary)sh.GetField("_handles",f).GetValue(null);var h=hs["'+id+'"];var ad=h.GetType().GetField("El",f).GetValue(h);var e=(UnityEngine.UIElements.VisualElement)sh.GetMethod("NativeElement",f).Invoke(null,new object[]{ad});var eb=a.GetType("FM27Access.Plugin.ElementBridge");var sw=a.GetType("FM27Access.Plugin.Server.ScreenWalker");for(var owner=e.parent;owner!=null;owner=owner.parent){if(owner.GetIl2CppType().Name=="Panel")break;if(owner.GetIl2CppType().Name!="BindableVariableSizedGridElement")continue;System.Console.WriteLine("SLOT|"+owner.name);System.Console.WriteLine("LABEL|"+ad.GetType().GetMethod("GetLabel",f).Invoke(ad,null));var title=eb.GetMethod("FindVisibleTileTitle",f).Invoke(null,new object[]{owner});System.Console.WriteLine("TITLE|"+(title==null?"":sw.GetMethod("ReadFirstText",f).Invoke(null,new object[]{title})));var stack=new System.Collections.Generic.Stack<UnityEngine.UIElements.VisualElement>();stack.Push(owner);int budget=512;while(stack.Count>0&&budget-->0){var child=stack.Pop();if(!(bool)eb.GetMethod("IsVisible",f).Invoke(null,new object[]{child}))continue;var type=child.GetIl2CppType().Name;if(type=="SIText")System.Console.WriteLine("TEXT|"+child.name+"|"+sw.GetMethod("ReadFirstText",f).Invoke(null,new object[]{child}));for(int i=child.hierarchy.childCount-1;i>=0;i--)stack.Push(child.hierarchy[i]);}break;}}';
  const raw=await page.evaluate(async s=>(await fetch('/eval',{method:'POST',body:s})).text(),code);assert.doesNotMatch(raw,/\[compile\]|\[exception\]/);
  const line=name=>raw.split(/\r?\n/).find(l=>l.startsWith(name+'|'))?.slice(name.length+1);
  const result={slot:line('SLOT'),label:line('LABEL'),title:line('TITLE'),texts:raw.split(/\r?\n/).filter(l=>l.startsWith('TEXT|'))};
  assert.ok(result.slot&&result.title);assert.equal(result.label,await page.locator('#'+id).getAttribute('aria-label'));return result;
 }
 async function audit(name){
  const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});assert.deepEqual(a.violations,[]);
  assert.doesNotMatch(await page.locator('#fmroot').innerText(),/unknown control|unlabeled icon button|chart, visual only|No readable text in this view|couldn't read all|table not loaded|Card-frame, image|Arrow, image|Gauge, gauge/i);
 }
 const write=()=>fs.writeFileSync(path.join(out,'card-options-cycle-progress.json'),JSON.stringify({at:new Date().toISOString(),mvid,id,original,changed,checks,audits,observations},null,2));
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  assert.equal(await page.getByRole('heading',{name:'Portal',exact:true}).count(),1,'Actual native Portal');
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned QA identity');observations.ownedSave=await file.inputValue();assert.match(observations.ownedSave,/^FM27Access QA Dialog Regression /);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  id=await page.getByRole('combobox',{name:'Customize card: CENTRAL DE DADOS',exact:true}).getAttribute('id');assert.match(id,/^[a-z0-9]+$/i);
  const initial=await open();observations.options=await initial.locator('option').evaluateAll(es=>es.map(e=>({label:e.textContent,disabled:e.disabled,selected:e.selected})));
  assert.ok(observations.options.length>1);assert.equal(new Set(observations.options.map(x=>x.label)).size,observations.options.length);
  original=observations.options.find(x=>x.selected)?.label;assert.ok(original);await c.cancelDropdown();
  observations.originalNative=await native();await audit('Original actual native card');write();
  for(const option of observations.options.filter(x=>!x.selected&&!x.disabled)){
   const select=await open(),fresh=select.locator('option').filter({hasText:option.label});
   // Selection below is exact-label; never retain an old option value/ordinal.
   const available=await select.locator('option').evaluateAll((es,label)=>es.filter(e=>e.textContent===label).map(e=>({disabled:e.disabled,selected:e.selected})),option.label);
   assert.equal(available.length,1);assert.equal(available[0].disabled,false,'Actual option remains available in this slot');
   changed=true;await c.choose(option.label);await poll(()=>page.locator('#'+id).count(),'same native customization slot after replacement');await settled();
   assert.equal(await page.evaluate(()=>document.activeElement.id),id,'Actual changed card preserves its customization origin');
   const value=await native();assert.equal(value.slot,observations.originalNative.slot);
   assert.equal(await page.getByRole('heading',{name:'Portal',exact:true}).count(),1,'Choice replaces a tile without navigating away');
   assert.ok(value.texts.length>0);await audit(option.label);
   const reopened=await open();const selected=await reopened.locator('option:checked').textContent();assert.equal(selected,option.label,'Reopened actual native menu retains the chosen tile');await c.cancelDropdown();assert.equal(await page.evaluate(()=>document.activeElement.id),id);
   observations.choices.push({label:option.label,native:value,reopenedSelection:selected,focusReturned:true});checks.push('Native tile '+option.label+' replaces the same slot, retains its native title/content and selection, and returns exact focus');write();console.log('PASS native tile',observations.choices.length,option.label);
  }
  await open();await c.choose(original);await settled();changed=false;observations.restoredNative=await native();assert.deepEqual(observations.restoredNative,observations.originalNative);await audit('Original native Data Hub tile restored');assert.equal(await page.evaluate(()=>document.activeElement.id),id);
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,mvid);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(error){
  observations.failure=error.stack;fs.writeFileSync(path.join(out,'card-options-cycle-failure-native.txt'),await c.fetchText('/gui'));
  fs.writeFileSync(path.join(out,'card-options-cycle-failure.html'),await page.locator('#fmroot').innerHTML());
  const shot=(await c.fetchText('/screenshot')).trim();if(fs.existsSync(shot)){observations.failureScreenshot=path.join(out,'card-options-cycle-failure-native.png');fs.copyFileSync(shot,observations.failureScreenshot);}throw error;
 }finally{
  if(changed&&id)try{if(await page.locator('#'+id).count()){if(await page.locator('#'+id).getAttribute('data-loaded'))await c.cancelDropdown();await open();await c.choose(original);await settled();observations.failedRunOriginalRestored=true;changed=false;}}catch(error){observations.cleanupError=error.message;}
  fs.writeFileSync(path.join(out,'card-options-cycle.json'),JSON.stringify({success,mvid,checks,audits,observations,restorationRequired:changed,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));await c.browser.close();
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
