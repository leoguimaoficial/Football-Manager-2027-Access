// Actual populated Portuguese QA Portal/Data Hub. No fixture data, simulation,
// save submission or graph/stat edits; native profile/card navigation only.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],audits=[];let success=false,failure=null,mvid,nativeEvidence=null;
 const metrics=()=>page.locator('table').filter({has:page.getByRole('columnheader',{name:'Stat',exact:true})});
 const players=()=>page.locator('table').filter({has:page.getByRole('columnheader',{name:'Quadrant',exact:true})});
 const cells=table=>table.locator('tbody tr').evaluateAll(rows=>rows.map(row=>[...row.querySelectorAll('th,td')].map(e=>e.textContent.trim())));
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 async function clean(){assert.equal(await page.getByText('unknown control',{exact:true}).count(),0);assert.ok(!(await page.locator('#fmroot').innerText()).includes('visual only'));assert.equal(await page.getByText('ThumbsUp, image',{exact:true}).count(),0);}
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  assert.equal(await page.getByRole('heading',{name:'Portal',exact:true}).count(),1,'Start in the actual owned QA Portal');
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned save identity');
  const ownedSave=await file.inputValue();assert.match(ownedSave,/^FM27Access QA /);
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));await settled();
  await poll(async()=>await metrics().count()===1&&await metrics().locator('tbody tr').count()===8,'eight actual Portal radar axes');
  const portalMetrics=await cells(metrics());assert.ok(portalMetrics.every(row=>row.length===2&&row.every(Boolean)));
  await clean();await audit('Populated Portal radar/feedback');
  checks.push('Eight native labelled/unscaled Portal metrics; assessment retained without duplicate thumbs artwork');
  await activate(page.getByRole('button',{name:'CENTRAL DE DADOS',exact:true}));
  await poll(()=>page.getByRole('heading',{name:'Central de Dados',exact:true}).count(),'actual Data Hub destination');await settled();
  assert.ok(await metrics().count()>=5);assert.deepEqual(await cells(metrics().first()),portalMetrics);
  await poll(async()=>await players().count()>=2&&(await players().allTextContents()).every(t=>!t.includes('Player 1')),'names across simultaneous scatter cards');
  const summaryPlayers=await cells(players().first());assert.ok(summaryPlayers.length>=1);
  assert.ok(summaryPlayers.every(row=>/\p{L}{3}/u.test(row[0])&&!/^Player \d+$/.test(row[0])));
  assert.equal(await players().first().getByRole('button').count(),0,'Native thumbnail points are informational, not invented profile buttons');
  await clean();await audit('Populated Data Hub radar/scatter summaries');
  checks.push('Multiple native radars and scatter contexts; actual player names survive simultaneous-card subscriptions');
  await activate(page.getByRole('button',{name:'JOGO AÉREO - DEFESAS',exact:true}).first());
  await poll(()=>page.getByRole('heading',{name:'Scatter Analysis',exact:true}).count(),'expanded native scatter card');
  await poll(async()=>await players().count()===1&&(await players().innerText()).includes(summaryPlayers[0][0]),'expanded named points');await settled();
  const nativeOut=path.resolve(__dirname,'../../artifacts/tests/web');fs.mkdirSync(nativeOut,{recursive:true});
  const nativeTree=path.join(nativeOut,'datahub-charts-expanded-native.txt');
  fs.writeFileSync(nativeTree,await c.fetchText('/gui'));
  nativeEvidence={module:mvid,tree:nativeTree};
  const nativeShot=(await c.fetchText('/screenshot')).trim();
  if(fs.existsSync(nativeShot)){nativeEvidence.screenshot=path.join(nativeOut,'datahub-charts-expanded-native.png');fs.copyFileSync(nativeShot,nativeEvidence.screenshot);}
  else nativeEvidence.screenshotObservation=nativeShot;
  assert.deepEqual(await cells(players()),summaryPlayers);
  assert.equal(await players().getByRole('button').count(),summaryPlayers.length,'expanded native points retain actual CellButton commands');
  await clean();await audit('Expanded native scatter card');
  const person=players().getByRole('button',{name:summaryPlayers[0][0],exact:true}),origin=await person.getAttribute('id');
  await activate(person);await poll(()=>page.getByRole('heading',{name:'Relatório de jogador',exact:true}).count(),'actual player report');
  await poll(()=>page.getByText(summaryPlayers[0][0],{exact:true}).count(),'correct person');await settled();await audit('Player report opened from native plot');
  await activate(page.getByRole('button',{name:'Back',exact:true}));await poll(()=>page.getByRole('heading',{name:'Scatter Analysis',exact:true}).count(),'native scatter return');
  await poll(()=>page.evaluate(id=>document.activeElement.id===id,origin),'exact point focus');
  checks.push('Expanded coordinates/ranks match thumbnail; native point opens the correct player and Back restores exact point focus');
  await activate(page.getByRole('button',{name:'Close',exact:true}));
  await poll(async()=>await page.getByRole('heading',{name:'Portal',exact:true}).count()||await page.getByRole('heading',{name:'Central de Dados',exact:true}).count(),'native card closure');await settled();
  checks.push('Native close returns to '+await page.title());
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(error){failure=error.stack||error.message;throw error;
 }finally{
  const out=path.resolve(__dirname,'../../artifacts/tests/web');fs.mkdirSync(out,{recursive:true});
  fs.writeFileSync(path.join(out,'datahub-charts.json'),JSON.stringify({success,failure,mvid,checks,audits,nativeEvidence,jsErrors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,manualNVDA:false},null,2));
  await c.browser.close();
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
