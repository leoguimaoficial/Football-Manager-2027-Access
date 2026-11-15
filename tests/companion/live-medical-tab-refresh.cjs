// Targeted investigation of the retained native Normal-tab refresh timeout.
// Production client only; no medical request, save or simulation is submitted.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate}=c;
 const observations=[],events=[];let original,success=false,failure,mvid,lastNativeHandles;
 async function nativeHandles(){
  const ids=await page.locator('tbody button[id]').evaluateAll(es=>es.map(e=>e.id));
  if(!ids.length)return[];
  // Read the production handles' actual native visibility/ownership guard.
  // No realization, scrolling or command execution; a same-sized old branch
  // must not pass merely because its stale DOM still contains player names.
  const code='foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'"){var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;var hs=(System.Collections.IDictionary)a.GetType("FM27Access.Plugin.Server.ServerHandles").GetField("_handles",f).GetValue(null);foreach(string id in new[]{'+ids.map(JSON.stringify).join(',')+'}){var h=hs[id];if(h==null){System.Console.WriteLine("GUARD|"+id+"|False|Missing handle");continue;}var args=new object[]{null};var valid=h.GetType().GetMethod("TryValidate").Invoke(h,args);var field=h.GetType().GetField("Table");var table=field==null?null:field.GetValue(h);var pointer=table==null?"":table.GetType().GetProperty("Pointer").GetValue(table,null).ToString();System.Console.WriteLine("GUARD|"+id+"|"+valid+"|"+pointer+"|"+args[0]);}}';
  const raw=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);
  assert.doesNotMatch(raw,/\[compile\].*error|\[exception\]/);
  const results=raw.split(/\r?\n/).filter(l=>l.startsWith('GUARD|')).map(l=>{const[,id,valid,pointer,error]=l.split('|');return{id,valid:valid==='True',pointer,error};});
  assert.equal(results.length,ids.length,'Every rendered player command has a native guard result');return results;
 }
 async function go(uid){const entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await poll(()=>page.evaluate(id=>FMTOKEN.split('\x1f').some(p=>p===id||p.startsWith(id+'#')),String(uid)),'medical native destination');await settled();await search.fill('');await settled();}
 try{
  mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const filename=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>filename.count(),'owned QA identity');assert.match(await filename.inputValue(),/^FM27Access QA /);await activate(page.getByRole('button',{name:'Cancelar',exact:true}));
  await go(3979388416);original=await page.getByRole('tab',{selected:true}).textContent();
  await page.evaluate(()=>{window.qaMedicalEvents=[];for(const type of ['screen','restructure','values'])FMES.addEventListener(type,e=>{qaMedicalEvents.push({time:Date.now(),type,data:e.data});if(qaMedicalEvents.length>150)qaMedicalEvents.shift();});});
  const captions=await page.getByRole('tab').allTextContents();assert.deepEqual(captions,['Muito Elevado','Elevado','Considerável','Normal','Baixo']);
  const rounds=Number(process.env.FM27_MEDICAL_ROUNDS||5);assert.ok(Number.isInteger(rounds)&&rounds>0&&rounds<=20);
  const sequence=process.env.FM27_MEDICAL_SEQUENCE?JSON.parse(process.env.FM27_MEDICAL_SEQUENCE):captions;
  assert.ok(Array.isArray(sequence)&&sequence.length>0&&sequence.every(c=>captions.includes(c)),'Use actual native severity captions');
  for(let round=0;round<rounds;round++)for(const caption of sequence){
   const before=Date.now();await activate(page.getByRole('tab',{name:caption,exact:true}));assert.equal(await page.getByRole('tab',{selected:true}).textContent(),caption);
   let empty=null;
   await poll(async()=>{if(await page.getByRole('combobox',{name:'Acções',exact:true}).count())return true;for(const text of await c.visibleEmptyTexts())if(await page.getByText(text,{exact:true}).count()){empty=text;return true;}return false;},'bound actual native medical result '+caption);
   const available=await page.getByRole('combobox',{name:'Acções',exact:true}).count(),rowCount=await page.locator('tbody tr').count();
   assert.ok(available?rowCount>0:rowCount===0);
   if(available)await poll(async()=>{lastNativeHandles=await nativeHandles();return lastNativeHandles.length>0&&lastNativeHandles.every(h=>h.valid);},'current native medical player-command ownership '+caption);
   observations.push({round,caption,available:!!available,rowCount,nativeEmptyText:empty,nativeHandles:available?lastNativeHandles:[],resultMs:Date.now()-before});
   if(available){const control=await c.dropdown('Acções'),origin=await control.getAttribute('id');assert.ok((await control.locator('option').allTextContents()).some(t=>t.includes('Obter Actualização')));await c.cancelDropdown();assert.equal(await page.evaluate(()=>document.activeElement?.id),origin);}
   console.log('Passed medical result:',round+1,caption,rowCount);
  }
  events.push(...await page.evaluate(()=>qaMedicalEvents));await activate(page.getByRole('tab',{name:original,exact:true}));await go(989379584);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);success=true;
 }catch(error){failure={error:error.stack,lastNativeHandles,html:await page.locator('#fmroot').innerHTML(),state:await page.evaluate(()=>({token:FMTOKEN,focus:document.activeElement?.outerHTML,refreshing:FM.core.refreshing,pending:FM.core.pending,dropdown:FM.core.dd,inputTrace:qaInputTrace,events:window.qaMedicalEvents})),native:await c.fetchText('/gui'),serverLog:await c.fetchText('/log'),serverEvents:await c.fetchText('/events/log'),transport:await c.transportTrace()};throw error;
 }finally{
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/medical-tab-refresh.json'),JSON.stringify({success,mvid,failure,observations,events,errors:c.errors,requestFailures:c.requestFailures,network:c.network,simulationAdvanced:false,saveSubmitted:false,manualNVDA:false},null,2));await c.browser.close();
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
