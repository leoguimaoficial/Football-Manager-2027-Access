// Real native staff approach/contract flow on the explicitly owned pristine
// recruitment checkpoint. --propose dispatches ONE offer; never Save/simulate.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..'),out=path.join(root,'artifacts/tests/web/staff-contract.json');
const manifest=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
const checkpoint=manifest.staffRecruitmentWorking,propose=process.argv.includes('--propose');
assert.ok(checkpoint&&manifest.ownedWorkingFiles.includes(checkpoint+'.fm'));
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,propose,checks:[],audits:[],offerDispatches:0,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 async function audit(name){const r=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({name,violations:r.violations.map(v=>v.id)});assert.deepEqual(r.violations,[]);}
 async function go(uid){const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"][data-obj="2000104296"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await settled();await search.fill('');await settled();}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),checkpoint);await c.activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await go('3400157696');assert.equal(await page.title(),'Pesquisa de Equipa Técnica');
  const person=page.getByRole('button',{name:'Kelly Adams',exact:true});assert.equal(await person.count(),1);report.originalRow=await person.locator('xpath=ancestor::tr').innerText();assert.match(report.originalRow,/Está livre/);assert.match(report.originalRow,/Olheira/);assert.match(report.originalRow,/N\/D/);
  await c.activate(person);await settled();assert.equal(await page.title(),'Relatório da Equipa Técnica');report.profile=await page.locator('#fmroot').innerText();assert.match(report.profile,/Kelly Adams[\s\S]*Livre/);assert.equal(await page.locator('#fmroot th').evaluateAll(es=>es.filter(e=>!e.textContent.trim()).length),0);await audit('Actual free staff profile');
  assert.equal(await c.activate(page.getByRole('button',{name:'Acções',exact:true})),'Opened menu');await audit('Actual staff Actions menu');
  const approach=page.getByRole('menuitem',{name:'Abordar Para Assinar',exact:true});assert.equal(await approach.count(),1);assert.equal(await approach.isEnabled(),true);assert.equal(await page.getByRole('menuitem',{name:/Retirar Oferta/}).count(),0);
  await c.activate(approach);await settled();assert.equal(await page.title(),'Negociação contratual');
  const role=page.getByRole('combobox',{name:'Cargo',exact:true}),wage=page.getByRole('combobox',{name:'Ordenado',exact:true}),duration=page.getByRole('combobox',{name:'Duração do contrato',exact:true});
  report.terms={role:await role.inputValue(),wage:await wage.inputValue(),duration:await duration.inputValue(),start:await page.getByRole('combobox',{name:'Data de início',exact:true}).inputValue()};assert.equal(report.terms.role,'Olheiro');assert.equal(report.terms.wage,'700 € p/s');assert.equal(report.terms.duration,'3 anos (Jul 2027 - Jun 2030), 3 of 5');await audit('Native staff contract initial terms');
  const origin=await duration.getAttribute('id');await c.dropdown('Duração do contrato');report.options=await duration.locator('option').allTextContents();assert.equal(report.options.length,5);assert.match(report.options[0],/Jun 2028/);assert.match(report.options[4],/Jun 2032/);await audit('Native staff duration choices');await c.cancelDropdown();await settled();assert.equal(await page.evaluate(()=>document.activeElement.id),origin);assert.equal(await duration.inputValue(),report.terms.duration);assert.equal(await wage.inputValue(),report.terms.wage);
  report.checks.push('Exact owned pristine checkpoint; actual free candidate/profile/native Actions; five real contract durations cancel with exact focus and unchanged requested terms');
  if(propose){
   report.uncertainOffer=true;report.offerDispatches=1;fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify(report,null,2));
   report.reply=await c.activate(page.getByRole('button',{name:'Finalizar Acordo',exact:true}));assert.match(report.reply,/^Activated /);await settled();report.pending=await page.locator('#fmroot').innerText();assert.match(report.pending,/Entrarei em contacto relativamente à minha decisão final em breve/);assert.equal(await page.getByRole('button',{name:'Finalizar Acordo',exact:true}).count(),0);assert.equal(await page.getByRole('button',{name:'Sair',exact:true}).count(),1);report.uncertainOffer=false;await audit('Native pending final staff decision');await c.activate(page.getByRole('button',{name:'Sair',exact:true}));await settled();assert.equal(await page.title(),'Relatório da Equipa Técnica');
   report.checks.push('One production offer dispatch reaches actual native pending final decision; Exit returns staff profile. This is NOT acceptance or saved persistence');
  }else{await c.activate(page.getByRole('button',{name:'Cancelar negociações',exact:true}));await settled();report.checks.push('Unchanged native negotiation cancelled; no proposal dispatched');}
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
 }catch(error){report.failure=error.stack||String(error);throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
