// Compare every actual roster cell with the retained native pre-correction
// document. Only the external selected-position face is consolidated into its
// existing dropdown; all other data, order and controls must remain identical.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'artifacts/tests/web/squad-selector-labels.json');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,checks:[],audits:[],saveSubmitted:false,simulationAdvanced:false,playerSelectionSubmitted:false,manualNVDA:false};
 try{
  const before=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-squad-position-label-before.json'),'utf8'));
  report.health=JSON.parse(await c.fetchText('/health'));assert.equal(report.health.pid,before.health.pid);assert.notEqual(report.health.coreMvid,before.health.coreMvid,'Require the corrected actual core');
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');report.checkpoint=await page.getByRole('textbox',{name:'File name',exact:true}).inputValue();assert.equal(report.checkpoint,'FM27Access QA Dialog Regression 20261110 Responsibilities Restored 20261110104840158');await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="1381513472"][data-obj="2000104296"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await settled();await search.fill('');await settled();
  const beforeHtml=fs.readFileSync(path.join(root,'artifacts/career-squad-position-label-closed-before.html'),'utf8');
  report.comparison=await page.evaluate(beforeHtml=>{
   const old=new DOMParser().parseFromString(beforeHtml,'text/html');
   function snapshot(root){
    const tables=[...root.querySelectorAll('table')].filter(t=>[...t.querySelectorAll('thead th')].some(h=>h.textContent.trim()==='Escolhida')&&[...t.querySelectorAll('thead th')].some(h=>h.textContent.trim()==='Jogadora'));
    if(tables.length!==1)throw Error('Require the actual single native roster table');const table=tables[0],headers=[...table.querySelectorAll('thead th')].map(h=>h.textContent.trim()),col=headers.indexOf('Escolhida');
    return{headers,col,rows:[...table.querySelectorAll('tbody tr')].map(row=>[...row.querySelectorAll(':scope > td,:scope > th')].map(cell=>{
     const controls=[...cell.querySelectorAll('button,input,select,textarea,a[href]')].map(e=>({tag:e.tagName,name:e.getAttribute('aria-label')||e.textContent.trim(),disabled:!!e.disabled,type:e.getAttribute('type'),checked:e.checked??null,text:e.textContent.trim()}));
     const clone=cell.cloneNode(true);clone.querySelectorAll('button,input,select,textarea,a[href]').forEach(e=>e.remove());return{text:cell.textContent.trim(),outside:clone.textContent.trim(),controls};
    }))};
   }
   return{before:snapshot(old),after:snapshot(document.getElementById('fmroot'))};
  },beforeHtml);
  const {before:b,after:a}=report.comparison;assert.deepEqual(a.headers,b.headers);assert.equal(a.rows.length,b.rows.length);assert.ok(a.rows.length>20);
  report.valuesRequiringNativeRecovery=[];
  let corrected=0;for(let row=0;row<b.rows.length;row++){
   assert.equal(a.rows[row].length,b.rows[row].length);
   for(let col=0;col<b.headers.length;col++){
    const old=b.rows[row][col],now=a.rows[row][col];
    if(col!==b.col){assert.deepEqual(now,old,'Every other cell/data/control remains unchanged at '+row+'/'+col);continue;}
    assert.equal(old.controls.length,1);assert.equal(now.controls.length,1);assert.equal(now.controls[0].tag,'SELECT');assert.equal(now.controls[0].name,a.headers[col]);assert.equal(now.controls[0].disabled,old.controls[0].disabled);
    const value=old.outside||old.controls[0].text;if(value)assert.equal(now.controls[0].text,value,'Actual native position value retained');else report.valuesRequiringNativeRecovery.push(row);assert.equal(now.outside,'','Value is read once through the dropdown, not duplicated as a sibling');if(old.controls[0].name==='-')corrected++;
   }
  }
  assert.equal(corrected,6);report.corrected=corrected;report.checks.push('All six hyphen purpose names corrected; every selected-position value retained once, all remaining roster cells/headers/order/control semantics identical');
  report.nativeValues=[];
  for(let index=0;index<a.rows.length;index++){
   const nativeRow=page.locator('table').filter({has:page.getByRole('columnheader',{name:'Jogadora',exact:true})}).locator('tbody > tr').nth(index),field=nativeRow.getByRole('combobox',{name:'Escolhida',exact:true});assert.equal(await field.count(),1);const owner=await field.getAttribute('id');
   await field.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');opened.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await opened).text(),/^</);await poll(()=>field.getAttribute('data-loaded'),'actual realized native roster choice');
   const native=await require('./native-dropdown-label-resolution.cjs')(c,report.health.coreMvid,[owner]);assert.equal(native.realized,1);const value=native.raw.split(/\r?\n/).find(l=>l.startsWith('RESOLVE|GetLabel|'))?.slice('RESOLVE|GetLabel|'.length);assert.ok(value);assert.equal(a.rows[index][a.col].controls[0].text,value,'Independent actual native face agrees, including previously empty HTML values');
   assert.ok(native.raw.split(/\r?\n/).some(l=>/SIText.*visible=True/.test(l)&&l.includes('text="'+value+'"')),'Value is independently present in the actual native cell/label-container tree');report.nativeValues.push({index,owner,value,previouslyMissing:report.valuesRequiringNativeRecovery.includes(index),raw:native.raw});await c.cancelDropdown();await settled();assert.equal(await page.evaluate(()=>document.activeElement.id),owner);
  }
  report.checks.push('Every actual position face independently matches its realized native text, including values absent from the old HTML; all native menus cancel with exact focus and no assignment');
  const table=page.getByRole('table').filter({has:page.getByRole('columnheader',{name:'Jogadora',exact:true})});
  const row=table.getByRole('row').filter({has:page.getByRole('button',{name:'Anneke Borbe',exact:true})}),field=row.getByRole('combobox',{name:'Escolhida',exact:true});assert.equal(await field.count(),1);assert.equal(await field.textContent(),'-');report.origin=await field.getAttribute('id');
  await field.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');opened.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await opened).text(),/^</);await poll(()=>field.getAttribute('data-loaded'),'actual same-player position menu');report.options=await field.locator('option').allTextContents();assert.ok(report.options.some(t=>t.includes('Daphne van Domselaar')));await c.cancelDropdown();await settled();assert.equal(await page.evaluate(()=>document.activeElement.id),report.origin);assert.equal(await field.textContent(),'-');report.checks.push('Corrected selector still opens actual filled lineup/bench choices, cancels without assignment and restores exact origin focus/value');
  const axe=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({name:'Corrected native roster',violations:axe.violations.map(v=>v.id)});assert.deepEqual(axe.violations,[]);
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
 }catch(e){report.failure=e.stack;report.html=await page.locator('#fmroot').innerHTML().catch(()=>null);throw e;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify(report,null,2));console.log(JSON.stringify({success:report.success,checks:report.checks.length,audits:report.audits.length,corrected:report.corrected,failure:report.failure}));await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
