// Existing Quick Start setup only: no Next, career creation or save commands.
// This cohort uses the native England men's Premier League default.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const output=path.resolve(__dirname,'../../artifacts/quick-start-refinement');fs.mkdirSync(output,{recursive:true});
 const report={success:false,checks:[],audits:[],careerCreated:false,manualNVDA:false};
 const club=name=>page.getByRole('button',{name:new RegExp('^'+name+',')});
 async function audit(label){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({label,violations:a.violations});assert.deepEqual(a.violations,[]);assert.equal(await page.locator('h1 button,h2 button,h3 button,h4 button,h5 button,h6 button').count(),0);}
 async function nativeGrid(id){
  if(!report.health.developerTools)return null;
  assert.match(id,/^[a-z0-9]+$/i);
  assert.match(report.health.coreMvid,/^[0-9a-f-]{36}$/i);
  const code=`foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${report.health.coreMvid}"){
var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;var sh=a.GetType("FM27Access.Plugin.Server.ServerHandles");var hs=(System.Collections.IDictionary)sh.GetField("_handles",f).GetValue(null);var h=hs["${id}"];var ad=h.GetType().GetField("El",f).GetValue(h);var e=sh.GetMethod("NativeElement",f).Invoke(null,new object[]{ad});
while(e!=null){var nt=e.GetType().GetMethod("GetIl2CppType").Invoke(e,null);if((string)nt.GetType().GetProperty("Name").GetValue(nt,null)=="GridSelector")break;e=e.GetType().GetProperty("parent").GetValue(e,null);}if(e==null)throw new System.InvalidOperationException("Actual grid owner required");
foreach(var aa in System.AppDomain.CurrentDomain.GetAssemblies()){var t=aa.GetType("SI.Bindable.GridSelector");if(t==null)continue;System.Reflection.MethodInfo cast=null;foreach(var m in e.GetType().GetMethods(f))if(m.Name=="TryCast"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0){cast=m;break;}var grid=cast.MakeGenericMethod(new[]{t}).Invoke(e,null);System.Console.WriteLine("GRID_INDEX|"+t.GetProperty("m_selectedIndex").GetValue(grid,null));}}
`;
  const raw=await page.evaluate(async code=>await(await fetch('/eval',{method:'POST',body:code})).text(),code);assert.doesNotMatch(raw,/\[compile\]|\[exception\]|error CS/i);
  const match=raw.match(/GRID_INDEX\|(-?\d+)/);assert.ok(match,raw);return Number(match[1]);
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));assert.equal(await page.title(),'Início rápido para criar jogo');
  if(!report.health.developerTools)for(const [route,method] of [['/eval','POST'],['/reload','POST'],['/gui','GET'],['/rendered/report','POST']]){
   assert.equal(await page.evaluate(async({route,method})=>(await fetch(route,{method})).status,{route,method}),404,route);
  }
  assert.doesNotMatch(await page.locator('#fmroot').innerText(),/LeagueBackground|Unemployed|CompLogo/);
  const unemployed=page.getByRole('checkbox',{name:'Sem emprego',exact:true});assert.equal(await unemployed.count(),1);assert.equal(await unemployed.isChecked(),false);
  const original=await club('Arsenal').innerText();assert.match(original,/, selected$/);assert.equal(await page.getByRole('button',{name:/Classificação Prevista/}).count(),20);
  report.originalGridIndex=await nativeGrid(await club('Arsenal').getAttribute('id'));
  await activate(club('Aston Villa'));await poll(async()=>/, selected$/.test(await club('Aston Villa').innerText()),'actual new club selection');
  assert.doesNotMatch(await club('Arsenal').innerText(),/, selected$/);report.changedGridIndex=await nativeGrid(await club('Aston Villa').getAttribute('id'));
  if(report.health.developerTools)assert.notEqual(report.changedGridIndex,report.originalGridIndex);
  await activate(club('Arsenal'));await poll(async()=>await club('Arsenal').innerText()===original,'original club restored');
  if(report.health.developerTools)assert.equal(await nativeGrid(await club('Arsenal').getAttribute('id')),report.originalGridIndex);
  await activate(club('Man City'));await poll(async()=>/, selected$/.test(await club('Man City').innerText()),'club in another native grid column');
  assert.doesNotMatch(await club('Arsenal').innerText(),/, selected$/);await activate(club('Arsenal'));
  await poll(async()=>await club('Arsenal').innerText()===original,'original club restored across columns');
  report.checks.push('All 20 native club choices retain names/predictions; actual selection changes and restores');
  await activate(unemployed,'Space');assert.equal(await unemployed.isChecked(),true);await activate(unemployed,'Space');assert.equal(await unemployed.isChecked(),false);
  report.checks.push('Official Sem emprego checkbox toggles and restores');
  const country=page.getByRole('combobox',{name:'Inglaterra',exact:true}),origin=await country.getAttribute('id');await c.dropdown('Inglaterra');await c.cancelDropdown();
  assert.equal(await page.evaluate(()=>document.activeElement?.id),origin);report.checks.push('Native country options open/cancel and restore exact focus');
  for(const name of ['Clube','Nação','Clube & Nação']){await activate(page.getByRole('tab',{name,exact:true}));assert.equal(await page.getByRole('tab',{name,exact:true}).getAttribute('aria-selected'),'true');assert.doesNotMatch(await page.locator('#fmroot').innerText(),/LeagueBackground|Unemployed/);await audit(name);}
  await activate(page.getByRole('tab',{name:'Clube',exact:true}));await settled();assert.equal(await club('Arsenal').innerText(),original);assert.equal(await unemployed.isChecked(),false);
  await activate(page.getByRole('button',{name:'Preferências',exact:true}));await poll(async()=>await page.title()!=='Início rápido para criar jogo','native preferences');await settled();
  report.preferencesCheckboxes=await page.locator('#fmroot input[type=checkbox]').evaluateAll(xs=>xs.map(x=>({name:x.labels?.[0]?.innerText||x.getAttribute('aria-label'),checked:x.checked,disabled:x.disabled})));
  const preferencesBaseline=path.join(output,'preferences-before.json');if(fs.existsSync(preferencesBaseline))assert.deepEqual(report.preferencesCheckboxes,JSON.parse(fs.readFileSync(preferencesBaseline)).checkboxes);
  await audit('Preferences checkbox regression');await activate(page.getByRole('button',{name:'Close',exact:true}));await poll(async()=>await page.title()==='Início rápido para criar jogo','Quick Start restored');await settled();
  assert.equal(await club('Arsenal').innerText(),original);assert.equal(await unemployed.isChecked(),false);assert.equal(await page.getByRole('tab',{name:'Clube',exact:true}).getAttribute('aria-selected'),'true');
  report.checks.push('All three native tabs and Preferences open/close preserve the original setup');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
 }catch(error){report.error=error.stack;fs.writeFileSync(path.join(output,'live-failure.html'),await page.content().catch(()=>''));throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;fs.writeFileSync(path.join(output,report.health?.developerTools?'debug-live.json':'release-live.json'),JSON.stringify(report,null,2));await c.browser.close();console.log(JSON.stringify({success:report.success,checks:report.checks,audits:report.audits.length}));}
})().catch(error=>{console.error(error);process.exitCode=1;});
