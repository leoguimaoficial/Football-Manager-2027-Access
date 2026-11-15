// Men's FC Porto tactics audit, using the sole original production client.
// Opens/cancels native controls only: no lineup, role, instruction or tactic edit.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],observations=[],audits=[];let success=false,failure;
 const root=()=>page.locator('#fmroot'),button=n=>page.getByRole('button',{name:n,exact:true}),tab=n=>page.getByRole('tab',{name:n,exact:true});
 const out=path.resolve(__dirname,'../../artifacts/tests/web/tactics-audit.json');fs.mkdirSync(path.dirname(out),{recursive:true});
 const health=JSON.parse(await c.fetchText('/health'));
 async function record(name){checks.push(name);console.log('PASS',name);}
 async function native(name){await poll(async()=>new RegExp('^\\[0\\] Panel name="'+name+'"').test(await c.fetchText('/gui')),'native '+name);await settled();}
 async function snapshot(name){fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tactics-audit-live-'+name+'.html'),await root().innerHTML());observations.push({name,title:await page.title(),text:(await root().innerText()).slice(-22000)});}
 async function act(control,destination){await c.activate(control);if(destination)await native(destination);}
 async function close(){await act(button('Close'),'TacticalPlannerTool');await poll(()=>tab('Panorâmico').count(),'returned tactics');await settled();}
 async function mode(name){await act(tab(name));await poll(async()=>await tab(name).getAttribute('aria-selected')==='true','native pitch mode '+name);await settled();}
 function selection(){return page.locator('table').filter({has:page.getByRole('columnheader',{name:/^Esc/})}).last();}
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});assert.deepEqual(a.violations,[]);}
 try{
  assert.equal(await page.title(),'Tácticas');await native('TacticalPlannerTool');
  await mode('Panorâmico');
  const tactic=await root().getByText(/^Active tactic, slot/).innerText();assert.match(tactic,/slot 1: 4-3-3/);observations.push({tactic});
  if(process.argv.includes('--details')){
   const prior=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../../artifacts/tests/web/tactics-audit-picker-cohort.json'),'utf8'));
   assert.equal(prior.mvid,health.coreMvid);assert.equal(prior.checks.length,4);
   checks.push(...prior.checks);observations.push({resumedFrom:'tactics-audit-picker-cohort.json',testSelectorFailure:prior.failure});
  }else{
  assert.equal(await root().getByText('Select in or out of possession to see pitch view here',{exact:true}).count(),1,'Explicit user request: inherited FM26 panoramic presentation');
  assert.equal(await page.getByRole('combobox',{name:/^S\d+$/}).count(),0,'FM26 hides the duplicate substitutes strip');
  assert.equal(await root().getByText('Suplentes',{exact:true}).count(),0);
  assert.equal(await root().getByText('Intensidade, 4 of 0 to 6',{exact:true}).count(),1);assert.equal(await root().getByText('Complexidade, 47 of 0 to 100',{exact:true}).count(),1);
  assert.equal(await button('Intensidade, 4 of 0 to 6').count(),0,'Native CanSubmit=false/no command');
  await audit('Inherited panoramic presentation and hidden duplicate substitutes strip');await record('Panoramic and hidden substitutes strip retain FM26 presentation with actual readonly header gauges');
  let row=selection().locator('tbody tr').filter({has:button('Diogo Costa')});assert.equal(await row.count(),1);
  await act(row.getByRole('button',{name:'Player Instructions',exact:true}),'PlayerInstructionsCard');
  await poll(()=>button('Diogo Costa').count(),'correct selected person');await settled();await snapshot('pi-ip');
  assert.equal(await tab('Jogador').getAttribute('aria-selected'),'true');
  await act(tab('Sem Posse de Bola'));await poll(()=>button('DESARME, Padrão').count(),'native out-of-possession instructions');await settled();
  await act(button('DESARME, Padrão'),'PlayerInstructionsEditCard');await snapshot('pi-oop-editor');
  await act(button('Back'),'PlayerInstructionsCard');await poll(async()=>await tab('Sem Posse de Bola').getAttribute('aria-selected')==='true','retained native possession tab');await settled();
  assert.equal(await button('DESARME, Padrão').count(),1);await close();
  row=selection().locator('tbody tr').nth(1);await act(row.getByRole('button',{name:'Player Instructions',exact:true}),'PlayerInstructionsCard');
  await poll(()=>root().getByText(/Todos os elementos que estejam a jogar como Defesa \(Direito\)/).count(),'exact vacant right defender');assert.equal(await button('Diogo Costa').count(),0);await close();
  await record('Table PI opens exact assigned/vacant native positions; both possession tabs/editor Back preserve context without committing a value');
  row=selection().locator('tbody tr').nth(1);await act(row.getByRole('button',{name:'Escolha de jogadores',exact:true}),'PlayerSelectionCard');await snapshot('position-picker');
  // Picker closure is the native card action, never a player choice.
  await close();await record('Empty position XI picker opens the actual native player-selection card and cancels');
  for(const name of ['Conselho','Tactic options','Add tactic','Controlo da Posse de Bola, Estilo','Positiva, Mentalidade','Escolha Rápida','Informação da Selecção']){
   const select=await c.dropdown(name);const options=await select.locator('option').allTextContents();assert.ok(options.length>0);observations.push({dropdown:name,options});await c.cancelDropdown();
  }
  await record('Native advice, tactic options/add, style, mentality, quick-pick and table views load options and cancel');
  }
  for(const name of ['Eficácia, 48 of 100']){
   await act(button(name).first(),'TacticalEffectivenessCard');await snapshot(name.split(',')[0]);
   assert.equal(await root().getByText('Eficácia global: 48 of 100',{exact:true}).count(),1);
   assert.equal(await root().getByText('Pontuação provisória até ser seleccionado um onze inicial completo.',{exact:true}).count(),1);
   const team=page.getByRole('checkbox',{name:'Visão da equipa',exact:true}),checked=await team.isChecked();
   await c.activate(team,'Space');await poll(async()=>await team.isChecked()!==checked,'native team view');await snapshot('efficacy-team-view');
   await c.activate(team,'Space');await poll(async()=>await team.isChecked()===checked,'native detail view restored');
   const back=page.waitForResponse(r=>new URL(r.url()).pathname==='/history');await page.keyboard.press('Alt+[');assert.equal(await(await back).text(),'');await native('TacticalPlannerTool');
  }
  await act(button('Filtrar'));await snapshot('filter');
  if(await button('Close').count())await close();else{await page.keyboard.press('Escape');await settled();}
  await record('Native efficacy detail card and selection filter open/close; intensity/complexity are actual readonly native meters');
  await act(button('GRC').first(),'PlayerInstructionRoleEditCard');await snapshot('role-ip');await close();
  await act(button('GRL').first(),'PlayerInstructionRoleEditCard');await snapshot('role-oop');await close();
  await record('Both panoramic role buttons open their native role detail/editor without selecting a role');
  for(const name of ['Com Posse de Bola','Sem Posse de Bola']){await mode(name);assert.equal(await page.locator('[data-pitch="browse"]').count(),1);await snapshot(name==='Com Posse de Bola'?'pitch-ip':'pitch-oop');}
  await mode('Ambos');await poll(async()=>await page.locator('[data-pitch="browse"]').count()===2,'both native phase pitches');await settled();
  assert.equal(await page.getByRole('heading',{name:'Com Posse de Bola',exact:true}).count(),1);assert.equal(await page.getByRole('heading',{name:'Sem Posse de Bola',exact:true}).count(),1);
  const duplicates=await root().locator('[id]').evaluateAll(xs=>xs.map(x=>x.id).filter((id,i,a)=>a.indexOf(id)!==i));assert.deepEqual(duplicates,[]);
  const sourceIds=[];
  for(let index=0;index<2;index++){
   const area=page.locator('.fm-pitch').nth(index),cell=area.locator('[data-pitch="browse"] [data-slot="GK"]');
   const cellId=await cell.getAttribute('id'),moveId=await cell.getAttribute('data-move');assert.ok(moveId);sourceIds.push(moveId);
   await cell.focus();await page.keyboard.press('Enter');await poll(()=>area.getByRole('menu').isVisible(),'own pitch menu');
   const change=area.getByRole('menuitem',{name:'Change pitch position',exact:true});await change.focus();await page.keyboard.press('Enter');
   await poll(()=>area.locator('[data-pitch="move"]').isVisible(),'own move grid');assert.equal(await page.locator('.fm-pitch').nth(1-index).locator('[data-pitch="browse"]').isVisible(),true);
   await page.keyboard.press('Escape');await poll(()=>page.evaluate(id=>document.activeElement.id===id,cellId),'own source focus after cancel');
   // Same-position request validates the real owner/player/phase but cannot
   // execute a drag/drop or alter a business value.
   const refusal=await page.evaluate(async id=>(await fetch('/tactics/move?source=GK&dest=GK&id='+id+'&screen='+encodeURIComponent(FMTOKEN),{method:'POST'})).text(),moveId);
   assert.equal(refusal,'That player is already there.');
  }
  assert.notEqual(sourceIds[0],sourceIds[1]);
  const oop=page.locator('.fm-pitch').nth(1);await oop.locator('[data-pitch="browse"] [data-slot="GK"]').focus();await page.keyboard.press('Enter');
  const instructions=oop.getByRole('menuitem',{name:'Edit player instructions',exact:true});await instructions.focus();await page.keyboard.press('Enter');await native('PlayerInstructionsCard');
  await poll(()=>button('Diogo Costa').count(),'actual second pitch person');
  // Native opening defaults to IP, even when the actual source phase is OOP.
  // Preserve that behavior and exercise the native phase tab explicitly.
  await act(tab('Sem Posse de Bola'));await poll(()=>button('DESARME, Padrão').count(),'native OOP instructions');await close();
  await poll(()=>page.evaluate(()=>document.activeElement.id==='bc1-GK'),'second pitch exact modal-origin focus');
  await audit('Both independently scoped phase pitches');await snapshot('both');await record('Both uses original pitch grids under native phase headings: unique IDs, independent menus/move-cancel/source focus, validated source handles and second-pitch PI/native phase tab');
  await mode('Panorâmico');
  const firstCount=await selection().locator('tbody tr').count();assert.equal(firstCount,50);await act(button('Next page').first());await poll(()=>button('Next page').first().isDisabled(),'last native table page');
  const secondCount=await selection().locator('tbody tr').count();assert.ok(secondCount>0&&secondCount<50);observations.push({firstCount,secondCount});await act(button('Previous page').first());await poll(async()=>await selection().locator('tbody tr').count()===50,'first page restored');
  await record('Native table paging covers both harvested pages and restores page one');
  await act(button('Instruções à Equipa'));await poll(()=>page.locator('select[data-ti]').count(),'native consolidated team instructions');await settled();await snapshot('team-ip');
  const instructionsTable=()=>page.locator('table').filter({has:page.getByRole('columnheader',{name:'Instruction',exact:true})});
  assert.equal(await instructionsTable().locator('tbody tr').count(),19);
  const fields=page.locator('select[data-ti]');assert.equal(await fields.count(),17);
  const beforeFields=await fields.evaluateAll(xs=>xs.map(x=>({id:x.id,current:x.getAttribute('data-ti-cur'),value:x.value})));
  for(let i=0;i<beforeFields.length;i++){
   const field=page.locator('#'+beforeFields[i].id);await field.focus();await page.keyboard.press('Enter');await poll(()=>field.getAttribute('data-loaded'),'native instruction options');
   assert.ok(await field.locator('option').count()>1);await page.keyboard.press('ArrowDown');await page.keyboard.press('Escape');
   assert.equal(await field.inputValue(),beforeFields[i].value,'Escape must not commit a browsed instruction');
  }
  const firstField=beforeFields[0];
  const unchanged=await page.evaluate(async field=>(await fetch('/teaminstructions/set?id='+field.id+'&opt='+field.current+'&screen='+encodeURIComponent(FMTOKEN),{method:'POST'})).text(),firstField);
  assert.equal(unchanged,'ok','Current native value confirmed without a native change');
  const compound=button('Apoiar Corridas com Bola, Equilibrado'),compoundId=await compound.getAttribute('id');await act(compound);await poll(()=>page.getByRole('menu').count(),'actual native compound instruction menu');
  observations.push({name:'native compound instruction menu',text:await page.getByRole('menu').innerText()});await page.keyboard.press('Escape');await settled();
  await poll(()=>page.evaluate(id=>document.activeElement.id===id,compoundId),'exact compound instruction opener focus');
  await act(tab('Sem Posse de Bola'));await poll(async()=>await instructionsTable().locator('tbody tr').count()===10,'all native OOP summary rows');await settled();await snapshot('team-oop');await audit('Native team instructions');
  const stale=await page.evaluate(async field=>(await fetch('/teaminstructions/set?id='+field.id+'&opt='+field.current+'&screen='+encodeURIComponent(FMTOKEN),{method:'POST'})).text(),firstField);
  assert.notEqual(stale,'ok','An old native instruction capability cannot target another phase');
  await act(tab('Com Posse de Bola'));await act(button('Esquema Táctico'));await mode('Panorâmico');await snapshot('restored');
  assert.equal(await root().getByText(/^Active tactic, slot/).innerText(),tactic);assert.equal(await selection().locator('tbody tr').filter({has:button('Diogo Costa')}).count(),1);
  await record('Team instructions in both possession phases and original tactical scheme restored');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(e){failure=e.stack;await snapshot('failure').catch(()=>{});throw e;}
 finally{fs.writeFileSync(out,JSON.stringify({at:new Date().toISOString(),success,failure,mvid:health.coreMvid,checks,observations,audits,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,manualNVDA:false,businessChangesSubmitted:false},null,2));await c.browser.close();}
 console.log(JSON.stringify({success,checks:checks.length}));
})().catch(e=>{console.error(e.stack);process.exitCode=1});
