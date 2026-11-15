// Game-independent coordination tests of the production FM.core functions.
// Native availability/frames are controlled inputs, never real-game evidence.
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const test=require('node:test'),assert=require('node:assert/strict');
const source=fs.readFileSync(path.resolve(__dirname,'../../FM27Access.Plugin/Server/app.js'),'utf8');
const start=source.indexOf('FM.core = {'),end=source.indexOf('\n};',start);
assert.ok(start>=0&&end>start,'Load the complete production core object');
function create(){
 let now=0,sequence=0,reads=0;const timers=new Map(),feedback=[],logs=[];
 const context={FM:{},Date:class extends Date{static now(){return now;}},setTimeout(fn,delay){const id=++sequence;timers.set(id,{fn,at:now+delay});return id;},clearTimeout(id){timers.delete(id);}};
 vm.runInNewContext(source.slice(start,end+3),context,{filename:'production-app-core.js'});
 const core=context.FM.core;core.historyIndex=8;core.refresh=()=>{reads++;core.refreshing=true;};core.say=t=>feedback.push(t);core.clog=t=>logs.push(t);
 return{context,core,timers,feedback,logs,get reads(){return reads;},advance(value){now=value;},land(index=7){core.historyIndex=index;core.historyReady=true;core.refreshing=false;const waiters=core.historyWaiters;core.historyWaiters=[];waiters.forEach(f=>f());},expire(){for(const[id,timer]of timers)if(timer.at<=now){timers.delete(id);timer.fn();}}};
}
test('a confirmed idle body dispatches exactly once from its actual index',async()=>{const s=create();s.core.historyReady=true;let sends=0;await s.core.historyCommand('back',()=>sends++);assert.equal(sends,1);assert.equal(s.core.historyMove.from,8);assert.equal(s.core.historyMove.waiting,false);assert.equal(s.reads,0);});
test('a missing index starts one read and waits for the actual confirmed body',async()=>{const s=create();let sends=0;const work=s.core.historyCommand('forward',()=>sends++);assert.equal(sends,0);assert.equal(s.reads,1);s.land(6);await work;assert.equal(sends,1);assert.equal(s.core.historyMove.from,6);assert.equal(s.timers.size,0);});
test('a body arriving after the deadline cannot dispatch before the timer task runs',async()=>{const s=create();let sends=0;const work=s.core.historyCommand('forward',()=>sends++);s.advance(25001);s.land();await work;assert.equal(sends,0);assert.equal(s.feedback.length,1);assert.equal(s.logs.length,1);assert.equal(s.core.historyMove,null);assert.equal(s.timers.size,0);});
test('an already confirmed body cannot dispatch after its queued continuation expires',async()=>{const s=create();s.core.historyReady=true;let sends=0;const work=s.core.historyCommand('back',()=>sends++);s.advance(25000);await work;assert.equal(sends,0);assert.equal(s.feedback.length,1);assert.equal(s.core.historyMove,null);});
test('new input cancels an undispatched command and its eventual feedback',async()=>{const s=create();let sends=0;const work=s.core.historyCommand('forward',()=>sends++);s.core.historyMove=null;s.land();await work;assert.equal(sends,0);assert.equal(s.feedback.length,0);assert.equal(s.timers.size,0);});
test('deadline failure removes its waiter and a late body never replays the command',async()=>{const s=create();let sends=0;const work=s.core.historyCommand('back',()=>sends++);s.advance(25000);s.expire();await work;assert.equal(sends,0);assert.equal(s.feedback.length,1);assert.equal(s.core.historyWaiters.length,0);s.land();assert.equal(sends,0);});
test('a newer pending history intent supersedes the old one without another read',async()=>{const s=create();const sends=[];const old=s.core.historyCommand('back',()=>sends.push('back')),current=s.core.historyCommand('forward',()=>sends.push('forward'));assert.equal(s.reads,1);s.land(12);await Promise.all([old,current]);assert.deepEqual(sends,['forward']);assert.equal(s.core.historyMove.from,12);assert.equal(s.feedback.length,0);assert.equal(s.timers.size,0);});

test('same-index native replacement arrives only at a different observed page owner',()=>{const s=create();const key=s.core.historyKey(8,'123#binding:1');const move={from:8,fromKey:key,direction:'back'};assert.equal(s.core.historyArrived(move,8,'123#binding:2'),false);assert.equal(s.core.historyArrived(move,8,'456#binding:3'),true);assert.equal(s.core.historyArrived({...move,waiting:true},8,'456'),false);assert.equal(s.core.historyArrived(move,null,'456'),false);});
test('same-index virtual message transitions are destinations in both directions',()=>{const s=create();for(const direction of ['back','forward']){const move={from:8,fromKey:s.core.historyKey(8,'123'),direction};assert.equal(s.core.historyArrived(move,8,'123\x1fmsgview:open'),true);assert.equal(s.core.historyArrived(move,9,'456'),direction==='forward');}});

test('a later native body can restore an exact history control after its provisional removal',()=>{const s=create(),body={},target={focus(){s.context.document.activeElement=this;}};let realized=false;s.context.FMTOKEN='123\x1fmsgview:open';s.context.document={body,activeElement:body,getElementById:()=>realized?target:null};s.core.historyFocus={key:s.core.historyKey(8,s.context.FMTOKEN),id:'native-origin'};s.core.restoreHistoryFocus();assert.equal(s.context.document.activeElement,body);realized=true;s.core.restoreHistoryFocus();assert.equal(s.context.document.activeElement,target);});
test('late history focus never replaces a newer chosen control or another native destination',()=>{const s=create(),body={},chosen={hasAttribute:()=>false};let calls=0;s.context.FMTOKEN='123';s.context.document={body,activeElement:chosen,getElementById:()=>({focus(){calls++;}})};s.core.historyFocus={key:s.core.historyKey(8,'123'),id:'origin'};s.core.restoreHistoryFocus();assert.equal(calls,0);s.context.document.activeElement=body;s.context.FMTOKEN='456';s.core.restoreHistoryFocus();assert.equal(calls,0);s.core.historyFocus=null;s.context.FMTOKEN='123';s.core.restoreHistoryFocus();assert.equal(calls,0);});

test('a completed history return cannot refocus an old control on a later neutral refresh',()=>{
 const s=create(),body={};let calls=0;
 const target={id:'origin',focus(){calls++;s.context.document.activeElement=this;}};
 s.context.FMTOKEN='123';s.context.document={body,activeElement:body,getElementById:()=>target};
 s.core.historyFocus={key:s.core.historyKey(8,'123'),id:'origin'};
 s.core.restoreHistoryFocus();assert.equal(calls,1);assert.equal(s.core.historyFocus,null);
 s.context.document.activeElement=body;s.core.restoreHistoryFocus();assert.equal(calls,1);
});
test('a history return already focused by the body commit consumes its handoff without speaking it again',()=>{
 const s=create(),target={id:'origin',focus(){throw Error('Already focused');}};
 s.context.FMTOKEN='123';s.context.document={activeElement:target,getElementById:()=>target};
 s.core.historyFocus={key:s.core.historyKey(8,'123'),id:'origin'};
 s.core.restoreHistoryFocus();assert.equal(s.core.historyFocus,null);
});
test('an unrealized or hidden history target retains its pending exact return',()=>{
 const s=create(),body={};const hidden={focus(){}};
 s.context.FMTOKEN='123';s.context.document={body,activeElement:body,getElementById:()=>hidden};
 const pending=s.core.historyFocus={key:s.core.historyKey(8,'123'),id:'origin'};
 s.core.restoreHistoryFocus();assert.equal(s.core.historyFocus,pending);
});
test('a component revealing its hidden menu leaf completes the history handoff in the same refresh',()=>{
 const s=create(),body={};let revealed=false;
 const target={id:'menu-leaf',focus(){if(revealed)s.context.document.activeElement=this;}};
 s.context.FMTOKEN='123';s.context.document={body,activeElement:body,getElementById:()=>target};
 s.core.historyFocus={key:s.core.historyKey(8,'123'),id:'menu-leaf'};
 s.core.onRefresh(()=>{revealed=true;target.focus();});s.core.onRefresh(s.core.restoreHistoryFocus);
 s.core.runAfterRefresh();assert.equal(s.context.document.activeElement,target);assert.equal(s.core.historyFocus,null);
 s.context.document.activeElement=body;s.core.runAfterRefresh=()=>s.core.restoreHistoryFocus();s.core.runAfterRefresh();assert.equal(s.context.document.activeElement,body);
 // Execute the production refresh-hook order, rather than testing a different
 // ordering that leaves the browser's actual deferred repair unobserved.
 assert.ok(source.indexOf('FM.core.onRefresh(FM.navigator.focusBox);')<source.indexOf('FM.core.onRefresh(FM.core.restoreHistoryFocus);'));
});

test('intermediate native Inbox cannot consume a message return while the own command is in flight',async()=>{const s=create();s.core.historyReady=true;let finish;const reply=new Promise(r=>finish=r),work=s.core.historyCommand('back',()=>reply);await Promise.resolve();const move=s.core.historyMove;assert.equal(move.inFlight,true);assert.equal(s.core.historyArrived(move,8,'different-page'),false);finish();await work;assert.equal(move.inFlight,false);assert.equal(s.reads,1);assert.equal(s.core.historyArrived(move,8,'different-page'),true);});
test('new input prevents a completed older history command from installing another focus handoff',async()=>{const s=create();s.core.historyReady=true;let finish;const reply=new Promise(r=>finish=r),work=s.core.historyCommand('back',()=>reply);await Promise.resolve();s.core.historyMove=null;finish();await work;assert.equal(s.reads,0);assert.equal(s.core.historyMove,null);});

test('a verified message return recovers its exact action across a smaller native slot and renewed binding',()=>{
 const s=create(),before='190#binding:1\x1f319#binding:7\x1fmsgview:open',after='190#binding:2\x1f319#binding:10\x1fmsgview:open';
 s.core.rememberHistoryFocus(2,before,'offer-action','80448');
 s.core.rememberHistoryFocus(2,'190#binding:1\x1f319#binding:7','message-row',null);
 s.core.rememberHistoryFocus(1,'190\x1f242','offer-editor',null);
 assert.equal(s.core.historyOriginPositions({index:2,token:before,message:'80449'},1),null);
 assert.equal(s.core.historyOriginPositions({index:2,token:'190\x1f555\x1fmsgview:open',message:'80448'},1),null);
 const origin=s.core.historyOriginPositions({index:2,token:before,message:'80448'},1);
 // Actual native branch truncation removes the old slot before its return.
 s.core.historyPos={};s.core.historyMessagePos={};s.core.restoreHistoryOrigin(origin);
 assert.equal(s.core.historyTarget(1,after,'<button data-fm-message-view-id="80448">'), 'offer-action');
 assert.equal(s.core.historyTarget(1,'190\x1f319',''), 'message-row');
 assert.equal(s.core.historyTarget(1,after,'<button data-fm-message-view-id="80449">'), null);
 assert.equal(s.core.historyTarget(1,'190\x1f555\x1fmsgview:open','<button data-fm-message-view-id="80448">'), undefined);
});

test('another selected message cannot inherit the action cursor of a recycled message slot',()=>{
 const s=create(),token='190\x1f319\x1fmsgview:open';
 s.core.rememberHistoryFocus(1,token,'old-action','80448');
 assert.equal(s.core.historyTarget(1,token,'<button data-fm-message-view-id="80449">'),null);
 s.core.rememberHistoryFocus(1,token,'new-action','80449');
 assert.equal(s.core.historyTarget(1,token,'<button data-fm-message-view-id="80449">'),'new-action');
});

test('a native forward arrival retains the source action through a same-template rebind before its index changes',()=>{
 const s=create(),source='190#binding:1\x1f314#binding:10',destination='190#binding:1\x1f314#binding:20';
 s.core.nativeReturn={from:4,token:source,id:'mci-link',message:null,until:25000};
 s.core.rememberHistoryFocus(4,destination,'surviving-header',null);
 assert.ok(s.core.confirmNativeArrival(5));
 assert.equal(s.core.historyTarget(4,'190#binding:2\x1f314#binding:30',''),'mci-link');
});
test('native origin retention preserves the exact message identity for later composite history',()=>{
 const s=create(),token='190\x1f319\x1fmsgview:open';
 s.core.nativeReturn={from:2,token,id:'player-link',message:'80448',until:25000};s.core.confirmNativeArrival(3);
 assert.equal(s.core.historyTarget(2,token,'<button data-fm-message-view-id="80448">'),'player-link');
 assert.equal(s.core.historyTarget(2,token,'<button data-fm-message-view-id="80449">'),null);
});
test('native arrival retains the dispatched message return snapshot after an intermediate Inbox consumed live state',()=>{
 const s=create(),token='190\x1f319\x1fmsgview:open',returnState={message:'81482',search:{query:'Cerci'}};
 s.context.FM.msgview={returnMessageId:null};
 s.core.nativeReturn={from:5,token,id:'player-link',message:'81482',returnState,until:25000};
 s.core.confirmNativeArrival(6);
 const key=s.core.historyKey(5,token);assert.equal(s.core.historyMessagePos[key].returnState,returnState);
 s.core.rememberHistoryFocus(5,token,'returned-player-link','81482');assert.equal(s.core.historyMessagePos[key].returnState,returnState);
});
test('a mismatched dispatched message snapshot is not attached to another native history message',()=>{
 const s=create(),token='190\x1f319\x1fmsgview:open';
 s.core.nativeReturn={from:5,token,id:'new-link',message:'44',returnState:{message:'22',search:{query:'old query'}},until:25000};
 s.core.confirmNativeArrival(6);assert.equal(s.core.historyMessagePos[s.core.historyKey(5,token)].returnState,null);
});
test('a new native message at a reused slot retires the preceding message return snapshot',()=>{
 const s=create(),token='190\x1f319\x1fmsgview:open',key=s.core.historyKey(5,token);
 s.core.historyMessagePos[key]={message:'22',id:'old',returnState:{message:'22'}};
 s.core.rememberHistoryFocus(5,token,'new','44');assert.equal(s.core.historyMessagePos[key].returnState,null);
});
test('native earlier-slot provenance transfers the dispatch-time message return context with its exact NewsItem',()=>{
 const s=create(),token='190\x1f319\x1fmsgview:open',state={message:'81482',search:{query:'Cerci'}};
 s.core.rememberHistoryFocus(5,token,'player-link','81482',state);
 const origin=s.core.historyOriginPositions({index:5,token,message:'81482'},3);assert.ok(origin);
 s.core.restoreHistoryOrigin(origin);
 assert.equal(s.core.historyMessagePos[s.core.historyKey(3,token)].returnState,state);
 assert.equal(s.core.historyOriginPositions({index:5,token,message:'44'},3),null);
});
test('an expired, cancelled or non-forward native return cannot overwrite a newer cursor',()=>{
 const s=create(),token='190\x1f314';s.core.rememberHistoryFocus(4,token,'new-choice',null);
 const origin={from:4,token,id:'old-choice',until:25000};s.core.nativeReturn=origin;
 assert.equal(s.core.confirmNativeArrival(4),null);assert.equal(s.core.confirmNativeArrival(3),null);
 s.advance(25001);assert.equal(s.core.confirmNativeArrival(5),null);s.core.nativeReturn=null;s.advance(0);assert.equal(s.core.confirmNativeArrival(5),null);
 assert.equal(s.core.historyTarget(4,token,''),'new-choice');
});

function openingRefresh(){const context={FM:{msgview:{opening:1}}};vm.runInNewContext(source.slice(start,end+3),context);return context.FM.core;}

function nativeOrigin(active,owner='world',history=false){
 const s=create(),root={hasAttribute:name=>name==='data-fm-history'&&history};
 s.context.FMTOKEN='190#binding:1\x1f314#binding:2';s.context.document={activeElement:active,getElementById:id=>id==='world'?root:null,querySelector:()=>null};s.context.FM.dropdown={own:()=>owner};return s;
}
const nativeChain=()=>({id:'',tagName:'SELECT',hasAttribute:name=>name==='data-chain'||name==='data-dd',closest:()=>({})});
test('a hierarchical native navigation retains its owning root before the idless chain is removed',()=>{
 const s=nativeOrigin(nativeChain());s.core.rememberNativeAction('world',true);assert.equal(s.core.nativeReturn.id,'world');assert.equal(s.core.historyTarget(8,'190#binding:9\x1f314#binding:10',''),'world');
 s.core.rememberHistoryFocus(8,s.context.FMTOKEN,'surviving-header',null);s.core.confirmNativeArrival(9);assert.equal(s.core.historyTarget(8,s.context.FMTOKEN,''),'world');
});
test('a dropdown cannot borrow another owner or an unrelated focused control as its history origin',()=>{
 for(const [active,owner] of [[nativeChain(),'other'],[{id:'chosen',tagName:'BUTTON',hasAttribute:()=>false},'world'],[{...nativeChain(),tagName:'DIV'},'world']]){const s=nativeOrigin(active,owner);s.core.rememberNativeAction('world',true);assert.equal(s.core.nativeReturn.id,null);assert.equal(Object.keys(s.core.historyPos).length,0);}
});
test('ordinary focused native buttons use the same exact source-slot memory',()=>{
 const s=nativeOrigin({id:'world'});s.core.rememberNativeAction('world');assert.equal(s.core.nativeReturn.id,'world');assert.equal(s.core.historyTarget(8,s.context.FMTOKEN,''),'world');
});
test('a history button or an unavailable native history index never creates a forward-return origin',()=>{
 const s=nativeOrigin({id:'world'},'world',true);s.core.rememberNativeAction('world');assert.equal(s.core.nativeReturn,null);assert.equal(Object.keys(s.core.historyPos).length,0);
 const missing=nativeOrigin(nativeChain());missing.core.historyIndex=null;missing.core.rememberNativeAction('world',true);assert.equal(missing.core.nativeReturn,null);
});
test('a chained native owner is checked with the existing encoded transport identity',()=>{
 const s=nativeOrigin(nativeChain(),'world%3Acontext');s.context.document.getElementById=id=>id==='world:context'?{hasAttribute:()=>false}:null;
 s.core.rememberNativeAction('world:context',true);assert.equal(s.core.nativeReturn.id,'world:context');
});
test('a body request beginning after message dispatch is queued without touching native handles',()=>{const core=openingRefresh();core.refresh(false,true);assert.equal(core.refreshing,false);assert.equal(core.pending,true);assert.equal(core.pendingNeutral,true);});
test('refreshes queued behind a message command preserve hard and nonneutral intent',()=>{const core=openingRefresh();core.refresh(false,true);core.refresh(true,false);core.refresh(false,true);assert.equal(core.refreshing,false);assert.equal(core.pending,true);assert.equal(core.pendingHard,true);assert.equal(core.pendingNeutral,false);});
test('a failed refresh component is logged while later accessibility hooks continue',()=>{const s=create();let later=0;s.core.onRefresh(()=>{throw Error('Controlled component failure');});s.core.onRefresh(()=>later++);s.core.runAfterRefresh();assert.equal(later,1);assert.equal(s.logs.length,1);assert.match(s.logs[0],/afterRefresh hook 0 failed: Controlled component failure/);});
