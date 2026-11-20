// Current pre-career club profile only. Never activate Iniciar Carreira.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,activate,poll,settled}=c;
 const output=path.resolve(__dirname,'../../artifacts/career-start-structure');fs.mkdirSync(output,{recursive:true});
 const report={success:false,audits:[],careerStartActivated:false,manualNVDA:false};
 async function audit(label){const result=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({label,violations:result.violations});assert.deepEqual(result.violations,[]);assert.equal(await page.locator('h1 button,h2 button,h3 button,h4 button,h5 button,h6 button').count(),0);}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));assert.equal(await page.title(),'Career Start');
  const profile=page.getByRole('tab',{name:'Perfil do clube',exact:true});assert.equal(await profile.getAttribute('aria-selected'),'true');
  const snapshot=await page.locator('#fmroot').evaluate(root=>({text:root.innerText,html:root.innerHTML}));
  const beforePath=path.join(output,'before.html');
  if(fs.existsSync(beforePath)){
   const comparison=await page.locator('#fmroot').evaluate((root,html)=>{
    const old=new DOMParser().parseFromString(html,'text/html').querySelector('#fmroot');
    function text(r){const iterator=document.createTreeWalker(r,NodeFilter.SHOW_TEXT);const values=[];while(iterator.nextNode())values.push(iterator.currentNode.textContent);return values.join(' ').replace(/:/g,'').replace(/\s+/g,' ').trim();}
    function controls(r){return[...r.querySelectorAll('button,input,select,a')].map(x=>({tag:x.tagName,id:x.id,role:x.getAttribute('role'),name:x.getAttribute('aria-label')||x.textContent,disabled:x.disabled,onclick:x.getAttribute('onclick')}));}
    return{beforeText:text(old),afterText:text(root),beforeControls:controls(old),afterControls:controls(root)};
   },fs.readFileSync(beforePath,'utf8'));
   assert.equal(comparison.afterText,comparison.beforeText,'all native text retained in native order');assert.deepEqual(comparison.afterControls,comparison.beforeControls,'all native controls/identities/actions/availability retained');report.completeBeforeAfterEqual=true;
  }
  const headings=await page.locator('#fmroot h3').allTextContents();assert.deepEqual(headings,['Equipamentos','Cultura da Direcção','Cultura dos Adeptos','Competições de taça','Rivais Notáveis']);report.headings=headings;
  report.listSizes=await page.locator('#fmroot ul').evaluateAll(lists=>lists.map(list=>list.querySelectorAll(':scope > li').length));assert.deepEqual(report.listSizes,[3,6,6,6,3,2]);
  const board=page.getByRole('heading',{name:'Cultura da Direcção',exact:true}).locator('xpath=following-sibling::ul[1]');
  report.board=await board.locator(':scope > li').allTextContents();assert.equal(report.board.at(-1),'Contratar jogadores da Espanha');
  assert.equal(await board.getByRole('button').count(),0,'native disabled inline entity links remain readonly');
  const cups=page.getByRole('heading',{name:'Competições de taça',exact:true}).locator('xpath=following-sibling::ul[1]');assert.match(await cups.locator(':scope > li').nth(1).innerText(),/Supertaça\s+Começa na Meia-final/);
  const read=await c.fetchText('/read');for(const goal of report.board)assert.ok(read.includes(goal),goal);assert.match(read,/Finanças: Ok/);assert.match(read,/Real Madrid/);fs.writeFileSync(path.join(output,'read.txt'),read);
  await audit('Structured native club profile');
  for(const name of ['Notícias de Última Hora','Melhor 11']){
   await activate(page.getByRole('tab',{name,exact:true}));await poll(async()=>await page.getByRole('tab',{name,exact:true}).getAttribute('aria-selected')==='true','actual native '+name+' tab');await settled();
   fs.writeFileSync(path.join(output,name.startsWith('Melhor')?'best-xi.html':'news.html'),await page.content());await audit(name);
   await activate(page.getByRole('tab',{name:'Perfil do clube',exact:true}));await poll(async()=>await profile.getAttribute('aria-selected')==='true','original profile tab restored');await settled();
   assert.equal(await page.locator('#fmroot').innerText(),snapshot.text,'complete original profile retained after native tab roundtrip');
  }
  assert.equal(await page.evaluate(()=>document.activeElement?.id),await profile.getAttribute('id'),'exact native tab focus retained');
  const finalHealth=JSON.parse(await c.fetchText('/health'));assert.equal(finalHealth.pid,report.health.pid);assert.equal(finalHealth.coreMvid,report.health.coreMvid);
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
  fs.writeFileSync(path.join(output,'final.html'),await page.content());
 }catch(error){report.error=error.stack;fs.writeFileSync(path.join(output,'failure.html'),await page.content().catch(()=>''));throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;fs.writeFileSync(path.join(output,'live.json'),JSON.stringify(report,null,2));await c.browser.close();console.log(JSON.stringify({success:report.success,listSizes:report.listSizes,audits:report.audits.length}));}
})().catch(error=>{console.error(error);process.exitCode=1;});
