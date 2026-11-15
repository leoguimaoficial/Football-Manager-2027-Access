// Production loopback framing; diagnostic Console.WriteLine only, no game edits.
const fs=require('node:fs'),path=require('node:path'),net=require('node:net'),assert=require('node:assert/strict');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),checks=[],observations={};
 const marker='QA_COMPLETE_FRAME_'+Date.now();
 const cookie=(await c.page.context().cookies()).filter(x=>x.domain==='127.0.0.1').map(x=>x.name+'='+x.value).join('; ');
 async function send(headers,body=Buffer.alloc(0),completeHeaders=true){
  return new Promise((resolve,reject)=>{
   let port;const chunks=[];
   const socket=net.createConnection({host:'127.0.0.1',port:8787},()=>{
    port=socket.localPort;
    socket.write('POST /eval HTTP/1.1\r\nHost: 127.0.0.1:8787\r\nCookie: '+cookie+'\r\n'+headers+(completeHeaders?'\r\n\r\n':''));
    socket.end(body);
   });
   socket.setTimeout(5000,()=>socket.destroy(new Error('Production framing probe timed out; not retried')));
   socket.on('error',reject);socket.on('data',b=>chunks.push(b));
   socket.on('end',()=>{const raw=Buffer.concat(chunks).toString('utf8'),boundary=raw.indexOf('\r\n\r\n');resolve({port,status:raw.split('\r\n')[0],body:boundary<0?'':raw.slice(boundary+4)});});
  });
 }
 async function expect(name,headers,body,status,completeHeaders=true){
  const observed=await send(headers,body,completeHeaders);
  assert.match(observed.status,new RegExp('^HTTP/1.1 '+status+' '),name);
  if(status!==200)assert.ok(!observed.body.includes(marker),'Incomplete payload must not execute');
  const trace=JSON.parse(await c.fetchText('/httptrace')).filter(x=>x.PeerPort===observed.port).at(-1);
  assert.ok(trace,name+' accepted trace');
  assert.equal(trace.Phase,status===200?'response written':'invalid request');
  if(status!==200)assert.equal(trace.Route,'before headers','Rejected before a route/dispatcher could execute');
  observations[name]={...observed,trace};return observed;
 }
 try{
  observations.health=JSON.parse(await c.fetchText('/health'));
  const code=Buffer.from('System.Console.WriteLine("'+marker+'");','utf8');
  await expect('truncatedBody','Content-Length: '+(code.length+8),code,400);
  await expect('incompleteHeaders','Content-Length: '+code.length,Buffer.alloc(0),400,false);
  checks.push('Actual EOF after partial declared payload or missing header terminator is rejected before route dispatch; the valid diagnostic prefix never executes');
  await expect('conflictingLengths','Content-Length: '+code.length+'\r\nContent-Length: 0',code,400);
  await expect('ambiguousTransfer','Content-Length: '+code.length+'\r\nTransfer-Encoding: chunked',code,400);
  await expect('unsupportedTransfer','Transfer-Encoding: chunked',Buffer.from('0\r\n\r\n'),501);
  checks.push('Ambiguous lengths and unsupported transfer framing return explicit errors and never enter a native command route');
  await expect('invalidUtf8','Content-Length: 1',Buffer.from([0xc3]),400);
  const unicode=Buffer.from('System.Console.WriteLine("'+marker+' ação 日本語");','utf8');
  const valid=await expect('completeUtf8','Content-Length: '+unicode.length,unicode,200);
  assert.equal(valid.body.trim(),marker+' ação 日本語');
  checks.push('Invalid UTF-8 is rejected; the complete exact-byte-length multibyte diagnostic executes once with its exact original text');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  const report={success:true,checks,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/http-request-framing.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
 }catch(error){fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/http-request-framing-failure.json'),JSON.stringify({error:error.stack,checks,observations,errors:c.errors,requestFailures:c.requestFailures,transport:await c.transportTrace()},null,2));throw error;}
 finally{await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
