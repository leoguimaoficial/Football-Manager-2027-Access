// Compiled Release against the real game, using only public companion operations.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true});
 const {page,poll,settled,activate}=c,checks=[],audits=[];
 const out=path.resolve(__dirname,'../../artifacts/tests/web');fs.mkdirSync(out,{recursive:true});
 let success=false,health,ownedSave;
 try{
  health=JSON.parse(await c.fetchText('/health'));assert.equal(health.developerTools,false);assert.equal(health.version,'0.1.0');
  assert.equal(await page.locator('html').getAttribute('data-fm-development'),'false');
  for(const [route,method] of [['/eval','POST'],['/reload','POST'],['/input','POST'],['/gui','GET'],['/rendered/report','GET'],['/screenshot','GET'],['/httptrace','GET'],['/navigator/list','GET'],['/does-not-exist','GET']]){
   const result=await page.evaluate(async({route,method})=>{const r=await fetch(route,{method});return{status:r.status,body:await r.text()};},{route,method});
   assert.equal(result.status,404,route);assert.equal(result.body,'Unknown endpoint.\n',route);
  }
  checks.push('Authenticated developer/unknown endpoints return explicit 404 without dispatching game operations');
  assert.match(await c.fetchText('/log'),/^cursor: /);checks.push('Session-protected support log remains available');
  // A live Unity pump/root can precede native MainMenu while the real loading
  // panel is still present. Observe an actual entry/career control, not health
  // alone, before selecting which native workflow to exercise.
  await page.waitForFunction(()=>document.title==='MainMenu'||!!document.querySelector('select[aria-label="FM Menu"]'),{},{timeout:120000});
  await settled();
  if(await page.title()==='MainMenu'){
   const checkpoint='FM27Access QA Dialog Regression 20261110 Responsibilities Restored 20261110104840158';
   const baseline=JSON.parse(fs.readFileSync(path.join(out,'../../current-original-save-preservation-baseline.json'),'utf8').replace(/^\uFEFF/,''));
   assert.ok(baseline.protectedFiles.some(f=>f.name===checkpoint+'.fm'));
   const audit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
   audits.push({screen:'MainMenu',violations:audit.violations});assert.deepEqual(audit.violations,[]);
   await activate(page.getByRole('button',{name:'CARREGAR JOGO',exact:true}));
   const row=page.getByRole('row').filter({hasText:checkpoint}).getByRole('radio');await poll(()=>row.count(),'protected QA checkpoint');assert.equal(await row.count(),1);
   await activate(row,'Space');await activate(page.getByRole('button',{name:'Carregar',exact:true}));
   await page.getByRole('heading',{name:'Portal',exact:true}).waitFor({timeout:90000});await settled();
   checks.push('Fresh Release main menu opens the native load dialog and loads only the protected QA checkpoint');
  }
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned QA identity');ownedSave=await file.inputValue();assert.match(ownedSave,/^FM27Access QA /);
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  checks.push('Native Save As reports the owned QA identity and cancels without writing');
  assert.deepEqual(await page.locator('#fmnav-tree > [role=treeitem] > span').allTextContents(),['PORTAL','PLANTEL','TÁCTICAS','RECRUTAMENTO','TREINO','CLUBE']);
  await page.keyboard.press('Alt+1');await poll(()=>page.getByRole('heading',{name:'Mensagens',exact:true}).count(),'Alt+1 Messages');await settled();
  assert.equal(await page.getByRole('heading',{name:/^mensagens$/i}).count(),1);
  const audit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({screen:'Messages',violations:audit.violations});assert.deepEqual(audit.violations,[]);
  checks.push('Alt+1 opens native Messages with one title and zero axe violations');
  assert.equal(c.requests.some(r=>r.path==='/rendered/report'&&r.method==='POST'),false,'Release must not serialize/report the DOM');
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,health.coreMvid);
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(error){checks.push('Failure: '+error.stack);throw error;}
 finally{fs.writeFileSync(path.join(out,'live-production-package.json'),JSON.stringify({at:new Date().toISOString(),success,health,ownedSave,checks,audits,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,client:'Compiled production document through the sole qualification client; process ownership and client mode are recorded in session evidence',saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));await c.browser.close();console.log(JSON.stringify({success,checks,audits:audits.length}));}
})().catch(error=>{console.error(error);process.exitCode=1;});
