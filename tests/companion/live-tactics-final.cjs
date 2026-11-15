// Final focused checks after native disabled-state/phase-group presentation changes.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],observations=[];let success=false,failure;
 const button=n=>page.getByRole('button',{name:n,exact:true}),tab=n=>page.getByRole('tab',{name:n,exact:true}),root=()=>page.locator('#fmroot');
 const out=path.resolve(__dirname,'../../artifacts/tests/web/tactics-final.json'),mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
 const native=async()=>(await c.fetchText('/gui')).split('\n')[0];
 async function record(name){checks.push(name);console.log('PASS',name);}
 try{
  assert.equal(await page.title(),'Tácticas');await c.activate(tab('Ambos'));await poll(async()=>await page.locator('[data-pitch="browse"]').count()===2,'two phase pitches');await settled();
  const order=await root().evaluate(r=>{const hs=[...r.querySelectorAll('h3')],ps=[...r.querySelectorAll('.fm-pitch')];return ['Com Posse de Bola','Sem Posse de Bola'].map((name,i)=>{const h=hs.filter(x=>x.textContent===name);return h.length===1&&!!(h[0].compareDocumentPosition(ps[i])&Node.DOCUMENT_POSITION_FOLLOWING);});});assert.deepEqual(order,[true,true]);
  assert.equal(await button('Formation').count(),2);
  for(let i=0;i<2;i++){
   const before=await native();await c.activate(button('Formation').nth(i));
   await poll(async()=>{const n=await native();return n!==before&&n.includes('visible=True')||await page.getByRole('menu').count();},'actual formation selector '+i);await settled();
   observations.push({formation:i,native:await native(),text:await root().innerText()});
   if(await button('Close').count())await c.activate(button('Close'));else if(await button('Cancelar').count())await c.activate(button('Cancelar'));else await page.keyboard.press('Escape');
   await poll(async()=>(await native()).includes('"TacticalPlannerTool"')&&await tab('Ambos').count()===1&&await page.getByRole('menu').count()===0,'formation cancelled');await settled();
  }
  await record('Both native phase headings precede their fields; both real formation selectors open/cancel without choosing a formation');
  await c.activate(button('Instruções à Equipa'));await poll(()=>page.locator('select[data-ti]').count(),'native team instruction table');await settled();
  const table=()=>page.locator('table').filter({has:page.getByRole('columnheader',{name:'Instruction',exact:true})});
  assert.equal(await table().locator('tbody tr').count(),19);assert.equal(await page.locator('select[data-ti]').count(),17);
  observations.push({phase:'IP',rows:19,disabled:await page.locator('select[data-ti]:disabled').count()});
  const field=page.locator('select[data-ti]').first(),current=await field.inputValue();await field.focus();await page.keyboard.press('Enter');await poll(()=>field.getAttribute('data-loaded'),'options');await page.keyboard.press('ArrowDown');await page.keyboard.press('Escape');assert.equal(await field.inputValue(),current);
  await c.activate(tab('Sem Posse de Bola'));await poll(async()=>await table().locator('tbody tr').count()===10,'native OOP fields');await settled();observations.push({phase:'OOP',rows:10,disabled:await page.locator('select[data-ti]:disabled').count()});
  await record('Final native instruction-table rendering retains 19/10 rows, current native availability and noncommitting option cancellation');
  await c.activate(tab('Com Posse de Bola'));await c.activate(button('Esquema Táctico'));await c.activate(tab('Panorâmico'));await settled();
  for(let i=0;i<2;i++){
   await c.activate(page.getByRole('button',{name:/^Escolha de jogadores/}).nth(i));
   await poll(async()=>(await native()).includes('"PlayerSelectionCard"'),'actual player selector');await settled();
   const text=await root().innerText();assert.doesNotMatch(text,/PositionIcon|LeftFoot|RightFoot|RoleIcon/);
   assert.match(text,/PÉ PREFERIDO/);assert.match(text,/Pé Direito/);assert.match(text,/Guarda-redes|Defesa \(Direito\)/);
   observations.push({name:'captioned native picker artwork',source:i,native:await native(),text});
   await c.activate(button('Close'));await poll(async()=>(await native()).includes('"TacticalPlannerTool"'),'picker closed');await settled();
  }
  await record('Assigned/vacant player selectors retain official position/role/preferred-foot captions without redundant technical glyph labels');
  assert.equal(await root().getByText('Select in or out of possession to see pitch view here',{exact:true}).count(),1);
  assert.equal(await button('Sem Regras de Jogo').count(),0);await c.activate(tab('Ambos'));await poll(async()=>await page.locator('[data-pitch="browse"]').count()===2,'final Both');await settled();
  await page.getByRole('heading',{name:'Com Posse de Bola',exact:true}).scrollIntoViewIfNeeded();await page.screenshot({path:path.resolve(__dirname,'../../artifacts/tactics-audit-both-companion-final.png')});
  await record('Original FM26 Panoramic and readonly no-rules state retained; final native screen left on Both');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(e){failure=e.stack;observations.push({name:'failure',native:await native(),text:await root().innerText()});throw e;}
 finally{fs.writeFileSync(out,JSON.stringify({at:new Date().toISOString(),success,failure,mvid,checks,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,businessChangesSubmitted:false,manualNVDA:false},null,2));await c.browser.close();}
})().catch(e=>{console.error(e.stack);process.exitCode=1});
