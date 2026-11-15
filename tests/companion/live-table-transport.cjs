// Genuine native 50-row database paging in the preserved owned Squads QA copy.
// Transport diagnostics record only paths/timing; no cookies, headers, bodies,
// fabricated responses, business commands, career advance or save submission.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate,fetchText}=c;
 const steps=[],traceWindows=[];let success=false,failure=null;
 const developerTools=JSON.parse(await fetchText('/health')).developerTools!==false;
 const trace=async()=>developerTools?JSON.parse(await fetchText('/httptrace')):{available:false,boundary:'Native HTTP tracing is absent in Release; actual CDP request/connection evidence is retained'};
 try{
  if(!await page.getByRole('heading',{name:'Portal',exact:true}).count()){
   const portal=page.locator('#fmnav-tree [role=treeitem][data-uid="989379584"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
   await search.fill(await portal.locator(':scope > span').textContent());await portal.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await poll(()=>page.getByRole('heading',{name:'Portal',exact:true}).count(),'owned Portal before native paging');await search.fill('');await settled();
  }
  assert.equal(await page.getByRole('heading',{name:'Portal',exact:true}).count(),1);
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned save');
  assert.match(await file.inputValue(),/^FM27Access QA Squads /);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="3173313024"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();
  const navigation=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');navigation.catch(()=>{});
  await page.keyboard.press('Enter');assert.match(await(await navigation).text(),/^Opening /);
  await poll(()=>page.evaluate(()=>FMTOKEN.split('\x1f').some(p=>p.startsWith('3173313024#'))),'native database');
  await settled();await search.fill('');await settled();
  const labels=()=>page.locator('p').filter({hasText:/^Page \d+ of \d+$/}).allTextContents();
  const first=await page.locator('table tbody tr').first().innerText();assert.equal(await page.locator('table tbody tr').count(),50);
  let failuresSeen=0;
  for(let round=0;round<20;round++){
   for(const name of ['Next page','Previous page']){
    const started=Date.now();await activate(page.getByRole('button',{name,exact:true}).first());
    assert.equal(await page.locator('table tbody tr').count(),50);
    const nowFirst=await page.locator('table tbody tr').first().innerText();assert.equal(nowFirst===first,name==='Previous page');
    steps.push({round:round+1,action:name,elapsedMs:Date.now()-started,pages:await labels()});
    if(c.requestFailures.length!==failuresSeen){
     failuresSeen=c.requestFailures.length;traceWindows.push({afterStep:steps.length,trace:await trace()});
    }
    console.log('native paging step',steps.length,'completed',steps.at(-1).elapsedMs+'ms','transport failures',failuresSeen);
   }
  }
  traceWindows.push({afterStep:steps.length,trace:await trace()});
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);success=true;
 }catch(error){failure=error.stack||String(error);try{traceWindows.push({failureTrace:await trace()});}catch{}throw error;
 }finally{
  const dir=path.resolve(__dirname,'../../artifacts/tests/web');fs.mkdirSync(dir,{recursive:true});
  const report={success,failure,steps,traceWindows,network:c.network,jsErrors:c.errors,requestFailures:c.requestFailures,simulationAdvanced:false};
  fs.writeFileSync(path.join(dir,'table-transport.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({success,failure,steps:steps.length,requestFailures:c.requestFailures,networkFailures:c.network.failures},null,2));await c.browser.close();
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
