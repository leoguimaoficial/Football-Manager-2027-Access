// Actual production listener; read-only routes and deliberately incomplete commands.
// No fixture server, cookies in output, native mutation or automatic retries.
const net=require('node:net'),fs=require('node:fs'),assert=require('node:assert/strict');
function exchange(text,endWrite=false,timeout=5000){return new Promise((resolve,reject)=>{
 const socket=net.createConnection({host:'127.0.0.1',port:8787}),parts=[];
 socket.setTimeout(timeout,()=>socket.destroy(Error('Raw protocol timeout')));
 socket.on('connect',()=>endWrite?socket.end(text):socket.write(text));
 socket.on('data',b=>parts.push(b));socket.on('error',reject);socket.on('end',()=>resolve(Buffer.concat(parts)));
});}
function responses(bytes,headIndexes=[]){const result=[];let at=0;while(at<bytes.length){const end=bytes.indexOf('\r\n\r\n',at);assert.ok(end>=at,'complete response headers');const header=bytes.subarray(at,end).toString('ascii'),length=+header.match(/\r\nContent-Length: (\d+)/i)[1],status=+header.match(/^HTTP\/1\.1 (\d+)/)[1];at=end+4;const count=headIndexes.includes(result.length)?0:length;assert.ok(at+count<=bytes.length,'complete response body');result.push({status,body:bytes.subarray(at,at+count).toString('utf8')});at+=count;}return result;}
(async()=>{
 const session=await fetch('http://127.0.0.1:8787/',{method:'HEAD'});assert.equal(session.status,200);
 const cookie=session.headers.get('set-cookie').split(';')[0],health=await(await fetch('http://127.0.0.1:8787/health')).json();
 const header='Host: 127.0.0.1:8787\r\nCookie: '+cookie+'\r\n',checks=[];
 let r=responses(await exchange('GET /health HTTP/1.1\r\n'+header+'\r\nHEAD /does-not-exist HTTP/1.1\r\n'+header+'\r\nGET /health HTTP/1.1\r\n'+header+'Connection: close\r\n\r\n'),[1]);
 assert.deepEqual(r.map(r=>r.status),[200,404,200]);assert.equal(JSON.parse(r[0].body).coreMvid,health.coreMvid);assert.equal(JSON.parse(r[2].body).coreMvid,health.coreMvid);checks.push('Pipelined GET/HEAD404/GET remain separate, ordered complete responses on one actual socket');
 r=responses(await exchange('GET /version HTTP/1.1\r\nHost: 127.0.0.1:8787\r\n\r\nGET /health HTTP/1.1\r\n'+header+'Connection: close\r\n\r\n'));
 assert.deepEqual(r.map(r=>r.status),[403,200]);checks.push('Each reused request revalidates its own authorization; a refused request cannot inherit or poison the next session');
 r=responses(await exchange('HEAD /events HTTP/1.1\r\n'+header+'Connection: close\r\n\r\n'),[0]);
 assert.deepEqual(r.map(r=>r.status),[405]);checks.push('HEAD cannot hand its socket to an unbounded SSE stream or emit a body');
 r=responses(await exchange('GET /health HTTP/1.1\r\n'+header+'\r\nPOST /activate?id=never-dispatched HTTP/1.1\r\n'+header+'Content-Length: 10\r\n\r\nshort',true));
 assert.deepEqual(r.map(r=>r.status),[200,400]);assert.match(r[1].body,/Incomplete request body/);checks.push('An incomplete POST after a successful request is refused400 before native command dispatch');
 const body='ação 日本語',frame='POST /health HTTP/1.1\r\n'+header+'Content-Length: '+Buffer.byteLength(body)+'\r\n\r\n'+body+'GET /health HTTP/1.1\r\n'+header+'Connection: close\r\n\r\n';
 r=responses(await exchange(frame));assert.deepEqual(r.map(r=>r.status),[200,200]);checks.push('UTF-8 byte length ends before the following request, without swallowing or executing its prefix');
 r=responses(await exchange(('GET /health HTTP/1.1\r\n'+header+'\r\n').repeat(256)));assert.equal(r.length,256);assert.ok(r.every(r=>r.status===200));checks.push('One actual connection serves256complete ordered requests then closes at its advertised bound');
 const idleStarted=Date.now();r=responses(await exchange('GET /health HTTP/1.1\r\n'+header+'\r\n',false,20000));const idleMs=Date.now()-idleStarted;
 assert.deepEqual(r.map(r=>r.status),[200]);assert.ok(idleMs>=14000&&idleMs<20000,'Idle connection closes after the15second bound');checks.push('An idle pooled connection closes after the bounded15second wait without requiring a new request');
 const result={at:new Date().toISOString(),success:true,core:health.coreMvid,pid:health.pid,checks,nativeCommandsExecuted:0,systemSettingsChanged:false};fs.writeFileSync('artifacts/persistent-http-live-framing.json',JSON.stringify(result,null,2));console.log(JSON.stringify(result));
})().catch(error=>{console.error(error);process.exitCode=1;});
