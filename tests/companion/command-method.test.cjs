const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const {test}=require('node:test'),assert=require('node:assert/strict');
const source=fs.readFileSync(path.join(__dirname,'../../FM27Access.Plugin/Server/app.js'),'utf8');
const body=source.slice(source.indexOf('requestText: function'),source.indexOf('\n  command: function')).replace(/^requestText:/,'requestText:').trim().replace(/,$/,'');
function client(fetch){return vm.runInNewContext('({'+body+'})',{fetch,AbortController,setTimeout,clearTimeout,FM:{core:{refreshing:false}},document:{getElementById(){return null;}}});}
test('the production command transport sends POST exactly once',async()=>{
 const calls=[];const c=client(async(u,o)=>{calls.push({u,o});return{ok:true,text:async()=>"Activated owned"};});
 assert.equal(await c.requestText('/activate?id=owned'),'Activated owned');assert.equal(calls.length,1);assert.equal(calls[0].o.method,'POST');
});
test('an uncertain failed command is never automatically replayed by the client',async()=>{
 let calls=0;const c=client(async()=>{calls++;throw Error('connection closed after dispatch');});
 await assert.rejects(c.requestText('/activate?id=owned'),/after dispatch/);assert.equal(calls,1);
});
test('explicit method and body options survive the shared command wrapper',async()=>{
 let options;const c=client(async(u,o)=>{options=o;return{ok:true,text:async()=>"ok"};});
 await c.requestText('/clientlog',{method:'POST',body:'diagnostic'});assert.equal(options.method,'POST');assert.equal(options.body,'diagnostic');
});
