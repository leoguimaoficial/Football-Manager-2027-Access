// The real FM27 favorite bar, distinct from the FM26 companion Ctrl editor.
// Every mutation has a native observed inverse; no save, simulation or reset.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],audits=[],observations={},out=path.resolve(__dirname,'../../artifacts/tests/web');
 let mvid,baseline,baselineRows,release,pending=null;
 const flags='System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static';
 const scoped=code=>'foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'"){var f='+flags+';'+code+'}';
 async function read(code){const raw=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),scoped(code));assert.doesNotMatch(raw,/\[exception\]|\[compile\]/);return raw;}
 const module=()=>c.fetchText('/bookmarkpopup?op=module');
 async function nativeRows(){
  const ids=await page.locator('table tbody tr th button').evaluateAll(es=>es.map(e=>e.id));
  const raw=await read('var sh=a.GetType("FM27Access.Plugin.Server.ServerHandles");var hs=(System.Collections.IDictionary)sh.GetField("_handles",f).GetValue(null);var surface=a.GetType("FM27Access.Plugin.Server.NativeBookmarkSurface");foreach(var id in new string[]{'+ids.map(id=>'"'+id+'"').join(',')+'}){var h=hs[id];var ad=h.GetType().GetField("El",f).GetValue(h);var el=sh.GetMethod("NativeElement",f).Invoke(null,new object[]{ad});var s=surface.GetMethod("Read",f).Invoke(null,new object[]{el});if(s==null){System.Console.WriteLine("MISSING|"+id);continue;}var t=s.GetType();var reference=(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)t.GetProperty("Reference",f).GetValue(s,null);System.Console.WriteLine(id+(char)9+t.GetProperty("Caption",f).GetValue(s,null)+(char)9+t.GetProperty("Identity",f).GetValue(s,null)+(char)9+t.GetProperty("Pinned",f).GetValue(s,null)+(char)9+reference.Pointer.ToInt64());}');
  assert.doesNotMatch(raw,/MISSING/);
  return raw.trim().split(/\r?\n/).filter(Boolean).map(line=>{const p=line.split(String.fromCharCode(9));assert.equal(p.length,5);return{id:p[0],caption:p[1],identity:p[2],pinned:p[3]==='True',reference:p[4]};});
 }
 async function token(){const raw=await read('var set=a.GetType("FM27Access.Plugin.Server.InteractivePanels").GetMethod("Current",f).Invoke(null,null);foreach(var m in a.GetType("FM27Access.Plugin.Server.ServerText").GetMethods(f))if(m.Name=="ScreenToken"&&m.GetParameters()[0].ParameterType.Name.StartsWith("IReadOnlyList"))System.Console.WriteLine("TOKEN|"+m.Invoke(null,new object[]{set}));');const t=raw.match(/^TOKEN\|([^\r\n]*)/m)?.[1];assert.ok(t);return t;}
 async function foreground(){const raw=await read('var p=a.GetType("FM27Access.Plugin.FM27PanelCatalog").GetMethod("GetForegroundPanel",f).Invoke(null,null);var id=p.GetType().GetProperty("PanelID",f).GetValue(p,null);System.Console.WriteLine("UID|"+id.GetType().GetProperty("ID",f).GetValue(id,null));');const uid=raw.match(/UID\|(\d+)/)?.[1];assert.ok(uid,raw);return uid;}
 const labels=()=>page.getByRole('checkbox',{name:'Mostrar etiquetas de favoritos',exact:true});
 async function nativeChecked(){const id=await labels().getAttribute('id');const raw=await read('var sh=a.GetType("FM27Access.Plugin.Server.ServerHandles");var hs=(System.Collections.IDictionary)sh.GetField("_handles",f).GetValue(null);var h=hs["'+id+'"];var ad=h.GetType().GetField("El",f).GetValue(h);var el=(UnityEngine.UIElements.VisualElement)sh.GetMethod("NativeElement",f).Invoke(null,new object[]{ad});foreach(var aa in System.AppDomain.CurrentDomain.GetAssemblies()){var t=aa.GetType(el.GetIl2CppType().FullName);if(t==null)continue;var native=System.Activator.CreateInstance(t,new object[]{el.Pointer});System.Console.WriteLine("CHECKED|"+t.GetProperty("value",f).GetValue(native,null));break;}');assert.match(raw,/CHECKED\|(True|False)/);return raw.includes('CHECKED|True');}
 async function open(){await c.activate(page.getByRole('button',{name:/^(?:Game bookmarks|Favoritos)(?:, Alt I for more info)?$/}));await poll(()=>page.getByRole('tab',{name:'Barra de favoritos',exact:true}).count(),'native favorites popup');}
 async function bar(){const tab=page.getByRole('tab',{name:'Barra de favoritos',exact:true});if(await tab.getAttribute('aria-selected')!=='true')await c.activate(tab);}
 async function all(){const tab=page.getByRole('tab',{name:'Todos os favoritos',exact:true});if(await tab.getAttribute('aria-selected')!=='true')await c.activate(tab);}
 async function close(){await c.activate(page.getByRole('button',{name:'Close',exact:true}));assert.match(await c.fetchText('/bookmarkpopup?op=state'),/IsVisible=False/);}
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 async function find(identity){
  for(let count=0;count<8;count++){
   const rows=await nativeRows(),found=rows.find(r=>r.identity===identity);if(found)return found;
   const earlier=page.getByRole('button',{name:'Show earlier items',exact:true}),more=page.getByRole('button',{name:'Show more items',exact:true});
   if(await earlier.count())await c.activate(earlier);else if(await more.count())await c.activate(more);else break;
  }throw Error('Native bookmark record is not in the observed window: '+identity);
 }
 const rowControl=(row,prefix)=>page.locator('#'+row.id).locator('xpath=ancestor::tr').getByRole('button',{name:prefix+row.caption,exact:true});
 async function heldAction(control,observe){
  const id=await control.getAttribute('id');let held=0;const gate=new Promise(r=>release=r);
  await page.route('**/body?*',async route=>{held++;await gate;await route.continue();});
  try{
   await control.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');await page.keyboard.press('Enter');
   assert.match(await(await response).text(),/^Activated /);await poll(()=>held>0,'held genuine replacement body');await poll(observe,'actual native mutation bound');
   const current=await token(),before=await module(),refused=await c.fetchText('/activate?id='+id+'&screen='+encodeURIComponent(current));
   assert.match(refused,/bookmark changed|left this view/i);assert.equal(await module(),before);
   return{id,refused,held,before};
  }finally{release();release=null;await page.unrouteAll({behavior:'wait'});await settled();}
 }
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned QA');assert.match(await file.inputValue(),/^FM27Access QA /);await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  baseline=await module();await open();await bar();baselineRows=await nativeRows();assert.ok(baselineRows.length>=2);
  observations.baseline=baselineRows;observations.originalLabels=await nativeChecked();assert.equal(await labels().isChecked(),observations.originalLabels);
  assert.deepEqual(await page.locator('table thead th').allTextContents(),['Bookmark','Move','Bookmark bar']);
  assert.equal(await page.getByRole('button',{name:'Move up: '+baselineRows[0].caption,exact:true}).isDisabled(),true);
  assert.equal(await page.getByRole('button',{name:'Move down: '+baselineRows.at(-1).caption,exact:true}).isDisabled(),true);await audit('Native favorite bar');
  checks.push('Native favorites use the original popup/tabs/table, named actions and genuine boundary availability, separately from Ctrl shortcuts');

  const barTab=page.getByRole('tab',{name:'Barra de favoritos',exact:true}),allTab=page.getByRole('tab',{name:'Todos os favoritos',exact:true});
  await barTab.focus();await page.keyboard.press('ArrowRight');assert.equal(await allTab.evaluate(e=>e===document.activeElement),true);assert.equal(await barTab.getAttribute('aria-selected'),'true');
  await page.keyboard.press('Home');assert.equal(await barTab.evaluate(e=>e===document.activeElement),true);
  await page.keyboard.press('End');assert.equal(await allTab.evaluate(e=>e===document.activeElement),true);assert.equal(await barTab.getAttribute('aria-selected'),'true');
  await c.activate(allTab);assert.equal(await allTab.getAttribute('aria-selected'),'true');
  observations.nativeTabKeyboard='ArrowRight/Home/End move focus without native activation; Enter changes the actual selected tab';
  const seen=new Map();observations.windows=[];
  for(let count=0;count<8;count++){
   const rows=await nativeRows();observations.windows.push(rows);rows.forEach(r=>seen.set(r.identity,r));
   const more=page.getByRole('button',{name:'Show more items',exact:true});if(!await more.count())break;await c.activate(more);
  }
  const nativeTotal=Number(baseline.match(/^selected \((\d+)\)/m)[1])+Number(baseline.match(/^unselected \((\d+)\)/m)[1]);
  assert.equal(seen.size,nativeTotal);assert.equal(await page.getByRole('combobox').last().getAttribute('aria-label'),'Todos');
  checks.push('All actual native records are reachable through inherited window paging and retain distinct bound destination IDs');

  const last=await find(baselineRows.at(-1).identity);pending='pin';
  observations.pinStale=await heldAction(rowControl(last,'Remove from bookmark bar: '),async()=>(await nativeRows()).find(r=>r.identity===last.identity)?.pinned===false);
  let current=await find(last.identity);assert.equal(current.pinned,false);await c.activate(rowControl(current,'Add to bookmark bar: '));await poll(async()=>await module()===baseline,'native pin inverse');pending=null;
  checks.push('Real remove/add persists in the game and restores exact native membership/order; a retained pre-render pin command refuses with the current token');

  await bar();const first=(await nativeRows())[0];pending='move';
  observations.moveStale=await heldAction(rowControl(first,'Move down: '),async()=>(await nativeRows())[0].identity!==first.identity);
  assert.equal(await page.evaluate(()=>document.activeElement.id),observations.moveStale.id);
  current=await find(first.identity);await c.activate(rowControl(current,'Move up: '));await poll(async()=>await module()===baseline,'native reorder inverse');pending=null;
  checks.push('Real reorder and inverse retain bookmark focus; a recycled old row cannot move a different record before replacement HTML arrives');

  await all();await c.dropdown('Todos');observations.filterChoices=await page.locator('select[data-loaded]').last().locator('option').allTextContents();await c.cancelDropdown();observations.filters=[];
  for(const choice of observations.filterChoices.filter(n=>n!=='Todos')){
   await c.dropdown(await page.getByRole('combobox').last().getAttribute('aria-label'));await c.choose(choice);
   const rows=await nativeRows();assert.ok(rows.every(r=>seen.has(r.identity)));observations.filters.push({choice,rows:rows.map(r=>r.identity)});
  }
  await c.dropdown(await page.getByRole('combobox').last().getAttribute('aria-label'));await c.choose('Todos');assert.equal(await module(),baseline);await audit('Native complete favorite list and filters');
  await c.dropdown('Favoritos');observations.options=await page.locator('select[data-loaded]').last().locator('option').allTextContents();await c.cancelDropdown();
  checks.push('All game-provided category filters work, native menu opens/cancels, and original full filter/membership are restored; reset is not executed');

  pending='labels';await c.activate(labels(),'Space');await poll(async()=>await labels().isChecked()!==observations.originalLabels,'native labels changed');assert.equal(await nativeChecked(),!observations.originalLabels);
  await bar();await close();await open();assert.equal(await nativeChecked(),!observations.originalLabels);
  await c.activate(labels(),'Space');await poll(async()=>await nativeChecked()===observations.originalLabels,'original labels restored');await close();await open();assert.equal(await nativeChecked(),observations.originalLabels);pending=null;assert.equal(await module(),baseline);
  checks.push('Native label setting changes and survives reopening; its exact original state and all favorite records are restored');

  observations.destinations=[];
  for(const original of baselineRows){
   await bar();const destination=await find(original.identity);await c.activate(page.locator('#'+destination.id));
   await poll(async()=>(await c.fetchText('/bookmarkpopup?op=state')).includes('IsVisible=False'),'native popup auto-close after actual navigation');await settled();
   const actualUid=await foreground();assert.equal(actualUid,destination.identity.split('|')[0]);
   observations.destinations.push({identity:destination.identity,caption:destination.caption,actualUid,title:await page.title()});await audit('Actual native favorite destination: '+destination.caption);
   await open();await bar();assert.deepEqual((await nativeRows()).map(r=>r.identity),baselineRows.map(r=>r.identity));
  }
  await close();
  checks.push('Every original native favorite destination UID agrees with its record, the popup closes after native callbacks, and exact original bar persists on every reopening');

  await c.activate(page.getByRole('button',{name:/^Edit bookmarks(?:, Alt I for more info)?$/}));await poll(()=>page.getByRole('heading',{name:'Bookmark editor',exact:true}).count(),'inherited Ctrl editor');
  const currentHeading=await page.getByRole('heading',{name:/^Current bookmarks \(\d+\/12\)$/}).textContent();
  observations.modShortcutCount=Number(currentHeading.match(/\((\d+)\/12\)/)[1]);
  if(observations.modShortcutCount===0){
   assert.equal(await page.locator('table').count(),0);
   assert.equal(await page.getByText('No bookmarks set. Add destinations from the list below.',{exact:true}).count(),1);
  }else assert.deepEqual(await page.locator('table thead th').allTextContents(),['Key','Action','Move','Delete']);
  await audit('Inherited companion Ctrl editor, including its genuine empty state');
  await c.activate(page.getByRole('button',{name:'Save and close',exact:true}));assert.equal(await module(),baseline);
  await page.keyboard.press('Alt+1');await poll(()=>page.getByRole('heading',{name:'Mensagens',exact:true}).count(),'original Messages shortcut');await settled();
  checks.push('The inherited mod Ctrl editor and Alt+1 remain distinct and functional without editing either bookmark store');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  const result={success:true,mvid,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,network:c.network,restorationRequired:false,saveSubmitted:false,simulationAdvanced:false,resetExecuted:false,manualNVDA:false};
  fs.writeFileSync(path.join(out,'native-bookmarks.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({success:true,mvid,checks,audits},null,2));
 }catch(error){
  fs.writeFileSync(path.join(out,'native-bookmarks-failure.json'),JSON.stringify({success:false,error:error.stack,mvid,checks,audits,observations,pending,baseline,currentModule:await module().catch(()=>null),errors:c.errors,requestFailures:c.requestFailures,network:c.network,restorationRequired:pending!==null},null,2));throw error;
 }finally{if(release)release();await page.unrouteAll({behavior:'wait'});await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
