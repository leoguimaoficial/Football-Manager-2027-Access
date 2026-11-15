const {test}=require('node:test'),assert=require('node:assert/strict'),{build,key}=require('./shared-navigation-reference.cjs');
const health={coreMvid:'same-module',pid:42},checkpoint='protected-source';
const field=()=>({id:'global-menu',name:'Native caption',options:[{value:'0',text:'Actual folder',folder:true}],folders:[{folder:'Actual folder',depth:1,options:[{value:'0',text:'Actual destination',folder:false}]}],status:'Native open/cancel and nested focus validated'});
const route=(uid,control=field())=>({destination:{uid,obj:null},status:'Ordinary native dropdown opening/cancellation checked; leaf and searchable behavior not covered',controls:[control]});
const reference=routes=>({health,checkpoint,routes,businessLeafRequests:0,errors:[],requestFailures:[],network:{failures:[]}});
test('Two distinct completed native contexts qualify one explicitly referenced shared tree',()=>{
 const cache=build(reference([route('one'),route('two')]),health,checkpoint);assert.equal(cache.size,1);assert.equal(cache.get(key(field(),field().options)).folders,1);
 assert.equal(cache.has(key({...field(),context:'another-record'},field().options)),false);assert.equal(cache.has(key(field(),[{value:'0',text:'Changed folder',folder:true}])),false);
});
test('A repeated context or differing descendant tree cannot qualify component reuse',()=>{
 assert.equal(build(reference([route('one'),route('one')]),health,checkpoint).size,0);
 const changed=field();changed.folders[0].options[0].text='Another native destination';assert.equal(build(reference([route('one'),route('two',changed)]),health,checkpoint).size,0);
});
test('Partial controls and changed runtime/checkpoint are never accepted as shared-tree proof',()=>{
 const incomplete=field();incomplete.status='In analysis';assert.equal(build(reference([route('one'),route('two',incomplete)]),health,checkpoint).size,0);
 assert.throws(()=>build(reference([route('one'),route('two')]),{...health,pid:99},checkpoint));assert.throws(()=>build(reference([route('one'),route('two')]),health,'another-save'));
});
