// Production paused-match regression. Requires the actual populated dugout,
// two native formations and xG observations; never fabricates match state.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')(),{page,poll,settled,activate,dropdown,choose}=c;
 const checks=[],audits=[];let success=false,failure=null;
 const compact=()=>page.locator('table').filter({has:page.getByRole('columnheader',{name:'Played',exact:true})});
 async function audit(name){const r=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:r.violations.map(v=>v.id)});assert.deepEqual(r.violations,[]);}
 try{
  assert.equal(await page.getByRole('button',{name:'Play (match paused)',exact:true}).count(),1,'Pause the real match before running');
  await poll(async()=>await compact().locator('tbody tr').count()>1,'populated compact league');
  await poll(async()=>await page.locator('button[onclick^="fmplayer"]').count()===11
   &&(await page.locator('button[onclick^="fmplayer"]').allTextContents()).every(t=>/\p{L}{3}/u.test(t.split(',').slice(1).join(','))),'eleven named native player cards');
  await settled();
  const viewer=page.getByRole('combobox',{name:'Estatísticas do Jogo',exact:true});
  assert.equal(await viewer.count(),1,'native mapped caption names the icon-only view selector');
  const viewerOrigin=await viewer.getAttribute('id');
  const statTables=page.locator('table').filter({has:page.getByRole('columnheader',{name:'Stat',exact:true})});
  assert.equal(await statTables.count(),2);for(let i=0;i<2;i++)assert.equal(await statTables.nth(i).locator('tbody tr').count(),10);
  await dropdown('Estatísticas do Jogo');await choose('Apenas estatísticas');
  await poll(()=>page.getByRole('combobox',{name:'Apenas estatísticas',exact:true}).count(),'stats-only native view');
  await audit('Native stats-only view');
  await dropdown('Apenas estatísticas');await choose('Eventos do Jogo');
  await poll(()=>page.getByRole('combobox',{name:'Eventos do Jogo',exact:true}).count(),'native events view');
  const inlineEvents=page.locator('table').filter({has:page.getByRole('columnheader',{name:'Event',exact:true})})
   .filter({hasNot:page.getByRole('columnheader',{name:'Team',exact:true})});
  assert.equal(await inlineEvents.count(),2,'one real incident list per team');
  assert.ok(await inlineEvents.locator('tbody tr').count()>=2);
  assert.ok((await inlineEvents.locator('tbody tr').allTextContents()).every(t=>t.includes('Goal')&&t.includes('assisted by')));
  assert.equal(await inlineEvents.locator('tbody th[scope=row] button').count(),await inlineEvents.locator('tbody tr').count());
  await audit('Native team incident lists');
  await dropdown('Eventos do Jogo');await choose('Estatísticas do Jogo');
  await poll(()=>page.getByRole('combobox',{name:'Estatísticas do Jogo',exact:true}).count(),'original match view restored');
  // FM26 deliberately focuses then blurs a flat committed select to leave
  // forms mode. Preserve that contract and its exact remembered/read anchor.
  assert.equal(await page.evaluate(()=>FM.core.pos[FM.core.screenIdentity(FMTOKEN)]),viewerOrigin);
  assert.ok(await page.evaluate(id=>qaInputTrace.some(e=>e.kind==='focus'&&e.id===id),viewerOrigin));
  checks.push('All three native viewer modes, official mapped captions, two ten-row stat tables, goal/assist incident tables, original mode/focus restored');
  assert.deepEqual(await page.locator('#fmroot [id]').evaluateAll(es=>es.map(e=>e.id).filter((id,i,a)=>a.indexOf(id)!==i)),[]);
  const grids=page.locator('.fm-pitch table[data-pitch=browse]');assert.ok(await grids.count()>=2);
  for(let i=0;i<await grids.count();i++){
   const grid=grids.nth(i),keeper=grid.locator('[data-slot=GK]'),next=await keeper.getAttribute('data-up');assert.ok(next);
   await keeper.focus();await page.keyboard.press('ArrowUp');
   assert.equal(await page.evaluate(()=>document.activeElement.id),next);
   assert.equal(await grid.locator('[id="'+next+'"]').count(),1,'Arrow remains in its own team');
  }
  checks.push('Distinct formation/cell IDs; actual ArrowUp stays in each native team');
  assert.deepEqual(await compact().locator('thead th').allTextContents(),['Pos','Team','Played','Points']);
  const rows=await compact().locator('tbody tr').count();
  assert.equal(await compact().locator('tbody th[scope=row] button').count(),rows);
  assert.ok((await compact().locator('tbody tr').allTextContents()).every(t=>t.trim()));
  const club=compact().locator('tbody th button').first(),clubName=await club.innerText(),clubOrigin=await club.getAttribute('id');
  // In this native match the global entity-menu providers are absent. A club
  // link still opens its real in-match profile, without a false menu promise.
  assert.equal(await club.getAttribute('aria-haspopup'),null);
  await activate(club);await poll(()=>page.getByRole('heading',{name:'Perfil do clube',exact:true}).count(),'native club profile');
  await poll(()=>page.getByText(clubName,{exact:true}).count(),'correct club identity');await settled();await audit('In-match club profile');
  await activate(page.getByRole('button',{name:'Close',exact:true}));await poll(()=>compact().count(),'dugout return');
  await poll(()=>page.evaluate(id=>document.activeElement?.id===id,clubOrigin),'exact club row focus');
  checks.push('Compact league has linked team row headers; real in-match club profile and exact return focus');
  await activate(page.getByRole('button',{name:'TABELA AO VIVO',exact:true}));
  await poll(()=>page.getByRole('heading',{name:'Tabela ao vivo',exact:true}).count(),'full native table');await settled();
  assert.equal(await page.locator('table tbody tr').count(),rows);await audit('Full native live table');
  await activate(page.getByRole('button',{name:'Close',exact:true}));await poll(()=>compact().count(),'compact table returns');await settled();
  checks.push('Compact tile opens the native full table with the same competition row count and closes');
  const advice=page.locator('table').filter({has:page.getByRole('columnheader',{name:'PTS',exact:true})});
  if(await advice.count()){
   assert.equal(await advice.locator('tbody tr').count(),rows);
   assert.deepEqual(await advice.locator('thead th').allTextContents(),['Pos.','Clube','PTS']);
   assert.equal(await advice.locator('tbody th[scope=row]').count(),rows);
   checks.push('Present native advice league retains three headers, all competition rows and club controls');
  }else checks.push('No advice league at this native pause; populated advice state was separately checked at 6:59');
  const positions=page.locator('table').filter({has:page.getByRole('columnheader',{name:'Rotation',exact:true})});
  assert.equal(await positions.locator('tbody tr').count(),23,'22 actual active players plus ball');
  assert.equal(await positions.getByRole('rowheader',{name:'Ball',exact:true}).count(),1);
  assert.ok((await positions.locator('tbody tr').allTextContents()).some(t=>t.includes('van Domselaar')));
  assert.ok((await positions.locator('tbody tr').allTextContents()).some(t=>t.includes('Tullis-Joyce')));
  checks.push('Current native map names 22 real active player entities plus ball');
  const xg=page.locator('table').filter({has:page.getByRole('columnheader',{name:'Chance xG',exact:true})});assert.equal(await xg.count(),1);
  const totals=(await page.locator('#fmroot').innerText()).match(/xG Total:\s*([\d,.]+)\s*-\s*([\d,.]+)/);assert.ok(totals);
  const last=await xg.locator('tbody tr').last().locator('td,th').allTextContents();
  if(JSON.stringify(last.slice(-2))!==JSON.stringify(totals.slice(1))){
   const note=await page.locator('#fmroot').innerText();
   assert.ok(note.includes('Native chart total: '+last.at(-2)+' - '+last.at(-1)+'.'));
   assert.ok(note.includes('Current displayed total: '+totals[1]+' - '+totals[2]+'.'));
   checks.push('Actual native xG dataset/printed-footer difference is explicit; both real totals retained');
  }else checks.push('Native xG observations/cumulative totals agree with the actual printed footer');
  const card=page.locator('button[onclick^="fmplayer"]').first(),origin=await card.getAttribute('id');
  await card.focus();const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/player/panel');await page.keyboard.press('Enter');
  assert.match(await(await opened).text(),/^</);await poll(()=>page.locator('.fm-player-panel button[data-player-leaf]').count(),'native player options');
  const leaf=page.locator('.fm-player-panel button[data-player-leaf]').first(),playerName=await leaf.innerText();assert.ok((await card.innerText()).includes(playerName));
  assert.ok(await page.locator('.fm-player-panel select').count()>=1);await leaf.focus();
  const selected=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/select');await page.keyboard.press('Enter');assert.match(await(await selected).text(),/ selected$/);
  await poll(()=>page.getByRole('heading',{name:'Relatório de Jogadores',exact:true}).count(),'native player report');
  await poll(()=>page.getByText(playerName,{exact:true}).count(),'correct player report');await settled();await audit('In-match player report');
  async function checkReport(lastHeader){
   const attrs=page.locator('table').filter({has:page.getByRole('columnheader',{name:'Stat',exact:true})});
   assert.ok(await attrs.locator('tbody tr').count()>=38,'original attribute tables plus career totals');
   const seasons=page.locator('table').filter({has:page.getByRole('columnheader',{name:'Season',exact:true})});
   assert.deepEqual(await seasons.locator('thead th').allTextContents(),['Season','Club','Transfer','Pres',lastHeader]);
   assert.ok(await seasons.locator('tbody th[scope=row]').count()>0);
   assert.ok(await seasons.locator('tbody button').count()>0,'native club controls retained');
   const total=page.locator('tfoot');assert.equal(await total.count(),1);
   const cells=await total.locator('th,td').allTextContents();assert.equal(cells.length,5);
   assert.equal(cells[0],'Total');assert.ok(cells.slice(1).every(t=>t.trim()),'actual bound totals retain all columns');
  }
  await checkReport('SG');
  await activate(page.getByRole('button',{name:'Close',exact:true}));await poll(()=>page.locator('button[onclick^="fmplayer"]').count(),'cards return');
  await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'exact player-card return focus');
  checks.push('Original FM26 player-card panel opens wrapped native dropdown, correct player report and exact origin');
  const outfield=page.locator('button[onclick^="fmplayer"]').last();await outfield.focus();
  const outOpened=page.waitForResponse(r=>new URL(r.url()).pathname==='/player/panel');await page.keyboard.press('Enter');await outOpened;
  await poll(()=>page.locator('.fm-player-panel button[data-player-leaf]').count(),'outfield native options');
  await page.locator('.fm-player-panel button[data-player-leaf]').first().focus();
  const outSelected=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/select');await page.keyboard.press('Enter');await outSelected;
  await poll(()=>page.getByRole('heading',{name:'Relatório de Jogadores',exact:true}).count(),'outfield report');await settled();
  await checkReport('Golos');await audit('In-match outfield report');
  await activate(page.getByRole('button',{name:'Close',exact:true}));await poll(()=>page.locator('button[onclick^="fmplayer"]').count(),'dugout return');
  checks.push('Keeper/outfield attribute variants, lifetime/season separation and native total footer values');
  const escapeCard=page.locator('button[onclick^="fmplayer"]').first(),escapeOrigin=await escapeCard.getAttribute('id');
  await escapeCard.focus();const escapeOpen=page.waitForResponse(r=>new URL(r.url()).pathname==='/player/panel');await page.keyboard.press('Enter');await escapeOpen;
  await poll(()=>page.locator('.fm-player-panel button[data-player-leaf]').count(),'root panel');
  const escapeClose=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/close');await page.keyboard.press('Escape');
  assert.equal((await escapeClose).status(),200);await poll(()=>page.evaluate(()=>FM.core.player===null&&FM.core.dd===null),'root panel closed');
  assert.equal(await page.evaluate(()=>document.activeElement.id),escapeOrigin);
  assert.equal(await escapeCard.getAttribute('aria-expanded'),'false');
  checks.push('Escape closes the inline player root from its card, closes native session and preserves exact focus');
  await audit('Populated paused dugout');assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);success=true;
 }catch(error){failure=error.stack||error.message;throw error;
 }finally{
  const out=path.resolve(__dirname,'../../artifacts/tests/web');fs.mkdirSync(out,{recursive:true});
  fs.writeFileSync(path.join(out,'match-dugout.json'),JSON.stringify({success,failure,checks,audits,jsErrors:c.errors,requestFailures:c.requestFailures},null,2));
  await c.browser.close();
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
