// FM26 duplicate-strip parity, preserving native table choices. No lineup edits.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],observations=[];let success=false,failure;
 const root=()=>page.locator('#fmroot'),tab=n=>page.getByRole('tab',{name:n,exact:true});
 const table=()=>page.locator('table').filter({has:page.getByRole('columnheader',{name:/^Esc/})}).last();
 const native=async()=>(await c.fetchText('/gui')).split('\n')[0];
 const signature=()=>table().evaluate(t=>{const index=[...t.querySelectorAll('thead th')].findIndex(h=>h.textContent.trim()==='Nome');if(index<0)throw Error('Native name column missing');return [...t.querySelectorAll('tbody tr')].map(r=>({slot:r.cells[0].textContent.trim(),name:r.cells[index].querySelector('button[aria-haspopup="menu"]')?.textContent.trim()||''}));});
 const health=JSON.parse(await c.fetchText('/health'));
 try{
  assert.ok((await native()).includes('"TacticalPlannerTool"'));await c.activate(tab('Panorâmico'));await settled();
  const before=await signature(),tactic=await root().getByText(/^Active tactic, slot/).innerText();observations.push({tactic,selection:before});
  for(const name of ['Panorâmico','Com Posse de Bola','Sem Posse de Bola','Ambos']){
   await c.activate(tab(name));await settled();
   assert.equal(await root().getByText('Suplentes',{exact:true}).count(),0,'Duplicate native strip title is hidden');
   assert.equal(await page.getByRole('combobox',{name:/^S\d+$/}).count(),0,'Duplicate strip pickers are hidden');
   for(let i=1;i<=15;i++)assert.equal(await table().locator('tbody tr td:first-child').getByText('S'+i,{exact:true}).count(),1,'Native table slot S'+i);
   assert.deepEqual(await signature(),before,'Changing pitch mode preserves the current lineup');
   if(name==='Ambos')assert.equal(await page.locator('[data-pitch="browse"]').count(),2);
  }
  checks.push('FM26 substitutes-strip title/count/pickers are hidden in all four modes; all 15 table slots and Both pitches remain');
  console.log('PASS',checks.at(-1));
  for(const slot of ['S1','S14']){
   const row=table().locator('tbody tr').filter({has:page.locator('td:first-child').getByText(slot,{exact:true})});
   const field=row.getByRole('combobox'),name=await field.getAttribute('aria-label');
   const select=await c.dropdown(name),options=await select.locator('option').allTextContents();assert.ok(options.length>1);
   observations.push({slot,name,options});await c.cancelDropdown();assert.deepEqual(await signature(),before);
  }
  checks.push('Actual first/last occupied substitute table dropdowns retain native candidate options and cancel without selection');
  console.log('PASS',checks.at(-1));
  const row=table().locator('tbody tr').filter({has:page.locator('td:first-child').getByText('S1',{exact:true})});
  const name=await row.getByRole('combobox').getAttribute('aria-label'),select=await c.dropdown(name);
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/select');
  await select.selectOption({label:'Ir para Selecção de jogadores'});await page.keyboard.press('Enter');
  const reply=await response;assert.equal(reply.status(),200);observations.push({pickerReply:await reply.text()});
  await poll(async()=>(await native()).includes('"PlayerSelectionCard"'),'native table player picker');await settled();
  observations.push({pickerNative:await native(),pickerText:await root().innerText()});
  await c.activate(page.getByRole('button',{name:'Close',exact:true}));
  await poll(async()=>(await native()).includes('"TacticalPlannerTool"'),'picker cancelled');await settled();assert.deepEqual(await signature(),before);
  checks.push('Table substitute menu opens the actual native player-selection card and closes without confirming a player');
  console.log('PASS',checks.at(-1));
  await c.activate(tab('Panorâmico'));await settled();assert.deepEqual(await signature(),before);assert.equal(await root().getByText(/^Active tactic, slot/).innerText(),tactic);
  assert.equal(await root().getByText('Select in or out of possession to see pitch view here',{exact:true}).count(),1);
  await table().scrollIntoViewIfNeeded();await page.screenshot({path:path.resolve(__dirname,'../../artifacts/tactics-substitutes-companion.png')});
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tactics-substitutes-after.html'),await root().innerHTML());
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(e){failure=e.stack;throw e;}
 finally{fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/tactics-substitutes.json'),JSON.stringify({at:new Date().toISOString(),success,failure,health,checks,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,businessChangesSubmitted:false,manualNVDA:false},null,2));await c.browser.close();}
})().catch(e=>{console.error(e.stack);process.exitCode=1});
