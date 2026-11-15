// Real signed-contract caption/value regression on the recorded accepted QA copy.
// No Save, offer, native percentage setter or simulation. Release uses the exact
// immutable checkpoint's independently retained native-caption evidence.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'artifacts/tests/web');
const manifest=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
const checkpoint=manifest.acceptedContractWorking,message='154686';
assert.ok(checkpoint&&manifest.ownedWorkingFiles.includes(checkpoint+'.fm'));
const referenceArg=process.argv.find(a=>a.startsWith('--native-reference='));
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,message,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);
  await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await page.keyboard.press('Alt+1');await poll(()=>page.getByRole('heading',{name:'Mensagens',exact:true}).count(),'actual Inbox');
  await c.newestMessages();await settled();
  const news=page.locator('tr[data-fm-message-id="'+message+'"] button[onclick^="FM.msgview.open"]');assert.equal(await news.count(),1);
  const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');opened.catch(()=>{});
  await news.focus();await page.keyboard.press('Enter');assert.equal(await(await opened).text(),'');
  await poll(()=>page.locator('[data-fm-message-view-id="'+message+'"]').count(),'actual signed-contract message');await settled();
  assert.match(await page.locator('#fmroot').innerText(),/Earl assina novo contrato de um ano/);
  const bars=page.getByRole('progressbar');assert.equal(await bars.count(),1);
  report.dom=await bars.evaluate(e=>({id:e.id,label:e.getAttribute('aria-label'),state:e.getAttribute('aria-valuetext'),text:e.textContent}));
  if(report.health.developerTools){
   const code=`var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;
var core=System.AppDomain.CurrentDomain.GetAssemblies()[0];foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${report.health.coreMvid}")core=a;
var sources=(System.Collections.IDictionary)core.GetType("FM27Access.Plugin.Server.LiveValues").GetField("_sources",f).GetValue(null);
var adapter=sources["${report.dom.id}"];if(adapter==null||adapter.GetType().Name!="ProgressBarAdapter")throw new System.Exception("Actual progress source required");
var e=(UnityEngine.UIElements.VisualElement)adapter.GetType().GetProperty("BackingElement",f).GetValue(adapter,null);
if(e.GetIl2CppType().Name!="SIProgressBar")throw new System.Exception("Actual native progress widget required");
var textType=core.GetType("UnityEngine.UIElements.TextElement");foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(textType==null)textType=a.GetType("UnityEngine.UIElements.TextElement");
for(int level=0;level<2;level++){var group=level==0?e.parent:e.parent.parent;for(int i=0;i<group.hierarchy.childCount;i++){var child=group.hierarchy[i];if(child.GetIl2CppType().Name!="SIText")continue;var text=System.Activator.CreateInstance(textType,new object[]{child.Pointer});System.Console.WriteLine((level==0?"VALUE|":"FIELD|")+textType.GetProperty("text",f).GetValue(text,null));}}`;
   const raw=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);
   assert.doesNotMatch(raw,/\[compile\]|\[exception\]|error CS/i);
   const values=raw.split(/\r?\n/).filter(x=>x.startsWith('VALUE|')).map(x=>x.slice(6));
   const fields=raw.split(/\r?\n/).filter(x=>x.startsWith('FIELD|')).map(x=>x.slice(6));
   assert.equal(values.length,1);assert.equal(fields.length,1);
   report.native={value:values[0],field:fields[0],raw,scope:'Direct native SIText siblings, without ProgressBarAdapter label/value formatting'};
  }else{
   assert.ok(referenceArg,'Release requires independent retained native evidence');
   const reference=JSON.parse(fs.readFileSync(path.resolve(root,referenceArg.slice(19)),'utf8'));
   assert.ok(reference.success&&reference.checkpoint===checkpoint&&reference.message===message&&reference.native);
   report.native=reference.native;report.nativeReferenceSource=referenceArg.slice(19);
  }
  assert.equal(report.native.value,'0,7%');assert.equal(report.dom.state,report.native.value);
  assert.equal(report.dom.label,report.native.field.trim().replace(/:\s*$/,''));
  assert.equal(report.dom.text,report.dom.label+': '+report.native.value);
  const audit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  report.audit=audit.violations.map(v=>v.id);assert.deepEqual(report.audit,[]);
  await c.activate(page.getByRole('button',{name:'Close message',exact:true}));await settled();
  assert.equal(await page.locator('[data-fm-message-view-id]').count(),0);
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
 }catch(error){report.failure=error.stack||String(error);throw error;}
 finally{report.jsErrors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;fs.mkdirSync(out,{recursive:true});fs.writeFileSync(path.join(out,'contract-progress.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
