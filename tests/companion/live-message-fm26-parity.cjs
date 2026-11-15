// Actual sole-Companion/native FM26 message contracts in the men's QA career.
// Starts and ends with every existing message read. Only native reading-state
// commands are used; never submit message actions, save or advance the game.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright'),{default:AxeBuilder}=require('@axe-core/playwright');
const endpoint=process.env.FM27_COMPANION_CDP;assert.ok(endpoint);assert.equal(new URL(endpoint).hostname,'127.0.0.1');
(async()=>{
 const browser=await chromium.connectOverCDP(endpoint),pages=browser.contexts().flatMap(c=>c.pages()).filter(p=>p.url().startsWith('http://127.0.0.1:8787/'));assert.equal(pages.length,1);const page=pages[0];
 const report={at:new Date().toISOString(),success:false,checks:[],opens:[],audits:[],errors:[],manualNVDA:false,saveSubmitted:false,simulationAdvanced:false,businessActionSubmitted:false};page.on('pageerror',e=>report.errors.push(String(e)));
 const rows=()=>page.locator('tr[data-fm-message-id]'),subjects=()=>rows().locator('button[onclick^="FM.msgview.open"]');
 const filter=label=>page.getByRole('button',{name:new RegExp('^'+label+'(?: \\(|$)')});
 async function idle(){await page.waitForFunction(()=>!FM.core.refreshing&&!FM.core.pending&&!FM.msgview.opening,null,{timeout:30000});let previous='',hits=0;for(let i=0;i<100;i++){let html=await page.locator('#fmroot').innerHTML();if(html===previous)hits++;else{previous=html;hits=0;}if(hits>=4&&await page.evaluate(()=>!FM.core.refreshing&&!FM.core.pending&&!FM.msgview.opening))return;await new Promise(r=>setTimeout(r,180));}throw Error('Native inbox did not settle');}
 async function activate(control){await idle();let id=await control.getAttribute('id');let response=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate'&&new URL(r.url()).searchParams.get('id')===id);await control.click();assert.match(await(await response).text(),/^Activated /);await idle();return id;}
 async function select(label){await activate(filter(label));await page.waitForFunction(label=>[...document.querySelectorAll('button[aria-pressed=true]')].some(b=>b.textContent===label||b.textContent.startsWith(label+' (')),label,{timeout:30000});await idle();}
 async function audit(stage){const result=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:result.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>n.target)}))});assert.deepEqual(result.violations,[]);}
 async function empty(){await idle();assert.equal(await rows().count(),0);assert.equal(await page.locator('#fmroot table').count(),0,'No empty inbox table/headers, matching FM26');assert.match(await filter('Por Ler').textContent(),/^Por Ler \(0\)$/);assert.equal(await filter('Por Ler').getAttribute('aria-pressed'),'true');}
 async function refresh(){await page.evaluate(()=>FM.core.refresh(false,true));await idle();}
 try{
  report.health=await page.evaluate(async()=>(await fetch('/health')).json());await idle();assert.equal(await page.title(),'Mensagens');assert.ok((await page.locator('#fmroot').innerText()).includes('FC Porto'));await empty();await audit('Already-empty native unread list');
  for(let i=0;i<2;i++){await refresh();await empty();}report.checks.push('Existing Por Ler(0) and repeated committed body updates expose no table or column headers');
  await select('Tudo');await page.waitForFunction(()=>document.querySelector('tr[data-fm-message-id="133"]'),null,{timeout:30000});await idle();
  report.originalRows=await rows().evaluateAll(rs=>rs.map(r=>({id:r.dataset.fmMessageId,status:r.cells[0].textContent,subject:r.cells[1].textContent,sender:r.cells[2].textContent,sent:r.cells[3].textContent})));
  assert.ok(report.originalRows.length>1);assert.ok(report.originalRows.every(r=>!/(^|,\s*)unread(,|$)/.test(r.status)));assert.equal(new Set(report.originalRows.map(r=>r.id)).size,report.originalRows.length);assert.deepEqual(await page.locator('#fmroot table thead th').allTextContents(),['Status','Subject','Sender','Sent']);
  const more=page.getByRole('combobox',{name:'Message actions',exact:true});await more.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');await page.keyboard.press('Space');assert.ok((await(await opened).text()).startsWith('<'));await page.waitForFunction(()=>document.querySelector('select[data-loaded]'),null,{timeout:30000});
  const unreadOption=more.locator('option').filter({hasText:/^Marcar todos os itens como não lidos$/});assert.equal(await unreadOption.count(),1);assert.equal(await unreadOption.isDisabled(),false);
  await more.selectOption({label:'Marcar todos os itens como não lidos'});const chosen=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/select');await page.keyboard.press('Enter');assert.equal(await(await chosen).text(),'Marcar todos os itens como não lidos selected');await idle();
  await select('Por Ler');await page.waitForFunction(count=>document.querySelectorAll('tr[data-fm-message-id]').length===count,report.originalRows.length,{timeout:30000});await idle();
  assert.ok((await rows().locator('td:first-child').allTextContents()).every(t=>/(^|,\s*)unread(,|$)/.test(t)));report.checks.push('Actual native Mark all unread populates the original semantic table and native unread state');
  const remaining=report.originalRows.map(r=>r.id);
  while(remaining.length){
   let id=await rows().first().getAttribute('data-fm-message-id');assert.ok(remaining.includes(id));const original=report.originalRows.find(r=>r.id===id),button=page.locator('tr[data-fm-message-id="'+id+'"] button[onclick^="FM.msgview.open"]');
   const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');await button.focus();await page.keyboard.press('Enter');assert.equal(await(await response).text(),'');await page.waitForSelector('[data-fm-message-view-id="'+id+'"]');await page.waitForFunction(()=>document.activeElement.hasAttribute('data-fm-msg-body'),null,{timeout:30000});await idle();assert.equal(await page.title(),original.subject);
   let body=page.locator('[data-fm-msg-body]').first();await page.keyboard.press('Alt+ArrowLeft');await page.keyboard.press('Alt+ArrowRight');await page.waitForFunction(()=>document.activeElement.hasAttribute('data-fm-msg-body'),null,{timeout:30000});assert.ok((await body.textContent()).trim().length>0);
   const escape=remaining.length%2===1;
   if(escape)await page.keyboard.press('Escape');else await activate(page.getByRole('button',{name:'Close message',exact:true}));
   remaining.splice(remaining.indexOf(id),1);await page.waitForFunction(count=>!document.querySelector('[data-fm-message-view-id]')&&document.querySelectorAll('tr[data-fm-message-id]').length===count,remaining.length,{timeout:30000});await idle();
   assert.equal(await page.locator('tr[data-fm-message-id="'+id+'"]').count(),0);assert.equal(await page.evaluate(()=>FM.core.msgStash),null);
   const focus=await page.evaluate(()=>({id:document.activeElement.id,message:document.activeElement.closest('tr[data-fm-message-id]')?.dataset.fmMessageId,filter:document.activeElement.hasAttribute('data-fm-msg-unread-filter')}));
   if(remaining.length){assert.equal(focus.message,await rows().first().getAttribute('data-fm-message-id'));assert.equal(await page.locator('#fmroot table').count(),1);}else{await empty();assert.equal(focus.filter,true);}
   report.opens.push({id,subject:original.subject,close:escape?'Escape':'Close message',remaining:remaining.length,focus});
  }
  report.checks.push('Every actual unread item opens its own body; both Close and Escape return to the next unread row and the final empty filter');await audit('Final unread close has no table');
  for(let i=0;i<2;i++){await refresh();await empty();}report.checks.push('Final unread table remains absent through later native/body updates');
  await select('Tudo');await page.waitForFunction(count=>document.querySelectorAll('tr[data-fm-message-id]').length===count,report.originalRows.length,{timeout:30000});await idle();
  const after=await rows().evaluateAll(rs=>rs.map(r=>({id:r.dataset.fmMessageId,status:r.cells[0].textContent,subject:r.cells[1].textContent,sender:r.cells[2].textContent,sent:r.cells[3].textContent})));
  assert.deepEqual(after,report.originalRows,'All-filter rows, status, subjects, senders and dates remain intact');await audit('All retains original read messages');report.checks.push('Tudo retains every original message and its four native metadata columns');
  for(const label of ['Novo','Tarefas','Tudo','Por Ler'])await select(label);await empty();
  await page.reload();await page.waitForSelector('[data-fm-msg-unread-filter]');await idle();await empty();report.checks.push('Other native filters and a fresh Companion document do not recreate the empty unread table');
  const portal=page.locator('#fmnav-tree [role=treeitem][data-uid="989379584"]:not([data-obj])').first();await page.getByRole('textbox',{name:'Search all screens',exact:true}).fill(await portal.locator(':scope>span').textContent());await portal.focus();const navigation=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');assert.match(await(await navigation).text(),/^Opening /);await page.waitForFunction(()=>document.title==='Portal',null,{timeout:30000});await idle();await page.getByRole('textbox',{name:'Search all screens',exact:true}).fill('');await select('Por Ler');assert.equal(await rows().count(),0);assert.equal(await page.locator('#fmroot table').filter({has:page.locator('thead th',{hasText:/^Subject$/})}).count(),0);report.checks.push('The shared Portal inbox also omits its empty message table');
  await page.keyboard.press('Alt+1');await page.waitForFunction(()=>document.title==='Mensagens',null,{timeout:30000});await idle();await empty();assert.deepEqual(report.errors,[]);report.success=true;
 }catch(error){report.failure=error.stack;report.lastState=await page.evaluate(()=>({title:document.title,token:FMTOKEN,focus:document.activeElement.id})).catch(()=>null);throw error;}
 finally{const out=path.resolve(__dirname,'../../artifacts/message-parity/live-parity.json');fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify(report,null,2));await browser.close();}
 console.log(JSON.stringify({success:report.success,checks:report.checks.length,messages:report.opens.length,audits:report.audits.length}));
})().catch(error=>{console.error(error);process.exitCode=1;});
