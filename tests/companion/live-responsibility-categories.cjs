// Actual native responsibility categories and reversible drafts, never Save or simulation.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true,netLogPath:path.resolve(__dirname,'../../artifacts/tests/web/responsibility-categories-netlog.json')}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],observations={categories:[]};let originalTab,originalCategory,mvid;
 const footer=name=>page.getByRole('button',{name,exact:true});
 const table=()=>page.getByRole('table').filter({has:page.getByRole('columnheader',{name:'Responsibility',exact:true})});
 const rows=()=>table().locator('tbody tr').filter({has:page.locator('th[scope=row]')});
 async function snapshot(){return rows().evaluateAll(rs=>rs.map(r=>{const headers=[...r.closest('table').querySelectorAll('thead th')].map(h=>h.textContent),assigned=headers.indexOf('Assigned to'),group=headers.indexOf('Group');return{group:group<0?'':r.cells[group].textContent,duty:r.querySelector('th[scope=row]').textContent,assignee:r.querySelector('select')?.options[0]?.text||r.cells[assigned]?.textContent};}));}
 async function counts(){const m=await poll(async()=>/(\d+) Controlado por si, (\d+) Delegado à Equipa Técnica/.exec(await page.locator('#fmroot').innerText()),'actual native responsibility totals bound');return m.slice(1).map(Number);}
 async function go(uid){const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');r.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await r).text(),/^Opening /);await poll(()=>page.evaluate(id=>FMTOKEN.split('\x1f').some(p=>p===id||p.startsWith(id+'#')),String(uid)),'actual native destination');await settled();await search.fill('');await settled();}
 async function reset(baseline,totals){assert.equal(await footer('Repor').isEnabled(),true,'A native draft enables Reset');await activate(footer('Repor'));await poll(async()=>JSON.stringify(await snapshot())===JSON.stringify(baseline),'native baseline restored');assert.deepEqual(await counts(),totals);assert.equal(await footer('Confirmar').isDisabled(),true);}
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned save identity');observations.ownedSave=await file.inputValue();assert.match(observations.ownedSave,/^FM27Access QA /);await activate(footer('Cancelar'));
  await go(3128627712);originalTab=await page.getByRole('tab',{selected:true}).textContent();originalCategory=await page.getByRole('radio',{checked:true}).evaluate(e=>e.labels[0].textContent.trim());
  observations.originalTab=originalTab;observations.originalCategory=originalCategory;
  if(originalTab!=='Delegado')await activate(page.getByRole('tab',{name:'Delegado',exact:true}));
  const categories=await page.getByRole('radio').evaluateAll(rs=>rs.map(r=>r.labels[0].textContent.trim()));assert.ok(categories.length>=9,'All native responsibility categories');observations.categoryNames=categories;
  const totals=await counts();observations.totals=totals;
  for(const category of categories){
   console.log('Native responsibility category: '+category);
   const choice=page.getByRole('radio',{name:category,exact:true});if(!await choice.isChecked())await activate(choice,'Space');await poll(()=>table().count(),'native category table');await settled();
   assert.equal(await footer('Confirmar').isDisabled(),true,'No draft enters another category');assert.doesNotMatch(await page.locator('#fmroot').innerText(),/Some responsibility groups could not be read|responsibilities.*unreadable/i);const baseline=await snapshot();assert.ok(baseline.length>0);assert.deepEqual(await counts(),totals);
   const observation={category,baseline,dropdown:null,draft:null};observations.categories.push(observation);
   const audit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({category,violations:audit.violations.map(v=>v.id)});assert.deepEqual(audit.violations,[]);
   // Always inspect actual eligible choices, including categories with no delegated duty.
   const field=table().locator('select:not([disabled])').first();
   if(await field.count()){
    const id=await field.getAttribute('id'),before=await field.locator('option').first().textContent();await field.focus();const r=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');r.catch(()=>{});await page.keyboard.press('Enter');const reply=await(await r).text();assert.ok(reply.startsWith('<'),reply);await poll(()=>field.getAttribute('data-loaded'),'actual eligible staff');observation.dropdown={id,before,choices:await field.locator('option').allTextContents()};assert.ok(observation.dropdown.choices.length>0);await c.cancelDropdown();assert.equal(await field.locator('option').first().textContent(),before);assert.equal(await field.evaluate(e=>e===document.activeElement),true);assert.deepEqual(await snapshot(),baseline);
    if(category==='Equipa Técnica'){
     const started=Date.now();await page.evaluate(()=>FM.core.refresh(false,true));await poll(async()=>(await c.transportTrace()).some(e=>e.Route==='GET /body'&&e.Phase==='handling'&&Date.parse(e.StartedUtc)>=started),'actual native responsibility sweep running');await field.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');opened.catch(()=>{});await page.keyboard.press('Enter');const response=await opened,fragment=await response.text();assert.ok(fragment.startsWith('<'),fragment);assert.ok(new URL(response.url()).searchParams.has('screen'));await poll(()=>field.getAttribute('data-loaded'),'native options after concurrent body');observation.concurrentBodyOpen={choices:await field.locator('option').allTextContents(),trace:await c.transportTrace()};assert.deepEqual(observation.concurrentBodyOpen.choices,observation.dropdown.choices);await c.cancelDropdown();assert.deepEqual(await snapshot(),baseline);
    }
   }
   const take=rows().getByRole('button',{name:'Assumir controlo',exact:true}).and(page.locator('button:not([disabled])')).first();
   if(await take.count()){
    const duty=await take.locator('xpath=ancestor::tr').locator('th[scope=row]').textContent();await activate(take);await poll(async()=>JSON.stringify(await snapshot())!==JSON.stringify(baseline),'native category draft');const changed=await snapshot();observation.draft={duty,changed};assert.deepEqual(changed.filter(r=>r.duty!==duty),baseline.filter(r=>r.duty!==duty),'Only the acted duty changes');assert.deepEqual(await counts(),[totals[0]+1,totals[1]-1]);assert.equal(await footer('Confirmar').isEnabled(),true);await reset(baseline,totals);
   }else if(observation.dropdown&&observation.dropdown.choices.length>1){
    // Categories already controlled by the manager still have real delegation
    // commands. Exercise a native eligible alternative, then Reset the draft.
    const duty=await field.locator('xpath=ancestor::tr').locator('th[scope=row]').textContent();await field.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');opened.catch(()=>{});await page.keyboard.press('Enter');assert.ok((await(await opened).text()).startsWith('<'));await poll(()=>field.getAttribute('data-loaded'),'native delegation choices reopened');const selected=await field.evaluate(e=>e.options[e.selectedIndex].text),alternative=(await field.locator('option').allTextContents()).find(label=>label!==selected);assert.ok(alternative);await c.choose(alternative);await poll(async()=>JSON.stringify(await snapshot())!==JSON.stringify(baseline),'native alternate delegation draft');const changed=await snapshot();observation.draft={method:'native eligible alternate',duty,alternative,changed,totals:await counts()};assert.equal(changed.length,baseline.length);assert.deepEqual(changed.filter(r=>r.duty!==duty),baseline.filter(r=>r.duty!==duty));assert.equal(observation.draft.totals.reduce((a,b)=>a+b),totals.reduce((a,b)=>a+b));assert.equal(await footer('Confirmar').isEnabled(),true);await reset(baseline,totals);
   }
   // Leaving/reopening observes the inverse from native data, not only the current DOM.
   await go(989379584);await go(3128627712);if(await page.getByRole('tab',{name:'Delegado',exact:true}).getAttribute('aria-selected')!=='true')await activate(page.getByRole('tab',{name:'Delegado',exact:true}));if(!await choice.isChecked())await activate(choice,'Space');await settled();assert.deepEqual(await snapshot(),baseline);assert.deepEqual(await counts(),totals);assert.equal(await footer('Confirmar').isDisabled(),true);
   checks.push(category+': native category data, eligible choices/cancel'+(observation.draft?', actual '+(observation.draft.method||'Take Control')+' draft/Reset':'')+', leave/reopen and exact baseline restoration');
  }
  if(!await page.getByRole('radio',{name:originalCategory,exact:true}).isChecked())await activate(page.getByRole('radio',{name:originalCategory,exact:true}),'Space');if(originalTab!=='Delegado')await activate(page.getByRole('tab',{name:originalTab,exact:true}));await go(989379584);
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  const result={success:true,mvid,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,network:c.network,networkFailures:c.network.failures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/responsibility-categories.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
 }catch(error){fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/responsibility-categories-failure.json'),JSON.stringify({error:error.stack,mvid,checks,audits,observations,html:await page.locator('#fmroot').innerHTML(),native:await c.fetchText('/gui'),trace:await c.transportTrace(),errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));throw error;}
 finally{await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
