// Actual Portuguese owned QA career. Read every registered finance destination,
// exercise native menus/dialog cancellation and retain applied allocations.
// Budget commit/reopen/exact checkpoint restoration is separate retained evidence.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true,initScript:()=>{
  // Delay only delivery of genuine native screen events for the menu arrival
  // race. The ordinary action and authoritative /body remain unmodified.
  window.qaHeldScreens=[];window.qaHoldScreens=false;
  const add=EventSource.prototype.addEventListener;
  EventSource.prototype.addEventListener=function(type,listener,options){
   if(type!=='screen')return add.call(this,type,listener,options);
   return add.call(this,type,function(event){
    if(window.qaHoldScreens){qaHeldScreens.push(()=>listener.call(this,event));return;}
    listener.call(this,event);
   },options);
  };
 }}),{page,poll,settled,activate}=c;
 const checks=[],screens=[],audits=[];let success=false,failure=null,mvid;
 async function go(uid){
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first();
  const search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});
  await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);
  await poll(()=>page.evaluate(id=>FMTOKEN.split('\x1f').some(part=>part.startsWith(id+'#')),String(uid)),'actual destination '+uid);
  await settled();await search.fill('');await settled();
 }
 async function audit(name){
  const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  audits.push({name,violations:a.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});assert.deepEqual(a.violations,[]);
 }
 const readouts=()=>page.locator('#fmroot p').filter({hasText:/^(Orçamento Disponível|Valor gasto esta época|Salários pagos):/}).allTextContents();
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  if(!await page.getByRole('heading',{name:'Portal',exact:true}).count())await go(989379584);
  assert.equal(await page.getByRole('heading',{name:'Portal',exact:true}).count(),1);
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned checkpoint');
  assert.match(await file.inputValue(),/^FM27Access QA /);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await go(5585921);const baseline=await readouts();assert.equal(baseline.length,6);
  assert.ok(baseline.every(text=>text.includes('€')));assert.ok(baseline.some(text=>text.includes('p/s')));
  assert.deepEqual(await page.getByRole('progressbar').evaluateAll(es=>es.map(e=>e.getAttribute('aria-label'))),[
   'ORÇAMENTO DE TRANSFERÊNCIAS, indicator fill','ORÇAMENTO DE OBSERVAÇÃO, indicator fill',
   'ORÇAMENTO SALARIAL, indicator fill','SALÁRIOS, indicator fill']);
  assert.equal(await page.getByText('107 825 €',{exact:true}).count(),1,'The current wage amount is readable, not swallowed as a bar label');
  assert.deepEqual(await page.locator('p').filter({hasText:/^Total actual de ordenados/}).allTextContents(),[
   'Total actual de ordenados (incluindo prémios) / orçamento: 107 825 € / 125 069 € p/s 86,212517%']);
  checks.push('Six actual native monetary pairs retain captions, currencies and weekly units; all four native fills have their own card context');
  for(const title of ['ORÇAMENTO DE TRANSFERÊNCIAS','ORÇAMENTO DE OBSERVAÇÃO','ORÇAMENTO SALARIAL']){
   const select=page.getByRole('combobox',{name:'Customize card: '+title,exact:true});const id=await select.getAttribute('id');
   await c.dropdown('Customize card: '+title);assert.ok(await page.locator('select[data-loaded] option').count()>0);await c.cancelDropdown();
   assert.equal(await page.evaluate(()=>document.activeElement.id),id);
  }
  checks.push('All three native finance customization menus load actual options and Escape restores exact origin');
  const adjust=page.getByRole('button',{name:'Ajustar orçamento para transferências',exact:true}),origin=await adjust.getAttribute('id');
  await activate(adjust);const range=page.getByRole('slider');const original=+await range.inputValue();
  await range.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/setvalue');response.catch(()=>{});
  await page.keyboard.press(original<+await range.getAttribute('max')?'ArrowRight':'ArrowLeft');assert.match(await(await response).text(),/^Set to /);
  await poll(async()=>+await range.inputValue()!==original,'native draft value');
  await audit('Native budget draft');await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  assert.deepEqual(await readouts(),baseline);await poll(()=>page.evaluate(id=>document.activeElement.id===id,origin),'budget cancel origin');
  checks.push('Native draft changes by keyboard; Cancel retains all applied overview amounts and exact origin focus');
  const board=page.getByRole('button',{name:'Fazer Pedido à Direcção',exact:true}),boardOrigin=await board.getAttribute('id');
  await page.evaluate(()=>qaHoldScreens=true);
  await activate(board);await poll(()=>page.getByRole('menu').count(),'actual board request menu');
  await poll(()=>page.locator('[data-fm-menu-first]').evaluate(e=>document.activeElement===e),'menu focus before SSE screen delivery');
  assert.ok(await page.getByRole('menuitem').count()>0);
  await page.keyboard.press('ArrowDown');const menuFocus=await page.evaluate(()=>document.activeElement.id);
  await page.evaluate(()=>FM.core.refresh(false,true));await settled();
  assert.equal(await page.evaluate(()=>document.activeElement.id),menuFocus,'Neutral menu refresh preserves the current option');
  await page.keyboard.press('Escape');
  await poll(async()=>!await page.getByRole('menu').count(),'board menu cancellation');
  await poll(()=>page.evaluate(id=>document.activeElement.id===id,boardOrigin),'board menu origin');
  await page.evaluate(()=>{qaHoldScreens=false;const queued=qaHeldScreens.splice(0);queued.forEach(deliver=>deliver());});await settled();
  checks.push('Actual board menu action refresh focuses before delayed native SSE; arrows, neutral refresh, Escape and exact origin work without a business request');
  const destinations=[['Overview',5585921],['Summary',1036018688],['Income',1025498598],['Expenditure',3643682816],['Wages',1837234432],['Sponsorship',1465386240],['Projections',1718185728],['Debts and loans',1561592576],['Transfer budget',3145537024],['Wage budget',3946954240],['Scouting budget',365823488],['Clauses',3407932416]];
  for(const [label,uid] of destinations){
   await go(uid);const body=await page.locator('#fmroot').innerText();assert.ok(body.trim());
   assert.equal(await page.locator('h1').count(),1);assert.doesNotMatch(body,/unknown control|unlabeled icon button|No navigable content|couldn't read/);
   const unnamed=await page.locator('button,input,select').evaluateAll(es=>es.filter(e=>!e.disabled&&e.type!=='hidden'&&!e.textContent.trim()&&!e.getAttribute('aria-label')&&!e.getAttribute('aria-labelledby')&&![...(e.labels||[])].some(l=>l.textContent.trim())).map(e=>e.outerHTML));
   assert.deepEqual(unnamed,[]);await audit(label);
   screens.push({label,uid,headings:await page.locator('h1,h2,h3').allTextContents(),tables:await page.locator('table').count(),rows:await page.locator('table tbody tr').count(),buttons:await page.getByRole('button').allTextContents()});
   console.log('finance destination checked:',label);
  }
  checks.push('All twelve registered finance destinations reached their actual native UID and retained content, semantic controls and clean axe');
  await go(5585921);assert.deepEqual(await readouts(),baseline);
  await go(989379584);assert.equal(await page.getByRole('heading',{name:'Portal',exact:true}).count(),1);
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,mvid);
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(error){failure=error.stack||String(error);throw error;
 }finally{
  const dir=path.resolve(__dirname,'../../artifacts/tests/web');fs.mkdirSync(dir,{recursive:true});
  const report={success,failure,mvid,checks,screens,audits,jsErrors:c.errors,requestFailures:c.requestFailures,network:c.network,manualNVDA:false};
  fs.writeFileSync(path.join(dir,'finance.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));await c.browser.close();
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
