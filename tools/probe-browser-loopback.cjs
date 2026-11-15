// Disposable installed Edge network diagnosis against public /health only.
// Does not navigate to the companion root, consume SSE, authenticate, execute
// game commands, modify the production browser or change OS networking.
const fs=require('node:fs'),path=require('node:path'),{createRequire}=require('node:module');
const requireTests=createRequire(path.resolve(__dirname,'../tests/companion/package.json'));
const {chromium}=requireTests('playwright');
const root=path.resolve(__dirname,'..'),count=Number(process.argv[2]||1500);
if(!Number.isInteger(count)||count<6||count>4000)throw Error('Use 6..4000 readonly requests per mode');
const prefix=path.join(root,'artifacts','browser-health-policy-'+Date.now());
const edge=[process.env.LOCALAPPDATA,process.env['ProgramFiles(x86)'],process.env.ProgramFiles].filter(Boolean).map(p=>path.join(p,'Microsoft','Edge','Application','msedge.exe')).find(p=>fs.existsSync(p));
(async()=>{
 const results=[];let originalDisabled;
 for(const mode of ['NativeRandomizationDefault','OrdinaryPorts']){
  const netlog=prefix+'-'+mode+'.json',options={channel:'msedge',...(edge?{executablePath:edge}:{}),headless:true,args:['--enable-automation','--log-net-log='+netlog,'--net-log-capture-mode=Default']};
  if(originalDisabled){options.ignoreDefaultArgs=[originalDisabled];options.args.push(originalDisabled+',TcpPortRandomizationWin');}
  const browser=await chromium.launch(options);
  try{
   const session=await browser.newBrowserCDPSession(),command=await session.send('Browser.getBrowserCommandLine');
   const disabled=command.arguments.filter(a=>a.startsWith('--disable-features='));
   console.log(JSON.stringify({mode,disabledFeatures:disabled}));
   if(mode==='NativeRandomizationDefault'){originalDisabled=disabled[0];if(disabled.length!==1||originalDisabled.split('=')[1].split(',').includes('TcpPortRandomizationWin'))throw Error('Native-default phase already has a port-policy override; inspect before comparison');}
   if(mode==='OrdinaryPorts'){
    if(disabled.length!==1||!disabled[0].split('=')[1].split(',').includes('TcpPortRandomizationWin'))throw Error('Diagnostic did not apply the single intended port-policy override');
    if(disabled[0].split('=')[1].split(',').filter(f=>f!=='TcpPortRandomizationWin').join(',')!==originalDisabled.split('=')[1])throw Error('An unrelated diagnostic feature changed; refuse the comparison');
   }
   const context=await browser.newContext(),page=await context.newPage(),failures=[];
   page.on('requestfailed',r=>failures.push({utc:new Date().toISOString(),path:new URL(r.url()).pathname,error:r.failure()?.errorText}));
   await page.goto('http://127.0.0.1:8787/health',{waitUntil:'load',timeout:10000});
   const observation=await page.evaluate(async({count,mode})=>{
    const started=performance.now(),rows=[];let next=0,failed=0;
    await Promise.all(Array.from({length:6},async()=>{
     for(;;){const i=next++;if(i>=count||failed>=5||performance.now()-started>90000)break;const at=performance.now(),controller=new AbortController(),timer=setTimeout(()=>controller.abort(),6000);
      try{const response=await fetch('/health?diagnostic='+mode+'-'+i,{cache:'no-store',signal:controller.signal});const body=await response.json();if(!response.ok||body.screen!=='Live')throw Error('Health not Live/200');rows.push({i,ok:true,ms:performance.now()-at});}
      catch(error){failed++;rows.push({i,ok:false,ms:performance.now()-at,error:error.message});}finally{clearTimeout(timer);}
     }
    }));return{requested:count,completed:rows.length,failed,elapsedMs:performance.now()-started,slow:rows.filter(r=>r.ms>=500),failures:rows.filter(r=>!r.ok)};
   },{count,mode});
   results.push({mode,version:browser.version(),disabledFeatures:disabled,netlog,...observation,requestFailures:failures});
   console.log(JSON.stringify({mode,completed:observation.completed,failed:observation.failed,networkFailures:failures.length,elapsedMs:Math.round(observation.elapsedMs)}));
  }finally{await browser.close();}
  fs.writeFileSync(prefix+'-report.json',JSON.stringify({at:new Date().toISOString(),target:'Public production /health only',nativeCommandsSubmitted:false,productionClientChanged:false,systemSettingsChanged:false,comparison:'Installed Edge, same Playwright options except its TcpPortRandomizationWin override; not full actual-WebView2 parity',results},null,2),{flag:results.length===1?'wx':'w'});
 }
 console.log('Evidence: '+prefix+'-report.json');
})().catch(error=>{console.error(error);process.exitCode=1;});
