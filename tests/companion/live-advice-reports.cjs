// Native advice/report settings. Reversible drafts only, no Save or simulation.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],observations={};
 const footer=name=>page.getByRole('button',{name,exact:true});
 const frequencies=()=>page.getByRole('table').filter({has:page.getByRole('columnheader',{name:'Frequency',exact:true})});
 const advice=()=>page.getByRole('table').filter({has:page.getByRole('columnheader',{name:'Assigned to',exact:true})});
 const row=title=>page.getByRole('row').filter({has:page.getByRole('rowheader',{name:title,exact:true})});
 async function snapshot(){return page.locator('table tbody tr').evaluateAll(rs=>rs.map(r=>({title:r.cells[0].textContent,description:r.cells[1].textContent,value:r.querySelector('select')?.options[0]?.text})));}
 async function go(uid){const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');r.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await r).text(),/^Opening /);await poll(()=>page.evaluate(id=>FMTOKEN.split('\x1f').some(p=>p===id||p.startsWith(id+'#')),String(uid)),'actual native destination');await settled();await search.fill('');await settled();}
 async function open(field){await field.focus();const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');r.catch(()=>{});await page.keyboard.press('Enter');const text=await(await r).text();assert.ok(text.startsWith('<'),text);await poll(()=>field.getAttribute('data-loaded'),'actual native options');return field.locator('option').allTextContents();}
 async function reset(baseline){assert.equal(await footer('Repor').isEnabled(),true);await activate(footer('Repor'));await poll(async()=>JSON.stringify(await snapshot())===JSON.stringify(baseline),'all native advice settings restored');assert.equal(await footer('Confirmar').isDisabled(),true);}
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 try{
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned QA save identity');observations.ownedSave=await file.inputValue();assert.match(observations.ownedSave,/^FM27Access QA /);await activate(footer('Cancelar'));
  await go(3128627712);await activate(footer('Conselho e Relatórios'));await poll(()=>frequencies().count(),'native advice frequencies');await settled();
  const expected=['Objectivos de Jogador','Reuniões da Equipa Técnica','Relatórios de analista','Frequência de resumo de responsabilidades'];
  await poll(async()=>JSON.stringify(await frequencies().locator('tbody th').allTextContents())===JSON.stringify(expected),'official frequency captions');const baseline=await snapshot();observations.baseline=baseline;
  assert.match(baseline.find(r=>r.title===expected[3]).description,/Definir a frequência/);assert.ok(baseline.every(r=>r.title&&!/unavailable/i.test(r.title)));assert.equal(await footer('Confirmar').isDisabled(),true);await audit('Native advice/report fields with restored official titles');
  observations.frequencyChoices=[];
  for(const title of expected){const field=row(title).getByRole('combobox'),before=await field.locator('option').first().textContent();observations.frequencyChoices.push({title,choices:await open(field)});await c.cancelDropdown();assert.equal(await field.locator('option').first().textContent(),before);assert.equal(await field.evaluate(e=>e===document.activeElement),true);}
  checks.push('All four official frequency titles and the actual roundup description occupy the proper table columns; every native choice menu opens/cancels with original value/focus');
  const frequency=row(expected[0]).getByRole('combobox');await open(frequency);await c.choose('Cada Duas Semanas');await poll(async()=>await frequency.locator('option').first().textContent()==='Cada Duas Semanas','native frequency draft');assert.match(await c.fetchText('/gui'),/text="Cada Duas Semanas"/);await reset(baseline);
  checks.push('The real player-target report frequency changes in the native tree; Reset restores every original frequency/assignee with no confirmation or save');
  const titles=await advice().locator('tbody th').allTextContents();assert.ok(titles.length>=8);observations.adviceChoices=[];
  for(const title of titles){const field=row(title).getByRole('combobox'),before=await field.locator('option').first().textContent();observations.adviceChoices.push({title,choices:await open(field)});await c.cancelDropdown();assert.equal(await field.locator('option').first().textContent(),before);}
  const title='Providenciar Informação sobre Desenvolvimento de Jovens',field=row(title).getByRole('combobox');const options=await open(field),selected=await field.evaluate(e=>e.options[e.selectedIndex].text),alternative=options.find(t=>t!==selected);assert.ok(alternative);observations.alternateAdvisor={title,alternative};await c.choose(alternative);await poll(async()=>JSON.stringify(await snapshot())!==JSON.stringify(baseline),'native advisor draft');assert.equal(await footer('Confirmar').isEnabled(),true);assert.deepEqual((await snapshot()).filter(r=>r.title!==title),baseline.filter(r=>r.title!==title));await reset(baseline);await audit('Native report settings after all menu cancellations and draft inverses');
  checks.push('Every visible advisor menu supplies native eligible people; one actual youth-development advisor changes while all other rows remain identical, and Reset restores the entire baseline');
  // More than one edit is essential: intermediate Undo/Redo operations leave
  // both history buttons enabled and cannot be detected from a Boolean edge.
  const history=[baseline],secondFrequency=row(expected[1]).getByRole('combobox');
  await open(frequency);await c.choose('Cada Duas Semanas');history.push(await snapshot());
  await open(field);await c.choose(alternative);history.push(await snapshot());
  await open(secondFrequency);await c.choose('Cada Duas Semanas');history.push(await snapshot());
  observations.history=history;
  for(let i=2;i>=0;i--){await activate(footer('Anular'));await poll(async()=>JSON.stringify(await snapshot())===JSON.stringify(history[i]),'native multi-edit Undo step '+i);assert.equal(await footer('Refazer').isEnabled(),true);if(i)assert.equal(await footer('Anular').isEnabled(),true);}
  assert.equal(await footer('Confirmar').isDisabled(),true);
  for(let i=1;i<4;i++){await activate(footer('Refazer'));await poll(async()=>JSON.stringify(await snapshot())===JSON.stringify(history[i]),'native multi-edit Redo step '+i);assert.equal(await footer('Anular').isEnabled(),true);if(i<3)assert.equal(await footer('Refazer').isEnabled(),true);}
  await reset(baseline);
  checks.push('Three native edits (two frequencies and a Person reference) Undo and Redo one step at a time, including intermediate states with both buttons enabled; Reset restores the full baseline without losing the edit history');
  await activate(footer('Responsabilidades'));await poll(()=>page.getByRole('tab',{name:'Delegado',exact:true}).count(),'native responsibilities return');await activate(footer('Conselho e Relatórios'));await settled();assert.deepEqual(await snapshot(),baseline);assert.equal(await footer('Confirmar').isDisabled(),true);await go(989379584);
  checks.push('Leaving/reopening advice/report settings retains the restored native baseline and clean edit state');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  const result={success:true,mvid:JSON.parse(await c.fetchText('/health')).coreMvid,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/advice-reports.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
 }catch(error){fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/advice-reports-failure.json'),JSON.stringify({error:error.stack,checks,audits,observations,html:await page.locator('#fmroot').innerHTML(),native:await c.fetchText('/gui'),trace:await c.transportTrace(),errors:c.errors,requestFailures:c.requestFailures},null,2));throw error;}
 finally{await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
