// Actual custom-database editor: cancel without committing setup or creating a world.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const c=await require('./original-client.cjs')(),{page}=c;
 const report={success:false,settingsCommitted:false,worldCreated:false,manualNVDA:false};
 const snapshot=()=>page.locator('#fmroot').evaluate(root=>({
  rows:[...root.querySelectorAll('table tbody tr')].map(e=>e.textContent),
  inputs:[...root.querySelectorAll('input')].map(e=>({type:e.type,name:e.labels?.[0]?.textContent?.trim()||e.getAttribute('aria-label'),checked:e.checked,value:e.value})),
  selects:[...root.querySelectorAll('select')].map(e=>({name:e.getAttribute('aria-label'),value:e.value,text:e.selectedOptions[0]?.textContent}))
 }));
 try{
  assert.equal(await page.title(),'Configuração Avançada');
  const before=await snapshot();await c.dropdown('Número total de jogadores');
  const options=await page.locator('select[data-dd][data-loaded]').last().locator('option').allTextContents();
  report.options=options;const editor=options.filter(x=>x==='Base de Dados Personalizada');
  assert.equal(editor.length,1,'Use the actual native custom-database option');
  const cursor=Number((await c.fetchText('/speech')).match(/^cursor: (\d+)/)[1]);
  await c.choose(editor[0]);report.editorTitle=await page.title();
  report.editorHeadings=await page.locator('#fmroot h1,#fmroot h2').allTextContents();
  report.editorText=await page.locator('#fmroot').innerText();
  report.spoken=(await c.fetchText('/speech?since='+cursor)).split('\n').filter(x=>/^\d+:/.test(x));
  assert.equal(report.editorTitle,'Definição Personalizável');
  assert.deepEqual(report.editorHeadings,['Definição Personalizável']);
  assert(!report.spoken.some(x=>/GameCreation|DatabaseSelectionCard|AddNationsModal/.test(x)));
  // Ordinary selection feedback remains; it is not a panel-entry announcement.
  assert(report.spoken.every(x=>x.replace(/^\d+: /,'')==='Base de Dados Personalizada selected'||x.replace(/^\d+: /,'')==='Definição Personalizável'));
  const cancel=page.getByRole('button',{name:'Cancelar',exact:true});assert.equal(await cancel.count(),1);
  await c.activate(cancel);await c.poll(async()=>await page.title()==='Configuração Avançada','database editor cancellation');
  assert.deepEqual(await snapshot(),before);report.advancedSettingsUnchanged=true;
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);report.success=true;
 }catch(e){report.error=e.stack;throw e;}
 finally{
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/database-title-arrival-check.json'),JSON.stringify(report,null,2));
  await c.browser.close();
 }
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
