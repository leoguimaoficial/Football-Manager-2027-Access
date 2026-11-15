const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const{default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')(),{page,poll,settled,activate,dropdown,choose,cancelDropdown,fetchText}=c;
 const checks=[],audits=[];
 async function audit(name){console.log('audit',name);const result=await Promise.race([new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze(),new Promise((_,reject)=>setTimeout(()=>reject(Error('axe timeout: '+name)),30000))]);audits.push({name,violations:result.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>n.target)}))});}
 async function main(){await poll(()=>page.getByRole('button',{name:'Preferências',exact:true}).count(),'main menu');await settled();assert.equal(await page.title(),'MainMenu');}
 async function close(){const x=page.getByRole('button',{name:'Close',exact:true}),cancel=page.getByRole('button',{name:'Cancelar',exact:true});if(await x.count())await activate(x.first());else if(await cancel.count())await activate(cancel.first());else throw Error('Native close/cancel button missing');}
 try{
  const health=JSON.parse(await fetchText('/health'));assert.equal(health.adapter,'fm26-original-core');assert.equal(health.screen,'Live');
  if(!(await page.getByRole('button',{name:'Preferências',exact:true}).count())){await close();}await main();
  console.log('phase main');assert.equal(await page.getByRole('button',{name:'unlabeled icon button',exact:true}).count(),0);
  assert.doesNotMatch(await page.locator('#fmroot').innerText(),/Club, image|Card-frame, image/);
  const blank=await page.context().request.get('http://127.0.0.1:8787/activate?id=missing',{headers:{Cookie:''}});assert.equal(blank.status(),403);
  const root=await page.locator('#fmroot').innerHTML(),creation=page.getByRole('button',{name:/^CRIAR NOVO JOGO/});
  await creation.focus();const spoken=page.waitForResponse(r=>new URL(r.url()).pathname==='/speak');await page.keyboard.press('Alt+i');assert.equal((await spoken).status(),200);
  assert.equal(await page.locator('#fmlive').textContent(),'');assert.equal(await page.locator('#fmroot').innerHTML(),root);assert.ok((await fetchText('/speech')).includes('Crie o seu próprio rumo'));
  checks.push('Original Alt+I/SpeechPipeline/Prism: no text inserted at the top');
  await page.route('**/speak?*',route=>route.fulfill({status:200,body:''}));
  for(let i=0;i<3;i++){const select=await dropdown('Mais');assert.deepEqual(await select.locator('option').allTextContents(),['Créditos','Políticas de Privacidade','Loja']);await cancelDropdown();await main();assert.equal(await page.evaluate(()=>document.activeElement?.getAttribute('aria-label')),'Mais');}
  checks.push('Original inline dropdown, retry/harvest, Escape and source focus');
  await dropdown('Mais');await choose('Créditos');await poll(()=>page.getByRole('tab').count(),'credits tabs');await settled();assert.equal(await page.getByRole('tablist').count(),1);await close();await main();
  await activate(page.getByRole('button',{name:/^CARREGAR JOGO/}));await poll(()=>page.getByRole('tab',{name:'FM24',exact:true}).count(),'save versions');await settled();
  assert.equal(await page.getByRole('tablist').count(),1);assert.deepEqual(await page.getByRole('tab').allTextContents(),['FM27','FM26','FM24']);assert.notEqual(await page.title(),'MainMenu');
  for(const version of['FM26','FM24','FM27']){await activate(page.getByRole('tab',{name:version,exact:true}));await poll(async()=>await page.getByRole('tab',{name:version,exact:true}).getAttribute('aria-selected')==='true','selected version');}
  const location=page.locator('select[data-dd]').first(),locationName=await location.getAttribute('aria-label'),previous=await location.inputValue();await dropdown(locationName);await cancelDropdown();assert.equal(await page.locator('select[data-dd]').first().inputValue(),previous);await audit('load game');await close();await main();
  checks.push('Load Game uses one original tab bar; versions, parent context and document title refresh');
  console.log('phase preferences');await activate(page.getByRole('button',{name:'Preferências',exact:true}));await poll(()=>page.getByRole('tab',{name:'Detalhado',exact:true}).count(),'preferences');await settled();assert.equal(await page.title(),'Preferências e Acessibilidade');
  await activate(page.getByRole('tab',{name:'Detalhado',exact:true}));await poll(()=>page.getByRole('tab',{name:'Ecrã',exact:true}).count(),'categories');await activate(page.getByRole('tab',{name:'Ecrã',exact:true}));await poll(()=>page.getByRole('combobox',{name:'Frames por segundo',exact:true}).count(),'FPS');
  const quality=page.getByRole('combobox',{name:'Qualidade Gráfica',exact:true}),old=await quality.locator('option:checked').textContent();
  try{await dropdown('Qualidade Gráfica');await choose('Personalizado');await poll(async()=>await page.getByRole('combobox',{name:'Frames por segundo',exact:true}).isEnabled(),'enabled FPS');const fps=page.getByRole('combobox',{name:'Frames por segundo',exact:true}),oldFps=await fps.locator('option:checked').textContent();try{await dropdown('Frames por segundo');await choose(oldFps==='30'?'60':'30');await poll(async()=>await page.getByRole('combobox',{name:'Frames por segundo',exact:true}).locator('option:checked').textContent()!==oldFps,'native FPS changed');}finally{await dropdown('Frames por segundo');await choose(oldFps);}}finally{await dropdown('Qualidade Gráfica');await choose(old);}
  assert.equal(await page.getByRole('combobox',{name:'Qualidade Gráfica',exact:true}).locator('option:checked').textContent(),old);checks.push('Original setting adapters and dropdown sessions change and restore FPS and graphics quality');
  await activate(page.getByRole('tab',{name:'Áudio',exact:true}));await poll(()=>page.getByRole('checkbox',{name:'Sons de ambiente',exact:true}).count(),'native audio names');assert.equal(await page.getByRole('checkbox',{name:'Ligado',exact:true}).count(),0);
  const audio=page.getByRole('checkbox',{name:'Sons de ambiente',exact:true}),was=await audio.isChecked();try{await activate(audio,'Space');await poll(async()=>await page.getByRole('checkbox',{name:'Sons de ambiente',exact:true}).isChecked()!==was,'audio toggled');}finally{if(await page.getByRole('checkbox',{name:'Sons de ambiente',exact:true}).isChecked()!==was)await activate(page.getByRole('checkbox',{name:'Sons de ambiente',exact:true}),'Space');}
  await audit('preferences audio');
  await activate(page.getByRole('tab',{name:'Atalhos',exact:true}));await poll(async()=>await page.locator('table tbody tr').count()===50,'first 50 rows');await activate(page.getByRole('button',{name:'Next page',exact:true}).first());await poll(async()=>await page.locator('table tbody tr').count()===3,'last three rows');await activate(page.getByRole('button',{name:'Previous page',exact:true}).first());await poll(async()=>await page.locator('table tbody tr').count()===50,'returned first page');
  checks.push('Original StreamedTableLoader/TablePages/TablePagerHtml: actual 53-row table, 50 + 3');
  console.log('phase shortcuts');const row=page.locator('table tbody tr').nth(1);await activate(row.getByRole('radio'),'Space');await poll(()=>page.locator('table tbody tr').nth(1).getByRole('button').count(),'selected row actions');console.log('shortcut actions',await page.locator('table tbody tr').nth(1).getByRole('button').allTextContents());
  await activate(page.locator('table tbody tr').nth(1).getByRole('button',{name:'Control+P',exact:true}));await poll(()=>page.locator('input[data-keycapture]').count(),'native rebinder');assert.equal(await page.locator('input[data-keycapture]').getAttribute('readonly'),null);
  await page.locator('input[data-keycapture]').focus();const cancelled=page.waitForResponse(r=>new URL(r.url()).pathname==='/settext');await page.keyboard.press('Escape');assert.equal((await cancelled).status(),200);await poll(async()=>!await page.locator('input[data-keycapture]').count()||await page.locator('input[data-keycapture]').getAttribute('readonly')!==null,'capture cancellation');
  if(!(await page.getByRole('tab',{name:'Atalhos',exact:true}).count()))await close();await poll(()=>page.getByRole('tab',{name:'Atalhos',exact:true}).count(),'returned preferences');await close();await main();await audit('main menu');
  checks.push('FM27 native key-capture widget runs inside original table/handle/renderer pipeline; capture cancelled without saving');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(audits.flatMap(a=>a.violations),[]);
  const output={success:true,checks,audits,errors:c.errors,requestFailures:c.requestFailures,manualNVDA:false,gamePid:health.pid,repeatedBrowsingSpeechMuted:true};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/live-full-core.json'),JSON.stringify(output,null,2));console.log(JSON.stringify(output));
 }finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
