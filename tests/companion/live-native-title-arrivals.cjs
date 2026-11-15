// Actual pending game setup: no database/world creation or configuration commit.
// Preserve every current country choice and the selected continent on return.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const checks=[],bodies=[];let success=false,error,initial,advanced,dropdowns;
 page.on('response',async r=>{if(new URL(r.url()).pathname!=='/body')return;try{const html=await r.text();bodies.push({headings:[...html.matchAll(/<h[1-6][^>]*>(.*?)<\/h[1-6]>/g)].map(m=>m[1].replace(/<[^>]+>/g,''))});}catch{}});
 async function speechCursor(){const text=await c.fetchText('/speech?since=0');return Number(text.match(/^cursor: (\d+)/)[1]);}
 async function spoken(since){const text=await c.fetchText('/speech?since='+since);return text.split('\n').filter(x=>/^\d+:/.test(x)).map(x=>x.replace(/^\d+: /,''));}
 async function countries(){
  const captions=await page.getByRole('radio').evaluateAll(es=>es.map(e=>({name:e.labels[0].textContent.trim(),checked:e.checked})));
  const values={};for(const {name}of captions){const r=page.getByRole('radio',{name,exact:true});if(!await r.isChecked())await c.activate(r,'Space');await settled();values[name]=await page.getByRole('checkbox').evaluateAll(es=>es.map(e=>({name:e.labels[0].textContent.trim(),checked:e.checked})));}
  return{continent:captions.find(x=>x.checked).name,values};
 }
 async function restoreCountries(state){
  for(const [continent,values]of Object.entries(state.values)){
   const r=page.getByRole('radio',{name:continent,exact:true});if(!await r.isChecked())await c.activate(r,'Space');await settled();
   const actual=await page.getByRole('checkbox').evaluateAll(es=>es.map(e=>({name:e.labels[0].textContent.trim(),checked:e.checked})));
   assert.deepEqual(actual.map(x=>x.name),values.map(x=>x.name),'Same actual country catalog');
   for(const value of values){const input=page.getByRole('checkbox',{name:value.name,exact:true});if(await input.isChecked()!==value.checked)await c.activate(input,'Space');}
  }
  const original=page.getByRole('radio',{name:state.continent,exact:true});if(!await original.isChecked())await c.activate(original,'Space');
 }
 async function advancedState(){return page.locator('#fmroot').evaluate(root=>({rows:[...root.querySelectorAll('table tbody tr')].map(x=>x.textContent),inputs:[...root.querySelectorAll('input')].map(e=>({type:e.type,name:e.labels?.[0]?.textContent?.trim()||e.getAttribute('aria-label'),checked:e.checked,value:e.value})),selects:[...root.querySelectorAll('select')].map(e=>({name:e.getAttribute('aria-label'),value:e.value,text:e.selectedOptions[0]?.textContent}))}));}
 try{
  assert.equal(await page.title(),'Adicionar Nações');initial=await countries();
  const r=page.getByRole('radio',{name:initial.continent,exact:true});if(!await r.isChecked())await c.activate(r,'Space');
  let cursor=await speechCursor();await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));await poll(async()=>await page.title()==='Configuração Avançada','actual advanced setup return');
  advanced=await advancedState();let lines=await spoken(cursor);assert(lines.every(x=>x==='Configuração Avançada'));checks.push({stage:'actual native advanced title retained',spoken:lines});
  for(let pass=0;pass<3;pass++){
   cursor=await speechCursor();await c.activate(page.getByRole('button',{name:'Adicionar Nações',exact:true}));await poll(async()=>await page.title()==='Adicionar Nações','native countries heading');
   lines=await spoken(cursor);assert(lines.every(x=>x==='Adicionar Nações'),'No internal identifier or automatic first control: '+JSON.stringify(lines));
   checks.push({stage:'country popup first entry',pass,spoken:lines,heading:await page.locator('#fmsec0-h').innerText()});
   cursor=await speechCursor();await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));await poll(async()=>await page.title()==='Configuração Avançada','native advanced return');
   lines=await spoken(cursor);assert(lines.every(x=>x==='Configuração Avançada'));assert.deepEqual(await advancedState(),advanced);checks.push({stage:'country popup cancel and native return',pass,spoken:lines});
  }
  dropdowns=await page.getByRole('combobox').evaluateAll(es=>es.map(e=>({name:e.getAttribute('aria-label'),text:e.textContent})));
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/setup-title-advanced.html'),await page.locator('#fmroot').innerHTML());
  console.log('ADVANCED DROPDOWNS',dropdowns);
  for(const item of dropdowns.filter(x=>/Base de Dados/i.test(x.name))){const dropdown=await c.dropdown(item.name);const options=await dropdown.locator('option').allTextContents();checks.push({stage:'actual database size choices inspected/cancelled',name:item.name,options});await c.cancelDropdown();assert.deepEqual(await advancedState(),advanced);console.log('DATABASE OPTIONS',item.name,options);}
  await c.activate(page.getByRole('button',{name:'Adicionar Nações',exact:true}));await poll(async()=>await page.title()==='Adicionar Nações','original popup restored');await restoreCountries(initial);
  const restored=await countries();assert.deepEqual(restored.values,initial.values);await restoreCountries(initial);
  assert(bodies.every(b=>b.headings.every(h=>!/AddNationsModal|DatabaseSelectionCard|GameCreationAdvancedSetupScreen/.test(h))));
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(e){error=e.stack;throw e;}
 finally{
  const report={success,error,health:JSON.parse(await c.fetchText('/health')),checks,bodies,initial,advanced,dropdowns,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,worldCreated:false,countryConfigurationConfirmed:false,databaseConfigurationConfirmed:false,manualNVDA:false};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/native-title-arrivals-live.json'),JSON.stringify(report,null,2));await c.browser.close();
 }
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
