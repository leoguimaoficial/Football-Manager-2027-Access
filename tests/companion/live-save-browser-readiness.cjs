// Run from the main menu before the first native save-browser open after a
// cold game start. Read existing files only; never load, delete or save one.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,activate}=c;
 const checks=[],bodies=[],observations={};let opened=false,originalSource;
 const source=()=>page.locator('#fmroot select[data-dd]').first();
 // FM26 folds a self-labelled dropdown's current caption into its accessible
 // name; the collapsed option contains only the ordinal state ("1 of 2").
 const sourceValue=()=>source().getAttribute('aria-label');
 async function selectSource(value){await c.dropdown(await source().getAttribute('aria-label'));await c.choose(value);}
 async function nativeSource(){
  const id=await source().getAttribute('id');
  const code='var asm=System.AppDomain.CurrentDomain.GetAssemblies()[0];foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+observations.mvid+'")asm=a;var hs=(System.Collections.IDictionary)asm.GetType("FM27Access.Plugin.Server.ServerHandles").GetField("_handles",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);var ad=hs["'+id+'"].GetType().GetField("El").GetValue(hs["'+id+'"]);var el=ad.GetType().GetProperty("BackingElement",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).GetValue(ad);var si=System.AppDomain.CurrentDomain.GetAssemblies()[0];foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.GetName().Name=="SI.Bindable")si=a;var dd=((UnityEngine.UIElements.VisualElement)el).TryCast<SI.Bindable.SIDropdown>();if(dd==null)throw new System.InvalidOperationException("The observed native control is not an SIDropdown.");System.Console.WriteLine(dd.GetType().GetProperty("SelectedIndex").GetValue(dd));';
  const text=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);
  assert.match(text.trim(),/^\d+$/);return Number(text.trim());
 }
 page.on('response',async r=>{if(new URL(r.url()).pathname!=='/body')return;try{const html=await r.text();bodies.push({at:Date.now(),hasSource:html.includes('data-dd='),radioCount:(html.match(/type="radio"/g)||[]).length});}catch{}});
 try{
  observations.mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  assert.equal(await page.title(),'MainMenu');
  const opener=page.getByRole('button',{name:/^CARREGAR JOGO/}),origin=await opener.getAttribute('id');
  const started=Date.now();await activate(opener);opened=true;
  // No FM.core.refresh, F5, second client or native warm-open before this wait.
  await poll(()=>source().count(),'late native save source without manual refresh');
  await poll(async()=>['Local','Cloud'].includes(await sourceValue()),'real native source caption');
  originalSource=await sourceValue();observations.sourceReadyMs=Date.now()-started;
  assert.deepEqual(await page.getByRole('tab').allTextContents(),['FM27','FM26','FM24']);
  assert.ok(await source().getAttribute('aria-label'));
  await c.dropdown(await source().getAttribute('aria-label'));
  const options=await source().locator('option').allTextContents();assert.deepEqual(options,['Local','Cloud']);
  await c.cancelDropdown();assert.equal(await sourceValue(),originalSource);
  assert.equal(await source().evaluate(e=>document.activeElement===e),true);
  checks.push('First native save-browser source and version tabs become accessible without manual refresh; real source options cancel with exact focus');
  for(const value of ['Cloud','Local']){
   if(await sourceValue()!==value)await selectSource(value);
   assert.equal(await nativeSource(),options.indexOf(value));
   if(value==='Cloud'){
    // Compare the actual current native status, without inventing cloud quota.
    await poll(()=>page.getByText('Ainda nenhuma gravação feita.',{exact:true}).count(),'native empty cloud result');
    const gui=await c.fetchText('/gui'),nativeSpace=gui.match(/SIText name="available-space-text" visible=True text="([^"]+)"/);
    assert.ok(nativeSpace,'Actual native cloud available-space label');
    assert.ok((await page.locator('#fmroot').innerText()).includes(nativeSpace[1]));
    observations.cloudSpace=nativeSpace[1];
   }else{
    const manifest=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../../artifacts/current-full-two-matches-original-global-state.json'),'utf8'));
    await poll(async()=>await page.getByRole('radio').count()===manifest.protectedFiles.length,'all existing local save records');
    observations.localRecords=await page.getByRole('radio').count();
   }
  }
  checks.push('Cloud/Local commits agree with actual native SelectedIndex; real cloud quota/empty result and all protected local records update');
  if(await sourceValue()!==originalSource)await selectSource(originalSource);
  assert.equal(await nativeSource(),options.indexOf(originalSource));
  const audit=(await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze()).violations.map(v=>v.id);assert.deepEqual(audit,[]);
  await activate(page.getByRole('button',{name:'Close',exact:true}));opened=false;
  await poll(async()=>await page.title()==='MainMenu','native main-menu return');
  await poll(()=>page.evaluate(id=>document.activeElement.id===id,origin),'exact original load button focus');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  checks.push('Original native source restored; browser closes to MainMenu with no file load, delete, save or simulation');
  const report={success:true,checks,observations,bodies,audit,errors:c.errors,requestFailures:c.requestFailures,network:c.network,manualNVDA:false};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/save-browser-readiness.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
 }catch(error){
  const restoration=[];if(opened)try{if(originalSource&&await source().count()&&await sourceValue()!==originalSource)await selectSource(originalSource);await activate(page.getByRole('button',{name:'Close',exact:true}));}catch(e){restoration.push(e.message);}
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/save-browser-readiness-failure.json'),JSON.stringify({error:error.stack,checks,observations,bodies,restoration,errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));throw error;
 }finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
