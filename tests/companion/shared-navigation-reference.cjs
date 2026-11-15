const assert=require('node:assert/strict'),crypto=require('node:crypto');
const hash=value=>crypto.createHash('sha256').update(JSON.stringify(value)).digest('hex');
const key=(field,options)=>JSON.stringify({id:field.id,name:field.name,context:field.context??null,options});
// Optional component-level reuse, never a claim that descendant states were
// re-read on every screen. Require two complete identical native trees from
// different destinations in the same process/module/checkpoint. Ordinary
// contextual/content controls are excluded by the caller's native HTML region.
function build(reference,health,checkpoint){
 assert.equal(reference.health.coreMvid,health.coreMvid);assert.equal(reference.health.pid,health.pid);assert.equal(reference.checkpoint,checkpoint);assert.equal(reference.businessLeafRequests,0);
 for(const values of [reference.errors,reference.requestFailures,reference.network.failures])assert.deepEqual(values,[]);
 const candidates=new Map();
 for(const route of reference.routes){
  if(!route.status?.startsWith('Ordinary native dropdown opening/cancellation checked'))continue;
  const destination=route.destination.uid+'|'+route.destination.obj;
  for(const field of route.controls){
   if(field.status!=='Native open/cancel and nested focus validated'||!field.folders?.length||!field.options?.length)continue;
   const k=key(field,field.options),treeSha256=hash({options:field.options,folders:field.folders}),entry=candidates.get(k);
   if(!entry)candidates.set(k,{treeSha256,folders:field.folders.length,contexts:new Set([destination]),coherent:true});
   else{entry.coherent&&=entry.treeSha256===treeSha256;entry.contexts.add(destination);}
  }
 }
 return new Map([...candidates].filter(([,c])=>c.coherent&&c.contexts.size>=2).map(([k,c])=>[k,{treeSha256:c.treeSha256,folders:c.folders,contexts:[...c.contexts]}]));
}
module.exports={build,key};
