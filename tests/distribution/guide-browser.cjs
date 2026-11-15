const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {pathToFileURL}=require('node:url');
const {chromium}=require('../companion/node_modules/playwright');
const {default:AxeBuilder}=require('../companion/node_modules/@axe-core/playwright');
(async()=>{
  const browser=await chromium.launch({headless:true});
  const observations=[];
  try{
    const context=await browser.newContext({locale:'pt-BR'}),page=await context.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
    const url=pathToFileURL(path.resolve(__dirname,'../../docs/user/FM27Access-UserGuide.html')).href;
    await page.goto(url+'?lang=pt-BR#navigation');assert.equal(await page.locator('html').getAttribute('lang'),'pt-BR');
    const codes=await page.locator('#language option').evaluateAll(es=>es.map(e=>e.value));
    for(const code of codes){
      await page.selectOption('#language',code);
      assert.equal(await page.locator('html').getAttribute('lang'),code);
      assert.equal(await page.locator('article:visible').count(),1);
      assert.equal(await page.locator('article:visible h1').count(),1);
      assert.equal(await page.locator('article:visible h2').count(),14);
      assert.equal(await page.locator('article:visible table').count(),2);
      assert.equal(await page.locator('#navigation').count(),1);
      const ids=await page.locator('[id]').evaluateAll(es=>es.map(e=>e.id));assert.equal(ids.length,new Set(ids).size);
      const audit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
      assert.deepEqual(audit.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)})),[],code);
      observations.push({code,sections:14,axeViolations:0});
    }
    await page.goto(url+'?lang=zz-ZZ');assert.equal(await page.locator('html').getAttribute('lang'),codes.at(-1)); // saved last explicit selection
    await page.goto(url+'?lang=de-DE');assert.equal(await page.locator('html').getAttribute('lang'),'de');
    await page.emulateMedia({forcedColors:'active'});assert.equal(await page.locator('article:visible').count(),1);
    await page.setViewportSize({width:360,height:800});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
    assert.deepEqual(errors,[]);
    const out=path.resolve(__dirname,'../../artifacts/tests/installer-guide-browser.json');fs.mkdirSync(path.dirname(out),{recursive:true});
    await page.setViewportSize({width:1100,height:850});await page.emulateMedia({forcedColors:'none'});
    await page.goto(url+'?lang=pt-BR');
    await page.screenshot({path:path.join(path.dirname(out),'installer-guide-pt-BR.png')});
    fs.writeFileSync(out,JSON.stringify({observations,errors,manualNVDA:false},null,2));
    console.log(`PASS: ${codes.length} guide languages, hash preservation, fallback, narrow viewport, high contrast and axe.`);
  }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
