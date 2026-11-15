// Actual January QA advice command and native loan-form result. Never infer a
// persistent player value from the transient advice acknowledgement or menu tick.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const out=path.resolve(__dirname,'../../artifacts/tests/web');fs.mkdirSync(out,{recursive:true});
 const expected='FM27Access QA Career Arrival 20261112221057414';
 const owned=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../../artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
 assert.equal(owned.arrivalWorking,expected);assert.ok(owned.ownedWorkingFiles.includes(expected+'.fm'));
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const cancelModes=['--native-cancel-submit','--native-cancel-event','--native-cancel-pointer','--native-cancel-click'].filter(mode=>process.argv.includes(mode));
 assert.ok(cancelModes.length<=1,'Choose one independent native Cancel diagnostic');
 const quietCancel=process.argv.includes('--quiet-cancel');
 assert.ok(!quietCancel||cancelModes.length===0,'Quiet sampling comparison retains the original ButtonAdapter');
 const report={at:new Date().toISOString(),nativeCancelMode:cancelModes[0]||'Original ButtonAdapter',quietCancelIsolation:quietCancel,checks:[],audits:[],saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const button=name=>page.getByRole('button',{name,exact:true});
 async function go(uid,{panelUid=uid,selectedTab}={}){
  const e=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await e.locator(':scope > span').textContent());await e.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});
  await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);
  await poll(()=>page.evaluate(id=>FMTOKEN.split('\x1f').some(part=>part===id||part.startsWith(id+'#')),String(panelUid)),'actual native containing panel '+panelUid+' for destination '+uid);
  if(selectedTab)await poll(()=>page.getByRole('tab',{name:selectedTab,exact:true}).getAttribute('aria-selected').then(value=>value==='true'),'actual native selected tab '+selectedTab);
  await settled();await search.fill('');await settled();
 }
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 async function folder(name){await page.getByRole('menuitem',{name,exact:true}).focus();await page.keyboard.press('ArrowRight');await settled();}
 async function cancelLoanForm(){
  if(quietCancel){
   await settled();
   const receiptPath=fs.readFileSync(path.resolve(__dirname,'../../artifacts/install/active-mod.txt'),'utf8').trim();
   const receipt=JSON.parse(fs.readFileSync(receiptPath,'utf8').replace(/^\uFEFF/,''));
   assert.equal(receipt.state,'installed');
   const serverLog=path.join(receipt.root,'BepInEx/plugins/FM27Access/server.log');
   const beforeLog=fs.readFileSync(serverLog,'utf8'),connections=[...beforeLog.matchAll(/SSE connect #(\d+)/g)];
   assert.ok(connections.length,'Observe the actual current native SSE worker');
   const currentConnection=connections.at(-1),connection=Number(currentConnection[1]);
   // Connection ordinals restart with a new core/game; retained logs contain
   // older identical ordinals. Only this latest connect begins the lifetime.
   const lifetime=text=>{const latest=[...text.matchAll(/SSE connect #(\d+)/g)].at(-1);assert.ok(latest&&Number(latest[1])===connection,'No other SSE connection may enter the quiet comparison');return text.slice(latest.index);};
   assert.ok(!lifetime(beforeLog).includes('SSE end #'+connection+' after '));
   const observation={connection,closeRequestedAt:Date.now(),nativeCancelSent:false};
   (report.quietSseStops??=[]).push(observation);
   await page.evaluate(()=>{window.qaQuietOriginalRefresh=FM.core.refresh;FM.core.refresh=()=>Promise.resolve();FMES.close();});
   try{
    await poll(()=>lifetime(fs.readFileSync(serverLog,'utf8')).includes('SSE end #'+connection+' after '),'actual SSE worker termination BEFORE the untouched form Cancel');
    observation.workerEndObservedAt=Date.now();
    observation.workerEnd=lifetime(fs.readFileSync(serverLog,'utf8')).split(/\r?\n/).find(line=>line.includes('SSE end #'+connection+' after '));
    observation.nativeCancelSent='Outcome uncertain until the new native document';
    await activate(button('Cancelar'));
   }finally{
    await page.evaluate(()=>{if(window.qaQuietOriginalRefresh){FM.core.refresh=qaQuietOriginalRefresh;delete window.qaQuietOriginalRefresh;}}).catch(()=>{});
   }
   // The old SSE sampler ended before Cancel. A fresh real page restores the
   // normal handlers and must observe the resulting native player profile.
   await page.reload({waitUntil:'domcontentloaded'});await settled();
   await poll(async()=>await page.getByRole('combobox',{name:'Empréstimo',exact:true}).count()===0,'actual native Cancel closes the untouched form');
   assert.equal(await page.title(),'Relatório de jogador');assert.match(await page.locator('#fmroot').innerText(),/Katie Reid/);
   observation.nativeCancelSent=true;observation.freshDocumentAt=Date.now();observation.title=await page.title();
   return;
  }
  const directEvent=process.argv.includes('--native-cancel-event');
  const directPointer=process.argv.includes('--native-cancel-pointer');
  const directClick=process.argv.includes('--native-cancel-click');
  if(!directPointer&&!directEvent&&!directClick&&!process.argv.includes('--native-cancel-submit'))return activate(button('Cancelar'));
  assert.equal(report.health.developerTools,true,'The independent native Submit diagnostic is Debug-only');
  const id=await button('Cancelar').getAttribute('id');assert.match(id,/^[-\w:.]+$/);
  // Keep the original adapter's pre-activation lifecycle in every comparison.
  // Only the final native activation mechanism differs; no supplemental click,
  // provider reset, explicit business event or native focus bypass is added.
  const nativePreparation=`
core.GetType("FM27Access.Plugin.Patches.PanelHistoryBackPatch").GetMethod("AllowBackThroughNextFrame",f).Invoke(null,null);
core.GetType("FM27Access.Plugin.Server.MatchTacticalPanelOwnership").GetMethod("BeforeActivate",f).Invoke(null,new object[]{nativeButton});
core.GetType("FM27Access.Plugin.Server.NativeEditSessionRefresh").GetMethod("BeforeActivate",f).Invoke(null,new object[]{nativeButton});
`;
  const code=`
var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;
var core=System.AppDomain.CurrentDomain.GetAssemblies()[0];var focusType=core.GetType("SI.UI.FocusSubsystem");var systems=core.GetType("SI.Core.GameSubsystems");
foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){
 if(a.ManifestModule.ModuleVersionId.ToString()=="${report.health.coreMvid}"&&a.GetType("FM27Access.Plugin.CoreEntry").GetField("_bridge",f).GetValue(null)!=null)core=a;
 if(focusType==null)focusType=a.GetType("SI.UI.FocusSubsystem");if(systems==null)systems=a.GetType("SI.Core.GameSubsystems");
}
var handlesType=core.GetType("FM27Access.Plugin.Server.ServerHandles");
var handles=(System.Collections.IDictionary)handlesType.GetField("_handles",f).GetValue(null);var handle=handles["${id}"];
if(handle==null)throw new System.InvalidOperationException("The exact Cancel handle is no longer registered");
var validation=new object[]{null};if(!(bool)handle.GetType().GetMethod("TryValidate",f).Invoke(handle,validation))throw new System.InvalidOperationException("Cancel is stale: "+validation[0]);
var adapter=handle.GetType().GetField("El",f).GetValue(handle);var element=handlesType.GetMethod("NativeElement",f).Invoke(null,new object[]{adapter});
var buttonType=core.GetType("SI.Bindable.SIButton");var visualType=core.GetType("UnityEngine.UIElements.VisualElement");
foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies()){if(buttonType==null)buttonType=a.GetType("SI.Bindable.SIButton");if(visualType==null)visualType=a.GetType("UnityEngine.UIElements.VisualElement");}
if(buttonType==null||visualType==null)throw new System.InvalidOperationException("Current native button metadata unavailable");
object nativeButton=null;
foreach(var m in visualType.GetMethods(f))if(m.Name=="TryCast"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0){nativeButton=m.MakeGenericMethod(new[]{buttonType}).Invoke(element,null);break;}
if(nativeButton==null)throw new System.InvalidOperationException("The exact Cancel control is not an SIButton");
System.Console.WriteLine("NATIVE_ONLY_CLICK|"+buttonType.GetProperty("OnlyAcceptsClickEvents",f).GetValue(nativeButton,null));
System.Console.WriteLine("NATIVE_ONLY_SUBMIT|"+buttonType.GetProperty("OnlyAcceptsSubmitEvents",f).GetValue(nativeButton,null));
System.Console.WriteLine("NATIVE_CAN_SUBMIT|"+buttonType.GetProperty("CanSubmit",f).GetValue(nativeButton,null));
${directClick?`
if(!(bool)handle.GetType().GetMethod("TryValidate",f).Invoke(handle,validation))throw new System.InvalidOperationException("Cancel changed before the single native ClickEvent");
${nativePreparation}
// Exactly one click, without SimulateButtonClick or a supplementary event.
// A failure to close must stop before advice; never send a second activation.
var clickType=core.GetType("UnityEngine.UIElements.ClickEvent");
foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(clickType==null)clickType=a.GetType("UnityEngine.UIElements.ClickEvent");
if(clickType==null)throw new System.InvalidOperationException("Actual native click type unavailable");
object click=null;foreach(var m in clickType.GetMethods(f|System.Reflection.BindingFlags.FlattenHierarchy))if(m.Name=="GetPooled"&&m.IsStatic&&m.GetParameters().Length==0&&m.ReturnType==clickType){click=m.Invoke(null,null);break;}
if(click==null)throw new System.InvalidOperationException("Actual pooled native click factory unavailable");
bool sent=false;foreach(var m in element.GetType().GetMethods(f))if(m.Name=="SendEvent"&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType.FullName=="UnityEngine.UIElements.EventBase"){m.Invoke(element,new object[]{click});sent=true;break;}
if(!sent)throw new System.InvalidOperationException("Actual native click dispatcher unavailable");
System.Console.WriteLine("NATIVE_CANCEL_CLICK_SENT|ClickEvent");
`:directPointer?`
if(!(bool)handle.GetType().GetMethod("TryValidate",f).Invoke(handle,validation))throw new System.InvalidOperationException("Cancel changed before native pointer activation");
${nativePreparation}
core.GetType("FM27Access.Plugin.Adapters.SyntheticPointer").GetMethod("LeftClick",f).Invoke(null,new object[]{element});
System.Console.WriteLine("NATIVE_CANCEL_POINTER_SENT|LeftClick");
`:directEvent?`
var submitType=core.GetType("UnityEngine.UIElements.NavigationSubmitEvent");
foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(submitType==null)submitType=a.GetType("UnityEngine.UIElements.NavigationSubmitEvent");
if(submitType==null)throw new System.InvalidOperationException("Actual native Submit type unavailable");
object submit=null;foreach(var method in submitType.GetMethods(f|System.Reflection.BindingFlags.FlattenHierarchy)){
 if(method.Name=="GetPooled"&&method.IsStatic&&method.GetParameters().Length==0&&method.ReturnType==submitType){submit=method.Invoke(null,null);break;}
}
if(submit==null)throw new System.InvalidOperationException("Actual pooled native Submit factory unavailable");
if(!(bool)handle.GetType().GetMethod("TryValidate",f).Invoke(handle,validation))throw new System.InvalidOperationException("Cancel changed before native Submit");
${nativePreparation}
bool sent=false;foreach(var method in element.GetType().GetMethods(f)){
 if(method.Name=="SendEvent"&&method.GetParameters().Length==1&&method.GetParameters()[0].ParameterType.FullName=="UnityEngine.UIElements.EventBase"){
  method.Invoke(element,new object[]{submit});sent=true;break;
 }
}
if(!sent)throw new System.InvalidOperationException("Actual native event dispatcher unavailable");
System.Console.WriteLine("NATIVE_CANCEL_EVENT_SENT|NavigationSubmitEvent");
`:`
object focus=null;
foreach(var m in systems.GetMethods(f))if(m.Name=="Get"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0){focus=m.MakeGenericMethod(new[]{focusType}).Invoke(null,null);break;}
if(focus==null)throw new System.InvalidOperationException("Native focus subsystem unavailable");
bool focused=false;foreach(var m in focusType.GetMethods(f))if(m.Name=="TryFocusElement"&&m.GetParameters().Length==1){focused=(bool)m.Invoke(focus,new object[]{element});break;}
var currentFocus=focusType.GetProperty("CurrentFocus",f).GetValue(focus,null);
var targetPointer=element.GetType().GetProperty("Pointer",f).GetValue(element,null);
bool targetObserved=currentFocus!=null&&targetPointer.Equals(currentFocus.GetType().GetProperty("Pointer",f).GetValue(currentFocus,null));
System.Console.WriteLine("NATIVE_FOCUS_RETURN|"+focused);System.Console.WriteLine("NATIVE_FOCUS_TARGET_OBSERVED|"+targetObserved);
if(!targetObserved)throw new System.InvalidOperationException("Native focus did not select the actual Cancel button");
if(!(bool)handle.GetType().GetMethod("TryValidate",f).Invoke(handle,validation))throw new System.InvalidOperationException("Cancel changed after native focus");
${nativePreparation}
// NavigationEventBlocker handles Move/Cancel, not Submit. No gate bypass is
// needed for this independently observed, exact-target native Submit.
System.Console.WriteLine(core.GetType("FM27Access.Plugin.Server.Dev.GameDriver").GetMethod("Drive",f).Invoke(null,new object[]{"confirm"}));
`}
`;
  const response=await page.evaluate(async code=>await(await fetch('/eval',{method:'POST',body:code})).text(),code);
  (report.nativeCancelSubmit??=[]).push(response);assert.doesNotMatch(response,/\[compile\]|\[exception\]|error CS/i);assert.match(response,directClick?/NATIVE_CANCEL_CLICK_SENT\|ClickEvent/:directPointer?/NATIVE_CANCEL_POINTER_SENT\|LeftClick/:directEvent?/NATIVE_CANCEL_EVENT_SENT\|NavigationSubmitEvent/:/confirm -> NavigationSubmitEvent on /);
  await poll(async()=>await page.getByRole('combobox',{name:'Empréstimo',exact:true}).count()===0,'single native Cancel event actually closes the form');await settled();
 }
 async function profileLoanForm(){
  await activate(button('Acções'));await folder('Transferência');
  await page.getByRole('menuitem',{name:'Definir Situação de Transferência',exact:true}).focus();await page.keyboard.press('Enter');await settled();
  await activate(page.getByRole('tab',{name:'Empréstimo',exact:true}));await poll(()=>page.getByRole('combobox',{name:'Empréstimo',exact:true}).count(),'actual native loan form');
  const value=await page.getByRole('combobox',{name:'Empréstimo',exact:true}).textContent();await audit('native loan form: '+value);await cancelLoanForm();return value;
 }
 async function loanForm(){
  await activate(page.getByRole('row').filter({hasText:'Katie Reid defesa central'}).getByRole('button',{name:'Katie Reid',exact:true}));
  return profileLoanForm();
 }
 async function loanMenuBaseline(){
  // Separately qualify advice before creating a native edit session. The
  // default test still exercises the known stale-session regression.
  await activate(page.getByRole('row').filter({hasText:'Katie Reid defesa central'}).getByRole('button',{name:'Katie Reid',exact:true}));
  await activate(button('Acções'));await folder('Transferência');await folder('Empréstimo');
  const selected=page.getByRole('menuitemcheckbox',{checked:true});assert.equal(await selected.count(),1);
  const value=await selected.textContent();await page.keyboard.press('Escape');await settled();return value;
 }
 async function changeFormOwner(){
  await go('4045698048');await activate(button('Cecily Wellesley-Smith'));
  (report.otherPlayerForms??=[]).push(await profileLoanForm());await go('4045698048');return loanForm();
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned January checkpoint');assert.equal(await file.inputValue(),expected);await activate(button('Cancelar'));
  await c.dropdown('FM Menu');await c.choose('Preferências');const autosave=page.getByRole('checkbox',{name:'Usar Gravação Automática',exact:true});await poll(()=>autosave.count(),'native autosave preservation state');assert.equal(await autosave.isChecked(),false,'No mutation against automatically saved careers');await activate(button('Close'));
  for(const uid of ['4287093760','2560554240']){
   // Squad Feedback is the native Performance Feedback panel with its Plantel
   // tab selected, not a distinct panel carrying the navigation entry's UID.
   await go(uid,uid==='2560554240'?{panelUid:'2079575296',selectedTab:'Plantel'}:{});const body=await page.locator('#fmroot').innerText();assert.match(body,/Double upward arrow \(green\)/);assert.doesNotMatch(body,/SupportChangeIcon/);assert.match(body,/47/);assert.match(body,/Satisfatório/);await audit('native support style '+uid);
  }
  report.checks.push('Both actual support surfaces retain 47/Satisfatório and describe the native double upward green glyph without inventing a delta');
  report.baselineSource=process.argv.includes('--advice-before-editor')?'Actual native selected menu entry; no editor created':'Actual native loan form';
  await go('4045698048');report.before=process.argv.includes('--advice-before-editor')?await loanMenuBaseline():await loanForm();assert.equal(report.before,'Sem estatuto definido','Require the restored native checkpoint, never replay an already applied advice');
  await go('4045698048');const row=page.getByRole('row').filter({hasText:'Katie Reid defesa central'});report.adviceBefore=await row.innerText();assert.match(report.adviceBefore,/Deveria ser emprestada/);await audit('named native development advice');
  await activate(button('Apply development advice'));report.commandSubmissions=1;
  report.adviceAfter=await row.innerText();report.after=await loanForm();
  if(report.after!=='Disponível para empréstimo'&&process.argv.includes('--diagnose-native-loan')){
   // Read a different real player's form, without edits, to distinguish a
   // retained edit-provider value from the actual effect of the advice command.
   report.afterDifferentFormOwner=await changeFormOwner();
   if(report.afterDifferentFormOwner!=='Disponível para empréstimo'){
    await activate(button('Acções'));await folder('Transferência');await folder('Empréstimo');
    report.menuBeforeDirect=await page.getByRole('menu').innerText();
    const available=page.getByRole('menuitem',{name:'Disponível para empréstimo',exact:true});
    await activate(available);report.directLoanCommandSubmissions=1;
    const confirmation=page.getByRole('dialog');await poll(()=>confirmation.count(),'native loan status confirmation');
    report.directConfirmation=await confirmation.innerText();assert.match(report.directConfirmation,/Definir o Estatuto de Empréstimo[\s\S]*Katie Reid/);
    await audit('actual native loan command confirmation');await activate(button('Sim'));report.directConfirmationAcceptedOnce=true;
    await poll(()=>button('Acções').count(),'native player profile after confirmation');
    report.afterDirectCommand=await profileLoanForm();
    report.afterDirectDifferentFormOwner=await changeFormOwner();
   }
   report.diagnosticCompleted=true;
  }
  assert.equal(report.after,'Disponível para empréstimo');
  report.checks.push('One actual native advice command changes Katie Reid from undefined loan status to available, verified through the native player loan form');
  await go('4045698048');report.reopened=await loanForm();assert.equal(report.reopened,report.after);
  report.checks.push('Cancel without form edits, leave/reopen the player form: native loan availability persists; advice text is allowed to update to a new assessment');
  assert.deepEqual(c.errors,[]);
  classifyQuietClosures();
  assert.deepEqual(report.unexpectedRequestFailures,[]);assert.deepEqual(report.unexpectedNetworkFailures,[]);report.success=true;
 }catch(error){report.error=error.stack;report.html=await page.locator('#fmroot').innerHTML().catch(e=>e.message);throw error;}
 finally{classifyQuietClosures();report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;fs.writeFileSync(path.join(out,'youth-development-advice'+(report.success?'':'-failure')+'.json'),JSON.stringify(report,null,2));await c.browser.close();}
 console.log(JSON.stringify({success:true,checks:report.checks.length,audits:report.audits.length,before:report.before,after:report.after,reopened:report.reopened}));
 function classifyQuietClosures(){
  const expected=(path,error,at,canceled)=>quietCancel&&path==='/events'&&error==='net::ERR_ABORTED'&&canceled!==false
   &&(report.quietSseStops||[]).some(s=>at>=s.closeRequestedAt-500&&at<=(s.workerEndObservedAt||s.closeRequestedAt)+2000);
  report.expectedIntentionalSseCancellations={requests:c.requestFailures.filter(f=>expected(f.path,f.error,f.failedAt,true)),network:c.network.failures.filter(f=>expected(f.request?.path,f.error,(f.request?.startedUtcMs||0)+(f.elapsedMs||0),f.canceled))};
  report.unexpectedRequestFailures=c.requestFailures.filter(f=>!report.expectedIntentionalSseCancellations.requests.includes(f));
  report.unexpectedNetworkFailures=c.network.failures.filter(f=>!report.expectedIntentionalSseCancellations.network.includes(f));
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
