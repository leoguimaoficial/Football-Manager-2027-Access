// Actual informative and navigable native cards, not fixture UI or simulated
// disabled state. Original QA only; no Save, offer or simulation is submitted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const checks=[],audits=[],observations=[],out=path.resolve(__dirname,'../../artifacts/tests/web/tile-availability.json');let mvid,ownedSave;
 async function go(uid,obj=null){const entry=page.locator('#fmnav-tree [data-uid="'+uid+'"]'+(obj===null?'':('[data-obj="'+obj+'"]'))).first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const reply=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');reply.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await reply).text(),/^Opening /);
  await poll(()=>page.evaluate(uid=>FMTOKEN.includes(String(uid)),uid),'actual native card destination');await settled();await search.fill('');await settled();}
 async function native(){const raw=await page.evaluate(async code=>await(await fetch('/eval',{method:'POST',body:code})).text(),require('./native-card-inventory.cjs')(mvid));
  assert.doesNotMatch(raw,/\[compile\]|\[exception\]/);const cards=[...raw.matchAll(/^CARD\|([^|]+)\|(True|False)\|(\d+)\|(True|False)/gm)].map(m=>({title:m[1],enabled:m[2]==='True',pointer:m[3],actionRegistered:m[4]==='True'}));
  for(const card of cards)card.description=raw.split(/\r?\n/).find(x=>x.startsWith('DETAIL|'+card.pointer+'|'))?.split('|').slice(2).join('|');return{raw,cards};}
 async function audit(name){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name,violations:a.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});assert.deepEqual(audits.at(-1).violations,[]);}
 const heading=()=>page.getByRole('heading',{name:'CONTRATOS',exact:true});
 async function informative(){const n=await native(),card=n.cards.find(x=>x.title==='CONTRATOS');assert.ok(card);assert.equal(card.enabled,false);assert.equal(card.actionRegistered,false,'Native fixed card has no production action registration');
  assert.equal(await page.getByRole('button',{name:'CONTRATOS',exact:true}).count(),0);assert.equal(await heading().count(),1);assert.equal(await heading().locator('button,a,input,select,[tabindex="0"]').count(),0);
  const text=await heading().evaluate(e=>{const a=[];for(let n=e.nextElementSibling;n&&!/^H[123]$/.test(n.tagName);n=n.nextElementSibling)if(n.textContent.trim())a.push(n.textContent.trim());return a;});
  assert.equal(text.join(', '),card.description,'Every native informative-card value remains present in order');observations.push({kind:'informative',native:n,heading:await heading().evaluate(e=>e.outerHTML),text});return n;}
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;assert.equal(await page.evaluate(()=>document.documentElement.lang),'pt');
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned QA identity');ownedSave=await file.inputValue();assert.match(ownedSave,/^FM27Access QA Dialog Regression /);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await go(756655104,2000104296);const n=await informative();await audit('Actual fixed native Contracts card');checks.push('Native disabled Contracts tile is an informative heading with every real value and no fake button/action registration');
  await page.evaluate(()=>FM.core.refresh(false,true));await settled();await informative();checks.push('Neutral authoritative refresh preserves informative semantics and every native value');
  const clause=n.cards.find(x=>x.title==='CLÁUSULAS');assert.ok(clause);assert.equal(clause.enabled,true);assert.equal(clause.actionRegistered,true);const action=page.getByRole('button',{name:'CLÁUSULAS',exact:true}),origin=await action.getAttribute('id');assert.equal(await action.isEnabled(),true);
  await activate(action);await poll(()=>page.evaluate(()=>FMTOKEN.includes('3407932416')),'actual native clauses destination');await settled();const gui=await c.fetchText('/gui');assert.match(gui,/\[0\] Panel name="RecruitmentClausesCard"/);await audit('Actual enabled card destination');checks.push('Available Clauses card executes the original native command and opens the independent native RecruitmentClausesCard');
  await page.keyboard.press('Alt+[');await poll(()=>heading().count(),'native Contracts parent');await settled();await poll(()=>page.evaluate(id=>document.activeElement?.id===id,origin),'exact available card origin');await informative();await audit('Native parent and exact card focus return');checks.push('Inherited Back returns exact available-card focus and retains informative-card semantics/data');
  await go(989379584);const portal=await native();assert.ok(portal.cards.length,'Actual Portal tile family');for(const card of portal.cards){const button=page.getByRole('button',{name:card.title,exact:true});if(!card.enabled)assert.equal(await button.count(),0,'Unavailable native Portal tile must not become an HTML action');}
  observations.push({kind:'Portal',native:portal});await audit('Actual Portal card family');checks.push('Portal native card availability and semantic regression preserve the inherited card family');
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,mvid);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  fs.writeFileSync(out,JSON.stringify({success:true,mvid,ownedSave,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));console.log('PASS: actual informative/navigable card availability, content and exact native return.');
 }catch(error){fs.writeFileSync(out,JSON.stringify({success:false,mvid,ownedSave,error:error.stack,checks,audits,observations,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures},null,2));throw error;}
 finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
