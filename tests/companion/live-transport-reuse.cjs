// Read-only real production server qualification. No command retries or game edits.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const baseline=process.argv.includes('--baseline'),arg=process.argv.find(a=>a.startsWith('--requests=')),count=arg?Number(arg.split('=')[1]):600;
 assert.ok(Number.isInteger(count)&&count>=600&&count<=20000);
 const unmitigated=process.argv.includes('--unmitigated-browser');
 const c=await require('./original-client.cjs')({networkDiagnostics:true,unmitigatedLoopback:unmitigated}),{page}=c;
 const requestedTransportArguments=unmitigated?[]:['--disable-features=TcpPortRandomizationWin'];let version;
 const session=await page.context().newCDPSession(page),connections=[];
 session.on('Network.responseReceived',e=>{if(new URL(e.response.url).pathname==='/health')connections.push({id:e.response.connectionId,reused:e.response.connectionReused,status:e.response.status});});
 await session.send('Network.enable');let success=false,failure=null,result;
 try{
  const browserSession=await c.browser.newBrowserCDPSession();version=await browserSession.send('Browser.getVersion');
  result=await page.evaluate(async count=>{const observations=[],started=performance.now();for(let i=0;i<count;i++){
   const at=performance.now();const r=await fetch('/health',{cache:'no-store'});if(!r.ok)throw Error('Health HTTP '+r.status);
   const h=await r.json();if(h.screen!=='Live')throw Error('Native health '+h.screen);
   observations.push({index:i,ms:performance.now()-at,pid:h.pid,core:h.coreMvid});
  }return{elapsedMs:performance.now()-started,observations};},count);
  assert.equal(result.observations.length,count);assert.equal(new Set(result.observations.map(r=>r.core)).size,1);assert.equal(new Set(result.observations.map(r=>r.pid)).size,1);
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  assert.equal(connections.length,count);if(!baseline){assert.ok(connections.filter(c=>c.reused).length>count*.95,'At least95% of real requests reuse their TCP connection');assert.ok(new Set(connections.map(c=>c.id)).size<=Math.ceil(count/256)+6,'The bounded persistent session avoids hundreds of new connections');}
  success=true;
 }catch(error){failure=error.stack;throw error;}
 finally{
  const summary={at:new Date().toISOString(),success,failure,baseline,browser:{product:version?.product,requestedTransportArguments,unmitigatedDiagnostic:unmitigated,argumentEvidence:'Actual owned headless process flags are inspected separately through read-only CIM; CDP command-line query is unavailable'},requests:result?.observations.length,elapsedMs:result?.elapsedMs,connections:new Set(connections.map(c=>c.id)).size,reused:connections.filter(c=>c.reused).length,core:result?.observations[0]?.core,pid:result?.observations[0]?.pid,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,nativeCommandsSubmitted:false,systemSettingsChanged:false};
  const out=path.resolve(__dirname,'../../artifacts/transport-'+(baseline?'baseline':'persistent')+(arg?'-'+count:'')+'.json');fs.writeFileSync(out,JSON.stringify(summary,null,2));console.log(JSON.stringify(summary));await c.browser.close();
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
