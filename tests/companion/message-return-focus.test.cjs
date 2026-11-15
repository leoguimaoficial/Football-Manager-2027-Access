// Game-independent tests of the actual inherited FM.msgview object. These DOM
// inputs exercise coordination only; native behavior is qualified separately.
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const test=require('node:test'),assert=require('node:assert/strict');
const source=fs.readFileSync(path.resolve(__dirname,'../../FM27Access.Plugin/Server/app.js'),'utf8');
const start=source.indexOf('FM.msgview = {'),end=source.indexOf('\n};',start);
assert.ok(start>=0&&end>start,'Load the complete production message-view object');
function create({same=false,unread=false}={}){
 const document={body:{id:'body'},activeElement:null};let rows=[];
 const filter={id:'unread-filter',getAttribute:name=>name==='role'?'tab':name==='aria-selected'?String(unread):null,hasAttribute:()=>false,focus(){document.activeElement=this;}};
 document.querySelectorAll=()=>rows;
 document.querySelector=selector=>selector==='[data-fm-msg-unread-filter]'?filter:null;
 document.getElementById=id=>id==='fmroot'?{innerHTML:'actual list placeholder'}:rows.find(r=>r.id===id)||null;
 const context={document,FMTOKEN:same?'Messages':'Portal',encodeURIComponent,setTimeout:fn=>fn(),hasMenu:()=>false,
  hasMsgView:t=>t.includes('msgview:'),stripMsgView:t=>t.replace(/\x1fmsgview:open/,''),
  FM:{core:{msgStash:null,screenIdentity:t=>t.replace(/\x1fnames:\d+/,''),command(){return Promise.resolve();},refresh(){},actResult(){}},messages:{refocus:e=>e.focus()}}};
 vm.runInNewContext(source.slice(start,end+3),context,{filename:'production-message-view.js'});
 function setRows(values){rows=values.map(([id,message])=>({id,closest:()=>({getAttribute:name=>name==='data-fm-message-id'?message:null}),focus(){document.activeElement=this;}}));return rows;}
 function open(){setRows([['old-first','11'],['old-origin','22'],['old-last','33']]);context.FM.msgview.open('old-origin');context.FM.msgview.viewObserved=true;}
 function land(values){context.FMTOKEN='Messages';setRows(values);document.activeElement=document.body;context.FM.msgview.refreshUnread();context.FM.msgview.restoreUnreadFocus();}
 return{document,view:context.FM.msgview,context,filter,setRows,open,land,focus:()=>document.activeElement?.id};
}
test('Portal to Messages restores the same native message despite changed DOM ids',()=>{const s=create();s.open();s.land([['new-first','11'],['new-origin','22'],['new-last','33']]);assert.equal(s.focus(),'new-origin');});
test('a retained message follows its native identity after a same-inbox reorder',()=>{const s=create({same:true});s.open();s.land([['new-first','11'],['new-last','33'],['new-origin','22']]);assert.equal(s.focus(),'new-origin');});
test('Unread removal preserves the inherited next-row position',()=>{const s=create({same:true,unread:true});s.open();s.land([['new-first','11'],['next-unread','33']]);assert.equal(s.focus(),'next-unread');});
test('the final unread message returns to the selected empty native filter',()=>{const s=create({same:true,unread:true});s.open();s.land([]);assert.equal(s.focus(),'unread-filter');});
test('an ambiguous native identity never arbitrarily chooses one duplicate',()=>{const s=create();s.open();s.land([['first','11'],['duplicate-one','22'],['duplicate-two','22']]);assert.equal(s.focus(),'first');});
test('late lost focus revalidates the native record instead of a recycled DOM id',()=>{const s=create({same:true});s.open();s.land([['new-first','11'],['returned','22']]);s.setRows([['returned','44'],['actual-message','22']]);s.document.activeElement=s.document.body;s.view.restoreUnreadFocus();assert.equal(s.focus(),'actual-message');});
test('a subsequent explicit cursor choice prevents late focus stealing',()=>{const s=create({same:true});s.open();s.land([['first','11'],['returned','22']]);const [chosen]=s.setRows([['chosen','33'],['new-origin','22']]);s.view.returnFocus=null;chosen.focus();s.view.restoreUnreadFocus();assert.equal(s.focus(),'chosen');});
test('a different native inbox identity invalidates the remembered return',()=>{const s=create({same:true});s.open();s.land([['first','11'],['returned','22']]);s.context.FMTOKEN='Other-native-owner';s.document.activeElement=s.document.body;s.view.restoreUnreadFocus();assert.equal(s.focus(),'body');assert.equal(s.view.returnFocus,null);});
test('a confirmed native close releases the preceding HTML stash',()=>{const s=create();s.open();assert.ok(s.context.FM.core.msgStash);s.land([['first','11'],['returned','22']]);assert.equal(s.context.FM.core.msgStash,null);});
test('an intermediate inbox before the view opens keeps the original stash',()=>{const s=create();s.open();s.view.viewObserved=false;s.context.FMTOKEN='Messages';s.view.refreshUnread();assert.ok(s.context.FM.core.msgStash);assert.equal(s.view.refreshAfterClose,true);});

function pending(){
 const s=create({same:true});let finish,fail;const sent=[],feedback=[];let refreshes=0;
 s.context.FM.core.refreshing=true;
 s.context.FM.core.waitForHistoryBody=()=>new Promise((resolve,reject)=>{finish=resolve;fail=reject;});
 s.context.FM.core.command=(url,reply)=>{sent.push(url);return new Promise(resolve=>{s.reply=text=>{reply(text);resolve(text);};});};
 s.context.FM.core.actResult=text=>feedback.push(text);
 s.context.FM.core.refresh=()=>refreshes++;
 s.setRows([['old-origin','22']]);
 const work=s.view.open('old-origin');
 return{...s,work,sent,feedback,reply:text=>s.reply(text),refreshes:()=>refreshes,land(values){s.setRows(values);s.context.FM.core.refreshing=false;finish();},fail:()=>fail(Error('body unavailable'))};
}
test('an in-flight real-body boundary defers one dispatch and resolves the same native message under its new DOM id',async()=>{const s=pending();assert.equal(s.sent.length,0);assert.equal(s.context.FM.core.msgStash,null);s.land([['recycled','33'],['new-origin','22']]);await s.work;assert.equal(s.sent.length,1);assert.match(s.sent[0],/id=new-origin&/);assert.equal(s.view.returnMessageId,'22');assert.equal(s.view.pendingOpen,null);});
test('a departed or ambiguous message never turns into the current row at the previous position',async()=>{for(const rows of [[['old-origin','33']],[['one','22'],['two','22']]]){const s=pending();s.land(rows);await s.work;assert.equal(s.sent.length,0);assert.equal(s.feedback.length,1);assert.equal(s.refreshes(),1);}});
test('later keyboard or pointer choice cancels an undispatched message intent',async()=>{const s=pending();s.view.pendingOpen=null;s.land([['new-origin','22']]);await s.work;assert.equal(s.sent.length,0);assert.equal(s.feedback.length,0);});
test('a changed native inbox cancels an undispatched message intent',async()=>{const s=pending();s.context.FMTOKEN='Different-native-inbox';s.land([['new-origin','22']]);await s.work;assert.equal(s.sent.length,0);assert.equal(s.feedback.length,0);});
test('a failed body read never dispatches or fabricates a message modal',async()=>{const s=pending();s.fail();await s.work;assert.equal(s.sent.length,0);assert.equal(s.feedback.length,1);assert.equal(s.refreshes(),1);assert.equal(s.context.FM.core.msgStash,null);});
test('a delayed continuation beyond its original deadline never dispatches',async()=>{const s=pending();s.view.pendingOpen.until=Date.now()-1;s.land([['new-origin','22']]);await s.work;assert.equal(s.sent.length,0);assert.equal(s.feedback.length,1);});
test('a definite native refusal refreshes the list and never retries the message action',async()=>{const s=pending();s.land([['new-origin','22']]);await s.work;s.reply('That element is no longer on screen. Refresh.');await Promise.resolve();assert.equal(s.sent.length,1);assert.equal(s.refreshes(),1);assert.equal(s.view.wantBody,false);assert.equal(s.context.FM.core.msgStash,null);assert.equal(s.view.opening,0);});
test('only the final in-flight message reply releases the coalesced body read',async()=>{const s=create({same:true}),replies=[];let reads=0;s.setRows([['first','11'],['second','22']]);s.context.FM.core.command=(url,reply)=>new Promise(resolve=>replies.push(text=>{reply(text);resolve(text);}));s.context.FM.core.refresh=()=>reads++;s.view.openNow('first');s.view.openNow('second');assert.equal(s.view.opening,2);replies[0]('');await Promise.resolve();assert.equal(s.view.opening,1);assert.equal(reads,0);replies[1]('');await Promise.resolve();assert.equal(s.view.opening,0);assert.equal(reads,1);});
test('a completed request followed by an actual native inbox retires failed modal intent and old HTML',()=>{const s=create({same:true});s.view.wantBody=true;s.context.FM.core.msgStash={base:'Messages',html:'preceding list'};s.view.refreshAfterClose=true;s.view.returnMessageId='22';s.view.focusBody();assert.equal(s.view.wantBody,false);assert.equal(s.context.FM.core.msgStash,null);assert.equal(s.view.returnMessageId,null);});
test('an inbox observed while an opening command is still in flight cannot retire its intent',()=>{const s=create({same:true});s.view.wantBody=true;s.view.opening=1;s.context.FM.core.msgStash={html:'list'};s.view.focusBody();assert.equal(s.view.wantBody,true);assert.ok(s.context.FM.core.msgStash);});
test('a busy page without a native inbox is not proof that an uncertain open failed',()=>{const s=create({same:true});s.document.querySelector=()=>null;s.view.wantBody=true;s.context.FM.core.msgStash={html:'list'};s.view.focusBody();assert.equal(s.view.wantBody,true);assert.ok(s.context.FM.core.msgStash);});

test('late message-open observation preserves the exact confirmed history action focus',()=>{const s=create({same:true});s.context.FMTOKEN='Messages\x1fmsgview:open';s.setRows([['offer-origin','22']]);s.context.FM.core.historyKey=()=> '1|Messages/msgview:open';s.context.FM.core.historyFocus={key:'1|Messages/msgview:open',id:'offer-origin'};s.view.wantBody=true;s.view.focusBody();assert.equal(s.focus(),'offer-origin');assert.equal(s.view.wantBody,false);});
test('history focus from another native destination never replaces ordinary message-body focus',()=>{const s=create({same:true});s.context.FMTOKEN='Messages\x1fmsgview:open';const body={id:'actual-body',focus(){s.document.activeElement=this;}};s.document.querySelector=selector=>selector==='[data-fm-msg-body]'?body:null;s.context.FM.core.historyKey=()=> '2|Messages/msgview:open';s.context.FM.core.historyFocus={key:'1|Other',id:'old-action'};s.view.wantBody=true;s.view.focusBody();assert.equal(s.focus(),'actual-body');assert.equal(s.view.wantBody,false);});
test('an actual native message observed after a lost reply still receives body focus',()=>{const s=create({same:true}),body={focus(){s.document.activeElement=this;}};s.context.FMTOKEN='Messages\x1fmsgview:open';s.document.querySelector=q=>q==='[data-fm-msg-body]'?body:null;s.view.wantBody=true;s.context.FM.core.msgStash={html:'list'};s.view.focusBody();assert.equal(s.view.wantBody,false);assert.equal(s.view.viewObserved,true);assert.equal(s.document.activeElement,body);assert.ok(s.context.FM.core.msgStash);});

test('an exact history message recovers its original row after an intermediate Inbox consumed transient return state',()=>{
 const s=create({same:true});s.open();s.view.searchReturn={key:'0|Messages',query:'Cerci',closing:false};
 const saved={message:'22',returnState:s.view.historyReturnState()};s.view.opening=0;
 s.land([['unrelated','99']]);assert.equal(s.view.returnMessageId,null);
 const read=s.document.querySelector;s.context.FMTOKEN='Messages\x1fmsgview:open';
 s.document.querySelector=q=>q==='[data-fm-message-view-id]'?{getAttribute:()=> '22'}:read(q);
 s.view.restoreHistoryReturn(saved);assert.equal(s.view.returnMessageId,'22');assert.equal(s.view.searchReturn.query,'Cerci');assert.equal(s.view.refreshAfterClose,true);
 s.context.FMTOKEN='Messages';s.land([['unrelated','99'],['actual-origin','22']]);assert.equal(s.focus(),'actual-origin');
});
test('a different native message cannot inherit another history entry search or row',()=>{
 const s=create({same:true});s.open();const saved={message:'22',returnState:s.view.historyReturnState()};s.view.opening=0;s.view.clearUnreadReturn();
 s.document.querySelector=q=>q==='[data-fm-message-view-id]'?{getAttribute:()=> '44'}:null;
 s.view.restoreHistoryReturn(saved);assert.equal(s.view.returnMessageId,null);assert.equal(s.view.refreshAfterClose,false);
});
test('a history return snapshot cannot be mutated by a later search-close handoff',()=>{
 const s=create({same:true});s.open();s.view.searchReturn={key:'0|Messages',query:'Cerci',closing:false};
 const saved=s.view.historyReturnState();s.view.searchReturn.query='new query';s.view.searchReturn.closing=true;
 assert.equal(saved.search.query,'Cerci');assert.equal(saved.search.closing,false);
});

function searched(){
 const s=create(),query={value:'Conselhos'},read=s.document.querySelector;
 s.context.FM.core.historyKey=(index,token)=>index+'|'+token;
 s.document.querySelector=selector=>selector==='[data-fm-message-query]'?query:read(selector);
 s.open();s.context.FMTOKEN='Messages\x1fmsgview:open';s.view.beginClose();
 return{...s,query};
}
test('a searched dashboard result waits through the intermediate unfiltered Inbox before restoring its exact row',()=>{
 const s=searched();s.land([['unrelated-newest','11']]);assert.equal(s.focus(),'body');assert.equal(s.view.returnMessageId,'22');assert.equal(s.view.searchReturn.closing,true);
 s.context.FMTOKEN='Portal';s.setRows([['filtered-first','11'],['filtered-origin','22']]);s.view.refreshUnread();s.view.restoreUnreadFocus();
 assert.equal(s.focus(),'filtered-origin');assert.equal(s.view.searchReturn,null);
});
test('a new keyboard choice cancels a pending search return without stealing focus on its later native arrival',()=>{
 const s=searched(),chosen={id:'new-choice'};s.view.cancelSearchReturn();s.document.activeElement=chosen;s.context.FMTOKEN='Portal';s.setRows([['filtered-origin','22']]);s.view.refreshUnread();s.view.restoreUnreadFocus();
 assert.equal(s.focus(),'new-choice');assert.equal(s.view.searchReturn,null);
});
test('another query cannot consume a pending search result return',()=>{
 const s=searched();s.context.FMTOKEN='Portal';s.query.value='Different';s.setRows([['recycled-origin','22']]);s.document.activeElement=s.document.body;s.view.refreshUnread();s.view.restoreUnreadFocus();
 assert.equal(s.focus(),'body');assert.equal(s.view.returnMessageId,'22');
});
test('native search return restores disclosure context without moving a newer chosen focus',()=>{
 const s=searched(),chosen={id:'new-choice'},opener={id:'search-open'};
 s.query.getAttribute=name=>name==='data-fm-message-query-opener'?opener.id:null;s.query.id='query-field';s.view.cancelSearchReturn();
 s.context.FMTOKEN='Portal';s.document.activeElement=chosen;s.document.getElementById=id=>id===opener.id?opener:null;
 s.view.restoreSearchDisclosure();assert.equal(s.focus(),'new-choice');assert.equal(s.context.FM.core.revealedInput.field,'query-field');assert.equal(s.context.FM.core.revealedInput.opener,opener.id);
});
