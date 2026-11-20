// Current Portuguese setup summary only; no Next, save or career creation.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const output=path.resolve(__dirname,'../../artifacts/current-screen-refinement');fs.mkdirSync(output,{recursive:true});
 const report={success:false,checks:[],audits:[],manualNVDA:false,careerCreated:false};
 const normalize=s=>s.replace(/\s+/g,' ').trim();
 async function audit(label){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({label,violations:a.violations});assert.deepEqual(a.violations,[]);assert.equal(await page.locator('h1 button,h2 button,h3 button,h4 button,h5 button,h6 button').count(),0);}
 async function openLeagues(){await activate(page.getByRole('button',{name:'ADICIONAR/REMOVER LIGAS',exact:true}));await poll(async()=>await page.title()==='Adicionar/Remover Ligas','native leagues card');await settled();}
 async function closeLeagues(){await activate(page.getByRole('button',{name:'Close',exact:true}));await poll(async()=>await page.title()==='Configuração do Jogo','summary restored');await settled();}
 async function selection(field){await c.dropdown(await field.getAttribute('aria-label'));const selected=await field.locator('option:checked').innerText();await c.cancelDropdown();return selected;}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  if(['Adicionar/Remover Ligas','Modo de Transferências','Fluxo de Progressão'].includes(await page.title()))await closeLeagues();
  assert.equal(await page.title(),'Configuração do Jogo');
  assert.equal(await page.locator('#fmroot h2').count(),1);assert.equal(await page.getByRole('heading',{name:'Data de início do jogo',exact:true}).count(),1);
  assert.equal(await page.locator('#fmroot table').count(),1);assert.deepEqual(await page.locator('thead th').allTextContents(),['Nação','Modo','Ligas Masculinas','Modo','Ligas Femininas']);
  assert.equal(await page.locator('tbody tr').count(),4);assert.equal(await page.locator('tbody tr').first().locator('td').first().innerText(),'Jogável');
  assert.match(await page.locator('#fmroot').innerText(),/Número Aproximado de Jogadores: 40190/);
  const baseline=normalize(await page.locator('#fmroot').innerText());
  await audit('Native setup summary');
  const date=page.getByRole('combobox',{name:'Data de início do jogo',exact:true}),dateId=await date.getAttribute('id');
  report.date=await date.innerText();await selection(date);assert.equal(await page.evaluate(()=>document.activeElement?.id),dateId);
  const leagueOrigin=await page.getByRole('button',{name:'ADICIONAR/REMOVER LIGAS',exact:true}).getAttribute('id');
  await openLeagues();assert.deepEqual(await page.locator('thead th').allTextContents(),['Nação','Ligas Masculinas','Configuração','Ligas Femininas','Configuração']);
  assert.equal(await page.locator('tbody tr').count(),4);assert.equal(await page.locator('tbody select').count(),13);assert.equal(await page.getByRole('button',{name:'Delete',exact:true}).count(),4);
  assert.equal(await page.locator('tbody tr').first().getByRole('button',{name:'Delete',exact:true}).isDisabled(),true);
  const beforePath=path.join(output,'league-values-before.json'),before=fs.existsSync(beforePath)?JSON.parse(fs.readFileSync(beforePath)):null;
  report.selections=[];
  const fields=await page.getByRole('combobox').all();
  for(const field of fields){
   const id=await field.getAttribute('id'),name=await field.getAttribute('aria-label'),selected=await selection(field);
   report.selections.push({id,name,selected});assert.equal(await page.evaluate(()=>document.activeElement?.id),id);
   if(before){const original=before.find(x=>x.id===id);assert.ok(original,id);assert.equal(selected,original.options.find(x=>x.selected).text);}
   if(name.includes('Configuração'))assert.equal(await field.locator('option').innerText(),'Jogável');
  }
  assert.match(await page.locator('tbody tr').first().innerText(),/2\/4/);assert.match(await page.locator('tbody tr').first().innerText(),/2\/2/);
  await audit('Native editable league table');
  const modeName='França, Ligas Masculinas, Configuração';await c.dropdown(modeName);
  const option=page.getByRole('combobox',{name:modeName,exact:true}).locator('option',{hasText:/^Apenas Ver$/});assert.equal(await option.isDisabled(),false);
  await c.choose('Apenas Ver');await poll(async()=>await page.getByRole('combobox',{name:modeName,exact:true}).innerText()==='Apenas Ver','actual view-only native selection');
  await closeLeagues();assert.equal(await page.evaluate(()=>document.activeElement?.id),leagueOrigin);
  await poll(async()=>await page.locator('tbody tr').filter({hasText:'FRA'}).locator('td').first().innerText()==='Apenas Ver','actual summary reflects native mode');
  await audit('Summary after native mode change');
  await openLeagues();await c.dropdown(modeName);await c.choose('Jogável');
  await poll(async()=>await page.getByRole('combobox',{name:modeName,exact:true}).innerText()==='Jogável','original native mode restored');
  await closeLeagues();await poll(async()=>normalize(await page.locator('#fmroot').innerText())===baseline,'complete original summary restored');
  await audit('Restored setup summary');
  for(const [opener,capture] of [[/^MODO DE TRANSFERÊNCIAS,/,'transfer-before.json'],[/^FLUXO DE PROGRESSÃO,/,'progression-before.json']]){
   const origin=page.getByRole('button',{name:opener}),id=await origin.getAttribute('id');await activate(origin);await settled();
   const state=await page.locator('#fmroot').evaluate(r=>({title:document.title,text:r.innerText,controls:[...r.querySelectorAll('input,button,select')].map(x=>({tag:x.tagName,type:x.type,name:x.getAttribute('aria-label'),text:x.labels?.[0]?.innerText||x.textContent,checked:x.checked,disabled:x.disabled,value:x.value}))}));
   const captured=path.join(output,capture);if(fs.existsSync(captured))assert.deepEqual(JSON.parse(JSON.stringify(state)),JSON.parse(fs.readFileSync(captured)));
   await audit(state.title);await activate(page.getByRole('button',{name:'Close',exact:true}));await poll(async()=>await page.title()==='Configuração do Jogo','native setup card dismissed');await settled();
   assert.equal(await page.evaluate(()=>document.activeElement?.id),id);assert.equal(normalize(await page.locator('#fmroot').innerText()),baseline);
  }
  const finalHealth=JSON.parse(await c.fetchText('/health'));assert.equal(finalHealth.pid,report.health.pid);assert.equal(finalHealth.coreMvid,report.health.coreMvid);
  report.checks.push('Five native columns and four full nation rows; all thirteen league selectors and four delete controls retained',
   'All fourteen current native dropdown selections equal the pre-change capture when supplied; cancel returns exact focus',
   'View-only France mode changes the actual summary and restores Jogável; complete summary and date restored without restart',
   'Transfer mode and progression cards preserve all pre-change text/controls/selections and exact return focus');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
 }catch(error){report.error=error.stack;fs.writeFileSync(path.join(output,'live-failure.html'),await page.content().catch(()=>''));throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;fs.writeFileSync(path.join(output,'live-summary.json'),JSON.stringify(report,null,2));await c.browser.close();console.log(JSON.stringify({success:report.success,checks:report.checks,audits:report.audits.length}));}
})().catch(error=>{console.error(error);process.exitCode=1;});
