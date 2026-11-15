// Saved, owned QA profile only. Exercise functional styles; never open the
// graphical appearance editor or submit Save changes.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')(),{page,poll,activate,settled}=c;
 const checks=[],audits=[];
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();assert.deepEqual(a.violations,[]);audits.push({name,violations:[]});}
 async function openStyle(){
  const index=await page.getByRole('radio').evaluateAll(rs=>rs.findIndex(r=>r.labels?.[0]?.textContent?.trim().startsWith('QA Portal, Access,')));
  assert.ok(index>=0,'Dedicated QA profile required');await activate(page.getByRole('button',{name:'Edit',exact:true}).nth(index));
  await activate(page.getByRole('button',{name:'ESTILO DE GESTÃO',exact:true}));await poll(()=>page.getByRole('tab',{name:'Treino',exact:true}).count(),'style editor');
  // The game remembers the last tab across cancellation/reopening.
  await activate(page.getByRole('tab',{name:'Treino',exact:true}));await settled();
 }
 async function nativeState(){
  const mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  // Read the native Manager node directly, independently of the adapter's
  // event-path/index resolver, and the game's separate selected-index list.
  const code='var qaStyleCore=System.AppDomain.CurrentDomain.GetAssemblies()[0];foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'")qaStyleCore=a;var qaStyleHandles=(System.Collections.IDictionary)qaStyleCore.GetType("FM27Access.Plugin.Server.ServerHandles").GetField("_handles",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);var qaStyleBindings=SI.Core.GameSubsystems.Get<SI.Bindable.BindingSubsystem>();foreach(System.Collections.DictionaryEntry e in qaStyleHandles){var f=e.Value.GetType().GetField("El");if(f==null)continue;var adapter=f.GetValue(e.Value);if(adapter==null||adapter.GetType().Name!="CheckBoxAdapter")continue;var ve=(UnityEngine.UIElements.VisualElement)qaStyleCore.GetType("FM27Access.Plugin.Adapters.PluginAdapterBase").GetProperty("BackingElement",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(adapter,null);var key=SI.Bindable.BindingUtils.GetViewKey(ve);for(var n=qaStyleBindings.GetNode(ref key);n!=null;n=n.Parent){foreach(var child in n.Children){if(child.Name!="Manager")continue;var persona=FM.UI.ManagerPersonaModule.GetManagerPersona(qaStyleBindings,child.Key);System.Console.WriteLine("STYLE "+persona.FirstName+"|"+persona.CoachingStylesSelected+"|"+persona.MentalTraitsSelected+"|"+FM.UI.ManagerPersonaModule.s_coachingIndexesSelected.Count);return;}}}';
  const output=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);
  const match=/STYLE QA Portal\|(\d+)\|(\d+)\|(\d+)/.exec(output);assert.ok(match,output);
  return{coaching:Number(match[1]),mental:Number(match[2]),coachingCount:Number(match[3])};
 }
 async function selected(){return page.getByRole('checkbox').evaluateAll(es=>es.filter(e=>e.checked).map(e=>e.labels[0].textContent.trim().split(',')[0]));}
 async function toggleAndRestore(name,kind,baseline){
  const checkbox=page.getByRole('checkbox',{name:new RegExp('^'+name+',')});assert.equal(await checkbox.isChecked(),true);
  const origin=await checkbox.getAttribute('id');await activate(checkbox,'Space');await poll(async()=>!await checkbox.isChecked(),'native removal '+name);
  const removed=await nativeState();assert.notEqual(removed[kind],baseline[kind]);
  if(kind==='coaching')assert.equal(removed.coachingCount,baseline.coachingCount-1);
  assert.equal(await page.evaluate(()=>document.activeElement.id),origin);
  await activate(checkbox,'Space');await poll(()=>checkbox.isChecked(),'native restoration '+name);
  assert.deepEqual(await nativeState(),baseline,'The original mask/list must be restored without duplicate indices');
 }
 try{
  assert.equal(await page.title(),'MainMenu');await page.route('**/speak?*',r=>r.fulfill({status:200,body:''}));
  await activate(page.getByRole('button',{name:'Perfis de Treinador',exact:true}));await openStyle();
  const baseline=await nativeState();assert.deepEqual(baseline,{coaching:5,mental:12288,coachingCount:2});
  assert.deepEqual(await selected(),['Ofensivo','Formador']);await audit('Saved coaching styles');
  await toggleAndRestore('Ofensivo','coaching',baseline);await toggleAndRestore('Formador','coaching',baseline);
  assert.deepEqual(await selected(),['Ofensivo','Formador']);checks.push('Saved coaching mask 5 and two native indices match Ofensivo/Formador; both remove/restore through Space without duplicate indices or lost focus');
  await activate(page.getByRole('tab',{name:'Mentais',exact:true}));assert.deepEqual(await selected(),['Convincente','Inspirador']);await audit('Saved mental traits');
  await toggleAndRestore('Convincente','mental',baseline);await toggleAndRestore('Inspirador','mental',baseline);
  assert.deepEqual(await selected(),['Convincente','Inspirador']);checks.push('Saved mental mask 12288 matches Convincente/Inspirador; both remove/restore through the native checkbox events and retain the original mask');
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));await openStyle();assert.deepEqual(await nativeState(),baseline);assert.deepEqual(await selected(),['Ofensivo','Formador']);
  await activate(page.getByRole('tab',{name:'Mentais',exact:true}));assert.deepEqual(await selected(),['Convincente','Inspirador']);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await activate(page.getByRole('button',{name:'Menu Principal',exact:true}));checks.push('Cancel and native reopening preserve the saved profile selections; no profile Save is submitted');
  await activate(page.getByRole('button',{name:'Preferências',exact:true}));await activate(page.getByRole('tab',{name:'Detalhado',exact:true}));await activate(page.getByRole('tab',{name:'Áudio',exact:true}));
  const audio=page.getByRole('checkbox',{name:'Sons de ambiente',exact:true}),was=await audio.isChecked();
  await activate(audio,'Space');await poll(async()=>await audio.isChecked()!==was,'ordinary checkbox changes');await activate(audio,'Space');await poll(async()=>await audio.isChecked()===was,'ordinary checkbox restored');await audit('Ordinary audio checkbox after shared adapter change');
  await activate(page.getByRole('button',{name:'Close',exact:true}));assert.equal(await page.title(),'MainMenu');checks.push('Ordinary native audio checkbox still toggles/restores and remains semantically clean');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);
  const report={checks,audits,errors:c.errors,requestFailures:c.requestFailures,profileSaved:false,appearanceOpened:false,manualNVDA:false};fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/management-style.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
 }finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
