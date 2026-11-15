// Actual returned-loan report actions and every native form-history value.
// Debug independently reads each native playerform binding. Release requires
// that retained native cohort from the SAME immutable owned checkpoint/date.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),args=process.argv.slice(2),option=n=>args.find(a=>a.startsWith('--'+n+'='))?.slice(n.length+3);
const checkpoint=option('checkpoint'),expectedPath=option('native-expected');
const owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
assert.equal(checkpoint,owned.juneDecisionWorking);assert.equal(checkpoint,owned.currentWorking);assert.ok(owned.ownedWorkingFiles.includes(checkpoint+'.fm'));
const retained=expectedPath?JSON.parse(fs.readFileSync(path.resolve(root,expectedPath),'utf8')):null;
if(retained){assert.equal(retained.success,true);assert.equal(retained.checkpoint,checkpoint);assert.equal(retained.nativeEvidenceSource,'Actual native NodeProbe playerform.MatchRating100 for each plotted bar');}
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const output=path.join(root,'artifacts/tests/web/career-loan-form-history-'+(expectedPath?'release':'native')+'.json');
 const report={at:new Date().toISOString(),success:false,checkpoint,message:'129917',players:[],audits:[],checks:[],saveSubmitted:false,simulationAdvanced:false,manualNVDA:false,nativeEvidenceSource:retained?'Retained native cohort at exact checkpoint/date':'Actual native NodeProbe playerform.MatchRating100 for each plotted bar'};
 async function audit(stage){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations.map(v=>v.id)});assert.equal(a.violations.length,0);}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));assert.ok(report.health.developerTools||retained,'Release cannot pretend to inspect absent Debug probes');
  if(await page.locator('[data-fm-message-view-id]').count())await activate(page.getByRole('button',{name:'Close message',exact:true}));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  report.date=await page.locator('#fmroot button').nth(1).innerText();assert.match(report.date,/, 4\/6\/2027,/);if(retained)assert.equal(report.date,retained.date);
  await page.keyboard.press('Alt+1');await poll(async()=>await page.title()==='Mensagens','actual native Inbox');await settled();const all=page.getByRole('tab',{name:'Tudo',exact:true});if(await all.getAttribute('aria-selected')!=='true')await activate(all);
  const field=()=>page.getByRole('textbox',{name:'Procurar...',exact:true});if(!await field().count())await activate(page.getByRole('button',{name:'Search messages',exact:true}));
  if(await field().inputValue()!=='emprestados'){await field().fill('emprestados');const reply=page.waitForResponse(r=>new URL(r.url()).pathname==='/settext');await field().press('Tab');assert.equal(await(await reply).text(),'Set to emprestados');await settled();}
  const news=page.locator('tr[data-fm-message-id="129917"] button[onclick^="FM.msgview.open"]');await poll(()=>news.count(),'actual returned-loans native result');await news.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');await page.keyboard.press('Enter');assert.equal(await(await opened).text(),'');await poll(()=>page.locator('[data-fm-message-view-id="129917"]').count(),'actual returned-loans news');await settled();await audit('Actual returned-loans message table');
  const names=await page.getByRole('table').filter({has:page.getByRole('columnheader',{name:'Jogadora',exact:true})}).locator('tbody tr').evaluateAll(rs=>rs.map(r=>r.cells[0].textContent.trim()));assert.equal(names.length,4);if(retained)assert.deepEqual(names,retained.players.map(p=>p.name));
  for(const name of names){
   const row=page.getByRole('row').filter({has:page.getByRole('button',{name,exact:true})}),button=row.getByRole('button',{name:'Relatório de Empréstimo',exact:true});assert.equal(await button.count(),1);const origin=await button.getAttribute('id');await activate(button);await poll(async()=>await page.title()==='Relatório de Empréstimo','actual native loan report');await settled();assert.ok((await page.locator('#fmroot').innerText()).includes(name));
   const table=page.getByRole('table').filter({has:page.getByRole('columnheader',{name:'Classificação',exact:true})});assert.equal(await table.count(),1);assert.equal(await table.getByRole('columnheader',{name:'Jogos',exact:true}).count(),1);
   const rows=await table.locator('tbody tr').evaluateAll(rs=>rs.map(r=>[r.cells[0].textContent,r.cells[1].textContent]));assert.equal(rows.length,20);
   const nativeRatings=[];
   if(retained)nativeRatings.push(...retained.players.find(p=>p.name===name).nativeRatings);
   else for(let n=0;n<20;n++){const raw=await c.fetchText('/nodeprobe?type=data_display-graphs-elements-bar_value_indicator&nth='+n+'&depth=2');const rating=/MatchRating100 = (-?\d+)/.exec(raw),index=/rowindex = (\d+)/.exec(raw);assert.ok(rating&&index,'Actual native point and rowindex must be independently readable');assert.equal(Number(index[1]),n);nativeRatings.push(Number(rating[1]));}
   assert.deepEqual(rows,nativeRatings.map((r,n)=>[String(n+1),(r/10).toFixed(1)]),'Every cell equals its actual native rating, preserving zeroes/order');report.players.push({name,nativeRatings,rows});await audit('Actual '+name+' full native form-history table');
   await page.locator('[data-fm-history="back"]').press('Enter');await poll(()=>page.locator('[data-fm-message-view-id="129917"]').count(),'same actual source news');await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'exact original player report action focus');await settled();await audit('Exact '+name+' source action return');
  }
  report.checks.push('All four native row actions open their actual player; every20-point rating table equals its corresponding native data/order','Each native Back restores the exact original row action and NewsItem');
  await activate(page.getByRole('button',{name:'Close message',exact:true}));await poll(()=>page.evaluate(()=>document.activeElement?.closest('tr[data-fm-message-id]')?.dataset.fmMessageId==='129917'),'same searched native news record focus');assert.equal(await field().inputValue(),'emprestados');await audit('Native query and exact returned-loans source record retained');report.checks.push('Full-Inbox query and exact record focus survive every profile and message close');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,report.health.coreMvid);report.success=true;
 }catch(error){report.failure=error.stack;report.body=await page.locator('#fmroot').innerText().catch(()=>null);throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;fs.writeFileSync(output,JSON.stringify(report,null,2));await c.browser.close();}
 console.log(JSON.stringify({success:report.success,players:report.players.length,points:report.players.reduce((n,p)=>n+p.rows.length,0),checks:report.checks.length,cleanAxe:report.audits.length}));
})().catch(error=>{console.error(error);process.exitCode=1;});
