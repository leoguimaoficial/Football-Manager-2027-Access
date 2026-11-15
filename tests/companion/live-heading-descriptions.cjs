// Real rendered section titles use arrival ids, not native command ids.
// Alt+I must resolve those readonly titles through the existing speech path.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page}=c;
 const checks=[],audits=[],readings=[];let success=false,health;
 async function read(control,expected){
  await control.focus();const before=await page.locator('#fmroot').innerHTML(),focus=await control.getAttribute('id');
  const description=page.waitForResponse(r=>new URL(r.url()).pathname==='/describe');
  const speech=page.waitForResponse(r=>new URL(r.url()).pathname==='/speak');
  await page.keyboard.press('Alt+i');const response=await description,text=await response.text();
  assert.equal(response.status(),200);assert.ok(text.trim());assert.doesNotMatch(text,/Unknown element|Refresh the screen|unavailable/);
  if(expected!==undefined)assert.equal(text,expected);
  assert.equal((await speech).status(),200);await c.poll(async()=>(await c.fetchText('/speech')).includes(text),'actual SpeechPipeline observation');
  assert.equal(await page.locator('#fmroot').innerHTML(),before);assert.equal(await page.evaluate(()=>document.activeElement.id),focus);
  readings.push({id:focus,text});return text;
 }
 async function audit(name){const result=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:result.violations.map(v=>v.id)});assert.deepEqual(result.violations,[]);}
 try{
  health=JSON.parse(await c.fetchText('/health'));assert.equal(await page.title(),'MainMenu');
  const title=page.locator('#fmsec0-h');await read(title,await title.innerText());await read(title,await title.innerText());
  assert.equal(await page.locator('#fmroot :is(h1,h2,h3,h4,h5,h6) :is(button,input,select,textarea)').count(),0);
  assert.equal(await page.locator('#fmroot button').count(),12,'Original native commands remain; the two unbound visual call-to-action children are not separate controls');
  assert.equal(await page.getByRole('button',{name:'Início rápido',exact:true}).count(),0);
  assert.equal(await page.getByRole('button',{name:'Iniciar Draft Aleatório',exact:true}).count(),0);
  const menu=[];
  for(const caption of ['Carregar Carreira Mais Recente','CRIAR NOVO JOGO','CARREGAR JOGO','ONLINE','TREINAR UM PAÍS','JOGAR DRAFT ALEATÓRIO']){
   const button=page.getByRole('button',{name:new RegExp('^'+caption)});assert.equal(await button.count(),1);
   menu.push({caption,id:await button.getAttribute('id'),label:await button.innerText(),button});
  }
  const nativeCode=`foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${health.coreMvid}"&&a.GetType("FM27Access.Plugin.CoreEntry").GetField("_bridge",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null)!=null){
   var flags=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;
   var handles=(System.Collections.IDictionary)a.GetType("FM27Access.Plugin.Server.ServerHandles").GetField("_handles",flags).GetValue(null);
   foreach(var id in new[]{${menu.map(m=>JSON.stringify(m.id)).join(',')}}){var h=handles[id];var el=h.GetType().GetField("El",flags).GetValue(h);
    var primary=(string)el.GetType().GetMethod("GetPrimaryLabel").Invoke(el,null);var description=(string)el.GetType().GetMethod("GetDescription").Invoke(el,null);
    System.Console.WriteLine("MENU|"+id+"|"+System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(primary??""))+"|"+System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(description??"")));
   }
  }`;
  const native=await page.evaluate(async code=>{const r=await fetch('/eval',{method:'POST',body:code});if(!r.ok)throw Error('Native observation HTTP '+r.status);return r.text();},nativeCode);
  const facts=new Map(native.split(/\r?\n/).filter(l=>l.startsWith('MENU|')).map(l=>{const[,id,p,d]=l.split('|');return[id,{primary:Buffer.from(p,'base64').toString(),description:Buffer.from(d,'base64').toString()}];}));
  assert.equal(facts.size,6,native);
  for(const m of menu){
   const fact=facts.get(m.id);assert.equal(fact.primary,m.caption);assert.ok(fact.description);
   // This is the pristine FM26 contract, independently applied to actual native
   // text: brief secondary data stays in the label; long help advertises Alt+I.
   const short=fact.description.length<=85;
   assert.equal(m.label,m.caption+', '+(short?fact.description:'Alt I for more info'));
   await read(m.button,short?'No additional information.':fact.description);
   if(m.caption==='Carregar Carreira Mais Recente')assert.ok(short&&m.label.includes(fact.description),'The complete actual recent-save summary remains in the same button');
  }
  checks.push('All six native menu buttons use the original FM26 label composition: complete recent-save data, short inline details and the spoken Alt I hint for long descriptions; twelve original commands remain outside headings without unbound child controls');
  await audit('Main menu with original FM26 controls');
  await c.dropdown('Mais');const opened=page.locator('select[data-dd][data-loaded]');await read(opened);
  assert.equal(await opened.count(),1);await c.cancelDropdown();
  checks.push('Alt+I remains readonly with an actual native dropdown open; Escape restores its original focus');
  await c.activate(page.getByRole('button',{name:/^CRIAR NOVO JOGO/}));
  assert.equal(await page.locator('#fmroot :is(h1,h2,h3,h4,h5,h6) :is(button,input,select,textarea)').count(),0);
  const nativeTitle=page.locator('#fmsec0-h');await read(nativeTitle,await nativeTitle.innerText());
  await audit('Native start-career title');
  await c.activate(page.getByRole('button',{name:'Anterior',exact:true}));
  await read(page.locator('#fmsec0-h'),'MainMenu');
  checks.push('Native-title and synthetic-title section arrivals retain original descriptions across actual navigation and return');
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,health.coreMvid);
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(error){checks.push(error.stack);throw error;}
 finally{
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/heading-descriptions.json'),JSON.stringify({at:new Date().toISOString(),success,health,checks,audits,readings,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));
  await c.browser.close();console.log(JSON.stringify({success,checks,audits:audits.length}));
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
