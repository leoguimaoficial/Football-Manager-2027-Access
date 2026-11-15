// Actual native captioned training arrows, preserving every other readonly
// value/control in the retained real before-image. No simulated game data.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const{default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],observations={};let success=false,mvid;
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned QA');observations.ownedSave=await file.inputValue();assert.match(observations.ownedSave,/^FM27Access QA Dialog Regression /);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  const e=page.locator('#fmnav-tree [role=treeitem][data-uid="1355920640"]:not([data-obj])').first(),q=page.getByRole('textbox',{name:'Search all screens',exact:true});await q.fill(await e.locator(':scope > span').textContent());await e.focus();const nav=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');nav.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await nav).text(),/^Opening /);await settled();await q.fill('');await settled();
  const gui=await c.fetchText('/gui'),lines=gui.split(/\r?\n/);
  observations.nativePairs=['Improvements','Regressions'].map(name=>{
   const start=lines.findIndex(l=>l.includes('name="'+name+'"'));assert.ok(start>=0);
   const depth=Number(lines[start].match(/\[(\d+)\]/)[1]),block=[];
   for(let i=start+1;i<lines.length;i++){const d=lines[i].match(/\[(\d+)\]/);if(d&&Number(d[1])<=depth)break;block.push(lines[i]);}
   const text=field=>{const line=block.find(l=>l.includes('SIText name="'+field+'" visible=True'));assert.ok(line);const match=line.match(/text="([^"]+)"/);assert.ok(match);return match[1];};
   assert.equal(block.filter(l=>l.includes('SIImage name="Arrow" visible=True')).length,1,'The game retains its actual painted arrow');
   return{branch:name,value:text('Value'),caption:text('Title')};
  });
  assert.equal(await page.getByText('Arrow, image',{exact:true}).count(),0);
  for(const pair of observations.nativePairs){assert.equal(await page.getByText(pair.caption,{exact:true}).count(),1);assert.ok(await page.getByText(pair.value,{exact:true}).count()>0);}
  checks.push('Both actual native arrow images remain visible in the game while their already-captioned artwork is omitted from the companion; native values and localized direction captions remain');
  const baseline=path.resolve(__dirname,'../../artifacts/current-1924-squad-arrow-companion.html');
  if(fs.existsSync(baseline)){
   const before=fs.readFileSync(baseline,'utf8');
   const unchanged=await page.evaluate(before=>{
    const doc=new DOMParser().parseFromString(before,'text/html');
    let removed=0;for(const e of doc.querySelectorAll('p'))if(e.textContent.trim()==='Arrow, image'){e.remove();removed++;}
    const normalize=e=>e.textContent.replace(/\s+/g,' ').trim();
    const controls=e=>[...e.querySelectorAll('button,input,select,textarea,a[href]')].map(x=>({tag:x.tagName,type:x.getAttribute('type'),name:x.getAttribute('aria-label')||x.labels?.[0]?.textContent||x.textContent}));
    return{removed,textEqual:normalize(doc.body)===normalize(document.getElementById('fmroot')),controlsEqual:JSON.stringify(controls(doc.body))===JSON.stringify(controls(document.getElementById('fmroot')))};
   },before);
   observations.retainedBeforeComparison=unchanged;assert.equal(unchanged.removed,2);assert.equal(unchanged.textEqual,true,'Every other actual text value/caption stays intact');assert.equal(unchanged.controlsEqual,true,'All actual control names/order stay intact');
   checks.push('The retained real before-HTML differs in human-readable text only by the two redundant Arrow announcements; every control name/order is unchanged');
  }
  const result=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name:'Native training tile without duplicate direction artwork',violations:result.violations.map(v=>v.id)});assert.deepEqual(result.violations,[]);
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,mvid);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(error){observations.failure=error.stack;throw error;}
 finally{fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/training-artwork.json'),JSON.stringify({success,mvid,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
