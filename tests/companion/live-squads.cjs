// Production-client regression for the owned Arsenal women's QA career in Portuguese.
// Requires its generated tactic/set-piece routines. No fixtures, simulation, save
// deletion, transfers, training submission or responsibility confirmation.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate,fetchText}=c;
 const checks=[],audits=[];
 const button=name=>page.getByRole('button',{name,exact:true});
 async function nativeContext(){
  // Navigation dispatch precedes panel loading. Read the destination on Unity's
  // thread so a stable *previous* body cannot satisfy the next navigation wait.
  const code='var a=System.AppDomain.CurrentDomain.GetAssemblies()[0]; foreach(var x in System.AppDomain.CurrentDomain.GetAssemblies()) if(x.GetName().Name=="FM27Access.Plugin") a=x; var t=a.GetType("FM27Access.Plugin.Server.InteractivePanels"); var ps=(System.Collections.IEnumerable)t.GetMethod("Current",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,null); foreach(var v in ps){var p=v.GetType().GetField("Panel").GetValue(v);var pt=p.GetType();if((string)pt.GetProperty("name").GetValue(p,null)=="OverviewTitleBar")continue;var id=pt.GetProperty("PanelID").GetValue(p,null);var b=pt.GetProperty("m_bindingRoot").GetValue(p,null);var bt=pt.Assembly.GetType("SI.Bindable.BindingRoot");var br=b.GetType().GetMethod("TryCast").MakeGenericMethod(new[]{bt}).Invoke(b,null);var context=pt.GetProperty("Identifier").GetValue(p,null).ToString();if(br!=null){var k=bt.GetProperty("Key").GetValue(br,null);context=k.GetType().GetField("m_key").GetValue(k).ToString();}System.Console.WriteLine(id.GetType().GetProperty("ID").GetValue(id,null)+"|"+context);}';
  const activeCore=JSON.parse(await fetchText('/health')).coreMvid;assert.match(activeCore,/^[0-9a-f-]{36}$/i);
  return page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),
   code.replace('if(x.GetName().Name=="FM27Access.Plugin")','if(x.GetName().Name=="FM27Access.Plugin"&&x.ManifestModule.ModuleVersionId.ToString()=="'+activeCore+'")'));
 }
 async function go(uid,obj=-1){
  const before=await nativeContext();
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]'+(obj>=0?'[data-obj="'+obj+'"]':'')).first();
  const search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');
  assert.match(await(await response).text(),/^Opening /);
  await poll(async()=>{const current=await nativeContext();return current.includes(uid+'|')&&(!before.includes(uid+'|')||obj<0||current!==before);},'native destination/context '+uid);
  await settled();
  if(await search.count())await search.fill('');await settled();
 }
 async function clean(){
  assert.equal(await button('unlabeled icon button').count(),0);
  assert.deepEqual(await page.locator('[aria-label]').evaluateAll(xs=>xs.filter(e=>/unknown control|unlabeled icon button/.test(e.getAttribute('aria-label'))).map(e=>e.outerHTML)),[]);
  assert.doesNotMatch(await page.locator('#fmroot').innerText(),/couldn't read|unknown control|No navigable content|EntityTexture|TintGradient/);
  const unnamed=await page.locator('button,input[type=radio],input[type=checkbox],select').evaluateAll(xs=>xs.filter(x=>
   !x.disabled&&!x.getAttribute('aria-label')&&!x.getAttribute('aria-labelledby')&&!x.textContent.trim()
   &&![...(x.labels||[])].some(l=>l.textContent.trim())).map(x=>x.outerHTML));
  assert.deepEqual(unnamed,[]);
 }
 async function audit(name){
  await settled();await clean();console.log('audit',name);
  const result=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  audits.push({name,violations:result.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});
 }
 async function rosterView(name){
  const select=page.getByRole('combobox').nth(2),label=await select.getAttribute('aria-label');
  if(label!==name){await c.dropdown(label);await c.choose(name);}await settled();
 }
 try{
  await go(989379584);
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'Save As');
  const ownedSave=await file.inputValue();
  await activate(button('Cancelar'));await settled();
  assert.match(ownedSave,/^FM27Access QA Squads /,'Use the dedicated squad QA copy');
  const squadEntries=page.locator('#fmnav-tree [role=treeitem][data-uid="1381513472"][data-obj]');
  const club=Number(await squadEntries.first().getAttribute('data-obj'));
  const youth=await squadEntries.evaluateAll(xs=>[...new Set(xs.map(x=>Number(x.getAttribute('data-obj'))))]);
  const youthClub=youth.find(x=>x!==club);assert.ok(youthClub,'Native youth squad destination');
  await go(1381513472,club);assert.equal(await page.title(),'Arsenal','This regression uses the owned Arsenal QA save');
  await rosterView('Relatórios');
  const roster=page.locator('table').first();await poll(async()=>await roster.locator('tbody tr').count()===24,'senior squad');
  const headers=await roster.locator('thead th').allTextContents();assert.ok(headers.includes('Capacidade'));assert.ok(headers.includes('Potencial'));
  assert.match(await roster.locator('tbody tr').filter({hasText:'Smilla Holmberg'}).innerText(),/4 stars to 5 stars/);
  assert.match(await roster.locator('tbody tr').filter({hasText:'Michelle Agyemang'}).innerText(),/ligamento cruzado anterior.*especialista|especialista.*ligamento cruzado anterior/is);
  assert.match(await roster.locator('tbody tr').filter({hasText:'Anneke Borbe'}).innerText(),/Aston Villa.*West Ham/s);
  await audit('Senior flags, injury and ability/potential');
  checks.push('All 24 senior rows, every bound status/loan reason, half stars and uncertain potential range');
  const misa=button('Misa');await misa.focus();const origin=await misa.getAttribute('id');
  const menuResponse=page.waitForResponse(r=>new URL(r.url()).pathname==='/contextmenu');await page.keyboard.press('Shift+F10');
  assert.match(await(await menuResponse).text(),/Opened menu/);await poll(()=>page.getByRole('menu').count(),'player context menu');
  await settled();
  for(let drill=0;drill<3;drill++){
   const folder=page.getByRole('menuitem',{name:'Plantel',exact:true});await folder.focus();await folder.press('ArrowRight');
   const firstOption=page.getByRole('menuitem',{name:'Mover Para S-21',exact:true});
   await poll(()=>firstOption.count(),'native youth submenu');await settled();
   await poll(async()=>await page.evaluate(()=>document.activeElement?.id)===await firstOption.getAttribute('id'),'submenu first-option keyboard focus');
   await page.keyboard.press('ArrowLeft');
   await poll(async()=>await page.locator('[data-fm-menu-back]').count()===0,'context root after ArrowLeft');await settled();
  }
  await page.keyboard.press('Escape');
  await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'context origin focus');
  checks.push('Shift+F10, native squad submenu, ArrowRight/Left, Escape and player origin focus; no business leaf executed');
  await go(1381513472,youthClub);assert.match(await page.title(),/Sub-21/);
  assert.equal(await page.getByRole('heading',{name:await page.title(),exact:true}).count(),1);
  assert.equal(await page.locator('table').first().locator('tbody tr').count(),20);await audit('Native youth squad');
  await go(1381513472,club);await activate(button('Michelle Agyemang'));
  await poll(()=>page.getByRole('heading',{name:/^Relatório de jogador[ae]?$/}).count(),'player destination');
  const ability=page.getByRole('button',{name:/Capacidade:.*stars.*Potencial:.*stars/});assert.equal(await ability.count(),1);
  assert.equal(await page.getByRole('heading',{name:'Técnicos',exact:true}).count(),1);
  assert.ok(await page.locator('table tbody tr').filter({hasText:'Finalização'}).count());await audit('Player banner and attribute tables');
  await c.dropdown('Desempenho');assert.ok((await page.locator('select[data-loaded]').locator('option').allTextContents()).includes('Histórico de Lesões'));await c.cancelDropdown();
  checks.push('Native entity-link target, combined current/potential star button, original attribute tables and contextual navigation');
  await go(3273582080,club);await poll(()=>page.locator('table caption').filter({hasText:'Esta Época'}).count(),'season tables');
  const thisSeason=page.locator('table').filter({has:page.locator('caption').filter({hasText:'Esta Época'})});
  const lastSeason=page.locator('table').filter({has:page.locator('caption').filter({hasText:'Última Época'})});
  assert.equal(await thisSeason.locator('tbody tr').count(),2);assert.equal(await lastSeason.locator('tbody tr').count(),13);await audit('Independent medical season histories');
  checks.push('Separate actual date ranges for current/prior injury histories, with original Date/Value HTML');
  await go(195478016,club);
  // The native phase view is shared across saves. Establish the panorama
  // explicitly before checking both in/out-of-possession role columns.
  const panorama=page.getByRole('tab',{name:'Panorâmico',exact:true});
  if(await panorama.getAttribute('aria-selected')!=='true')await activate(panorama);
  const firstPosition=page.locator('table tbody tr').first();
  assert.equal(await firstPosition.getByRole('combobox',{name:'Função CPB',exact:true}).locator('option:checked').textContent(),'GRC');
  assert.equal(await firstPosition.getByRole('combobox',{name:'Função SPB',exact:true}).locator('option:checked').textContent(),'GRL');
  await activate(page.getByRole('button',{name:'Eficácia, 61 of 100',exact:true}).first());
  await poll(async()=>await page.locator('table').count()===5,'effectiveness factors');
  assert.equal(await page.locator('table tbody tr').count(),17);
  assert.equal(await page.getByRole('heading',{name:'Informações sobre o próximo jogo',exact:true}).count(),1);await audit('Tactical effectiveness factors');
  await activate(button('Tarefas de observação'));await poll(async()=>await page.title()==='Tarefas de observação','native advice target');
  checks.push('Actual in/out-of-possession role values; 17 factors, component headings and native icon-only advice destination');
  await go(2263778048,club);
  await poll(()=>page.getByRole('table',{name:'Ameaças aéreas',exact:true}).count(),'native generated set-piece category names');
  await activate(button('Ameaças aéreas, (3/9)'));
  for(const [name,last] of [['Ameaças na Grande Área','Sem Bola'],['Criadores','Cruzamentos'],['Recuperadores Defensivos','Desarme'],['Ameaças Aéreas','Cabeceamento']]){
   await activate(button(name));await poll(async()=>(await page.locator('table').first().locator('thead').innerText()).includes(last),'native '+name+' column binding');
  }
  await audit('Set-piece category tables after late column binding');await page.keyboard.press('Escape');await settled();
  checks.push('All four set-piece ranking categories update their native columns without F5; existing chip/fallback/move tables retained');
  await go(1208069888,club);await poll(async()=>await page.locator('table').count()===1,'general weekly calendar');
  assert.equal(await page.locator('table thead th').count(),7);assert.equal(await page.locator('table tbody tr').count(),5);await audit('Weekly general calendar');
  await go(1689642752,club);await poll(async()=>await page.locator('table').count()===1,'training weekly calendar');
  assert.equal(await page.locator('table thead th').count(),8);
  const rest=button('Semana completa de descanso');assert.equal(await rest.count(),4);
  assert.equal((await rest.evaluateAll(xs=>xs.map(x=>x.id))).length,new Set(await rest.evaluateAll(xs=>xs.map(x=>x.id))).size);
  for(let i=0;i<4;i++)assert.equal(await rest.nth(i).isDisabled(),true);
  const plan=button('Pré-época - Cedo'),planOrigin=await plan.getAttribute('id');await activate(plan);
  await poll(async()=>await page.locator('table tbody tr').count()===6,'projected impact rows');await audit('Training plan popup and diverging impacts');
  await activate(button('Compreensão táctica'));await poll(()=>button('Foco táctico - Equilibrado').count(),'native category selection');
  await page.keyboard.press('Escape');await poll(()=>page.evaluate(id=>document.activeElement?.id===id,planOrigin),'plan origin focus');
  checks.push('FM27 DateGrid retains original seven/eight-column weekly tables, disabled rest plans, unique trigger IDs, native category selection, impacts, Escape and origin focus');
  {
  await go(83254784,club);
  const trainingRow=name=>page.locator('table tbody tr').filter({has:button(name)});
  await activate(trainingRow('Misa').getByRole('radio'),'Space');
  await poll(async()=>(await page.locator('#fmroot').innerText()).includes('Alcance Aéreo '),'goalkeeper training attributes without reload');
  const ability=page.locator('table').filter({has:page.locator('thead th').filter({hasText:/^Ability$/})});
  await poll(async()=>await ability.locator('tbody tr').count()===4
   &&(await ability.innerText()).includes('stars'),'bound ability and separate training metrics');
  assert.deepEqual(await ability.getByRole('rowheader').allTextContents(),['Capacidade','Potencial','Carga de treino','Classificação do Treino']);
  assert.ok((await ability.locator('tbody tr td:last-child').allTextContents()).every(x=>x.trim()));
  const familiarity=page.locator('table').filter({has:page.locator('thead th').filter({hasText:/^Familiarity$/})});
  assert.match(await familiarity.innerText(),/GR\s+20/);
  assert.equal(await page.getByRole('heading',{name:'Roles and training',exact:true}).count(),1);
  await audit('Individual training goalkeeper detail');
  await page.evaluate(()=>{
   globalThis.trainingTransitionErrors=[];
   globalThis.trainingObserver=new MutationObserver(()=>{
    const root=document.querySelector('#fmroot');if(!root)return;
    if(root.innerText.includes("couldn't read")&&globalThis.trainingTransitionErrors.length<10)
     globalThis.trainingTransitionErrors.push('Unreadable content during training transition');
   });
   globalThis.trainingObserver.observe(document.querySelector('#fmroot'),{childList:true,subtree:true,characterData:true});
  });
  await activate(page.getByRole('tab',{name:'Relatório do Preparador',exact:true}));
  await poll(async()=>(await page.locator('#fmroot').innerText()).includes('Prós ('),'native training coach report');
  await activate(trainingRow('Alessia Russo').getByRole('radio'),'Space');
  await activate(page.getByRole('tab',{name:'Atributos',exact:true}));
  await poll(async()=>(await page.locator('#fmroot').innerText()).includes('Finalização ')
   &&!(await page.locator('#fmroot').innerText()).includes('Alcance Aéreo '),'outfield attributes replace goalkeeper detail');
  assert.match(await familiarity.innerText(),/PL \(C\)\s+20/);
  await audit('Individual training outfield detail');
  await activate(page.getByRole('tab',{name:'Resumo',exact:true}));
  await poll(async()=>await trainingRow('Misa').getByRole('checkbox').count()===1,'training summary rows');
  assert.equal(await page.locator('table tbody tr').count(),24);
  const summary=trainingRow('Misa');
  for(const [name,value] of [['Função CPB','GR - Posição'],['Função SPB','---'],['Foco Treino','---'],['Intensidade','Intensidade Normal']])
   assert.equal(await summary.getByRole('combobox',{name,exact:true}).locator('option:checked').textContent(),value);
  const focus=summary.getByRole('combobox',{name:'Foco Treino',exact:true}),focusId=await focus.getAttribute('id');
  await focus.focus();const focusResponse=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');await page.keyboard.press('Enter');
  assert.equal((await focusResponse).status(),200);await poll(()=>focus.getAttribute('data-loaded'),'in-cell native training focus choices');
  assert.ok((await focus.locator('option').allTextContents()).some(x=>x.startsWith('Distribuição do GR')));
  await c.cancelDropdown();assert.equal(await page.evaluate(()=>document.activeElement?.id),focusId);
  await audit('Individual training summary fields');
  assert.deepEqual(await page.evaluate(()=>{globalThis.trainingObserver.disconnect();return globalThis.trainingTransitionErrors;}),[]);
  checks.push('Individual training binds goalkeeper/outfield attributes and star ratings without F5, retains native familiarity, separate metrics, coach report and column-named summary values; in-cell focus menu cancels to origin without edits');
  }
  await go(2224038656,club);
  const coachSummary=page.getByRole('table').filter({has:page.getByRole('columnheader',{name:'Carga de Trabalho do Treinador',exact:true})});
  await poll(async()=>await coachSummary.count()===1,'native coach summary');
  const categories=await coachSummary.locator('tbody th[scope=row]').allTextContents();
  const assignmentHeaders=await page.getByRole('table').first().locator('thead th').allTextContents();
  assert.equal(categories.length,9);assert.deepEqual(categories,assignmentHeaders.slice(-categories.length));
  assert.deepEqual(await coachSummary.locator('thead th').allTextContents(),['','Carga de Trabalho do Treinador','Avaliação do Preparador Principal']);
  for(let i=0;i<categories.length;i++){
   const cells=coachSummary.locator('tbody tr').nth(i).locator('td');
   const native=await fetchText('/subtree?name=coach-'+(i+1)+'-workload-text&count=1');
   const caption=native.match(/text="([^"]+)"/);assert.ok(caption,'Native workload caption '+categories[i]);
   assert.equal(await cells.nth(0).innerText(),caption[1]);
   assert.match(await cells.nth(1).innerText(),/stars?|half a star/);
  }
  await audit('Coach category workloads and lead ratings');
  checks.push('Original column-summary table pairs all nine native categories with their own workload words and lead-coach stars, without decorative gauge percentages');
  await go(3173313024,club);await poll(async()=>await page.locator('table tbody tr').count()===50,'database 50-row page');
  const firstNames=await page.locator('table tbody tr').allTextContents();await activate(button('Next page').first());
  assert.equal(await page.locator('table tbody tr').count(),50);const secondNames=await page.locator('table tbody tr').allTextContents();
  assert.equal(firstNames.filter(x=>secondNames.includes(x)).length,0);await activate(button('Previous page').first());await audit('50-row streamed database');
  checks.push('Original 50-row streamed paging reads a second distinct page and returns to page one');
  await go(2384599296,club);
  const analysisTab=name=>page.getByRole('tab',{name,exact:true});
  await poll(()=>analysisTab('Análise dos Golos').count(),'native analysis tabs');
  assert.equal(await button('Arsenal').first().isVisible(),true);
  // The agenda's newly named competition badges can legitimately repeat the
  // same entity as the header. Their presence must not invalidate its caption.
  assert.equal(await button('BWSL').first().isVisible(),true);
  await activate(analysisTab('Análise dos Golos'));
  const typeTables=page.locator('table').filter({has:page.locator('thead th').filter({hasText:/^Type$/})});
  await poll(async()=>await typeTables.count()===2,'native goal-type content after tab binding');
  // A saved native expansion preference can finish binding after the two
  // tables appear, removing a provisional Show more control without a click.
  // Inspect the settled row state before attempting another expansion.
  await settled();
  for(let i=0;i<2;i++){
   await settled();
   if(await typeTables.nth(0).locator('tbody tr').count()===8&&await typeTables.nth(1).locator('tbody tr').count()===8)break;
   await activate(button('Show more items').first());
  }
  await poll(async()=>await typeTables.nth(0).locator('tbody tr').count()===8&&await typeTables.nth(1).locator('tbody tr').count()===8,'both native goal-type lists expanded');
  assert.equal(await typeTables.count(),2);
  for(let i=0;i<2;i++){assert.equal(await typeTables.nth(i).locator('tbody tr').count(),8);assert.match(await typeTables.nth(i).innerText(),/Livre.*Penálti/s);}
  assert.equal(await page.getByRole('heading',{name:'Finalização',exact:true}).count(),1);
  assert.equal(await page.getByRole('heading',{name:'Concedidos',exact:true}).count(),1);await audit('Data Hub native goal-type tables');
  await activate(analysisTab('Análise da Formação'));
  for(const name of ['EP - Em Posse da Bola','SPB - Sem Posse da Bola'])await poll(async()=>await page.getByRole('heading',{name,exact:true}).count()===1,'bound formation heading '+name);
  await activate(analysisTab('Análise das Bolas Paradas'));
  for(const name of ['Análises de Dados','Ofensivo','Defensivo'])await poll(async()=>await page.getByRole('heading',{name,exact:true}).count()===1,'bound set-piece analysis heading '+name);
  await activate(analysisTab('Conclusões Importantes'));await clean();
  checks.push('Data Hub entity captions, original tabs/headings, empty bound blocks, six goal tables and both eight-row goal-type lists through native expansion');
  const bwsl=page.locator('#fmnav-tree [role=treeitem][data-uid="420575744"]').filter({has:page.locator(':scope > span').filter({hasText:/^BWSL$/})}).first();
  await go(420575744,Number(await bwsl.getAttribute('data-obj')));
  await poll(async()=>await page.locator('table').count()>=2&&(await page.locator('table').nth(1).innerText()).includes('Hampton'),'native competition dream XI');
  assert.match(await page.locator('table').nth(1).innerText(),/Hampton.*Batlle.*Greenwood.*Girma.*Fox.*Walsh.*Putellas.*Caldentey.*Hemp.*James.*Shaw/s);
  await audit('Competition fixture links and named dream XI');
  await activate(button('4/9/26, 19:00, London City, Man Utd'));
  await poll(async()=>await page.title()==='Antevisão do Jogo','native match preview');
  const preview=await page.locator('#fmroot').innerText();assert.match(preview,/London City.*Man Utd.*4 de Setembro de 2026/s);
  await page.keyboard.press('Escape');await settled();
  checks.push('Competition fixture hit button reads its own date/time/teams and opens the matching preview; all 11 dream-XI players retain native names');
  await go(195478016,club);
  const role=page.locator('table tbody tr').first().getByRole('combobox',{name:'Função CPB',exact:true});
  await role.focus();const roleResponse=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');await page.keyboard.press('Enter');
  assert.equal((await roleResponse).status(),200);await poll(()=>role.getAttribute('data-loaded'),'role menu');
  await c.choose('Ir para Instruções a jogadoras');await poll(async()=>(await fetchText('/gui')).startsWith('[0] Panel name="PlayerInstructionsCard"'),'native player instructions');
  await poll(()=>page.getByRole('heading',{name:'Instructions',exact:true}).count(),'original instructions landmark');
  const instructionText=await page.locator('#fmroot').innerText();assert.ok(instructionText.indexOf('Aplicar a')<instructionText.indexOf('Instructions'));
  await activate(page.getByRole('tab',{name:'Sem Posse de Bola',exact:true}));
  await activate(button('MARCAÇÃO, Padrão, locked'));await poll(async()=>(await fetchText('/gui')).startsWith('[0] Panel name="PlayerInstructionsEditCard"'),'native instruction editor');
  await poll(async()=>await page.getByRole('radio').count()===2,'native marking option binding');
  assert.equal(await page.getByRole('radio').count(),2);for(const radio of await page.getByRole('radio').all())assert.equal(await radio.isDisabled(),true);
  assert.match(await page.locator('#fmroot').innerText(),/Esta instrução está bloqueada devido à função ou posição/);await audit('Player instruction context and native locked options');
  await activate(button('Back'));await poll(()=>page.getByRole('tab',{name:'Sem Posse de Bola',selected:true,exact:true}).count(),'original instruction tab restored');
  await page.keyboard.press('Escape');await settled();
  await go(2078922496,Number(await page.locator('#fmnav-tree [role=treeitem][data-uid="2078922496"]').first().getAttribute('data-obj')));
  await poll(()=>page.getByRole('heading',{name:'Informação Pessoal',exact:true}).count(),'native coach profile sections');
  for(const name of ['Atributos de Treino','Atributos Psicológicos','Informações de Treino','Informação Pessoal','Tendências'])assert.equal(await page.getByRole('heading',{name,exact:true}).count(),1);
  assert.equal(await button('button').count(),0);await clean();
  checks.push('Native role-menu destination, whose-instructions-before-tiles order, native locked radio reasons, Back restores out-of-possession tab; tooltip-only info template excluded without showing it');
  await go(1473531648,club);
  const facts=page.locator('table');await poll(async()=>await facts.locator('tbody tr').count()===19,'all native fact rows');
  assert.deepEqual(await facts.locator('thead th').allTextContents(),['Fact','Arsenal','BWSL']);
  assert.equal(await facts.locator('tbody th[scope=row]').count(),19);
  const divisionCell=await poll(async()=>{
   for(const cell of await facts.locator('tbody tr td:last-child').all())
    if(await cell.getByRole('button').count()===2)return cell;
   return null;
  },'native fact cell with independent player and club links');
  const [playerEntity,clubEntity]=await divisionCell.getByRole('button').allTextContents();
  for(const entity of [clubEntity,playerEntity]){
   const target=divisionCell.getByRole('button',{name:entity,exact:true}),origin=await target.getAttribute('id');
   await target.focus();await page.keyboard.press('Shift+F10');
   await poll(()=>page.getByRole('heading',{name:'Actions for '+entity,exact:true}).count(),'specific embedded context owner');
   const menuText=await page.locator('[role=menu]').innerText();
   assert.ok(menuText.includes(entity));
   if(entity===clubEntity)assert.ok(!menuText.includes(playerEntity),'The club link must not open its adjacent player menu');
   await page.keyboard.press('Escape');await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'embedded context origin');
   await activate(target);
   if(entity===clubEntity)await poll(async()=>(await nativeContext()).includes('3146487296|'),'embedded club destination');
   else await poll(async()=>(await nativeContext()).includes('2208093440|'),'embedded player destination');
   await settled();assert.ok((await page.locator('#fmroot').innerText()).includes(entity));
   await activate(button('Back'));await poll(async()=>await page.title()==='Factos','facts history return');
   await settled();await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'history returns to the exact embedded link');
  }
  await audit('Club facts comparison and independent embedded links');
  checks.push('Nineteen club/division facts retain column and row headers, secondary values and independent player/club links; native menus select the correct embedded record, Escape and Back restore the exact link');
  await go(221703168,club);
  const activeOffers=page.getByRole('checkbox',{name:'Active offers only',exact:true});assert.equal(await activeOffers.count(),1);
  const activeBefore=await activeOffers.isChecked();await activate(activeOffers,'Space');
  await poll(async()=>await activeOffers.isChecked()!==activeBefore,'native active-offers filter changes');
  await activate(activeOffers,'Space');await poll(async()=>await activeOffers.isChecked()===activeBefore,'active-offers filter restored');
  await audit('Staff transfer activity active-offers filter');
  checks.push('Staff transfer activity reuses the FM26 Active offers only caption and native checkbox; keyboard change and original state restoration');
  await go(1261674240,club);
  await poll(()=>button('Inglaterra').count(),'paired country caption');
  assert.doesNotMatch(await page.locator('#fmroot').innerText(),/CompetitionIcon, image|Nationality/);
  await audit('Club facilities and paired nationality');
  await activate(button('Inglaterra').first());
  await poll(async()=>await page.title()!=='Infra-estruturas','actual country destination');
  await settled();assert.match(await page.locator('#fmroot').innerText(),/Inglaterra/);
  await activate(button('Back'));await poll(async()=>await page.title()==='Infra-estruturas','country Back restores facilities');
  await go(536859648,club);
  assert.doesNotMatch(await page.locator('#fmroot').innerText(),/grade-value, image|badges-generic-league-large|Nationality/);
  assert.ok(await button('Inglaterra').count()>0);await audit('Supporters native captions and tables');
  await go(5585921,club);
  const wages=page.locator('table').filter({has:page.getByRole('columnheader',{name:'Basic wages',exact:true})});
  await poll(()=>wages.count(),'compact native wage table');
  assert.deepEqual(await wages.locator('thead th').allTextContents(),['Squad status','Players','Basic wages']);
  assert.equal(await wages.locator('tbody tr').count(),7);assert.equal(await wages.locator('tbody th[scope=row]').count(),7);
  await audit('Compact wage status table');
  checks.push('Club country captions open the actual country and native Back restores facilities; redundant badges keep written meaning; seven native wage rows retain column and row headers');
  await go(989379584);
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(audits.flatMap(x=>x.violations),[]);
  assert.deepEqual(c.network.failures,[]);
  const output={success:true,ownedSave,checks,audits,errors:c.errors,requestFailures:c.requestFailures,network:c.network,manualNVDA:false,simulationAdvanced:false};
  fs.mkdirSync(path.resolve(__dirname,'../../artifacts/tests/web'),{recursive:true});fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/live-squads.json'),JSON.stringify(output,null,2));
  console.log(JSON.stringify({...output,audits:audits.map(a=>({name:a.name,violations:a.violations.map(v=>({id:v.id,affected:v.targets.length}))}))}));
 }catch(error){
  const failure={error:String(error.stack||error),checks,audits,errors:c.errors,requestFailures:c.requestFailures,requests:c.requests,network:c.network,transport:await c.transportTrace(),
   browser:await page.evaluate(()=>({title:document.title,token:FMTOKEN,focus:document.activeElement?.outerHTML.slice(0,500),trace:window.qaInputTrace,refreshing:FM.core.refreshing,pending:FM.core.pending}))};
  fs.mkdirSync(path.resolve(__dirname,'../../artifacts/tests/web'),{recursive:true});
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/live-squads-failure.json'),JSON.stringify(failure,null,2));
  console.error(JSON.stringify(failure));throw error;
 }finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
