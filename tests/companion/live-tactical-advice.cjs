// Actual native acknowledgment, distinct from applying tactical advice. The
// owned QA save must later be reloaded without Save to restore consumed advice.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const out=path.resolve(__dirname,'../../artifacts/tests/web/tactical-advice.json'),checks=[],audits=[],observations={};
 let mvid,success=false;
 const acknowledgments=()=>page.getByRole('button',{name:'Acknowledge advice',exact:true});
 const nativeCount=(text,name)=>text.split('\n').filter(line=>line.includes('name="'+name+'" visible=True')).length;
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 async function open(id){
  const button=page.locator('tr[data-fm-message-id="'+id+'"] button[onclick^="FM.msgview.open"]');assert.equal(await button.count(),1);
  await button.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');response.catch(()=>{});
  await page.keyboard.press('Enter');assert.equal(await(await response).text(),'');
  await poll(()=>page.locator('[data-fm-message-view-id="'+id+'"]').count(),'exact native advice message');await settled();
 }
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const filename=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>filename.count(),'owned QA identity');
  observations.ownedSave=await filename.inputValue();assert.match(observations.ownedSave,/^FM27Access QA /);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await page.keyboard.press('Alt+1');await poll(async()=>await page.title()==='Mensagens','native Messages');await settled();
  await activate(page.getByRole('tab',{name:'Tudo',exact:true}));await c.newestMessages();
  const subject=()=>page.locator('tr[data-fm-message-id] button[onclick^="FM.msgview.open"]').filter({hasText:/^Conselho táctico$/});
  observations.windows=[];
  while(!await subject().count()){
   assert.ok(observations.windows.length<50,'Bounded actual native inbox discovery');
   const before=await page.locator('tr[data-fm-message-id]').evaluateAll(rs=>rs.map(r=>r.dataset.fmMessageId).join(','));
   assert.ok(!observations.windows.includes(before),'Native older window must progress');observations.windows.push(before);
   const older=page.getByRole('button',{name:'Show older messages',exact:true});assert.equal(await older.count(),1,'An actual tactical advice report is required');await activate(older);
   await poll(async()=>await page.locator('tr[data-fm-message-id]').evaluateAll(rs=>rs.map(r=>r.dataset.fmMessageId).join(','))!==before,'older native records');
  }
  const id=await subject().first().locator('xpath=ancestor::tr').getAttribute('data-fm-message-id');assert.match(id,/^\d+$/);observations.messageId=id;await open(id);
  assert.equal(await page.getByRole('button',{name:'unlabeled icon button',exact:true}).count(),0);
  const beforeGui=await c.fetchText('/gui'),before=await acknowledgments().count();assert.ok(before>0);assert.equal(nativeCount(beforeGui,'AcknowledgeSuggestionButton'),before);
  const first=acknowledgments().first(),row=first.locator('xpath=ancestor::tr'),cells=await row.locator('th,td').allTextContents();assert.ok(cells.length>=3);const reaction=cells[1];assert.ok(reaction.trim());
  observations.before={count:before,cells,commandId:await first.getAttribute('id'),nativeAcknowledged:nativeCount(beforeGui,'SuggestionAcknowledgedFrame')};
  await audit('Native tactical advice rows with real command captions');checks.push('Every actual native acknowledgment button has a name; semantic rows retain subject/reaction/context and independent Apply controls');
  observations.reply=await activate(first);
  await poll(async()=>await acknowledgments().count()===before-1,'one native advice consumed');await settled();
  const afterGui=await c.fetchText('/gui'),afterRow=page.getByRole('row').filter({has:page.getByRole('cell',{name:reaction,exact:true})});
  assert.equal(await afterRow.count(),1);assert.equal(await afterRow.getByRole('cell',{name:'Ciente',exact:true}).count(),1);
  assert.equal(nativeCount(afterGui,'AcknowledgeSuggestionButton'),before-1);assert.equal(nativeCount(afterGui,'SuggestionAcknowledgedFrame'),observations.before.nativeAcknowledged+1);assert.match(afterGui,/SuggestionActionedText.*text="Ciente"/);
  observations.after={count:await acknowledgments().count(),row:await afterRow.innerText(),nativeAcknowledged:nativeCount(afterGui,'SuggestionAcknowledgedFrame')};
  checks.push('One keyboard command changes the same native advice row to Ciente; actual acknowledged frame increases and native/DOM actions decrease by exactly one');
  await activate(page.getByRole('button',{name:'Close message',exact:true}));await poll(()=>page.locator('[data-fm-message-view-id]').count().then(n=>n===0),'closed message');await open(id);
  assert.equal(await acknowledgments().count(),before-1);assert.equal(await page.getByRole('row').filter({has:page.getByRole('cell',{name:reaction,exact:true})}).getByRole('cell',{name:'Ciente',exact:true}).count(),1);
  await audit('Acknowledged native advice after close/reopen');checks.push('Closing/reopening the exact NewsItem preserves the acknowledged row and all other pending advice');
  await activate(page.getByRole('button',{name:'Close message',exact:true}));
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,mvid);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(error){observations.failure=error.stack;throw error;}
 finally{
  fs.writeFileSync(out,JSON.stringify({success,mvid,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,restorationRequired:true,saveSubmitted:false,simulationAdvanced:false,tacticalApplicationClaimed:false,manualNVDA:false},null,2));await c.browser.close();
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
