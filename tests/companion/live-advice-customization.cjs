// Real native advice settings in an owned QA career. Every committed topic
// change has an explicit inverse and reopening check; no Save or simulation.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true,initScript:()=>{
  window.qaAdviceUpdates=[];
  const state=()=>Array.from(document.querySelectorAll('input[type=checkbox]')).map(e=>({id:e.id,checked:e.checked}));
  const record=x=>{qaAdviceUpdates.push({time:Date.now(),...x});if(qaAdviceUpdates.length>160)qaAdviceUpdates.shift();};
  const source=window.EventSource;window.EventSource=function(...args){const stream=new source(...args);stream.addEventListener('values',e=>{const data=JSON.parse(e.data);setTimeout(()=>record({kind:'values',data,checked:state(),refreshing:FM.core.refreshing}),0);});return stream;};
  const fetchOriginal=window.fetch;window.fetch=function(...args){const result=fetchOriginal.apply(this,args);if(String(args[0]).startsWith('/body?'))result.then(r=>r.clone().text()).then(text=>{const parsed=new DOMParser().parseFromString(text,'text/html');record({kind:'body',checked:Array.from(parsed.querySelectorAll('input[type=checkbox]')).map(e=>({id:e.id,checked:e.checked}))});}).catch(()=>{});return result;};
  window.addEventListener('load',()=>FM.core.afterRefresh.push(()=>record({kind:'morph',checked:state()})));
 }}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],observations={};
 const button=name=>page.getByRole('button',{name,exact:true});
 const tab=name=>page.getByRole('tab',{name,exact:true});
 const snapshot=()=>page.getByRole('checkbox').evaluateAll(es=>es.map(e=>({label:e.labels[0].textContent.trim(),checked:e.checked,disabled:e.disabled})));
 let mvid,origin,originalTab;
 async function responsibilities(){
  const entry=page.locator('#fmnav-tree [data-uid="3128627712"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await poll(()=>button('Conselho e Relatórios').count(),'actual responsibilities destination');await settled();await search.fill('');await settled();
 }
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});assert.deepEqual(a.violations,[]);}
 async function open(index=0){
  const opener=button('Personalizar conselho').nth(index);origin=await opener.getAttribute('id');await settled();observations.opening={index,origin};
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate');response.catch(()=>{});await opener.press('Enter');const reply=await response;assert.equal(reply.status(),200);assert.doesNotMatch(await reply.text(),/unavailable|not found|stale|disabled|failed/i);await settled();
  await poll(()=>page.locator('[data-fm-native-overlay]').count(),'actual native customization card');
  assert.equal(await page.getByRole('textbox',{name:'Search all screens',exact:true}).count(),0,'Native card suppresses its background');
  await poll(()=>page.evaluate(()=>document.activeElement?.tagName==='H1'),'native card title receives focus');
  originalTab=await page.getByRole('tab',{selected:true}).textContent();
 }
 async function select(name){if(await tab(name).getAttribute('aria-selected')!=='true')await activate(tab(name));
  if(name==='Oferecer via TransferRoom')await poll(()=>page.getByRole('checkbox',{name:'Lista de empréstimos',exact:true}).count(),'actual bound TransferRoom form');
  else {const first={'Recrutamento':'Jogadores a Considerarem Pedir Uma Melhoria Contratual','Desenvolvimento':'Aceitável para Capitão','Treinos':'Mudar Intensidade de Treinos','Contratações para a Equipa Técnica':'Vagas para Treinador Adjunto'}[name];await poll(()=>page.getByRole('checkbox',{name:first,exact:true}).count(),'actual selected native topic list');await poll(()=>page.getByRole('checkbox',{name:'Todos os conselhos da equipa técnica',exact:true}).count(),'official all-topics caption');}await settled();}
 async function close(){await activate(button('Close'));await poll(()=>button('Personalizar conselho').count(),'original advice screen');await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'exact customization opener focus');}
 async function nativeCheckbox(field){
  const id=await field.getAttribute('id');assert.match(id,/^e[0-9a-f]+$/);
  const code=`var asm=System.AppDomain.CurrentDomain.GetAssemblies()[0];foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}")asm=a;var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic;var handles=(System.Collections.IDictionary)asm.GetType("FM27Access.Plugin.Server.ServerHandles").GetField("_handles",flags).GetValue(null);var h=handles["${id}"];if(h==null)throw new System.Exception("Current checkbox handle absent");var e=h.GetType().GetField("El",flags).GetValue(h);var native=asm.GetType("FM27Access.Plugin.Adapters.PluginAdapterBase").GetProperty("BackingElement",flags).GetValue(e);var box=((UnityEngine.UIElements.VisualElement)native).TryCast<SI.Bindable.SICheckBox>();if(box==null)throw new System.InvalidOperationException("The observed native control is not an SICheckBox.");System.Console.WriteLine("nativeValue="+box.value);`;
  // Native SICheckBox.Value is not Unity Toggle.value. Read the latter: it is
  // the actual rendered checked state used by the inherited adapter.
  const result=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);assert.doesNotMatch(result,/\[exception\]|\[compile\].*error/);const match=result.match(/nativeValue=(True|False)/);assert.ok(match,result);return match[1]==='True';
 }
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const filename=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>filename.count(),'owned QA identity');observations.ownedSave=await filename.inputValue();assert.match(observations.ownedSave,/^FM27Access QA /);await activate(button('Cancelar'));
  if(!await button('Conselho e Relatórios').count()&&!await button('Personalizar conselho').count())await responsibilities();
  if(await button('Conselho e Relatórios').count())await activate(button('Conselho e Relatórios'));
  observations.openers=[];
  for(let i=0;i<3;i++){await open(i);observations.openers.push({index:i,selected:originalTab,origin});await close();}
  checks.push('All three original customization buttons open their real native card, focus its title, suppress background and restore their exact distinct opener');
  await open();const tabs=await page.getByRole('tab').allTextContents();assert.deepEqual(tabs,['Recrutamento','Oferecer via TransferRoom','Desenvolvimento','Treinos','Contratações para a Equipa Técnica']);
  observations.tabs=[];
  for(const name of tabs){
   await select(name);await audit('Actual advice customization: '+name);
   if(name==='Oferecer via TransferRoom'){
    observations.tabs.push({name,checkboxes:await snapshot(),fields:await page.getByRole('combobox').evaluateAll(es=>es.map(e=>({name:e.getAttribute('aria-label'),value:e.options[0]?.text})))});
    const fields=page.locator('select[data-dd]');for(let i=0;i<await fields.count();i++){const id=await fields.nth(i).getAttribute('id'),field=page.locator('#'+id),before=await field.locator('option').first().textContent();assert.equal(await field.isEnabled(),true);await field.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');await page.keyboard.press('Enter');const text=await(await response).text();assert.ok(text.startsWith('<'),text);await poll(()=>field.getAttribute('data-loaded'),'actual TransferRoom default options');await c.cancelDropdown();assert.equal(await field.locator('option').first().textContent(),before);assert.equal(await field.evaluate(e=>e===document.activeElement),true);}checks.push('TransferRoom native default form: every enabled dropdown opens/cancels with unchanged value and exact focus; native-disabled fields retained');continue;
   }
   const baseline=await snapshot();assert.ok(baseline.length>1);assert.equal(baseline[0].label,'Todos os conselhos da equipa técnica');observations.tabs.push({name,baseline});
   // Keep the native edit session intact across multiple history operations.
   // Late native checkbox updates must arrive through SSE, without F5/navigation.
   const history=[baseline];observations.tabs.at(-1).history=history;
   for(const [index,option] of baseline.slice(1,4).entries()){
    await activate(page.getByRole('checkbox',{name:option.label,exact:true}),'Space');
    const expected=baseline.map((r,i)=>i>0&&i<=index+1?{...r,checked:!r.checked}:{...r});
    expected[0].checked=expected.slice(1).every(r=>r.checked);
    // Wait for every intended value, including the dependent all-topics switch.
    // A snapshot taken while its native/SSE update is pending is not an oracle.
    await poll(async()=>JSON.stringify(await snapshot())===JSON.stringify(expected),'complete native topic draft '+index);
    assert.equal(await nativeCheckbox(page.getByRole('checkbox',{name:option.label,exact:true})),!option.checked);
    assert.equal(await nativeCheckbox(page.getByRole('checkbox').first()),expected[0].checked);
    history.push(expected);
   }
   let olderBody;
   if(name==='Recrutamento'){
    // Retain an ACTUAL earlier native body, including its original read order.
    // Deliver it late below; do not manufacture checkbox values or replay edits.
    await page.route('**/body?c=1',async route=>{const response=await route.fetch();olderBody={status:response.status(),headers:response.headers(),body:await response.text()};await route.fulfill({response});},{times:1});
    await page.evaluate(()=>FM.core.refresh());await settled();assert.ok(olderBody);
   }
   for(let i=2;i>=0;i--){await activate(button('Anular'));await poll(async()=>JSON.stringify(await snapshot())===JSON.stringify(history[i]),'complete native topic Undo '+i);for(const option of history[i].slice(1,4))assert.equal(await nativeCheckbox(page.getByRole('checkbox',{name:option.label,exact:true})),option.checked);}
   for(let i=1;i<4;i++){await activate(button('Refazer'));await poll(async()=>JSON.stringify(await snapshot())===JSON.stringify(history[i]),'complete native topic Redo '+i);for(const option of history[i].slice(1,4))assert.equal(await nativeCheckbox(page.getByRole('checkbox',{name:option.label,exact:true})),option.checked);}
   await activate(button('Repor'));await poll(async()=>JSON.stringify(await snapshot())===JSON.stringify(baseline),'every original native topic after Reset');assert.equal(await button('Confirmar').isDisabled(),true);
   if(olderBody)for(const hard of [false,true]){
    await page.route('**/body?c=1',route=>route.fulfill(olderBody),{times:1});
    await page.evaluate(()=>{window.qaLateBodyState=null;const hook=()=>{qaLateBodyState=Array.from(document.querySelectorAll('input[type=checkbox]')).map(e=>({label:e.labels[0].textContent.trim(),checked:e.checked,disabled:e.disabled}));FM.core.afterRefresh.splice(FM.core.afterRefresh.indexOf(hook),1);};FM.core.afterRefresh.push(hook);});
    await page.evaluate(hard=>FM.core.refresh(hard),hard);
    await poll(()=>page.evaluate(()=>qaLateBodyState!==null),'first late-body application');
    const applied=await page.evaluate(()=>qaLateBodyState);
    // The deliberately old response also carries old footer availability.
    // Fetch the current real body before any subsequent native command.
    await settled();await page.evaluate(()=>FM.core.refresh());await settled();
    assert.deepEqual(applied,baseline,'Older actual native body must not overwrite newer checked values, hard='+hard);
    assert.deepEqual(await snapshot(),baseline);assert.equal(await button('Confirmar').isDisabled(),true);
   }
   if(olderBody)checks.push('Transport-order fault injection: an actual earlier native draft body cannot overwrite newer Reset checkbox values during either morph or full replacement; fresh native footer restored before continuing');
   checks.push(name+': three topic drafts Undo/Redo through every history level, actual native checkbox values match each DOM state, and Reset restores every original checkbox without reopening or manual refresh');
   const label=baseline[1].label,field=()=>page.getByRole('checkbox',{name:label,exact:true});assert.equal(await nativeCheckbox(field()),baseline[1].checked);
   await activate(field(),'Space');await poll(async()=>await nativeCheckbox(field())!==baseline[1].checked,'actual native checkbox draft');assert.equal(await field().isChecked(),!baseline[1].checked);await poll(()=>button('Confirmar').isEnabled(),'native edit footer becomes dirty');
   await activate(button('Confirmar'));await close();await open();await select(name);const expected=baseline.slice(1).map((r,i)=>i===0?{...r,checked:!r.checked}:r);await poll(async()=>JSON.stringify((await snapshot()).slice(1))===JSON.stringify(expected),'exact committed individual topics after reopening');const changed=await snapshot();assert.deepEqual(changed.slice(1),expected,'Only the committed individual topic changes after reopening');observations.tabs.at(-1).masterAfterCommit=changed[0];
   await activate(field(),'Space');await poll(()=>button('Confirmar').isEnabled(),'inverse native edit becomes dirty');await activate(button('Confirmar'));await close();await open();await select(name);await poll(async()=>JSON.stringify(await snapshot())===JSON.stringify(baseline),'complete native inverse after reopening');assert.deepEqual(await snapshot(),baseline,'Explicit native inverse survives reopening');assert.equal(await nativeCheckbox(field()),baseline[1].checked);assert.equal(await button('Confirmar').isDisabled(),true);
   checks.push(name+': exact official master caption; individual topic native draft, Confirm, persistence after reopen, inverse Confirm/reopen, every baseline checkbox restored');
   console.log('Native topic Confirm/reopen/inverse passed:',name);
  }
  await select('Recrutamento');await close();await c.dropdown('FM Menu');await c.choose('Preferências');await poll(()=>page.getByRole('checkbox',{name:'Sons do Jogo',exact:true}).count(),'native Preferences switch labels');assert.equal(await page.getByRole('checkbox',{name:/^(Ligado|Desligado|Sim|Não)$/,exact:true}).count(),0);await audit('Preferences conditional state text still resolves to actual setting names');await activate(button('Close'));
  checks.push('Preferences On/Off bindings retain setting names; no preference was changed');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  const result={success:true,mvid,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/advice-customization.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
 }catch(error){fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/advice-customization-failure.json'),JSON.stringify({error:error.stack,mvid,checks,audits,observations,checkboxes:await snapshot(),updateTrace:await page.evaluate(()=>qaAdviceUpdates),inputTrace:await page.evaluate(()=>qaInputTrace),uiState:await page.evaluate(()=>({focused:document.activeElement?.id,refreshing:FM.core.refreshing,pending:FM.core.pending,blur:FM.core.blur,dialog:FM.dialog.state,returning:FM.dialog.returning})),html:await page.locator('#fmroot').innerHTML(),native:await c.fetchText('/gui'),errors:c.errors,requestFailures:c.requestFailures,trace:await c.transportTrace()},null,2));throw error;}
 finally{await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
