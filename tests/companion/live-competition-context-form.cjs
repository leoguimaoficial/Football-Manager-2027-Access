// Real senior/youth competition groups and league-stage forms. No save,
// simulation, sorting, team selection or business operation is submitted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),artifacts=path.join(root,'artifacts');
const clean=s=>s.replace(/\s+/g,' ').trim();
const referenceArg=process.argv.find(a=>a.startsWith('--native-reference='));
const reference=referenceArg?JSON.parse(fs.readFileSync(path.resolve(root,referenceArg.slice(19)),'utf8')):null;
const checkpoint='FM27Access QA Dialog Regression 20261110 Responsibilities Restored 20261110104840158';
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,activate,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,checks:[],audits:[],nativeDatasets:{},observations:[],saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const output=path.join(artifacts,'tests/web/competition-context-form.json');
 const heading=name=>page.getByRole('heading',{name,level:3,exact:true});
 async function go(obj){const item=page.locator('#fmnav-tree [role=treeitem][data-uid="3574631936"][data-obj="'+obj+'"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});await search.fill(await item.locator(':scope > span').textContent());await item.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await poll(()=>page.evaluate(()=>FMTOKEN.split('\x1f').some(p=>p.startsWith('3574631936#'))),'actual competition overview');await settled();}
 async function tables(){return page.locator('#fmroot table').evaluateAll(ts=>ts.map(t=>({headers:[...t.querySelectorAll('thead th')].map(e=>e.innerText.trim()),rows:[...t.querySelectorAll('tbody tr')].map(r=>[...r.cells].map(e=>e.innerText.replace(/\s+/g,' ').trim()))})));}
 async function audit(stage){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations});assert.equal(a.violations.length,0,stage);}
 function expanded(value){if(!value)return[];return value.split(', ').flatMap(token=>{const m=/^(\d+)?([VED])$/.exec(token);assert.ok(m,'Official Portuguese result abbreviation: '+token);return Array(Number(m[1]||1)).fill(m[2]);});}
 async function verifyForms(key,actual){
  let native;
  if(report.health.developerTools)native=await require('./native-team-form-probe.cjs')(c,report.health.coreMvid);
  else{assert.ok(reference&&reference.success&&reference.checkpoint===checkpoint,'Release requires a retained actual native dataset for this exact immutable QA');native=reference.nativeDatasets[key];assert.ok(native);report.nativeReferenceSource=referenceArg.slice(19);}
  const formTables=actual.filter(t=>t.headers.includes('Forma')),rows=formTables.flatMap(t=>t.rows.map(row=>({headers:t.headers,row})));
  assert.equal(native.rows.length,rows.length,'Every native form series has its actual table row');
  let populated=0,empty=0,totalConformance=0;
  rows.forEach(({headers,row},i)=>{const source=native.rows[i],team=row[headers.indexOf('Equipa')];assert.equal(source.team,team);assert.equal(source.bound,true);assert.equal(source.children,source.values.length);
   const expected=[...source.values].reverse().map(v=>{assert.ok([0,1,2,3].includes(v.value));return v.value===3?'V':v.value===0?'D':'E';});assert.deepEqual(expanded(row[headers.indexOf('Forma')]),expected,team);
   assert.ok(source.rank,'Retained real native position/direction data is required');
   const glyphs={0:['chevron top_','Upward arrow'],1:['line_','Horizontal line'],2:['chevron down_','Downward arrow']},glyph=glyphs[source.rank.state];assert.ok(glyph);assert.ok(source.rank.sprite.startsWith(glyph[0]),'The actual drawn asset agrees with its native arrow-state value');
   assert.equal(row[headers.indexOf('Pos')],source.rank.position+' '+glyph[1],team+' retains its actual numerical position and native direction glyph');
   if(expected.length)populated++;else empty++;
   const played=Number(row[headers.indexOf('J')]);if(played>0&&played<=5&&played===expected.length){for(const letter of ['V','E','D'])assert.equal(expected.filter(x=>x===letter).length,Number(row[headers.indexOf(letter)]),team+' native total '+letter);totalConformance++;}
  });
  report.nativeDatasets[key]=native;report.observations.push({key,teams:rows.length,populated,empty,independentTotalConformance:totalConformance});
 }
 function preserveOtherCells(before,after){assert.equal(after.length,before.length);for(let i=0;i<before.length;i++){const a=before[i],b=after[i];assert.deepEqual(b.headers,a.headers);assert.equal(b.rows.length,a.rows.length);const form=a.headers.indexOf('Forma'),pos=a.headers.indexOf('Pos');a.rows.forEach((r,n)=>{const cells=Array.isArray(r)?r:r.split('\t');assert.deepEqual(b.rows[n].map((value,col)=>col===pos?value.replace(/ (?:Upward arrow|Downward arrow|Horizontal line)$/,''):value).filter((_,col)=>col!==form),cells.map(clean).filter((_,col)=>col!==form),'Every original non-form value stays aligned; native ranking glyph additions are independently verified');});}}
 async function retainedControls(baselineHtml){
  const result=await page.evaluate(baseline=>{function names(html){const doc=new DOMParser().parseFromString(html,'text/html'),h=doc.querySelector('#fmsec1-h');return[...doc.querySelectorAll('button,select,input')].filter(e=>!!(h.compareDocumentPosition(e)&Node.DOCUMENT_POSITION_FOLLOWING)).map(e=>(e.getAttribute('aria-label')||e.textContent).replace(/\s+/g,' ').trim()).sort();}return{before:names(baseline),after:names(document.getElementById('fmroot').innerHTML)};},baselineHtml);assert.deepEqual(result.after,result.before,'No original content control is lost or duplicated by heading/form adaptation');
 }
 async function outline(names){assert.deepEqual(await page.locator('#fmroot h3').allTextContents(),names);assert.equal(await page.locator('#fmroot h4').count(),names.length);for(const name of names)assert.equal(await heading(name).getByRole('button',{name,exact:true}).count(),1);assert.ok((await page.locator('#fmroot h4').allTextContents()).every(x=>x==='MELHORES EM CAMPO'));}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await go('2000104296');const seniorNames=['BWSL','UEFA W. Champions League','Taça Ing. Fem.'];await outline(seniorNames);let senior=await tables();
  const seniorBefore=JSON.parse(fs.readFileSync(path.join(artifacts,'career-teamform-current-blank-cell-baseline.json'),'utf8'));preserveOtherCells(seniorBefore.tables,senior);await retainedControls(JSON.parse(fs.readFileSync(path.join(artifacts,'career-competitions-heading-context-release.json'),'utf8')).html);await verifyForms('senior',senior);await audit('Three real competition groups, native links and all senior form results');
  report.checks.push('Competition names identify the three true groups; native player-leader titles are nested subsections, all original controls/non-form cells retained');
  for(const name of seniorNames){const link=heading(name).getByRole('button',{name,exact:true}),origin=await link.getAttribute('id');await activate(link);await poll(()=>page.evaluate(()=>FMTOKEN.split('\x1f').some(p=>p.startsWith('420575744#'))),'actual competition profile '+name);await settled();assert.match(await page.locator('#fmroot').innerText(),new RegExp(name.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')));await audit('Real heading link destination '+name);
   if(name==='BWSL'){const mini=await tables();assert.ok(!mini.some(t=>t.headers.includes('Forma')),'Native overview mini-table has no form column; do not invent one');await c.dropdown('Fases');await c.choose('Fases');await poll(async()=>await page.title()==='Fases','actual full league stage');await settled();await verifyForms('leagueStage',await tables());await audit('Actual full league-stage form column');await activate(page.getByRole('button',{name:'Back',exact:true}));await poll(async()=>await page.title()==='Visão geral','original league overview');await settled();}
   await activate(page.getByRole('button',{name:'Back',exact:true}));await poll(()=>heading(name).count(),'actual competition group return');await settled();assert.equal(await page.evaluate(()=>document.activeElement?.id),origin);await outline(seniorNames);
  }
  report.checks.push('All three preserved heading links open their actual competition, then native Back restores the exact link; the full league stage shares the same form reader while its mini-table stays native');
  await go('2000797209');await outline(['Sub-21']);const youth=await tables(),youthBefore=JSON.parse(fs.readFileSync(path.join(artifacts,'career-u21-competitions-heading-before.json'),'utf8'));preserveOtherCells(youthBefore.tables,youth);await retainedControls(youthBefore.html);await verifyForms('youth',youth);await audit('Actual youth competition group, fourteen rows and native form values');
  const youthLink=heading('Sub-21').getByRole('button',{name:'Sub-21',exact:true}),youthOrigin=await youthLink.getAttribute('id');await activate(youthLink);await poll(()=>page.evaluate(()=>FMTOKEN.split('\x1f').some(p=>p.startsWith('420575744#'))),'actual youth competition profile');await settled();assert.match(await page.locator('#fmroot').innerText(),/Sub-21/);await audit('Actual youth heading link destination');await activate(page.getByRole('button',{name:'Back',exact:true}));await poll(()=>heading('Sub-21').count(),'original youth overview');await settled();assert.equal(await page.evaluate(()=>document.activeElement?.id),youthOrigin);await outline(['Sub-21']);report.checks.push('The separately bound youth group preserves every original row/control, reads its own native results and returns to its own exact heading link');
  await go('2000104296');await outline(seniorNames);preserveOtherCells(seniorBefore.tables,await tables());await audit('Original senior competition groups restored');report.checks.push('Original senior overview and immutable game date remain; no save, simulation or business operation is performed');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;console.log(JSON.stringify({success:true,module:report.health.coreMvid,groups:report.checks.length,audits:report.audits.length,forms:report.observations}));
 }catch(error){report.failure=error.stack;report.body=await page.locator('#fmroot').innerText().catch(()=>null);throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;fs.writeFileSync(output,JSON.stringify(report,null,2));await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
