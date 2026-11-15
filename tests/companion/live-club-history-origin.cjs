// Actual same-template club navigation and return. No Save, simulation or offers.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),args=process.argv.slice(2);
const option=name=>args.find(a=>a.startsWith('--'+name+'='))?.slice(name.length+3);
const owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
const checkpoint=option('checkpoint')||owned.postLoanWorking,cycles=Number(option('cycles')||3);
assert.ok(owned.ownedWorkingFiles.includes(checkpoint+'.fm'));assert.ok(Number.isInteger(cycles)&&cycles>=1&&cycles<=20);
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,cycles,checks:[],audits:[],observations:[],saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const bodyTrace=[];let bodyContext=null;
 if(args.includes('--trace-body-history')){
  report.actualBodyHeaders=[];
  report.bodyTraceBoundary='Actual response index/title/screen token and literal source DOM ID presence only; independent native entity identity and delayed-input behavior are not inspected';
  page.on('response',response=>{
   if(new URL(response.url()).pathname!=='/body')return;
   const context=bodyContext,at=Date.now();
   const trace=(async()=>{
    const index=await response.headerValue('X-FM-History-Index'),title=await response.headerValue('X-FM-Title'),token=await response.headerValue('X-FM-Token');
    const html=await response.text();
    const decode=value=>value===null?null:decodeURIComponent(value.replace(/\+/g,' '));
    report.actualBodyHeaders.push({at,index:index===null?null:Number(index),title:decode(title),token:decode(token),context,sourceActionPresent:context?html.includes('id="'+context.action+'"'):null});
   })();
   bodyTrace.push(trace);trace.catch(()=>{});
  });
 }
 async function audit(stage){const r=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:r.violations.map(v=>v.id)});assert.equal(r.violations.length,0);}
 async function state(stage){const s=await page.evaluate(()=>({title:document.title,index:FM.core.historyIndex,ready:FM.core.historyReady,token:FMTOKEN,focus:document.activeElement.id,historyMove:FM.core.historyMove,historyFocus:FM.core.historyFocus,historyPositions:FM.core.historyPos}));report.observations.push({stage,...s});console.log(JSON.stringify({stage,title:s.title,index:s.index,focus:s.focus}));return s;}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  if(await page.locator('[data-fm-message-view-id]').count())await activate(page.getByRole('button',{name:'Close message',exact:true}));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const portal=page.locator('#fmnav-tree [role=treeitem][data-uid="989379584"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await portal.locator(':scope > span').textContent());await portal.focus();const nav=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');assert.match(await(await nav).text(),/^Opening /);await poll(async()=>await page.title()==='Portal','actual native Portal');await search.fill('');await settled();
  // Inherited flat HTML puts native navigation before the main page heading.
  // Resolve that actual rendered link, not a hardcoded hash or DOM child index.
  const headerLinks=await page.getByRole('button',{name:'Arsenal',exact:true}).evaluateAll(bs=>{const nav=document.querySelector('#fmnav-tree'),heading=[...document.querySelectorAll('#fmroot h1,#fmroot h2')].find(h=>nav&&(nav.compareDocumentPosition(h)&Node.DOCUMENT_POSITION_FOLLOWING));return bs.filter(b=>heading&&(b.compareDocumentPosition(heading)&Node.DOCUMENT_POSITION_FOLLOWING)).map(b=>b.id);});
  assert.equal(headerLinks.length,1,'Actual named club link before main content');await activate(page.locator('[id="'+headerLinks[0]+'"]'));await poll(async()=>await page.title()==='Arsenal','actual club destination');await settled();
  assert.equal(await page.getByRole('button',{name:'Club crest',exact:true}).count(),0,'Official names replace fixed crest captions on this qualified club page');await audit('Actual Arsenal club page');
  report.checks.push('Native header opens the actual Arsenal page with official club names and original links preserved');
  for(let cycle=0;cycle<cycles;cycle++){
   // The native last-match crest and adjacent text are both real links. Use
   // the first in their original order; the observed destination is mandatory.
   const city=page.getByRole('button',{name:'MCI',exact:true}).first();assert.equal(await city.count(),1);const origin=await city.getAttribute('id');await city.focus();const source=await state('source-'+(cycle+1));bodyContext={cycle:cycle+1,sourceIndex:source.index,sourceToken:source.token,action:origin};
   await activate(city);await poll(async()=>await page.title()==='Manchester City','actual linked Manchester City');await settled();const destination=await state('destination-'+(cycle+1));
   assert.match(await page.locator('#fmroot').innerText(),/Joie Stadium/);if(cycle===0)await audit('Actual linked Manchester City club');
   const back=page.waitForResponse(r=>{const u=new URL(r.url());return cycle%2?u.pathname==='/history'&&u.searchParams.get('dir')==='back':u.pathname==='/activate';});
   if(cycle%2)await page.keyboard.press('Alt+[');else{await page.locator('[data-fm-history="back"]').focus();await page.keyboard.press('Enter');}
   const response=await back;assert.equal(response.status(),200);assert.equal(await response.text(),'');await poll(async()=>await page.title()==='Arsenal','original native Arsenal restored');
   await poll(()=>page.evaluate(id=>document.activeElement.id===id,origin),'exact source club-link focus after native return');await settled();const returned=await state('returned-'+(cycle+1));assert.ok(returned.index<destination.index);assert.equal(returned.focus,origin);bodyContext=null;
  }
  report.checks.push('Same-template club links and native Back/Alt+[ preserve the exact originating action for all requested cycles');await audit('Final exact club-link return');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,report.health.coreMvid);report.success=true;
 }catch(error){report.failure=error.stack;report.body=await page.locator('#fmroot').innerText().catch(()=>null);await state('failure').catch(()=>{});throw error;}
 finally{const traced=await Promise.allSettled(bodyTrace);report.bodyTraceErrors=traced.filter(r=>r.status==='rejected').map(r=>String(r.reason));report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;fs.mkdirSync(path.join(root,'artifacts/tests/web'),{recursive:true});fs.writeFileSync(path.join(root,'artifacts/tests/web/club-history-origin-'+(report.success?'passed':'failure')+'.json'),JSON.stringify(report,null,2));await c.browser.close();}
 console.log(JSON.stringify({success:report.success,cycles,checks:report.checks.length,cleanAxe:report.audits.length}));
})().catch(error=>{console.error(error);process.exitCode=1;});
