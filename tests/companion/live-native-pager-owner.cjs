// Actual production pager retained while native navigation replaces its owner.
// Replacement HTML is withheld before server rendering. No synthetic native UI,
// message selection, save, simulation or command replay.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],audits=[],observations={},out=path.resolve(__dirname,'../../artifacts/tests/web');let mvid,release;
 const rows=()=>page.locator('tr[data-fm-message-id]').evaluateAll(rs=>rs.map(r=>r.dataset.fmMessageId));
 async function read(code){const raw=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);assert.doesNotMatch(raw,/\[exception\]|\[compile\].*error/);return raw;}
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  await page.keyboard.press('Alt+1');await poll(()=>page.getByRole('heading',{name:'Mensagens',exact:true}).count(),'actual Messages');await settled();
  const all=page.getByRole('tab',{name:'Tudo',exact:true});if(await all.getAttribute('aria-selected')!=='true')await c.activate(all);await c.newestMessages();await settled();
  observations.before=await rows();const id=await page.getByRole('button',{name:'Show older messages',exact:true}).getAttribute('id'),owner=await page.evaluate(()=>FMTOKEN);
  const capture='object qaRetainedPagerOwnerHandle=null;foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'"){var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;var hs=(System.Collections.IDictionary)a.GetType("FM27Access.Plugin.Server.ServerHandles").GetField("_handles",f).GetValue(null);qaRetainedPagerOwnerHandle=hs["'+id+'"];System.Console.WriteLine("CAPTURED|"+(qaRetainedPagerOwnerHandle!=null));}';
  assert.match(await read(capture),/CAPTURED\|True/);await audit('Actual Messages pager before native departure');
  const gate=new Promise(r=>release=r);let held=0;
  await page.route('**/body?*',async route=>{held++;await gate;try{await route.continue();}catch{}});
  const navigation=page.waitForResponse(r=>new URL(r.url()).pathname==='/matchchord');navigation.catch(()=>{});await page.keyboard.press('Alt+6');assert.equal(await(await navigation).text(),'');await poll(()=>held>0,'actual replacement body held');
  const state='foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'"){var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;var h=qaRetainedPagerOwnerHandle;var ad=h.GetType().GetField("El",f).GetValue(h);var scroll=(UnityEngine.UIElements.VisualElement)ad.GetType().GetField("_scroll",f).GetValue(ad);System.Console.WriteLine("ATTACHED|"+(scroll.panel!=null));System.Console.WriteLine("NATIVE_VISIBLE|"+a.GetType("FM27Access.Plugin.ElementBridge").GetMethod("IsVisibleInHierarchy",f).Invoke(null,new object[]{scroll}));System.Console.WriteLine("ADAPTER_VISIBLE|"+ad.GetType().GetMethod("IsVisible",f).Invoke(ad,null));var args=new object[]{null};System.Console.WriteLine("HANDLE_VALID|"+h.GetType().GetMethod("TryValidate",f).Invoke(h,args)+"|"+args[0]);var set=a.GetType("FM27Access.Plugin.Server.InteractivePanels").GetMethod("Current",f).Invoke(null,null);foreach(var m in a.GetType("FM27Access.Plugin.Server.ServerText").GetMethods(f))if(m.Name=="ScreenToken"&&m.GetParameters()[0].ParameterType.Name.StartsWith("IReadOnlyList"))System.Console.WriteLine("CURRENT_TOKEN|"+m.Invoke(null,new object[]{set}));}';
  // The inherited Agenda chord opens Portal before its actual Calendar tool.
  // Messages can still be attached/visible during that intermediate parent;
  // only the actual requested destination proves this departure has finished.
  let raw;await poll(async()=>{raw=await read(state);return raw.includes('CURRENT_TOKEN|')&&raw.match(/^CURRENT_TOKEN\|([^\r\n]*)/m)[1].split('\x1f').some(p=>p.startsWith('837367296#'));},'actual native Agenda destination after intermediate Portal');
  assert.match(raw,/ADAPTER_VISIBLE\|False/);assert.match(raw,/HANDLE_VALID\|False\|That list is no longer on screen/);observations.native=raw;
  const current=raw.match(/^CURRENT_TOKEN\|([^\r\n]*)/m)[1];assert.notEqual(current,owner);
  observations.oldTokenReply=await c.fetchText('/activate?id='+id+'&screen='+encodeURIComponent(owner));assert.match(observations.oldTokenReply,/screen changed/i);
  observations.currentTokenReply=await c.fetchText('/activate?id='+id+'&screen='+encodeURIComponent(current));assert.match(observations.currentTokenReply,/That list is no longer on screen/);assert.doesNotMatch(observations.currentTokenReply,/^Activated /);
  const after=await read(state),afterToken=after.match(/^CURRENT_TOKEN\|([^\r\n]*)/m)[1];
  // Late native captions may advance names while both refused requests leave
  // the actual document/binding/template destination untouched.
  assert.equal(await page.evaluate(t=>FM.core.screenIdentity(t),afterToken),await page.evaluate(t=>FM.core.screenIdentity(t),current));
  assert.match(after,/HANDLE_VALID\|False/);observations.afterRefusals=after;observations.heldBodies=held;
  checks.push('Actual native departure makes the retained pager and handle unavailable before any replacement HTML is rendered');
  checks.push('Old-token and current-token stale pager requests are both refused; the detached list never reports Activated and the native destination remains unchanged');
  release();release=null;await page.unrouteAll({behavior:'wait'});await settled();assert.equal(await page.locator('[data-fm-message-view-id]').count(),0);await audit('Actual native destination after blocked stale pager');
  await page.keyboard.press('Alt+1');await poll(()=>page.getByRole('heading',{name:'Mensagens',exact:true}).count(),'actual native Inbox return');await settled();await c.newestMessages();await settled();assert.equal((await rows())[0],observations.before[0]);await c.activate(page.getByRole('button',{name:'Show older messages',exact:true}));await poll(async()=>JSON.stringify(await rows())!==JSON.stringify(observations.before),'current native pager remains functional');await c.newestMessages();await settled();assert.equal((await rows())[0],observations.before[0]);await audit('Current native pager and original first record restored');
  checks.push('Fresh native pager still pages older/newer and restores the original newest message after refusing the stale owner');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  fs.writeFileSync(path.join(out,'native-pager-owner.json'),JSON.stringify({success:true,mvid,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,network:c.network,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));
 }catch(error){fs.writeFileSync(path.join(out,'native-pager-owner-failure.json'),JSON.stringify({success:false,error:error.stack,mvid,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));throw error;}
 finally{if(release)release();await page.unrouteAll({behavior:'wait'});await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
