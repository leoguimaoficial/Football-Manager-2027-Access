// Real native training calendar and floating schedule picker. No schedule/session
// write, save or simulation. A fresh production page is essential: a remembered
// popup position concealed the original first-open focus defect.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],observations={};let popup=false,editor=false,baseline;
 const out=path.resolve(__dirname,'../../artifacts/tests/web');
 const snapshot=()=>page.locator('#fmroot table tbody tr').evaluateAll(rs=>rs.map(r=>({header:r.querySelector('th')?.innerText,cells:Array.from(r.querySelectorAll('td')).map(e=>e.innerText)})));
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 try{
  observations.mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  if(!await page.evaluate(()=>FMTOKEN.split('\x1f').some(p=>p.startsWith('1689642752#')))){
   const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="1689642752"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
   await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await poll(()=>page.evaluate(()=>FMTOKEN.includes('1689642752')),'actual training calendar');await settled();await search.fill('');await settled();
  }
  assert.equal(await page.title(),'Treino');
  // Native navigation can retain the calendar's preceding General view while
  // changing its panel UID. Establish the actual tab, not only a title/token.
  const trainingTab=page.getByRole('tab',{name:'Treino',exact:true});
  if(await trainingTab.getAttribute('aria-selected')!=='true')await activate(trainingTab);
  assert.equal(await trainingTab.getAttribute('aria-selected'),'true');
  const native=await c.fetchText('/gui'),lines=native.split(/\r?\n/),ancestors=[];
  let start=-1;
  for(let i=0;i<lines.length;i++){const node=lines[i].match(/\[(\d+)\].*visible=(True|False)/);if(!node)continue;const level=Number(node[1]);ancestors.length=level;const visible=node[2]==='True'&&!ancestors.includes(false);ancestors[level]=visible;if(visible&&lines[i].includes('name="DayNames"')){start=i;break;}}
  assert.ok(start>=0,'Real localized native weekday header exists');
  const depth=Number(lines[start].match(/\[(\d+)\]/)[1]),captions=[];
  for(const line of lines.slice(start+1)){const d=line.match(/\[(\d+)\]/);if(d&&Number(d[1])<=depth)break;const m=line.match(/SIText name="([^"]+)" visible=True text="([^"]+)"/);if(m)captions.push(m[2]);}
  assert.equal(captions.length,7);observations.nativeHeaders=captions;
  const table=page.locator('#fmroot table');assert.equal(await table.count(),1);
  assert.deepEqual((await table.locator('thead th').allTextContents()).slice(-7),captions);
  baseline=await snapshot();assert.ok(baseline.length>0);assert.ok(baseline.every(r=>r.cells.length===7));
  await audit('Actual training calendar with native localized weekday headers');
  checks.push('Seven actual native localized headers preserve their weekday columns and every seven-day row; no duplicate native DayNames paragraph');
  const choice=await table.locator('tbody tr').evaluateAll(rs=>rs.flatMap(r=>{const b=r.querySelector('th button:not(:disabled)');return b?[{id:b.id,label:b.textContent,date:r.querySelector('td')?.innerText.split('\n')[0]}]:[]}));
  assert.ok(choice.length,'Native future week is editable');observations.week=choice[0];
  await activate(page.locator('#'+choice[0].id));popup=true;
  await poll(()=>page.locator('[data-fm-native-overlay]').count(),'native floating picker overlay');await settled();
  assert.equal(await page.evaluate(()=>document.activeElement.id),'fmsec0-h');
  assert.equal(await page.locator('#fmsec0-h').textContent(),choice[0].label);
  assert.equal(await page.getByRole('button',{name:'Close',exact:true}).count(),1);
  assert.equal(await page.getByRole('button',{name:'Back',exact:true}).count(),0);
  assert.equal(await page.locator('#fmnav-tree').count(),0,'Modal background is absent');
  await page.evaluate(()=>FM.core.refresh(false,true));await settled();assert.equal(await page.evaluate(()=>document.activeElement.id),'fmsec0-h');
  await page.keyboard.press('Tab');assert.equal(await page.getByRole('button',{name:'Close',exact:true}).evaluate(e=>e===document.activeElement),true);
  await audit('First unremembered native floating schedule picker');
  checks.push('First opening focuses its actual title, a neutral reread preserves focus, Tab reaches its single inherited Close and no synthetic Back/background is exposed');
  observations.categories=[];
  for(const name of ['Pré-Temporada','Compreensão táctica','Preparação do Jogo','Mistura táctica e jogo','Desenvolvimento futebolístico']){
   const button=page.getByRole('button',{name:new RegExp('^'+name+'(?:, selected)?$')}),id=await button.getAttribute('id');
   await activate(button);assert.equal(await page.evaluate(()=>document.activeElement.id),id);assert.ok((await button.textContent()).endsWith(', selected'));
   const names=await page.getByRole('button').allTextContents();observations.categories.push({name,buttons:names});
   assert.ok(names.length>7,'Native category supplies at least one real schedule');
   await audit('Actual schedule category '+name);
  }
  checks.push('All five actual categories update native selected state and available schedules, retain exact category focus and preserve accessible impact content');
  await page.keyboard.press('Escape');await poll(async()=>await page.title()==='Treino','native Escape closes picker');popup=false;await settled();
  assert.equal(await page.evaluate(()=>document.activeElement.id),choice[0].id);assert.deepEqual(await snapshot(),baseline);
  await audit('Unchanged complete calendar after native picker Escape');
  checks.push('Native Escape closes without a schedule edit and restores exact originating week, all week headers and all original day/session values');
  await activate(page.locator('#'+choice[0].id));popup=true;
  await activate(page.getByRole('button',{name:'Criar novo Plano',exact:true}));
  await poll(async()=>await page.title()==='Criar plano de treinos','actual unsent custom-plan editor');popup=false;editor=true;await settled();
  assert.equal(await page.evaluate(()=>document.activeElement.id),'fmsec1-h');
  assert.equal(await page.locator('#fmsec1-h').textContent(),'Criar plano de treinos');
  // The native miniweek has weekday-reference cells, not the dated month's
  // separate DayNames header. Read its visible Header text independently.
  const editorGui=(await c.fetchText('/gui')).split(/\r?\n/),visibleParents=[];
  const nativeMiniweek=[];let cellDepth=-1,headerDepth=-1,readHeader=false;
  for(const line of editorGui){
   const node=line.match(/^\s*\[(\d+)\]\s+(\S+).*visible=(True|False)/);if(!node)continue;
   const depth=Number(node[1]);visibleParents.length=depth;
   const visible=node[3]==='True'&&!visibleParents.includes(false);visibleParents[depth]=visible;
   if(cellDepth>=0&&depth<=cellDepth){cellDepth=-1;headerDepth=-1;}
   if(headerDepth>=0&&depth<=headerDepth)headerDepth=-1;
   if(visible&&node[2]==='DayCell'){cellDepth=depth;headerDepth=-1;readHeader=false;}
   if(cellDepth>=0&&visible&&/name="Header"/.test(line))headerDepth=depth;
   const text=line.match(/SIText.*text="([^"\r\n]+)"/);
   if(cellDepth>=0&&headerDepth>cellDepth&&visible&&!readHeader&&text){nativeMiniweek.push(text[1]);readHeader=true;}
  }
  assert.equal(nativeMiniweek.length,7,'Seven actual native miniweek captions');
  const miniweek=page.locator('#fmroot table').filter({has:page.getByRole('columnheader',{name:nativeMiniweek[0],exact:true})});
  assert.equal(await miniweek.count(),1);assert.deepEqual(await miniweek.locator('thead th').allTextContents(),nativeMiniweek);
  assert.equal(await miniweek.locator('tbody tr').count(),1);assert.equal(await miniweek.locator('tbody td').count(),7);
  assert.equal(await miniweek.getByRole('button',{name:'Descanso',exact:true}).count(),21);
  observations.nativeMiniweekHeaders=nativeMiniweek;
  checks.push('The unsent native editor retains all seven localized cell-derived weekday column headers, one seven-day row and all21 actual session controls');
  await audit('First forward native editor arrival after the floating picker closes');
  await activate(page.getByRole('button',{name:'Back',exact:true}));await poll(async()=>await page.title()==='Treino','unsent editor native Back');editor=false;await settled();
  assert.deepEqual(await snapshot(),baseline);await audit('Original training calendar after leaving the unsent editor');
  checks.push('First full-screen custom editor arrival focuses its actual content title after the intermediate popup closure; native Back creates/saves no plan and restores every original calendar value');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  fs.writeFileSync(path.join(out,'training-calendar.json'),JSON.stringify({success:true,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,network:c.network,trainingChanged:false,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));
 }catch(error){
  const before={title:await page.title(),html:await page.locator('#fmroot').innerHTML(),focus:await page.evaluate(()=>document.activeElement.id)};
  const cleanup=[];if(popup)try{await activate(page.getByRole('button',{name:'Close',exact:true}));}catch(e){cleanup.push(e.message);}
  if(editor)try{await activate(page.getByRole('button',{name:'Back',exact:true}));}catch(e){cleanup.push(e.message);}
  fs.writeFileSync(path.join(out,'training-calendar-failure.json'),JSON.stringify({success:false,error:error.stack,checks,audits,observations,before,cleanup,errors:c.errors,requestFailures:c.requestFailures,network:c.network},null,2));throw error;
 }finally{await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
