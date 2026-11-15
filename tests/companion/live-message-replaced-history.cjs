// Real owned-career message -> native same-slot destination regression.
// No offer acceptance, simulation, Save, fixtures or private readiness polling.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');

async function run(c){
 const {page,poll,settled,activate}=c,root=path.resolve(__dirname,'../..');
 const observations=[],audits=[],checks=[];
 const health=JSON.parse(await c.fetchText('/health'));
 const message=process.env.FM27_HISTORY_NEWS_ID||'80448';
 assert.match(message,/^\d+$/);
 const owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
 async function state(stage){
  await settled();
  const dom=await page.evaluate(()=>({title:document.title,index:FM.core.historyIndex,token:FMTOKEN,
   news:document.querySelector('[data-fm-message-view-id]')?.dataset.fmMessageViewId||null,
   focus:{id:document.activeElement.id,text:(document.activeElement.textContent||'').slice(0,160),
    news:document.activeElement.closest('tr[data-fm-message-id]')?.dataset.fmMessageId||null},
   historyMemory:FM.core.historyPos,historyMove:FM.core.historyMove,
   viewOfferId:[...document.querySelectorAll('button')].find(b=>b.textContent==='Ver Prop.')?.id||null}));
  const native=health.developerTools?await c.fetchText('/history'):null;
  const result={stage,...dom,native};observations.push(result);
  console.log(JSON.stringify({stage,index:dom.index,message:dom.news,focus:dom.focus.id}));
  return result;
 }
 async function audit(stage){const r=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({stage,violations:r.violations.map(v=>v.id)});assert.equal(r.violations.length,0);}
 async function chord(direction){await settled();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/history'&&new URL(r.url()).searchParams.get('dir')===direction);await page.keyboard.press(direction==='back'?'Alt+[':'Alt+]');assert.equal(await(await response).text(),'');await settled();}
 async function chrome(direction){await activate(page.locator('[data-fm-history="'+direction+'"]'));}
 async function expectNews(){await poll(()=>page.locator('[data-fm-message-view-id="'+message+'"]').count(),'same actual NewsItem restored');}
 function nativeAnchor(s){return s?.replace(/ canBack=.*? shape=/,' shape=').replace(/ slots=.*$/,'');}
 function sameNativePosition(actual,expected){
  assert.ok(Number.isInteger(actual.index)&&Number.isInteger(expected.index),'Both positions have observed native body-header indices');
  assert.equal(actual.index,expected.index,'Composite traversal retains the actual native history position');
  if(health.developerTools){assert.ok(actual.native&&expected.native);assert.equal(nativeAnchor(actual.native),nativeAnchor(expected.native));}
 }
 let success=false;
 try{
  // Verify actual native filename, without writing, before touching QA read state.
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const filename=await page.getByRole('textbox',{name:'File name',exact:true}).inputValue();
  assert.ok(owned.ownedWorkingFiles.includes(filename+'.fm'),'Explicit owned career only');
  assert.equal(filename,owned.februaryWorking.replace(/\.fm$/,''));
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const destination=page.locator('#fmnav-tree [role=treeitem][data-uid="3199626752"]').first();
  const search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await destination.locator(':scope > span').textContent());await destination.focus();
  const nav=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');
  assert.match(await(await nav).text(),/^Opening /);await poll(async()=>await page.title()==='Mensagens','actual Inbox');await search.fill('');await c.newestMessages();await settled();
  const row=page.locator('tr[data-fm-message-id="'+message+'"] button[onclick^="FM.msgview.open"]');
  assert.equal(await row.count(),1);await row.focus();
  const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');await page.keyboard.press('Enter');assert.equal(await(await opened).text(),'');await expectNews();
  const subject=await page.title(),origin=page.getByRole('button',{name:'Ver Prop.',exact:true});
  const originId=await origin.getAttribute('id');await state('message-origin');await audit('message-origin');
  await activate(origin);await poll(async()=>await page.title()==='Prop. de Transferência','actual native offer editor');
  await poll(()=>page.getByRole('combobox',{name:/^Amanhã/}).count(),'native offer terms settled');
  const editor=await state('native-replaced-destination');assert.equal(editor.news,null);await audit('native-replaced-destination');
  if(process.argv.includes('--require-native-reuse'))assert.ok(editor.index<observations[0].index,'The actual native action must reuse an earlier destination, not only replace the same slot');
  await chrome('back');await expectNews();const returned=await state('back-message');
  assert.equal(returned.title,subject);observations.push({stage:'origin-focus-check',expected:originId,actual:returned.focus.id});sameNativePosition(returned,editor);
  checks.push(health.developerTools?'Native offer Back restores exact NewsItem without changing the inspected native stack':'Native offer Back restores exact NewsItem at the observed native body-header history index');
  await chord('back');const inbox=await state('back-inbox');assert.equal(inbox.title,'Mensagens');assert.equal(inbox.focus.news,message);
  assert.equal(await page.locator('[data-fm-history="forward"]').isEnabled(),true);await audit('back-inbox');
  await chrome('forward');await expectNews();await state('forward-message');
  await chord('forward');await poll(async()=>await page.title()==='Prop. de Transferência','actual native offer restored');
  const restored=await state('forward-native-destination');sameNativePosition(restored,editor);
  assert.match(await page.locator('#fmroot').innerText(),/Selina Cerci/);await audit('forward-native-destination');
  checks.push(health.developerTools?'Forward chrome and inherited chord traverse Inbox, message and actual offer at the unchanged inspected native index/count/shape':'Forward chrome and inherited chord traverse Inbox, message and actual offer at the unchanged observed native body-header index; internal count/shape are not inspected in Release');
  await chrome('back');await expectNews();await chord('back');await chord('back');
  const parent=await state('native-parent');assert.equal(parent.title,'Portal');assert.ok(parent.index<editor.index);
  await chrome('forward');const landing=await state('native-forward-inbox');assert.equal(landing.title,'Mensagens');assert.equal(landing.news,null);
  await chrome('forward');await expectNews();await chord('forward');
  assert.equal((await state('native-forward-destination')).title,'Prop. de Transferência');
  checks.push('Native Back leaves the composite node; native Forward enters its Inbox before message and destination');
  await chrome('back');await expectNews();
  const closed=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/close');await page.keyboard.press('Escape');assert.equal(await(await closed).text(),'');await settled();
  assert.equal((await state('explicit-dismissal')).title,'Mensagens');
  await chrome('forward');assert.equal((await state('dismissed-forward-destination')).title,'Prop. de Transferência');
  await chrome('back');const dismissedBack=await state('dismissed-back-inbox');assert.equal(dismissedBack.title,'Mensagens');assert.equal(dismissedBack.news,null);
  checks.push('Explicit Escape retires the virtual message, preserves destination and never resurrects dismissed content');
  assert.equal(returned.focus.id,originId,'Exact original message action focus is restored');

  // Explicitly selecting a message creates a NEW branch. The old native offer
  // node remains read-only, but its virtual Forward destination is retired.
  const selected=page.locator('tr[data-fm-message-id="'+message+'"] button[onclick^="FM.msgview.open"]');
  await selected.focus();const reopened=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');await page.keyboard.press('Enter');assert.equal(await(await reopened).text(),'');await expectNews();await settled();
  const branchMessage=await state('explicit-new-message-branch');
  assert.equal(await page.locator('[data-fm-history="forward"]').count(),0,'Message modal retains original shortcut-only history');
  await activate(page.getByRole('button',{name:'Ver Prop.',exact:true}));await poll(async()=>await page.title()==='Prop. de Transferência','new native offer branch');
  const branchOffer=await state('new-native-offer-branch');assert.ok(branchOffer.index>=0,'Actual native destination has a valid history position');
  await chrome('back');await expectNews();const branchReturn=await state('new-native-branch-back-message');assert.equal(branchReturn.index,branchOffer.index>branchMessage.index?branchMessage.index:branchOffer.index);
  await chord('forward');await poll(async()=>await page.title()==='Prop. de Transferência','native Forward follows new branch');
  const branchForward=await state('new-native-branch-forward-offer');assert.equal(branchForward.index,branchOffer.index);
  checks.push('Explicit reselection truncates old virtual Forward; actual native push/replacement/reuse returns to the exact message and Forward follows that observed branch');

  await poll(()=>page.getByRole('rowheader',{name:'Taxa mensal por utilização',exact:true}).count(),'actual loan draft term');
  const term=page.getByRole('row').filter({has:page.getByRole('rowheader',{name:'Taxa mensal por utilização',exact:true})});
  assert.equal(await term.count(),1);const amount=term.getByRole('combobox').first(),beforeDraft=(await amount.textContent()).trim();
  await term.getByRole('button',{name:'increase',exact:true}).focus();const step=page.waitForResponse(r=>new URL(r.url()).pathname==='/step');await page.keyboard.press('Enter');
  const stepResponse=await step;assert.equal(stepResponse.status(),200);const stepReply=await stepResponse.text();assert.doesNotMatch(stepReply,/failed|unavailable|stale/i);
  await poll(async()=>(await amount.textContent()).trim()!==beforeDraft,'actual changed native loan amount');
  const editedDraft=(await amount.textContent()).trim();await activate(page.getByRole('button',{name:'Undo',exact:true}));
  await poll(async()=>(await amount.textContent()).trim()===beforeDraft,'native Undo restores actual original amount');
  assert.equal(await page.getByRole('button',{name:'Undo',exact:true}).isDisabled(),true);
  observations.push({stage:'actual-draft-and-undo',beforeDraft,editedDraft,stepReply,afterUndo:(await amount.textContent()).trim()});await audit('native-draft-undone');
  checks.push('Actual native monetary step changes the draft, Undo restores its amount and native clean state; no offer is submitted');

  // Hold only the reply AFTER native Back completes. Later actual Tab input
  // cancels its focus handoff; releasing the old reply must not replay Back or
  // move that newer focus. SSE and actual native data remain unmodified.
  let releaseReply=()=>{},held=0,backRequests=0;
  const replyGate=new Promise(resolve=>releaseReply=resolve);
  const countBack=r=>{const u=new URL(r.url());if(u.pathname==='/history'&&u.searchParams.get('dir')==='back')backRequests++;};
  const holdReply=async route=>{const response=await route.fetch();held++;await replyGate;await route.fulfill({response});};
  page.on('request',countBack);await page.route('**/history?dir=back',holdReply);
  try{
   await page.keyboard.press('Alt+[');await poll(()=>held===1,'one completed native Back reply held');await expectNews();await settled();
   await page.keyboard.press('Tab');const chosenFocus=await page.evaluate(()=>document.activeElement.id);assert.ok(chosenFocus);
   assert.equal(await page.evaluate(()=>FM.core.historyMove),null);const late=page.waitForResponse(r=>new URL(r.url()).pathname==='/history'&&new URL(r.url()).searchParams.get('dir')==='back');releaseReply();assert.equal(await(await late).text(),'');await settled();
   assert.equal(await page.evaluate(()=>document.activeElement.id),chosenFocus);assert.equal(backRequests,1);
   observations.push({stage:'late-history-reply-newer-focus',chosenFocus,backRequests});
  }finally{releaseReply();await page.unroute('**/history?dir=back',holdReply);page.off('request',countBack);}
  checks.push('A held completed native Back reply cannot steal later keyboard focus or replay the command');

  // Build a REAL native Forward branch from an ordinary Inbox: a profile opened
  // from this message. The offer then replaces the Inbox AND drops that native
  // Forward branch (observed count3 ->2). This is not a stale source anchor.
  const closeForParent=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/close');await page.keyboard.press('Escape');assert.equal(await(await closeForParent).text(),'');await settled();await chord('back');
  assert.equal(await page.title(),'Portal');
  const inboxLeaf=page.locator('#fmnav-tree [role=treeitem][data-uid="3199626752"]').first();
  const nativeSearch=page.getByRole('textbox',{name:'Search all screens',exact:true});await nativeSearch.fill(await inboxLeaf.locator(':scope > span').textContent());await inboxLeaf.focus();
  const inboxNav=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');assert.match(await(await inboxNav).text(),/^Opening /);await poll(async()=>await page.title()==='Mensagens','new ordinary native Inbox');await nativeSearch.fill('');await settled();
  async function selectActualMessage(){const button=page.locator('tr[data-fm-message-id="'+message+'"] button[onclick^="FM.msgview.open"]');assert.equal(await button.count(),1);await button.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');await page.keyboard.press('Enter');assert.equal(await(await response).text(),'');await expectNews();await settled();}
  await selectActualMessage();await activate(page.getByRole('button',{name:'Selina Cerci',exact:true}).first());await poll(async()=>await page.locator('[data-fm-message-view-id]').count()===0,'actual linked native player profile');assert.match(await page.locator('#fmroot').innerText(),/Selina Cerci/);
  await chrome('back');await expectNews();await chord('back');
  const sourceForward=await state('ordinary-inbox-existing-forward');assert.equal(await page.locator('[data-fm-history="forward"]').isEnabled(),true);
  await selectActualMessage();const truncatedOrigin=await page.getByRole('button',{name:'Ver Prop.',exact:true}).getAttribute('id');await activate(page.getByRole('button',{name:'Ver Prop.',exact:true}));
  await poll(async()=>await page.title()==='Prop. de Transferência','replacement with native Forward discarded');const truncatedOffer=await state('replacement-truncates-native-forward');
  assert.equal(await page.locator('[data-fm-history="forward"]').isEnabled(),false,'The actual discarded native profile Forward branch is unavailable on the new destination');
  if(health.developerTools)assert.ok(Number(/count=(\d+)/.exec(truncatedOffer.native)[1])<Number(/count=(\d+)/.exec(sourceForward.native)[1]),'Native replacement/reuse really truncates its existing Forward branch');
  await chrome('back');await expectNews();const truncatedReturn=await state('message-restored-after-native-forward-truncation');assert.equal(truncatedReturn.focus.id,truncatedOrigin);sameNativePosition(truncatedReturn,truncatedOffer);await audit('native-forward-truncation-return');
  checks.push(health.developerTools?'A real profile Forward branch is discarded with an inspected native count decrease and restores the exact message/action':'A real profile Forward branch becomes unavailable after offer replacement; observed native index and exact message/action return pass without claiming an uninspected internal count');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,health.coreMvid);
  success=true;
 }finally{
  const report={at:new Date().toISOString(),success,health,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,
   nativeHistoryEvidence:health.developerTools?'Native body-header indices plus independent internal history count/identity/shape inspection':'Production native body-header indices, actual Back/Forward availability, native destinations and verified NewsItem/action focus; internal count/identity/shape inspection is unavailable in Release',
   offerSubmitted:false,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
  fs.mkdirSync(path.join(root,'artifacts/tests/web'),{recursive:true});
  fs.writeFileSync(path.join(root,'artifacts/tests/web/message-replaced-history-'+(success?'passed':'failure')+'.json'),JSON.stringify(report,null,2));
 }
 return {checks:checks.length,audits:audits.length};
}
module.exports=run;
if(require.main===module)(async()=>{const c=await require('./original-client.cjs')({networkDiagnostics:true});try{console.log(await run(c));}finally{await c.browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
