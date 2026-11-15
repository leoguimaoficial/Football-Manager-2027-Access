// Native proxy destination: SquadFeedbackCard opens FeedbackCard with its real
// grouptoopen binding. No business action, save or simulation is submitted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
const root=path.resolve(__dirname,'../..');
const owned=JSON.parse(fs.readFileSync(path.join(root,'artifacts/career-coverage-working-save.json'),'utf8').replace(/^\uFEFF/,''));
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,activate,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,checks:[],audits:[],observations:[],saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const out=path.join(root,'artifacts/tests/web/feedback-alias.json');
 async function go(uid){
  const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"][data-obj="2000104296"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const origin=await entry.getAttribute('id'),response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});
  await page.keyboard.press('Enter');const reply=await(await response).text();assert.match(reply,/^Opening /);
  const panel=uid==='2560554240'?'2079575296':uid;
  await poll(()=>page.evaluate(panel=>FMTOKEN.split('\x1f').some(part=>part===panel||part.startsWith(panel+'#')),panel),'actual native containing panel '+panel);await settled();return{uid,panel,reply,origin};
 }
 async function expectTab(name){
  await poll(()=>page.getByRole('tab',{name,exact:true}).getAttribute('aria-selected').then(x=>x==='true'),'actual native '+name+' tab');await settled();
  assert.equal(await page.getByRole('tab',{selected:true}).count(),1);return await page.locator('#fmroot').innerText();
 }
 async function audit(stage){const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();report.audits.push({stage,violations:a.violations});assert.equal(a.violations.length,0,stage);}
 async function native(name){
  if(!report.health.developerTools)return{inspection:'Unavailable in production Release; native authoritative state arrives through the production adapters'};
  const id=await page.getByRole('tab',{name,exact:true}).getAttribute('id');assert.match(id,/^[-\w:.]+$/);
  const code=`var core=System.AppDomain.CurrentDomain.GetAssemblies()[0];foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${report.health.coreMvid}")core=a;var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;var sh=core.GetType("FM27Access.Plugin.Server.ServerHandles");var hs=(System.Collections.IDictionary)sh.GetField("_handles",f).GetValue(null);var h=hs["${id}"];var ad=h.GetType().GetField("El",f).GetValue(h);var el=(UnityEngine.UIElements.VisualElement)sh.GetMethod("NativeElement",f).Invoke(null,new object[]{ad});bool found=false;for(var cur=el;cur!=null;cur=cur.parent){var group=cur.TryCast<SI.Bindable.ButtonGroup>();if(group==null)continue;found=true;System.Console.WriteLine("PATH|"+group.SelectionBinding.m_path);System.Console.WriteLine("INDEX|"+group.GetSelectedIndex());System.Console.WriteLine("OPTION|"+group.GetOptionValue(group.GetSelectedIndex()));break;}if(!found)throw new System.InvalidOperationException("Actual native tab group unavailable");var pending=(System.Collections.IDictionary)core.GetType("FM27Access.Plugin.Server.NativeDestinationTab").GetField("Pending",f).GetValue(null);System.Console.WriteLine("PENDING|"+pending.Count);`;
  const raw=await page.evaluate(async code=>await(await fetch('/eval',{method:'POST',body:code})).text(),code);assert.doesNotMatch(raw,/\[exception\]|error CS/i);assert.match(raw,/PATH\|grouptoopen/);assert.match(raw,/PENDING\|0/);return{raw};
 }
 try{
  report.health=JSON.parse(await c.fetchText('/health'));await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');assert.equal(await page.getByRole('textbox',{name:'File name',exact:true}).inputValue(),owned.arrivalWorking);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await go('4287093760');
  const firstAlias=await go('2560554240');await expectTab('Plantel');await audit('First shared-panel alias opening after the actual Board Support destination');report.observations.push({case:'Original fresh-career transition',firstAlias,native:await native('Plantel')});
  report.checks.push('The original Board Support→Squad Feedback transition waits for the actual shared panel and applies its native local-tab argument on the first opening, without replaying the open event');
  for(let cycle=0;cycle<3;cycle++){
   const global=await go('2079575296');await expectTab('Desempenho Global');const before=await native('Desempenho Global'),historyBefore=await page.evaluate(()=>FM.core.historyIndex);
   const squad=await go('2560554240');const content=await expectTab('Plantel');const after=await native('Plantel');assert.match(content,/47/);assert.match(content,/Satisfatório/);
   assert.equal(await page.evaluate(()=>FM.core.historyIndex),historyBefore,'The actual shared-panel local selection must not insert another history screen');assert.equal(await page.evaluate(()=>document.activeElement?.id),squad.origin,'Same native panel retains the inherited tree origin');
   report.observations.push({cycle,global,squad,before,after});await audit('Native Global to Squad Feedback alias cycle '+cycle);
   await activate(page.getByRole('tab',{name:'Recrutamento',exact:true}));await expectTab('Recrutamento');await go('2560554240');await expectTab('Plantel');
   const already=await go('2560554240');await expectTab('Plantel');report.observations.push({cycle,case:'Local Recruitment inverse and already selected alias',already,native:await native('Plantel')});
  }
  report.checks.push('Three explicit Global→Squad alias selections resolve the actual opening target/parameter, preserve history/tree focus and release all native bindings');
  report.checks.push('A different local Recruitment tab and an already selected alias each return the actual Plantel tab without guessed option indices or extra native openings');
  await go('1381513472');await poll(()=>page.getByRole('button',{name:'Daphne van Domselaar',exact:true}).count(),'actual unrelated senior squad');await go('2560554240');await expectTab('Plantel');await audit('Alias destination from an unrelated actual squad screen');
  report.checks.push('The same alias also opens its actual shared native panel from an unrelated screen');
  await go('2079575296');await expectTab('Desempenho Global');await audit('Original Global feedback destination restored');report.checks.push('The inverse direct Global destination still resolves its own native local-tab argument');
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,report.health.coreMvid);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
  console.log(JSON.stringify({success:true,module:report.health.coreMvid,checks:report.checks.length,audits:report.audits.length}));
 }catch(error){report.failure=error.stack;report.body=await page.locator('#fmroot').innerText().catch(()=>null);throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;fs.writeFileSync(out,JSON.stringify(report,null,2));await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
