// Attach only to the sole actual Companion, in an already-open men's career.
// No fixture server, auxiliary browser, save, simulation or business edit.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright'),{default:AxeBuilder}=require('@axe-core/playwright');
const endpoint=process.env.FM27_COMPANION_CDP;
assert.ok(endpoint,'Supply the loopback CDP endpoint of the actual production Companion');
assert.equal(new URL(endpoint).hostname,'127.0.0.1');
const output=path.resolve(__dirname,'../../artifacts/native-auto-speech/inbox-interaction.json');
(async()=>{
 const browser=await chromium.connectOverCDP(endpoint);
 const pages=browser.contexts().flatMap(c=>c.pages()).filter(p=>p.url().startsWith('http://127.0.0.1:8787/'));
 assert.equal(pages.length,1);const page=pages[0];
 const report={at:new Date().toISOString(),success:false,manualNVDA:false,saveSubmitted:false,simulationAdvanced:false,checks:[],timings:[],audits:[],nativeQueries:[],errors:[]};
 page.on('pageerror',e=>report.errors.push(String(e)));
 const idle=()=>page.waitForFunction(()=>!FM.core.refreshing&&!FM.core.pending&&!FM.msgview.opening,null,{timeout:25000});
 const read=p=>page.evaluate(async p=>{const r=await fetch(p);if(!r.ok)throw Error('HTTP '+r.status);return r.text();},p);
 async function nativeQueries(stage){
  const mvid=report.health.coreMvid;
  const code='System.Reflection.Assembly a=null;foreach(var x in AppDomain.CurrentDomain.GetAssemblies())if(x.GetName().Name=="FM27Access.Plugin"&&x.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'")a=x;Console.WriteLine(a.GetType("FM27Access.Plugin.Server.Dev.NativeQueryEquivalenceProbe").GetMethod("Compare",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,null));';
  const raw=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);
  const rows=JSON.parse(raw);assert.ok(rows.length>0);assert.ok(rows.every(r=>r.equal),raw);report.nativeQueries.push({stage,rows});
 }
 async function activate(locator){
  await idle();const id=await locator.getAttribute('id');
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/activate'&&new URL(r.url()).searchParams.get('id')===id);
  await locator.click();const reply=await(await response).text();assert.match(reply,/^Activated /);return reply;
 }
 async function audit(stage){const result=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:result.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>n.target)}))});assert.equal(result.violations.length,0);}
 try{
  report.health=JSON.parse(await read('/health'));await idle();
  assert.ok((await page.locator('#fmroot').innerText()).includes('FC Porto'),'The current explicitly qualified men\'s FC Porto career');
  if(await page.locator('[data-fm-message-view-id]').count()){await page.keyboard.press('Escape');await page.waitForSelector('tr[data-fm-message-id="133"]');await idle();}
  assert.equal(await page.title(),'Mensagens');await nativeQueries('inbox');
  await page.evaluate(()=>{
   window.qaOpenMeasure={};
   document.addEventListener('click',e=>{const row=e.target.closest('tr[data-fm-message-id]');if(row)qaOpenMeasure={id:row.dataset.fmMessageId,start:performance.now()};},true);
   document.addEventListener('focusin',e=>{if(e.target.matches('[data-fm-msg-body]')&&qaOpenMeasure.start&&!qaOpenMeasure.focus)qaOpenMeasure.focus=performance.now();},true);
   new MutationObserver(()=>{const m=qaOpenMeasure;if(m.start&&!m.complete&&document.querySelector('[data-fm-message-view-id="'+m.id+'"]')&&document.querySelectorAll('table tbody tr').length===18)m.complete=performance.now();}).observe(document.getElementById('fmroot'),{subtree:true,childList:true});
  });
  for(let cycle=0;cycle<3;cycle++){
   const row=page.locator('tr[data-fm-message-id="133"] button').first(),origin=await row.getAttribute('id');
   const reply=page.waitForResponse(r=>new URL(r.url()).pathname==='/message/open');
   await row.click();assert.equal(await(await reply).text(),'');
   await page.waitForFunction(()=>qaOpenMeasure.focus&&qaOpenMeasure.complete&&!FM.core.refreshing&&!FM.core.pending,null,{timeout:25000});
   report.timings.push(await page.evaluate(()=>({message:qaOpenMeasure.id,bodyFocusMs:qaOpenMeasure.focus-qaOpenMeasure.start,complete18RowsMs:qaOpenMeasure.complete-qaOpenMeasure.start})));
   const cells=await page.locator('table tbody tr td:first-child').evaluateAll(cs=>cs.map(c=>({text:c.textContent,links:c.querySelectorAll('button').length,menu:c.querySelector('button')?.getAttribute('aria-haspopup')})));
   assert.equal(cells.length,18);assert.ok(cells.every(c=>c.links===1&&c.menu==='menu'));if(!cycle){report.clubCells=cells;await audit('complete transfer message');}
   await page.keyboard.press('Escape');await page.waitForFunction(id=>!FM.core.refreshing&&!FM.core.pending&&document.activeElement.id===id,origin,{timeout:25000});
  }
  report.checks.push('Three actual clicks open NewsItem133, focus its body, retain all18rows and return to the exact row with Escape');
  await page.locator('tr[data-fm-message-id="133"] button').first().click();await page.waitForFunction(()=>!FM.core.refreshing&&!FM.core.pending&&document.querySelectorAll('table tbody tr').length===18,null,{timeout:25000});
  for(const club of ['Benfica','Sporting CP']){
   const link=page.locator('table tbody tr td:first-child button').filter({hasText:new RegExp('^'+club.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')+'$')});
   assert.equal(await link.count(),1);const origin=await link.getAttribute('id');await activate(link);
   await page.waitForFunction(club=>document.title===club&&!FM.core.refreshing&&!FM.core.pending,club,{timeout:25000});
   await nativeQueries(club);
   // The actual header retains its named native text link; redundant image
   // hit surfaces are absent. Breadcrumb and biography remain separate contexts.
   const ids=await page.getByRole('button',{name:club,exact:true}).evaluateAll(bs=>bs.map(b=>b.id));
   assert.ok(!ids.includes('ef33a405c'));report.checks.push({club,namedLinks:ids});
   await audit(club+' club page');
   const reply=page.waitForResponse(r=>new URL(r.url()).pathname==='/history'&&new URL(r.url()).searchParams.get('dir')==='back');
   await page.keyboard.press('Alt+[');assert.equal(await(await reply).text(),'');
   await page.waitForFunction(id=>!FM.core.refreshing&&!FM.core.pending&&document.activeElement.id===id,origin,{timeout:25000});
   assert.equal(await page.evaluate(()=>FM.core.historyFocus),null,'Consumed exact history return');
   await page.evaluate(()=>{document.activeElement.blur();FM.core.refresh(false,true);});await idle();
   assert.equal(await page.evaluate(()=>document.activeElement===document.body),true,'Neutral refresh must not refocus the old native origin');
   report.checks.push('Exact '+club+' origin restored once; subsequent neutral body update leaves the reader position alone');
  }
  await page.keyboard.press('Escape');await page.waitForSelector('tr[data-fm-message-id="133"]');await idle();
  const search=page.getByRole('button',{name:'Search messages',exact:true});await activate(search);
  await page.waitForSelector('input[data-fm-message-query]');await idle();await nativeQueries('open message search');
  await activate(page.getByRole('button',{name:'Close',exact:true}));
  await page.waitForFunction(()=>!document.querySelector('[data-fm-message-query]')&&!FM.core.refreshing&&!FM.core.pending,null,{timeout:25000});
  await page.keyboard.press('Alt+2');
  await page.waitForFunction(()=>document.querySelector('#fmroot h3')&&[...document.querySelectorAll('#fmroot h2,#fmroot h3')].some(h=>h.textContent==='Squad')&&!FM.core.refreshing&&!FM.core.pending,null,{timeout:25000});
  const source=await page.evaluate(()=>({index:FM.core.historyIndex,key:FM.core.historyKey(FM.core.historyIndex,FMTOKEN)}));
  await page.getByRole('textbox',{name:'Search all screens',exact:true}).fill('Tácticas');
  const menu=page.getByRole('treeitem',{name:'Tácticas',exact:true}),origin=await menu.getAttribute('id');await menu.focus();
  const go=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');assert.match(await(await go).text(),/^Opening /);
  await page.waitForFunction(()=>document.title==='Tácticas'&&!FM.core.refreshing&&!FM.core.pending,null,{timeout:25000});
  await page.keyboard.press('Alt+[');
  await page.waitForFunction(s=>FM.core.historyIndex===s.index&&FM.core.historyKey(FM.core.historyIndex,FMTOKEN)===s.key&&document.activeElement.id===s.origin&&!FM.core.refreshing&&!FM.core.pending,{...source,origin},{timeout:25000});
  assert.equal(await page.evaluate(()=>FM.core.historyFocus),null);
  await page.evaluate(()=>{document.activeElement.blur();FM.core.refresh(false,true);});await idle();
  assert.equal(await page.evaluate(()=>document.activeElement===document.body),true);
  report.checks.push('Actual nested Tactics menu leaf restores once on native Back to Squad; neutral refresh does not pull focus back into the menu');
  await page.getByRole('textbox',{name:'Search all screens',exact:true}).fill('');await page.keyboard.press('Alt+1');
  await page.waitForFunction(()=>document.title==='Mensagens'&&!FM.core.refreshing&&!FM.core.pending,null,{timeout:25000});
  assert.equal(JSON.parse(await read('/health')).coreMvid,report.health.coreMvid);assert.deepEqual(report.errors,[]);report.success=true;
 }catch(error){report.failure=error.stack;report.lastState=await page.evaluate(()=>({title:document.title,token:FMTOKEN,focus:document.activeElement.id,body:document.getElementById('fmroot').innerText.slice(0,6000)}));throw error;}
 finally{fs.mkdirSync(path.dirname(output),{recursive:true});fs.writeFileSync(output,JSON.stringify(report,null,2));await browser.close();}
 console.log(JSON.stringify({success:report.success,timings:report.timings,checks:report.checks.length,audits:report.audits.length}));
})().catch(error=>{console.error(error);process.exitCode=1;});
