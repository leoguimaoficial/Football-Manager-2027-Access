// Native-menu regression in the owned Portuguese Arsenal QA career. No
// graphical appearance work, business leaf, simulation or Save is submitted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],audits=[],medicalStates=[];let mvid,developerTools;
 async function go(uid){
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first();
  const filter=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await filter.fill(await entry.locator(':scope > span').textContent());await entry.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');
  assert.match(await(await response).text(),/^Opening /);await settled();await filter.fill('');await settled();
 }
 async function audit(name){
  const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  audits.push({name,violations:a.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});
  console.log('audit',name);
 }
 async function openActions(owner){
  const button=page.getByRole('button',{name:'Acções',exact:true});assert.equal(await button.count(),1);
  const origin=await button.getAttribute('id');await c.activate(button);
  await poll(()=>page.getByRole('menu').count(),'native actions menu');await settled();
  assert.equal(await page.getByRole('menuitem',{name:owner,exact:true}).count(),1);
  return origin;
 }
 async function cancelActions(origin){
  await page.keyboard.press('Escape');await poll(()=>page.getByRole('button',{name:'Acções',exact:true}).count(),'actions origin');
  await settled();assert.equal(await page.evaluate(()=>document.activeElement.id),origin);
 }
 try{
  const initialHealth=JSON.parse(await c.fetchText('/health'));assert.equal(initialHealth.screen,'Live');mvid=initialHealth.coreMvid;developerTools=initialHealth.developerTools!==false;
  await go(989379584);await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const name=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>name.count(),'QA identity');
  const ownedSave=await name.inputValue();assert.match(ownedSave,/^FM27Access QA /);
  await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));

  // The native wrong-type object is never shown/attached. A real typed guard
  // must reject it before SIDropdown's native ShowDropdown can be entered.
  if(developerTools){
  const module=JSON.parse(await c.fetchText('/health')).coreMvid;
  const code='var menuGuardObject=new SI.Bindable.SIMenu();var menuGuardAssembly=System.AppDomain.CurrentDomain.GetAssemblies()[0];foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+module+'")menuGuardAssembly=a;var menuGuardType=menuGuardAssembly.GetType("FM27Access.Plugin.Adapters.DropdownAdapter");var menuGuardAdapter=System.Activator.CreateInstance(menuGuardType,new object[]{menuGuardObject});try{menuGuardType.GetMethod("ShowPopup").Invoke(menuGuardAdapter,null);System.Console.WriteLine("UNSAFE: wrong-type call returned");}catch(System.Reflection.TargetInvocationException e){System.Console.WriteLine(e.InnerException.GetType().Name+": "+e.InnerException.Message);}';
  const guarded=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);
  assert.match(guarded,/InvalidOperationException: The native SIMenu control is not an SIDropdown/);
  assert.doesNotMatch(guarded,/UNSAFE/);assert.equal(JSON.parse(await c.fetchText('/health')).screen,'Live');
  checks.push('Isolated actual SIMenu is rejected by the typed dropdown guard before native ShowDropdown; no graphical editor is opened');
  }

  await go(2078922496);await poll(async()=>await page.title()==='Meu Perfil','own QA coach profile');
  for(let pass=0;pass<3;pass++){
   const origin=await openActions('Teste FM27 Acesso');
   assert.equal(await page.getByRole('menuitem',{name:'Editar treinador',exact:true}).count(),1);
   if(pass===0)await audit('Own-coach native Actions menu');await cancelActions(origin);
  }
  checks.push('Unnamed-icon native LeftMouse Actions trigger opens the correct coach menu three times and Escape restores its exact origin; no business leaf activated');

  await go(1381513472);await c.activate(page.getByRole('button',{name:'Misa',exact:true}));
  await poll(async()=>await page.title()==='Relatório de jogador','Misa report');
  const playerOrigin=await openActions('Misa');
  const training=page.getByRole('menuitem',{name:'Treino',exact:true});await training.focus();await page.keyboard.press('ArrowRight');
  await poll(async()=>await page.getByRole('menuitem').count()>2&&await page.locator('[data-fm-menu-back]').count()>0,'native training submenu');
  await page.keyboard.press('ArrowLeft');await poll(async()=>await page.locator('[data-fm-menu-back]').count()===0,'native player root');
  await cancelActions(playerOrigin);await audit('Player profile after native Actions return');
  checks.push('Player Actions resolves Misa, expands/collapses its native training folder and Escape retains the player-profile origin');

  await go(3706171392);await poll(async()=>await page.title()==='Clube','club overview');
  if(developerTools){
   const native=await c.fetchText('/subtree?name=BottomFrame&count=5');assert.match(native,/SIRadialGauge/);assert.match(native,/Global/);
  }
  const indicator=page.getByText('Global: 100 of 0 to 100',{exact:true});assert.equal(await indicator.count(),1);
  assert.equal(await page.getByText('INSTALAÇÕES DO CLUBE: 100 of 0 to 100',{exact:true}).count(),0,'The gauge retains its own native Global caption instead of borrowing the card heading');
  assert.doesNotMatch(await page.locator('#fmroot').innerText(),/, gauge,/);
  const totals=await page.locator('p').filter({hasText:/^(Contratações|Vendas) \(/}).allTextContents();
  assert.equal(totals.length,2);assert.ok(totals.every(t=>/\(2026\/27\):.*€.*\d+ jogadoras?/.test(t)));
  checks.push('Native club transfer totals keep type/season, money and player count in two live-tracked paragraphs');
  assert.equal(await page.getByRole('button',{name:'Nationality',exact:true}).count(),0);
  const country=page.getByRole('button',{name:'Inglaterra',exact:true}).first(),countryOrigin=await country.getAttribute('id');
  await c.activate(country);await poll(async()=>await page.title()!=='Clube','actual country destination');
  assert.match(await page.locator('#fmroot').innerText(),/Inglaterra/);
  await c.activate(page.getByRole('button',{name:'Back',exact:true}));await poll(async()=>await page.title()==='Clube','club history return');
  await settled();assert.equal(await page.evaluate(()=>document.activeElement.id),countryOrigin);await audit('Club native gauge and paired country action');
  checks.push('Native Global gauge retains its 100/0/100 value without repeated role text; paired country caption opens England and Back restores the flag control');

  await go(3979388416);await poll(async()=>await page.title()==='Avaliação de risco','medical risk');
  const tabs=page.getByRole('tab'),captions=await tabs.allTextContents();assert.deepEqual(captions,['Muito Elevado','Elevado','Considerável','Normal','Baixo']);
  const selected=await page.getByRole('tab',{selected:true}).textContent();
  await tabs.first().focus();
  for(const [key,expected] of [['ArrowLeft',4],['ArrowRight',0],['End',4],['Home',0],['ArrowRight',1]]){
   await page.keyboard.press(key);
   assert.equal(await tabs.nth(expected).evaluate(e=>e===document.activeElement),true,key+' moves focus within its native tablist');
   assert.equal(await page.getByRole('tab',{selected:true}).textContent(),selected,'Focus movement must not change native selection');
  }
  await c.activate(tabs.nth(1));assert.equal(await tabs.nth(1).getAttribute('aria-selected'),'true');
  for(const caption of captions){
   const tab=page.getByRole('tab',{name:caption,exact:true});await c.activate(tab);assert.equal(await tab.getAttribute('aria-selected'),'true');
   // Native selection changes before the asynchronously bound table/zero-state
   // arrives. Stable DOM alone is not a loaded medical result.
   let emptyText=null;
   await poll(async()=>{
    if(await page.getByRole('combobox',{name:'Acções',exact:true}).count())return true;
    // Release exposes the game's localized zero-count result in the actual DOM.
    // Native EmptyText pointer/type correlation remains a Debug-only comparison.
    const texts=developerTools?await c.visibleEmptyTexts():(await page.locator('#fmroot > p').allTextContents()).filter(t=>/^0\b.*risco de lesão/i.test(t));
    for(const text of texts)if(await page.getByText(text,{exact:true}).count()){emptyText=text;return true;}
    return false;
   },'actual native medical result for '+caption);
   const actionsPresent=await page.getByRole('combobox',{name:'Acções',exact:true}).count();
   const state={caption,actionsPresent,rows:await page.locator('tbody tr').count(),emptyText:emptyText?await page.getByText(emptyText,{exact:true}).allTextContents():[]};medicalStates.push(state);
   if(actionsPresent){
    state.riskReadouts=await page.locator('tbody tr td:last-child').allTextContents();
    assert.ok(state.riskReadouts.length>0);
    assert.ok(state.riskReadouts.every(text=>!text.includes('Gauge:')&&/\d+ of \d+ to \d+/.test(text)),'Each native risk indicator retains its own rating, full numeric scale and explanation');
    const actions=await c.dropdown('Acções'),menuOrigin=await actions.getAttribute('id');
    assert.match((await actions.locator('option').allTextContents()).join('\n'),/Obter Actualização sobre Jogadores em Risco de Lesão no Arsenal/);
    await c.cancelDropdown();assert.equal(await page.evaluate(()=>document.activeElement.id),menuOrigin);
   }else assert.equal(state.emptyText.length,1,'An absent native Actions control is valid only in the observed empty medical view');
  }
  await c.activate(page.getByRole('tab',{name:selected,exact:true}));
  assert.equal(await page.getByRole('combobox',{name:'Nome',exact:true}).count(),0);await audit('Medical severity tabs and fixed-caption Actions');
  checks.push('Medical tab arrows wrap, Home/End move focus without native activation, Enter changes native selection; all five native severity states are read and original selection restored');
  checks.push(medicalStates.some(s=>s.actionsPresent)?'Available native medical Actions update choices open/cancel with exact focus return':'All five actual medical views are empty; native zero-player text is preserved without inventing an Actions control. Populated medical Actions not exercised in this run');

  await go(989379584);
  assert.deepEqual(audits.flatMap(a=>a.violations),[]);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  const finalHealth=JSON.parse(await c.fetchText('/health'));assert.equal(finalHealth.screen,'Live');assert.equal(finalHealth.coreMvid,mvid,'Exact native module remains unchanged during menu qualification');
  const report={success:true,mvid,ownedSave,checks,medicalStates,audits,scope:developerTools?'Native menu actions, isolated developer type guard and gauge subtree':'Release menu actions and observed gauge output; developer type guard/subtree introspection are outside this run',developerTypeGuardTested:developerTools,developerSubtreeInspected:developerTools,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,manualNVDA:false,simulationAdvanced:false};
  const output=path.resolve(__dirname,'../../artifacts/tests/web/functional-menus.json');fs.mkdirSync(path.dirname(output),{recursive:true});fs.writeFileSync(output,JSON.stringify(report,null,2));
  console.log(JSON.stringify(report,null,2));
 }catch(error){
  const report={error:String(error.stack||error),checks,medicalStates,audits,network:c.network,transportTrace:await c.transportTrace(),errors:c.errors,requestFailures:c.requestFailures,
   browser:await page.evaluate(()=>({token:FMTOKEN,focus:document.activeElement?.outerHTML,html:document.getElementById('fmroot')?.innerHTML,refreshing:FM.core.refreshing,pending:FM.core.pending,dropdown:FM.core.dd,inputTrace:window.qaInputTrace})),native:developerTools?await c.fetchText('/gui'):'Native inspection is absent in Release; production DOM evidence only'};
  const output=path.resolve(__dirname,'../../artifacts/tests/web/functional-menus-failure.json');fs.mkdirSync(path.dirname(output),{recursive:true});fs.writeFileSync(output,JSON.stringify(report,null,2));throw error;
 }finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
