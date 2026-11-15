// Actual native table-header options. Open/cancel only; never edit a column/view.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],observations={};let success=false,mvid;
 const rows=()=>page.locator('#fmroot table').evaluateAll(ts=>ts.map(t=>({headers:[...t.querySelectorAll('thead th')].map(e=>e.textContent),rows:[...t.querySelectorAll('tbody tr')].map(e=>e.innerText)})));
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned QA identity');observations.ownedSave=await file.inputValue();assert.match(observations.ownedSave,/^FM27Access QA /);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="1381513472"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const nav=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');nav.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await nav).text(),/^Opening /);await settled();await search.fill('');await settled();
  const before=await rows();observations.before=before;
  const headers=page.locator('#fmroot thead button[aria-haspopup="menu"]:enabled');observations.headers=await headers.evaluateAll(xs=>xs.map(x=>({id:x.id,name:x.textContent,handler:x.getAttribute('onclick')})));assert.ok(observations.headers.length>=2,'Actual native squad has two column-option menus');
  observations.nativeInventory=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),require('./native-handle-inventory.cjs')(mvid,observations.headers.map(h=>h.id)));
  assert.doesNotMatch(observations.nativeInventory,/\[compile\]|\[exception\]/);
  for(const header of observations.headers){const line=observations.nativeInventory.split(/\r?\n/).find(l=>l.startsWith('HANDLE|'+header.id+'|'));assert.ok(line&&line.includes('|HeaderHandle|ColumnHeaderAdapter|UnityEngine.UIElements.Button|')&&line.includes('Header.ContextMenu=True'),line);assert.ok(line.includes('Header.PrimaryClick='+(header.handler.startsWith('FM.contextmenu.open(')?'False':'True')),line);}
  for(const header of observations.headers.slice(0,2)){
   const origin=page.locator('#'+header.id);await origin.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/contextmenu');response.catch(()=>{});await page.keyboard.press('Shift+F10');assert.match(await(await response).text(),/^Opened menu/);await poll(()=>page.getByRole('menu').count(),'actual native header menu');await settled();
   const menu=await page.getByRole('menu').innerText();assert.ok(menu.trim());(observations.menus??=[]).push({header,menu,nativePresent:(await c.fetchText('/gui')).length>0});
   const audit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name:'Native column options '+header.name,violations:audit.violations.map(v=>v.id)});assert.deepEqual(audit.violations,[]);
   await page.keyboard.press('Escape');await poll(()=>page.evaluate(id=>document.activeElement.id===id,header.id),'exact native header origin');await settled();assert.equal(await page.getByRole('menu').count(),0);assert.deepEqual(await rows(),before);
  }
  checks.push('Two actual native column-option menus open by Shift+F10 and Escape returns exact header focus with every native row/column/order unchanged; no business leaf submitted');
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,mvid);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(error){observations.failure=error.stack;throw error;}
 finally{fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/header-menu.json'),JSON.stringify({success,mvid,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
