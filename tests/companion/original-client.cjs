const{chromium}=require('playwright');
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path');
module.exports=async(options={})=>{
 // Windows can retain a removed per-user Edge registration while the working
 // system installation remains available. Prefer an existing installed binary.
 // A diagnostic worker may omit ProgramFiles entirely. Its caller can supply
 // an independently discovered installed executable without changing global env.
 const edge=options.browserExecutable||[process.env.LOCALAPPDATA,process.env['ProgramFiles(x86)'],process.env.ProgramFiles]
  .filter(Boolean).map(root=>path.join(root,'Microsoft','Edge','Application','msedge.exe')).find(file=>fs.existsSync(file));
 // Optional socket diagnostics for spontaneous pre-HTTP failures. Chromium's
 // Default mode strips cookies/auth and excludes socket bytes. Never enable
 // IncludeSensitive/Everything or attach this to the user's browser profile.
 const netLogPath=options.netLogPath||process.env.FM27_NETLOG_PATH;
 if(netLogPath)fs.mkdirSync(path.dirname(path.resolve(netLogPath)),{recursive:true});
 // Qualification can attach to the sole production WebView2 launched by Host.
 // Never create a second context/page or attach to a non-loopback debug target.
 const cdpEndpoint=options.cdpEndpoint||process.env.FM27_COMPANION_CDP;
 if(cdpEndpoint){const u=new URL(cdpEndpoint);assert.equal(u.protocol,'http:');assert.equal(u.hostname,'127.0.0.1');}
 // Match the production WebView's local Windows loopback policy. A default
 // auxiliary Edge process otherwise reintroduces the already mitigated port
 // allocation path and is not transport qualification of that player profile.
 // The explicit opt-out exists only for read-only diagnostic comparisons.
 const browserArguments=options.unmitigatedLoopback?[]:['--disable-features=TcpPortRandomizationWin'];
 if(netLogPath)browserArguments.push('--log-net-log='+path.resolve(netLogPath),'--net-log-capture-mode=Default');
 const baseLaunch={channel:'msedge',headless:true,...(edge?{executablePath:edge}:{})};
 const launch=cdpEndpoint?null:options.unmitigatedLoopback?{options:{...baseLaunch,args:browserArguments}}:
  await require('./browser-launch-options.cjs').launchOptions(chromium,baseLaunch,browserArguments);
 const browser=cdpEndpoint?await chromium.connectOverCDP(cdpEndpoint):await chromium.launch(launch.options);
 const context=cdpEndpoint?browser.contexts()[0]:await browser.newContext();
 let page;
 try{
  if(cdpEndpoint){
   page=context.pages().find(p=>p.url().startsWith('http://127.0.0.1:8787/'));
   // CDP becomes available before WebView2's initial Navigate(target). Wait
   // on that native navigation rather than racing it or creating another page.
   if(!page){const candidates=context.pages();if(candidates.length===1&&candidates[0].url()==='about:blank'){
    page=candidates[0];await page.waitForURL(u=>u.href.startsWith('http://127.0.0.1:8787/'),{timeout:20000});
   }}
  }else page=await context.newPage();
  assert.ok(page,'The original production companion page must already be open.');
 }catch(error){await browser.close();throw error;}
 // End the preceding test's document before observing this test's fresh load.
 // Otherwise its still-running SSE/body reads can start while goto waits for
 // the next native entry page, then be canceled by the intentional navigation.
 // All failures from the new actual game document remain observed and strict.
 if(cdpEndpoint)await page.goto('about:blank');
 const errors=[],requestFailures=[];page.on('pageerror',e=>errors.push(e.stack||e.message));
 const startedRequests=new WeakMap();let requestSequence=0;
 page.on('requestfailed',r=>{const started=startedRequests.get(r),failedAt=Date.now();requestFailures.push({path:new URL(r.url()).pathname,method:r.method(),error:r.failure()?.errorText,timing:r.timing(),request:started,elapsedMs:started?failedAt-started.time:null,failedAt,recentRequests:requests.slice(-12)});});
 const requests=[];page.on('request',r=>{const started={id:++requestSequence,path:new URL(r.url()).pathname,method:r.method(),time:Date.now()};startedRequests.set(r,started);requests.push(started);if(requests.length>80)requests.shift();});
 const network={enabled:!!options.networkDiagnostics,failures:[],responses:[],preAttachmentCancellations:[]};
 if(options.networkDiagnostics){
  const session=await context.newCDPSession(page),pending=new Map();
  session.on('Network.requestWillBeSent',e=>pending.set(e.requestId,{path:new URL(e.request.url).pathname,method:e.request.method,startedUtcMs:e.wallTime*1000,stamp:e.timestamp}));
  session.on('Network.responseReceived',e=>{
   const r=e.response;network.responses.push({request:pending.get(e.requestId),status:r.status,connectionId:r.connectionId,connectionReused:r.connectionReused,remoteIP:r.remoteIPAddress,remotePort:r.remotePort,timing:r.timing});
   if(network.responses.length>100)network.responses.shift();
  });
  session.on('Network.loadingFailed',e=>{const request=pending.get(e.requestId),failure={request,error:e.errorText,canceled:e.canceled,blockedReason:e.blockedReason,elapsedMs:request?(e.timestamp-request.stamp)*1000:null};
   // Reloading an attached production page cancels its pre-existing SSE. Its
   // request started before this observer. Retain that evidence separately;
   // all observed requests and every other transport failure stay strict.
   (cdpEndpoint&&!request&&e.canceled&&e.errorText==='net::ERR_ABORTED'?network.preAttachmentCancellations:network.failures).push(failure);pending.delete(e.requestId);});
  session.on('Network.loadingFinished',e=>pending.delete(e.requestId));
  await session.send('Network.enable');
 }
 await page.addInitScript(()=>{
  window.qaInputTrace=[];
  function record(kind,e){qaInputTrace.push({kind,time:Date.now(),id:e.target?.id,role:e.target?.getAttribute?.('role'),tag:e.target?.tagName,key:e.key});if(qaInputTrace.length>100)qaInputTrace.shift();}
  document.addEventListener('focusin',e=>record('focus',e),true);
  document.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' ')record('key',e);},true);
 });
 page.setDefaultTimeout(15000);
 if(options.initScript)await page.addInitScript(options.initScript);
 try{await page.goto('http://127.0.0.1:8787/');}catch(error){await browser.close();throw error;}
 async function poll(read,label){const until=Date.now()+20000;do{const value=await read();if(value)return value;await new Promise(r=>setTimeout(r,150));}while(Date.now()<until);throw Error('Timeout: '+label);}
 async function settled(){let last='',hits=0;await poll(async()=>{const s=await page.locator('#fmroot').innerHTML();if(s===last)hits++;else{hits=0;last=s;}return hits>=4&&await page.evaluate(()=>!FM.core.refreshing&&!FM.core.pending);},'stable original body');}
 async function fetchText(url){return page.evaluate(async u=>{const r=await fetch(u);if(!r.ok)throw Error('HTTP '+r.status+': '+u);return r.text();},url);}
 async function transportTrace(){return page.evaluate(async()=>{
  const ac=new AbortController(),timer=setTimeout(()=>ac.abort(),3000);
  try{return await(await fetch('/httptrace',{signal:ac.signal})).json();}catch(error){return{diagnosticError:error.message};}finally{clearTimeout(timer);}
 }).catch(error=>({diagnosticError:error.message}));}
 async function activate(control,key='Enter'){
  await settled();assert.equal(await control.isEnabled(),true,'Native control is disabled');await control.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');
  await page.keyboard.press(key);const r=await response;assert.equal(r.status(),200);const body=await r.text();
  assert.ok(!/unavailable|not found|stale|disabled|failed/i.test(body),body);await settled();return body;
 }
 async function dropdown(name){await settled();const control=page.getByRole('combobox',{name,exact:true});await control.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');await page.keyboard.press('Enter');const r=await response;assert.equal(r.status(),200);const body=await r.text();assert.ok(body.startsWith('<'),body);await poll(()=>control.getAttribute('data-loaded'),'original options');return control;}
 async function choose(label){const control=page.locator('select[data-dd][data-loaded]').last();await control.selectOption({label});const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/select');await page.keyboard.press('Enter');const r=await response;assert.equal(r.status(),200);const body=await r.text();assert.match(body,/ (?:selected|checked|unchecked)$/,'Native dropdown acknowledgement: '+body);await poll(()=>page.evaluate(()=>!FM.core.dd&&!FM.dropdown.pending),'completed original commit');await settled();return body;}
 async function cancelDropdown(){const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/close');await page.keyboard.press('Escape');assert.equal((await response).status(),200);await poll(()=>page.evaluate(()=>!FM.core.dd&&!FM.dropdown.pending),'closed original popup');await settled();}
 // A cold native boot legitimately serves the inherited waiting document before
 // the interactive root exists. Observe its recovery instead of applying the
 // normal 15s action timeout to the game's initial loading sequence.
 try{await page.locator('#fmroot').waitFor({state:'attached',timeout:120000});if(!options.observeUnsettled)await settled();}catch(error){await browser.close();throw error;}
 async function newestMessages(){
  const seen=new Set(),windows=[];
  const ids=()=>page.locator('tr[data-fm-message-id]').evaluateAll(rs=>rs.map(r=>r.dataset.fmMessageId).join(','));
  while(await page.getByRole('button',{name:'Show newer messages',exact:true}).count()){
   const before=await ids();assert.ok(!seen.has(before),'Native newer-message paging must progress');seen.add(before);
   await activate(page.getByRole('button',{name:'Show newer messages',exact:true}));
   // Overscan can retain the same rows while the last page-up reaches the real
   // native scroll start. The paging control's disappearance is the observed
   // endpoint in that case; a different ID window is not required.
   await poll(async()=>await ids()!==before||await page.getByRole('button',{name:'Show newer messages',exact:true}).count()===0,'actual newer message window or native start');windows.push({before,after:await ids()});
  }
  return windows;
 }
 async function visibleEmptyTexts(){
  // Use the actual localized native result, not a translation inferred from
  // its tab caption (e.g. Considerável legitimately says "risco ... aumentado").
  const native=await fetchText('/gui');
  return[...new Set([...native.matchAll(/SIText name="EmptyText" visible=True text="([^"\r\n]+)"/g)].map(m=>m[1]))];
 }
 return{browser,page,browserLaunchProfile:launch?.profile??null,errors,requestFailures,requests,requestInfo:r=>startedRequests.get(r),network,transportTrace,poll,settled,fetchText,activate,dropdown,choose,cancelDropdown,newestMessages,visibleEmptyTexts};
};
