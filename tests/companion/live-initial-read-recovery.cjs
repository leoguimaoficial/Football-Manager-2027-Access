// Controlled initial document from the ACTUAL production failure builder;
// recovery uses the REAL loopback/SSE/native body. No native exception, method
// patch, alternative server, invented game data or native command is injected.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const {chromium}=require('playwright'),{default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 assert.ok(process.env.FM27_COMPANION_CDP,'Use the sole owned production companion');
 const browser=await chromium.connectOverCDP(process.env.FM27_COMPANION_CDP),context=browser.contexts()[0],root='http://127.0.0.1:8787/',origin=root.slice(0,-1),page=context.pages().find(p=>p.url().startsWith(root));assert.ok(page);
 const out=path.resolve(__dirname,'../../artifacts/tests/web/initial-read-recovery.json'),checks=[],audits=[],errors=[],requestFailures=[],requests=[],navigations=[];
 let mvid,expected,initial=null,failure=null,cleanupError=null,served=0,handler;
 const matcher=url=>url.href===root;
 async function evalNative(code){const r=await context.request.post(root+'eval',{data:code,headers:{Origin:origin}});assert.equal(r.status(),200);const text=await r.text();assert.doesNotMatch(text,/\[compile\]|\[exception\]/);return text;}
 try{
  const health=await(await context.request.get(root+'health')).json();mvid=health.coreMvid;assert.equal(health.screen,'Live');
  expected=await page.evaluate(()=>({title:document.title,scene:FM.core.screenIdentity(FMTOKEN),language:document.documentElement.lang,dropdown:!!FM.core.dd||!!FM.dropdown.pending}));assert.equal(expected.dropdown,false);
  const code='foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'"){var f=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;var method=a.GetType("FM27Access.Plugin.Server.AccessibilityServer").GetMethod("InitialFailureDocument",f);if(method==null)throw new System.Exception("Actual failure builder unavailable");var html=(string)method.Invoke(null,new object[]{"Controlled initial-document component check"});System.Console.WriteLine("HTML|"+System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(html)));}';
  const raw=await evalNative(code),encoded=raw.match(/^HTML\|([^\r\n]+)/m);assert.ok(encoded,'Actual production failure document returned');const html=Buffer.from(encoded[1],'base64').toString('utf8');assert.ok(html.startsWith('<!doctype html>'));
  initial={length:html.length,sha256:crypto.createHash('sha256').update(html).digest('hex'),productionBuilder:true};
  // End the previous actual document before observing this controlled initial
  // response. Only that response is substituted; /events and /body stay real.
  await page.goto('about:blank');await page.addInitScript(()=>{window.qaInitialSources=0;window.qaInitialOpens=0;window.qaInitialErrors=0;const E=window.EventSource;window.EventSource=class extends E{constructor(...a){super(...a);qaInitialSources++;this.addEventListener('open',()=>qaInitialOpens++);this.addEventListener('error',()=>qaInitialErrors++);}};});
  page.on('pageerror',e=>errors.push(e.stack||e.message));page.on('requestfailed',r=>requestFailures.push({path:new URL(r.url()).pathname,error:r.failure()?.errorText}));page.on('request',r=>requests.push({path:new URL(r.url()).pathname,method:r.method()}));
  page.on('framenavigated',frame=>{if(frame===page.mainFrame())navigations.push(frame.url());});
  handler=async route=>{served++;assert.equal(served,1,'One controlled initial response only');await route.fulfill({status:200,contentType:'text/html; charset=utf-8',body:html});};await page.route(matcher,handler);
  await page.goto(root,{waitUntil:'domcontentloaded'});await page.unroute(matcher,handler);handler=null;
  await page.waitForFunction(expected=>typeof FM!=='undefined'&&document.getElementById('fmroot')&&document.title===expected.title&&FM.core.screenIdentity(FMTOKEN)===expected.scene,expected,{timeout:20000});
  const actual=await page.evaluate(()=>({title:document.title,scene:FM.core.screenIdentity(FMTOKEN),language:document.documentElement.lang,sources:qaInitialSources,opens:qaInitialOpens,errors:qaInitialErrors}));assert.equal(actual.language,expected.language);assert.deepEqual({sources:actual.sources,opens:actual.opens,errors:actual.errors},{sources:1,opens:1,errors:0});
  assert.ok(requests.some(r=>r.path==='/events'));assert.ok(requests.some(r=>r.path==='/body'));checks.push('Actual production failure document starts the original client and recovers the unchanged real native screen through real SSE/body');
  const readOnlyRequests=new Set(['/ GET','/events GET','/body GET','/sound GET','/soundsettings GET','/rendered/report POST']);
  assert.deepEqual(requests.filter(r=>!readOnlyRequests.has(r.path+' '+r.method)),[],'Initial recovery must not dispatch or replay a native command');
  assert.deepEqual(navigations,[root],'Recovery uses the same document without a hidden browser reload');
  const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name:'Real native screen recovered from controlled production failure document',violations:a.violations.map(x=>x.id)});assert.deepEqual(a.violations,[]);assert.deepEqual(errors,[]);assert.deepEqual(requestFailures,[]);checks.push('Recovered native semantics/language and sole SSE are intact without a page reload or game command');
  assert.equal((await(await context.request.get(root+'health')).json()).coreMvid,mvid);
 }catch(error){failure=error.stack;throw error;}
 finally{
  const observed=await page.evaluate(()=>({title:document.title,language:document.documentElement.lang,text:document.body?.innerText.slice(0,1200),hasCore:typeof FM!=='undefined',hasRoot:!!document.getElementById('fmroot'),sources:window.qaInitialSources,opens:window.qaInitialOpens,errors:window.qaInitialErrors})).catch(e=>({error:e.message}));
  const execution={errors:errors.slice(),requestFailures:requestFailures.slice(),requests:requests.slice(),navigations:navigations.slice()};
  try{if(handler)await page.unroute(matcher,handler);await page.goto('about:blank');await page.goto(root,{waitUntil:'domcontentloaded'});await page.waitForFunction(()=>typeof FM!=='undefined'&&document.getElementById('fmroot'),null,{timeout:20000});}catch(error){cleanupError=error.stack;process.exitCode=1;}
  fs.writeFileSync(out,JSON.stringify({success:!failure&&!cleanupError,mvid,expected,initial,observed,served,checks,audits,...execution,failure,cleanupError,controlledInitialDocument:true,nativeExceptionInjected:false,nativeMethodsPatched:false,realBackendRecovery:!failure,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));await browser.close();
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
