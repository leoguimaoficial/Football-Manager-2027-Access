// Coordination tests of the production guard, independent of native-game qualification.
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const test=require('node:test'),assert=require('node:assert/strict');
const source=fs.readFileSync(path.resolve(__dirname,'../../FM27Access.Plugin/Server/app.js'),'utf8');
const start=source.indexOf('FM.core = {'),end=source.indexOf('\n};',start);
function create(){
 const context={FM:{},FMTOKEN:'Squad'};
 vm.runInNewContext(source.slice(start,end+3),context);
 const core=context.FM.core;
 const guard=core.toggles.row={pending:1,seq:0,owner:'Squad'};
 return{core,guard,context};
}
function checkbox(checked,at=0){
 const attributes={'data-fm-value-at':String(at)};
 return{id:'row',tagName:'INPUT',type:'checkbox',checked,
  getAttribute:name=>attributes[name]||null,setAttribute:(name,value)=>attributes[name]=String(value),
  toggleAttribute(name,on){if(on)attributes[name]='';else delete attributes[name];}};
}
test('a body completed before an activation reply cannot untick the optimistic checkbox',()=>{
 const s=create(),previous=checkbox(true),next=checkbox(false);
 s.core.preserveCheckedValue(previous,next,1);assert.equal(next.checked,true);
 s.core.fetchSeq=2;s.core.answerToggle('row');
 s.core.preserveCheckedValue(previous,next,2);assert.equal(next.checked,true);
 s.core.retireToggles(2);assert.ok(s.core.toggles.row);
});
test('the first fresh body accepts a native refusal and retires the guard',()=>{
 const s=create();s.core.fetchSeq=2;s.core.answerToggle('row');
 const next=checkbox(false);s.core.preserveCheckedValue(checkbox(true),next,3);
 assert.equal(next.checked,false);s.core.retireToggles(3);assert.equal(s.core.toggles.row,undefined);
});
test('two pending presses remain guarded until both replies arrive',()=>{
 const s=create();s.guard.pending=2;s.core.answerToggle('row');
 assert.equal(s.core.toggleGuarded('row',9),true);
 s.core.fetchSeq=10;s.core.answerToggle('row');assert.equal(s.core.toggleGuarded('row',10),true);
 assert.equal(s.core.toggleGuarded('row',11),false);
});
test('a reused DOM id on another screen cannot inherit or release an old guard',()=>{
 const s=create();s.context.FMTOKEN='Database';assert.equal(s.core.toggleGuarded('row',1),false);
 const current=s.core.toggles.row={pending:1,seq:0,owner:'Database'};
 s.core.answerToggle('row',s.guard);assert.equal(current.pending,1);
 s.context.FMTOKEN='Other';s.core.retireToggles(1);assert.equal(s.core.toggles.row,undefined);
});
test('native value timestamps continue to protect a newer checked observation',()=>{
 const s=create();s.core.toggles={};const next=checkbox(false,10);
 s.core.preserveCheckedValue(checkbox(true,11),next,20);
 assert.equal(next.checked,true);assert.equal(next.getAttribute('data-fm-value-at'),'11');
});
test('a replacement body from another native owner never inherits the outgoing optimistic tick',()=>{
 const s=create(),next=checkbox(false);
 s.core.preserveCheckedValue(checkbox(true),next,1,'Database');
 assert.equal(next.checked,false);assert.equal(s.core.toggleGuarded('row',1,'Database'),false);
});
