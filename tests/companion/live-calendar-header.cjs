// Actual owned QA fixture calendar: native backdrop, selected season and focus.
// Opens the native Calendário fixture view. No fixture/save/simulation edit.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],observations={};let mvid,success=false,failure;
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  if(await page.title()!=='Calendário'){
   const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="989379584"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
   await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await poll(async()=>await page.title()==='Portal','actual Portal destination');await settled();await search.fill('');await settled();
   await c.activate(page.getByRole('button',{name:'Agenda',exact:true}));await poll(async()=>await page.title()==='Calendário','actual fixture calendar');await settled();
  }
  assert.equal(await page.title(),'Calendário');
  assert.equal(await page.getByRole('tab',{name:'Calendário',selected:true,exact:true}).count(),1);
  const native=await c.fetchText('/gui'),lines=native.split(/\r?\n/);
  const start=lines.findIndex(l=>l.includes('SIDropdown name="SeasonDropdown"'));
  assert.ok(start>=0,'Actual native season selector exists');
  const depth=Number(lines[start].match(/\[(\d+)\]/)[1]);let caption;
  for(const line of lines.slice(start+1)){
   const d=line.match(/\[(\d+)\]/);if(d&&Number(d[1])<=depth)break;
   const value=line.match(/SIText name="StringValue" visible=True text="([^"\r\n]+)"/);if(value){assert.equal(caption,undefined);caption=value[1];}
  }
  assert.ok(caption,'Read the actual localized native selected-season caption');
  const season=page.getByRole('combobox',{name:'Season',exact:true});
  assert.equal(await season.locator('option').first().textContent(),caption);
  const id=await season.getAttribute('id');
  const code='foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'") {var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;var sources=(System.Collections.IDictionary)a.GetType("FM27Access.Plugin.Server.LiveValues").GetField("_sources",f).GetValue(null);var ad=sources["'+id+'"];var el=ad.GetType().GetProperty("BackingElement",f).GetValue(ad,null);System.Console.WriteLine("ENABLED|"+el.GetType().GetProperty("enabledInHierarchy").GetValue(el,null));}';
  const availability=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);
  assert.match(availability,/^ENABLED\|(True|False)/);assert.equal(await season.isDisabled(),availability.includes('ENABLED|False'));
  if(await season.isDisabled())assert.equal(await season.getAttribute('data-dd'),null);
  observations.season={caption,disabled:await season.isDisabled(),availability};
  checks.push('Official native selected-season text survives the inherited profile field name; HTML availability agrees with native enabledInHierarchy');
  // Current native templates use SIStyleSetter for this decorative layer;
  // earlier qualified templates used SIImage. Its presence is native evidence,
  // not a requirement that every package retain the old implementation type.
  assert.match(native,/(?:SIImage|SIStyleSetter) name="MiddleLayer" visible=True/);
  assert.doesNotMatch(await page.locator('#fmroot').innerText(),/MiddleLayer, image/);
  checks.push('The native decorative backdrop/style layer is absent from companion reading; header/data containers remain accessible');
  const before=await page.locator('#fmroot').innerText(),rows=await page.locator('tbody tr').count();assert.ok(rows>0);
  const target=page.locator('tbody button:not([disabled])').first(),origin=await target.getAttribute('id');await target.focus();
  await page.evaluate(()=>FM.core.refresh(false,true));await settled();
  assert.equal(await page.locator('#fmroot').innerText(),before);assert.equal(await page.locator('tbody tr').count(),rows);
  assert.equal(await page.evaluate(()=>document.activeElement?.id),origin);
  observations.rows=rows;checks.push('Neutral native reread preserves every calendar text/row, official season and exact fixture-link focus');
  const audit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  observations.violations=audit.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}));assert.deepEqual(observations.violations,[]);
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(error){failure={error:error.stack,html:await page.locator('#fmroot').innerHTML(),native:await c.fetchText('/gui'),transport:await c.transportTrace()};throw error;
 }finally{
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/calendar-header.json'),JSON.stringify({success,mvid,checks,observations,failure,errors:c.errors,requestFailures:c.requestFailures,network:c.network,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));await c.browser.close();
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
