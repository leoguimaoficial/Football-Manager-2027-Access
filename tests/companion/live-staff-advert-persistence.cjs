// Read-only saved staff-advertisement outcomes. Publishing/withdrawal are
// separately recorded native commands; this driver never repeats either action.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),artifacts=path.join(root,'artifacts');
const state=process.argv.find(a=>a.startsWith('--state='))?.slice(8);
assert.ok(['published','withdrawn'].includes(state),'Explicit saved outcome required');
const manifest=JSON.parse(fs.readFileSync(path.join(artifacts,'career-coverage-working-save.json'),'utf8'));
const defaultKey=state==='published'?'publishedStaffAdvertWorking':'withdrawnStaffAdvertWorking';
const checkpointKey=process.argv.find(a=>a.startsWith('--checkpoint-key='))?.slice(17)||defaultKey;
assert.ok([defaultKey,state==='published'?'maintainedPublishedStaffAdvertWorking':'maintainedWithdrawnStaffAdvertWorking'].includes(checkpointKey),'Only the explicitly recorded advertisement outcome branches');
const checkpoint=manifest[checkpointKey];
assert.ok(checkpoint&&manifest.ownedWorkingFiles.includes(checkpoint+'.fm'));
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,state,checkpoint,checkpointKey,checks:[],audits:[],businessDispatches:0,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({name,violations:a.violations.map(v=>v.id)});assert.deepEqual(a.violations,[]);}
 async function go(uid,obj){const entry=page.locator(`#fmnav-tree [role=treeitem][data-uid="${uid}"][data-obj="${obj}"]`).first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});assert.equal(await entry.count(),1);await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const result=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');result.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await result).text(),/^Opening /);await settled();await search.fill('');await settled();}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  report.checkpointSha256=crypto.createHash('sha256').update(fs.readFileSync(path.join(process.env.USERPROFILE,'Documents/Sports Interactive/Football Manager 27/games',checkpoint+'.fm'))).digest('hex');
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await go('3400157696','2000104296');assert.equal(await page.title(),'Pesquisa de Equipa Técnica');
  const own=state==='published'?1:0,total=43+own;
  const ownText=own?'1 anúncio de emprego criado':'0 anúncios de emprego criados',totalText=total+' anúncios de emprego no total';
  await poll(()=>page.locator('#fmroot').getByText(ownText,{exact:true}).count(),'saved own-advert counter');
  assert.equal(await page.locator('#fmroot').getByText(totalText,{exact:true}).count(),1);
  report.summary={own,total,ownText,totalText};
  if(report.health.developerTools){report.nativeLines=(await c.fetchText('/gui')).split(/\r?\n/).filter(l=>/SIText visible=True text="\d+ anúncios? de emprego/.test(l));assert.ok(report.nativeLines.some(l=>l.includes('text="'+ownText+'"')));assert.ok(report.nativeLines.some(l=>l.includes('text="'+totalText+'"')));}
  else{const arg=process.argv.find(a=>a.startsWith('--native-reference='));assert.ok(arg,'Release requires independent exact-checkpoint native counter proof');const reference=JSON.parse(fs.readFileSync(path.resolve(root,arg.slice(19)),'utf8'));assert.ok(reference.success&&reference.state===state&&reference.checkpoint===checkpoint);assert.equal(reference.checkpointSha256,report.checkpointSha256);assert.deepEqual(reference.summary,report.summary);assert.ok(reference.nativeLines.length>=2);report.nativeReference=arg.slice(19);}
  await audit('Saved native advertisement counters');report.checks.push('Exact owned saved filename/hash; both actual counters agree with independently visible native SIText');
  const card=page.getByRole('button',{name:'ANÚNCIOS PARA ELEMENTOS DA EQUIPA TÉCNICA',exact:true}),origin=await card.getAttribute('id');await c.activate(card);await poll(()=>page.title().then(t=>t==='Anúncios para Elementos da Equipa Técnica'),'actual native staff vacancy destination');await settled();
  const table=page.locator('#fmroot table').filter({has:page.getByRole('columnheader',{name:'Vaga',exact:true})});assert.equal(await table.count(),1);
  report.rows=await table.locator('tbody tr').evaluateAll(rs=>rs.map(r=>[...r.cells].map(c=>c.textContent.trim().replace(/\s+/g,' '))));assert.equal(report.rows.length,total);
  const baseline=JSON.parse(fs.readFileSync(path.join(artifacts,'career-staff-adverts-before-rows.json'),'utf8')).rows;
  const ownRows=report.rows.filter(r=>r[1].includes('Arsenal'));assert.equal(ownRows.length,own);if(own)assert.equal(ownRows[0][4],'Olheiro');assert.deepEqual(report.rows.filter(r=>!r[1].includes('Arsenal')),baseline,'Every other native vacancy remains unchanged');
  await audit('Saved native vacancy records');await c.activate(page.getByRole('button',{name:'Back',exact:true}));await settled();assert.equal(await page.evaluate(()=>document.activeElement.id),origin);assert.equal(await page.locator('#fmroot').getByText(ownText,{exact:true}).count(),1);await audit('Vacancy return and exact card focus');
  report.checks.push('Actual card opens all saved vacancies; own scout advertisement matches state and all43other records retain exact order/values; Back restores exact origin');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
 }catch(e){report.failure=e.stack||String(e);throw e;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;const out=path.join(artifacts,'tests/web/staff-advert-'+state+'-persistence.json');fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
