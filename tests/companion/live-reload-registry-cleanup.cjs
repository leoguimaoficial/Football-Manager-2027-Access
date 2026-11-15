// One deliberate same-source Debug reload with an actually open native menu.
// Retired-core cleanup is observed; the test NEVER purges old registries itself.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const registries=require('./native-core-registries.cjs');
const out=path.resolve(__dirname,'../../artifacts/tests/web/reload-registry-cleanup.json');
(async()=>{
 let c=await require('./original-client.cjs')({networkDiagnostics:true});
 const report={at:new Date().toISOString(),success:false,checks:[],audits:[],reloadDispatches:0,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 try{
  report.healthBefore=JSON.parse(await c.fetchText('/health'));assert.equal(report.healthBefore.developerTools,true);assert.equal(await c.page.title(),'Portal');
  const beforeCookies=JSON.stringify((await c.page.context().cookies()).map(x=>({name:x.name,value:x.value})).sort((a,b)=>a.name.localeCompare(b.name)));assert.ok(beforeCookies!=='[]','Real Host session cookie is present');
  report.before=await registries(c);const active=report.before.rows.find(r=>r.mounted);assert.equal(active.mvid,report.healthBefore.coreMvid);assert.ok(active.handles>0&&active.values>0&&active.contexts>0);
  await c.dropdown('FM Menu');report.menuBefore=await c.page.getByRole('combobox',{name:'FM Menu',exact:true}).locator('option').allTextContents();assert.ok(report.menuBefore.includes('Preferências'));report.open=await registries(c);assert.ok(report.open.rows.find(r=>r.mounted).dropdownLevels>0);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  report.reloadDispatches=1;report.reloadAt=Date.now();report.uncertainReload=true;fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify(report,null,2));
  try{report.reply=await c.page.evaluate(async()=>(await fetch('/reload',{method:'POST'})).text());}catch(error){report.reloadTransportObservation=String(error.message);report.replayed=false;}
  if(report.reply!==undefined)assert.match(report.reply,/^reloading/);
  report.oldClientClosedAt=Date.now();await c.browser.close();report.oldClient={errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures};c=null;
  const within=at=>at>=report.reloadAt-250&&at<=report.oldClientClosedAt+2000;
  const expectedPath=p=>['/events','/reload','/body','/health'].includes(p);
  report.unexpectedOldRequests=report.oldClient.requestFailures.filter(f=>!expectedPath(f.path)||!within(f.failedAt));
  report.unexpectedOldNetwork=report.oldClient.networkFailures.filter(f=>!expectedPath(f.request?.path)||!within((f.request?.startedUtcMs||0)+(f.elapsedMs||0)));
  assert.deepEqual(report.unexpectedOldRequests,[]);assert.deepEqual(report.unexpectedOldNetwork,[]);assert.deepEqual(report.oldClient.errors,[]);
  c=await require('./original-client.cjs')({networkDiagnostics:true});await c.poll(async()=>JSON.parse(await c.fetchText('/health')).screen==='Live','replacement core running');await c.settled();report.healthAfter=JSON.parse(await c.fetchText('/health'));
  assert.equal(report.healthAfter.pid,report.healthBefore.pid);assert.notEqual(report.healthAfter.instance,report.healthBefore.instance,'Health instance identifies a core lifetime, not the permanent Host');assert.equal(report.healthAfter.coreMvid,report.healthBefore.coreMvid);
  const afterCookies=JSON.stringify((await c.page.context().cookies()).map(x=>({name:x.name,value:x.value})).sort((a,b)=>a.name.localeCompare(b.name)));report.sessionCookieRetained=beforeCookies===afterCookies;assert.ok(report.sessionCookieRetained,'Host authentication survives reload without exposing its token');
  report.after=await registries(c);const retired=report.after.rows.filter(r=>r.mvid===report.healthBefore.coreMvid&&!r.mounted);assert.ok(retired.length>0,'Actual corrected core was unmounted, not merely refreshed');assert.ok(retired.every(r=>r.handles===0&&r.values===0&&r.contexts===0&&r.dropdownLevels===0),'Every corrected retired instance releases command/value/context/popup registrations');const fresh=report.after.rows.find(r=>r.mounted);assert.ok(fresh.handles>0&&fresh.values>0&&fresh.contexts>0);report.uncertainReload=false;
  report.checks.push('One same-source native reload releases all observed retired corrected-core command/value/context/popup registries; replacement remains populated, same game/Host/authentication');
  await c.dropdown('FM Menu');report.menuAfter=await c.page.getByRole('combobox',{name:'FM Menu',exact:true}).locator('option').allTextContents();assert.deepEqual(report.menuAfter,report.menuBefore);await c.choose('Preferências');await c.poll(()=>c.page.title().then(t=>t==='Preferências e Acessibilidade'),'real preferences after reload');await c.settled();
  const a=await new AxeBuilder({page:c.page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({name:'Actual preferences after owned-popup reload',violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);await c.activate(c.page.getByRole('button',{name:'Close',exact:true}));await c.settled();assert.equal(await c.page.title(),'Portal');
  report.checks.push('Fresh sole client receives identical real FM Menu options, opens native Preferences and closes to Portal; no settings, Save, simulation or native provider reset');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
 }catch(error){report.failure=error.stack||String(error);throw error;}
 finally{if(c){report.newClient={errors:c.errors,requestFailures:c.requestFailures,network:c.network};await c.browser.close();}fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));}
})().catch(e=>{console.error(e);process.exitCode=1;});
