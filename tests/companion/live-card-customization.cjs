// Native Portal card replacement in an owned Portuguese QA career. Restores
// the original Data Hub tile; no simulation or save submission.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 let changed=false;
 try{
  // The preceding native regression may finish in a different destination.
  // Establish Portal through its real keyboard navigation before locating tiles.
  if(!await page.getByRole('heading',{name:'Portal',exact:true}).count()){
   const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="989379584"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
   await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const reply=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');reply.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await reply).text(),/^Opening /);await poll(()=>page.getByRole('heading',{name:'Portal',exact:true}).count(),'actual Portal before card regression');await settled();await search.fill('');await settled();
  }
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'QA identity');
  const ownedSave=await file.inputValue();assert.match(ownedSave,/^FM27Access QA /);
  await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));await settled();
  const original='Customize card: CENTRAL DE DADOS',medical='Customize card: DEPARTAMENTO MÉDICO';
  const id=await page.getByRole('combobox',{name:original,exact:true}).getAttribute('id');
  const dd=await c.dropdown(original),options=await dd.locator('option').allTextContents();
  assert.ok(options.includes('Central de Dados (Predefinição)'));assert.ok(options.includes('Depto. Médico'));
  changed=true;await c.choose('Depto. Médico');
  await poll(()=>page.getByRole('combobox',{name:medical,exact:true}).count(),'native replacement');await settled();
  assert.equal(await page.getByRole('heading',{name:'DEPARTAMENTO MÉDICO',exact:true}).count(),1);
  assert.equal(await page.evaluate(()=>document.activeElement.id),id);
  const developerTools=JSON.parse(await c.fetchText('/health')).developerTools!==false;
  if(developerTools){
   const native=await c.fetchText('/subtree?name=swap-tile-dropdown&count=20');
   assert.ok(native.includes('SIDropdown name="swap-tile-dropdown"'));
  }
  const medicalAudit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  await c.dropdown(medical);await c.cancelDropdown();
  assert.equal(await page.evaluate(()=>document.activeElement.id),id);
  assert.equal(await page.locator('#'+id).getAttribute('data-card-options'),'1');
  await c.dropdown(medical);await c.choose('Central de Dados (Predefinição)');
  await poll(()=>page.getByRole('combobox',{name:original,exact:true}).count(),'restored original content');await settled();changed=false;
  assert.equal(await page.evaluate(()=>document.activeElement.id),id);
  const report={ownedSave,mvid:JSON.parse(await c.fetchText('/health')).coreMvid,nativeOptions:options.length,changedCard:'DEPARTAMENTO MÉDICO',restoredCard:'CENTRAL DE DADOS',origin:id,focusAfterCommitAndCancel:true,developerSubtreeInspected:developerTools,scope:developerTools?'Native subtree and production card actions':'Release card actions/results; developer native subtree inspection is outside this run',violations:medicalAudit.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)})),errors:c.errors,network:c.network,manualNVDA:false};
  assert.deepEqual(report.violations,[]);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);
  const output=path.resolve(__dirname,'../../artifacts/tests/web/card-customization.json');fs.mkdirSync(path.dirname(output),{recursive:true});fs.writeFileSync(output,JSON.stringify({...report,success:true,requestFailures:c.requestFailures},null,2));
  console.log(JSON.stringify(report,null,2));
 }catch(error){
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/card-customization-failure.json'),JSON.stringify({error:error.stack,html:await page.locator('#fmroot').innerHTML(),errors:c.errors,requestFailures:c.requestFailures,network:c.network,transport:await c.transportTrace()},null,2));throw error;
 }finally{
  if(changed&&await page.getByRole('combobox',{name:'Customize card: DEPARTAMENTO MÉDICO',exact:true}).count()){
   await c.dropdown('Customize card: DEPARTAMENTO MÉDICO');await c.choose('Central de Dados (Predefinição)');await settled();
  }
  await c.browser.close();
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
