const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const {test}=require('node:test'),assert=require('node:assert/strict');
const source=fs.readFileSync(path.join(__dirname,'../../FM27Access.Navigation/SurfaceStartupHtml.cs'),'utf8');
const script=source.match(/RecoveryScript = @"([\s\S]*?)";/)[1].replace(/""/g,'"');
async function run(response){
 const timers=new Map(),requests=[];let serial=0,reloads=0;
 const context={AbortController,Error,setTimeout(fn,ms){timers.set(++serial,{fn,ms});return serial;},clearTimeout(id){timers.delete(id);},location:{reload(){reloads++;}},fetch:async(url,options)=>{requests.push({url,options});if(response instanceof Error)throw response;return response;}};
 vm.runInNewContext(script,context);await new Promise(resolve=>setImmediate(resolve));
 return{timers,requests,reloads};
}
test('startup reloads once after observed Live health, without a native command',async()=>{
 const r=await run({ok:true,json:async()=>({screen:'Live'})});
 assert.equal(r.reloads,1);assert.equal(r.timers.size,0);assert.equal(r.requests.length,1);
 assert.equal(r.requests[0].url,'/health');assert.equal(r.requests[0].options.cache,'no-store');
});
for(const [name,response] of [['loading',{ok:true,json:async()=>({screen:'Loading'})}],['stalled',{ok:true,json:async()=>({screen:'GameNotResponding'})}],['HTTP error',{ok:false}],['connection failure',new Error('offline')],['invalid JSON',{ok:true,json:async()=>{throw Error('invalid JSON');}}]]){
 test('startup retains content and schedules one recovery after '+name,async()=>{
  const r=await run(response);assert.equal(r.reloads,0);assert.equal(r.requests.length,1);
  assert.equal(r.timers.size,1);assert.equal([...r.timers.values()][0].ms,1500);
 });
}
test('a stalled health request is bounded and cannot overlap another poll',async()=>{
 const timers=new Map();let id=0,signal,requests=0;
 vm.runInNewContext(script,{AbortController,Error,setTimeout(fn,ms){timers.set(++id,{fn,ms});return id;},clearTimeout(i){timers.delete(i);},location:{reload(){assert.fail('Must not reload');}},fetch(url,options){requests++;signal=options.signal;return new Promise((resolve,reject)=>signal.addEventListener('abort',()=>reject(Error('aborted'))));}});
 assert.equal(requests,1);assert.equal([...timers.values()][0].ms,3000);
 [...timers.values()][0].fn();await new Promise(resolve=>setImmediate(resolve));
 assert.equal(signal.aborted,true);assert.equal(requests,1);assert.equal(timers.size,1);assert.equal([...timers.values()][0].ms,1500);
});
