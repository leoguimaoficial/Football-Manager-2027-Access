// Real native news headers, including the separate same-name story group.
// Reuse the production client and actual readonly native classification; no fixture UI.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],observations=[],audits=[];
 async function go(uid){
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first();
  const search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');
  await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);
  await settled();await search.fill('');await settled();
 }
 try{
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned QA filename');
  assert.match(await file.inputValue(),/^FM27Access QA /);await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  for(const uid of [3720559107,3720559106]){
   await go(uid);const heading=page.locator('#fmsec1-h'),title=await heading.innerText();
   const native=await c.fetchText('/subtree?name=ForYouHeader');
   const raw=native.split('\n').find(l=>/SIText name="(?:Title|ForYouTitle)"/.test(l));
   assert.ok(raw,'Actual native news header must be readable');
   const nativeTitle=raw.match(/text="([^"]*)"/)?.[1];assert.ok(nativeTitle);
   assert.equal(nativeTitle.toLocaleLowerCase(),title.toLocaleLowerCase(),'Native header names this page');
   const source=heading.locator('span[id]');assert.equal(await source.count(),1,'Reuse the native live text in the existing heading');
   assert.equal(await source.innerText(),nativeTitle);
   const next=heading.locator('xpath=following-sibling::*[1]');assert.notEqual(await next.innerText(),nativeTitle,'No duplicate leading paragraph');
   const classify=await c.fetchText('/classify');
   const nativeCount=classify.split('\n').filter(l=>l.includes('[Text]')&&l.includes('text="'+nativeTitle+'"')).length;
   const leafCount=await page.locator('#fmroot').evaluate((root,title)=>Array.from(root.querySelectorAll('p,span')).filter(e=>!e.closest('#fmnav-tree')&&!e.children.length&&e.textContent===title).length,nativeTitle);
   assert.equal(leafCount,nativeCount,'Later same-name native story groups remain present');
   const before=await page.locator('#fmroot table').evaluateAll(es=>es.map(e=>e.innerText));
   const id=await source.getAttribute('id');await heading.focus();await page.evaluate(()=>FM.core.refresh(false,true));await settled();
   assert.equal(await page.evaluate(()=>document.activeElement.id),'fmsec1-h');
   assert.equal(await source.getAttribute('id'),id);assert.equal(await source.innerText(),nativeTitle);
   assert.deepEqual(await page.locator('#fmroot table').evaluateAll(es=>es.map(e=>e.innerText)),before);
   const axe=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
   audits.push({uid,violations:axe.violations.map(v=>v.id)});assert.deepEqual(axe.violations,[]);
   observations.push({uid,title,nativeTitle,nativeCount,leafCount,sourceId:id,tables:before.length});
  }
  checks.push('Both native news destinations reuse their actual first header source, with no second leading title or lost same-name story group');
  checks.push('Neutral refresh preserves native text identity, section focus and all actual table contents');
  await go(989379584);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  const report={success:true,mvid:JSON.parse(await c.fetchText('/health')).coreMvid,checks,observations,audits,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/news-title.json'),JSON.stringify(report,null,2));
  console.log(JSON.stringify(report,null,2));
 }catch(error){
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/news-title-failure.json'),JSON.stringify({success:false,error:error.stack,checks,observations,audits,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures},null,2));throw error;
 }finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
