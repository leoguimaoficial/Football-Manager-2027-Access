// Actual elapsed-wait reapplication news in an explicitly owned June QA career.
// Default reads availability; --commit submits ONCE. --processed is a separate
// read-only verification of an already processed native request, never approval.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),args=process.argv.slice(2),option=n=>args.find(a=>a.startsWith('--'+n+'='))?.slice(n.length+3);
const checkpoint=option('checkpoint'),message=option('message')||'129118',commit=args.includes('--commit'),processed=args.includes('--processed');
assert.ok(!(commit&&processed));assert.match(message,/^\d+$/);
const owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
assert.ok([owned.juneWorking,owned.postPermitWorking].filter(Boolean).includes(checkpoint));assert.equal(checkpoint,owned.currentWorking);assert.ok(owned.ownedWorkingFiles.includes(checkpoint+'.fm'));
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const output=path.join(root,'artifacts/tests/web/career-permit-reapplication-'+(processed?'processed':commit?'commit':'availability')+'.json');
 const report={at:new Date().toISOString(),success:false,checkpoint,message,commit,processed,requestSubmitted:false,approvalVerified:false,saveSubmitted:false,simulationAdvanced:false,checks:[],audits:[],manualNVDA:false};
 const action=()=>page.getByRole('button',{name:'Pedir Licença de Trabalho',exact:true});
 const persist=()=>fs.writeFileSync(output,JSON.stringify(report,null,2));
 async function audit(stage){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations.map(v=>v.id)});assert.equal(a.violations.length,0);}
 async function open(){const b=page.locator('tr[data-fm-message-id="'+message+'"] button[onclick^="FM.msgview.open"]');assert.equal(await b.count(),1);await b.focus();const reply=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');await page.keyboard.press('Enter');assert.equal(await(await reply).text(),'');await poll(()=>page.locator('[data-fm-message-view-id="'+message+'"]').count(),'actual native reapplication NewsItem');await settled();}
 async function close(){await activate(page.getByRole('button',{name:'Close message',exact:true}));await poll(()=>page.evaluate(id=>document.activeElement?.closest('tr[data-fm-message-id]')?.dataset.fmMessageId===id,message),'exact reapplication source-row focus');}
 async function verifyProcessed(){const text=await page.locator('#fmroot').innerText();assert.match(text,/Tratado[\s\S]*Foi feito um pedido para uma licença de trabalho para o Aisha Masaka/);assert.equal(await action().count(),0);return text;}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  if(await page.locator('[data-fm-message-view-id]').count())await activate(page.getByRole('button',{name:'Close message',exact:true}));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  report.date=await page.locator('#fmroot button').nth(1).innerText();assert.match(report.date,/, 1\/6\/2027,/);
  await page.keyboard.press('Alt+1');await poll(async()=>await page.title()==='Mensagens','actual native Inbox');await settled();const all=page.getByRole('tab',{name:'Tudo',exact:true});if(await all.getAttribute('aria-selected')!=='true')await activate(all);await c.newestMessages();await open();
  assert.equal(await page.title(),'Recandidatar-se a uma autorização de trabalho para Masaka');report.before=await page.locator('#fmroot').innerText();assert.match(report.before,/passaram 120 dias/);await audit('Actual elapsed-wait native reapplication news');
  if(!processed){assert.match(report.before,/Imediatamente/);assert.equal(await action().isEnabled(),true);report.checks.push('Actual elapsed120-day native notice offers the available reapplication action');}
  if(commit){report.requestSubmitted='Outcome not yet observed';persist();report.reply=await activate(action());report.requestSubmitted=true;await poll(async()=>/Tratado[\s\S]*Foi feito um pedido/.test(await page.locator('#fmroot').innerText()),'native processed application notice');}
  if(commit||processed){report.processedBody=await verifyProcessed();await audit('Native processed request and removed duplicate application action');report.checks.push('Native notice confirms a submitted request and removes the duplicate action; no permit approval is inferred');await close();await open();report.reopenedBody=await verifyProcessed();await audit('Processed request persists on actual NewsItem reopening');report.checks.push('Actual processed state persists on reopening the same native NewsItem');}
  await close();await audit('Exact native source record focus restored');report.checks.push('Closing returns focus to the exact native reapplication record');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,report.health.coreMvid);report.success=true;
 }catch(error){report.failure=error.stack;report.body=await page.locator('#fmroot').innerText().catch(()=>null);throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;persist();await c.browser.close();}
 console.log(JSON.stringify({success:report.success,commit,processed,checks:report.checks.length,cleanAxe:report.audits.length,approvalVerified:false}));
})().catch(error=>{console.error(error);process.exitCode=1;});
