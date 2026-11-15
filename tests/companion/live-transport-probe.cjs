// Read-only Chromium socket qualification alongside the sole real companion.
// Navigate only to JSON /health: never compose a second game document or replace
// the production command registry. No native actions or automatic request retries.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
(async()=>{
 const args=process.argv.slice(2),option=n=>args.find(a=>a.startsWith('--'+n+'='))?.slice(n.length+3);
 const count=Number(option('requests')||12000),fresh=args.includes('--fresh'),unmitigated=args.includes('--unmitigated');
 assert.ok(Number.isInteger(count)&&count>=100&&count<=20000);
 const club=option('club'),clubId=option('club-id');
 assert.equal(club,'FC Porto');assert.equal(clubId,'1478','Use the recorded male QA team, never a historical female checkpoint');
 const base='http://127.0.0.1:8787',edge=[process.env.LOCALAPPDATA,process.env['ProgramFiles(x86)'],process.env.ProgramFiles]
  .filter(Boolean).map(p=>path.join(p,'Microsoft','Edge','Application','msedge.exe')).find(p=>fs.existsSync(p));
 assert.ok(edge,'Require the installed Edge engine');
 const launch={channel:'msedge',executablePath:edge,headless:true};
 const config=unmitigated?{options:launch}:await require('./browser-launch-options.cjs').launchOptions(chromium,launch,['--disable-features=TcpPortRandomizationWin']);
 const browser=await chromium.launch(config.options),context=await browser.newContext(fresh?{extraHTTPHeaders:{Connection:'close'}}:{});
 const report={at:new Date().toISOString(),success:false,career:{club,clubId,gender:'male',evidence:'Recorded native Quick Start men and current exact club navigation identity'},freshConnections:fresh,unmitigatedDiagnostic:unmitigated,requests:0,connections:0,reused:0,errors:[],requestFailures:[],networkFailures:[],nativeCommandsSubmitted:0,secondGameDocument:false,systemSettingsChanged:false,manualNVDA:false};
 const connections=new Set();let page;
 try{
  // HEAD sets only the existing session cookie, never BeginBody.
  const handshake=await context.request.head(base+'/');assert.equal(handshake.status(),200);
  const domResponse=await context.request.get(base+'/rendered');assert.equal(domResponse.status(),200);
  const dom=await domResponse.text();assert.ok(dom.includes('data-obj="'+clubId+'"'));assert.ok(dom.includes('>'+club+'<'));
  page=await context.newPage();await page.goto(base+'/health');
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('requestfailed',r=>report.requestFailures.push({path:new URL(r.url()).pathname,error:r.failure()?.errorText}));
  const session=await context.newCDPSession(page);
  session.on('Network.responseReceived',e=>{if(new URL(e.response.url).pathname==='/health'){connections.add(e.response.connectionId);report.requests++;if(e.response.connectionReused)report.reused++;}});
  session.on('Network.loadingFailed',e=>report.networkFailures.push({error:e.errorText,canceled:e.canceled}));
  await session.send('Network.enable');
  report.browser=await(await browser.newBrowserCDPSession()).send('Browser.getVersion');
  const started=Date.now();
  const result=await page.evaluate(async count=>{let pid,core,maxMs=0;for(let i=0;i<count;i++){
   const start=performance.now(),r=await fetch('/health',{cache:'no-store',signal:AbortSignal.timeout(12000)});
   if(!r.ok)throw Error('Health HTTP '+r.status);const h=await r.json();if(h.screen!=='Live')throw Error('Native surface '+h.screen);
   if(i===0){pid=h.pid;core=h.coreMvid;}else if(pid!==h.pid||core!==h.coreMvid)throw Error('Runtime changed during read-only qualification');
   maxMs=Math.max(maxMs,performance.now()-start);
  }return{pid,core,maxMs};},count);
  report.elapsedMs=Date.now()-started;Object.assign(report,result);report.connections=connections.size;
  assert.equal(report.requests,count);assert.deepEqual(report.errors,[]);assert.deepEqual(report.requestFailures,[]);assert.deepEqual(report.networkFailures,[]);
  if(fresh){assert.equal(report.connections,count,'Actually exercise a fresh TCP connection per request');assert.equal(report.reused,0);}
  else{assert.ok(report.reused>count*.95);assert.ok(report.connections<=Math.ceil(count/256)+6);}
  report.success=true;
 }catch(error){report.failure=error.stack;throw error;}
 finally{
  report.connections=connections.size;
  const output=path.resolve(__dirname,'../../artifacts/male-transport-'+(fresh?'fresh':'persistent')+(unmitigated?'-unmitigated':'')+'.json');
  fs.writeFileSync(output,JSON.stringify(report,null,2));await browser.close();
  console.log(JSON.stringify({success:report.success,requests:report.requests,connections:report.connections,reused:report.reused,elapsedMs:report.elapsedMs,report:output}));
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
