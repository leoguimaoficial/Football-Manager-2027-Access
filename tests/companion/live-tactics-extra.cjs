// Additional native control inventory; no choice/business leaf is submitted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],observations=[];let success=false,failure;
 const root=()=>page.locator('#fmroot'),button=n=>page.getByRole('button',{name:n,exact:true}),tab=n=>page.getByRole('tab',{name:n,exact:true});
 const out=path.resolve(__dirname,'../../artifacts/tests/web/tactics-extra.json');
 const mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
 const native=async()=>(await c.fetchText('/gui')).split('\n')[0];
 async function record(name){checks.push(name);console.log('PASS',name);}
 async function back(){const reply=page.waitForResponse(r=>new URL(r.url()).pathname==='/history');await page.keyboard.press('Alt+[');assert.equal(await(await reply).text(),'');await poll(async()=>(await native()).includes('"TacticalPlannerTool"'),'native tactics return');await settled();}
 async function dismiss(){if(await button('Close').count())await c.activate(button('Close'));else await back();await poll(async()=>(await native()).includes('"TacticalPlannerTool"'),'closed native details');await settled();}
 async function inspect(control,name){const before=await native();await c.activate(control);await poll(async()=>{const n=await native();return n!==before&&n.includes('visible=True');},'actual '+name+' destination');await settled();observations.push({name,native:await native(),text:await root().innerText()});await dismiss();}
 try{
  assert.equal(await page.title(),'Tácticas');await c.activate(tab('Panorâmico'));await settled();
  await c.activate(button('Collapse/Expand Pitch View'));await poll(async()=>await tab('Ambos').count()===0,'native collapsed mode');await settled();
  assert.equal(await button('Collapse/Expand Pitch View').count(),1,'The collapsed pitch must retain its native expand control');
  await c.activate(button('Collapse/Expand Pitch View'));await poll(async()=>await tab('Ambos').count()===1,'native expanded mode');await settled();
  await record('Native pitch collapse/expand inverse retains the real expand control and restores Both availability');
  const name=await c.dropdown('Diogo Costa');observations.push({name:'assigned-player dropdown',options:await name.locator('option').allTextContents()});await c.cancelDropdown();
  await button('Diogo Costa').focus();await page.keyboard.press('Shift+F10');await poll(()=>page.getByRole('menu').count(),'native person context menu');observations.push({name:'assigned-player context menu',text:await page.getByRole('menu').innerText()});await page.keyboard.press('Escape');await settled();
  await inspect(button('Diogo Costa'),'assigned player profile');await record('Assigned player dropdown/context menu and real profile/history return retain native controls');
  const header=page.getByRole('columnheader',{name:'Esc',exact:true}).getByRole('button');await header.focus();await page.keyboard.press('Shift+F10');await poll(()=>page.getByRole('menu').count(),'native header menu');observations.push({name:'Esc header context menu',text:await page.getByRole('menu').innerText()});await page.keyboard.press('Escape');await settled();
  await record('Native selection-header context menu remains reachable without changing sort/view settings');
  await inspect(button('4 stars').first(),'tactical role ability');
  assert.equal(await button('Sem Regras de Jogo').count(),0,'Native CanSubmit=false/no Events/custom action: friendly rules are readonly');
  assert.equal(await root().getByText('Sem Regras de Jogo',{exact:true}).count(),1);await record('Actual tactical-ability command opens native details; native no-rules status remains readonly');
  await c.activate(button('GRC').first());await poll(async()=>(await native()).includes('"PlayerInstructionRoleEditCard"'),'native role detail');await settled();
  for(const [name,url]of [['role-icons','/spritecensus?name=RoleIcon'],['role-icon-binding','/nodeprobe?type=RoleIcon&depth=2']])fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tactics-audit-'+name+'.txt'),await c.fetchText(url));
  observations.push({name:'native role descriptions',text:await root().innerText()});await dismiss();
  await c.activate(tab('Ambos'));await poll(async()=>await page.locator('[data-pitch="browse"]').count()===2,'both original pitch grids');await settled();
  await page.getByRole('heading',{name:'Com Posse de Bola',exact:true}).scrollIntoViewIfNeeded();
  await page.screenshot({path:path.resolve(__dirname,'../../artifacts/tactics-audit-both-companion.png')});
  await record('Both is left open with the two inherited pitch grids under their native headings');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(e){failure=e.stack;observations.push({name:'failure',native:await native(),text:await root().innerText()});throw e;}
 finally{fs.writeFileSync(out,JSON.stringify({at:new Date().toISOString(),success,failure,mvid,checks,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,businessChangesSubmitted:false,manualNVDA:false},null,2));await c.browser.close();}
})().catch(e=>{console.error(e.stack);process.exitCode=1});
