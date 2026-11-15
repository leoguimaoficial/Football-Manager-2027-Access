// Actual owned Arsenal QA postmatch press lobby. Native start, three answers
// and finish; no fixtures, save overwrite, simulation or fabricated speech.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')(),{page,poll,settled,activate,fetchText}=c;
 const checks=[],audits=[],stale=[];
 const key='FM27AccessQA.ConversationRegressionHandle';
 const evaluate=code=>page.evaluate(async text=>(await fetch('/eval',{method:'POST',body:text})).text(),code);
 const question=()=>page.locator('[data-fm-conversation-question]').last();
 const progress=n=>page.getByText('Pergunta '+n+' de 3',{exact:true});
 async function audit(name){
  const scan=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  audits.push({name,violations:scan.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});
 }
 async function focusedQuestion(label){
  await poll(()=>question().evaluate(e=>document.activeElement===e),label);
  assert.equal(await question().evaluate(e=>e.tagName),'P','Reuse the original readonly message paragraph pattern');
 }
 let mvid;
 try{
  await page.evaluate(()=>{
   window.qaConversationTrace=[];
   function note(kind){qaConversationTrace.push({kind,waiting:FM.conversation.waiting&&Object.assign({},FM.conversation.waiting),
    questions:[...document.querySelectorAll('[data-fm-conversation-question]')].map(e=>({id:e.id,text:e.textContent.trim()})),
    answers:document.querySelectorAll('[data-fm-conversation-answer]').length,focus:document.activeElement?.id});
    if(qaConversationTrace.length>100)qaConversationTrace.shift();}
   const begin=FM.conversation.begin,restore=FM.conversation.restore;
   FM.conversation.begin=function(el){begin(el);note('begin');};
   FM.conversation.restore=function(){note('before restore');restore();note('after restore');};
   FM.core.afterRefresh=FM.core.afterRefresh.map(fn=>fn===restore?FM.conversation.restore:fn);
   document.addEventListener('keydown',e=>note('key '+e.key),true);
  });
  assert.equal(await page.getByRole('heading',{name:'Conferência de Imprensa',exact:true}).count(),1);
  assert.match(await page.locator('#fmroot').innerText(),/Teste FM27 Acesso/,'Owned QA coach');
  assert.ok((await page.locator('#fmroot').innerText()).includes('19 de setembro de 2026'));
  assert.ok((await page.locator('#fmroot').innerText()).includes('Man Utd'));
  mvid=JSON.parse(await fetchText('/health')).coreMvid;
  await audit('Native postmatch press lobby');
  const start=page.getByRole('button',{name:'Iniciar Conferência de Imprensa',exact:true});
  assert.notEqual(await start.getAttribute('data-fm-conversation-answer'),null);
  await activate(start);await poll(()=>progress(1).count(),'native question one');
  await focusedQuestion('first question focus after native start');
  checks.push('Native start focuses the actual first question without a new heading, landmark or live page region');
  const choices=[
   'De forma alguma, a Eva e eu conversámos depois do jogo e ficou claro que não há nenhum ressentimento entre nós. Eu acho que não é preciso dizer mais nada sobre isto, para ser sincero',
   'Todos os elementos desta equipa vão recuperar deste desaire. Tenho um grupo mentalmente muito forte',
   'Fantástico. Ele foi o melhor jogador em campo'
  ];
  for(let i=0;i<choices.length;i++){
   const answer=page.getByRole('button',{name:choices[i],exact:true});
   assert.notEqual(await answer.getAttribute('data-fm-conversation-answer'),null);
   const id=await answer.getAttribute('id'),screen=await page.evaluate(()=>FMTOKEN);
   if(i===0){
    await answer.focus();
    const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/contextmenu');
    await page.keyboard.press('Shift+F10');assert.equal(await(await response).text(),'Opened menu');
    await poll(()=>page.getByRole('menu').count(),'native answer link context menu');
    assert.match(await page.getByRole('menu').innerText(),/Eva Olid/);
    await page.keyboard.press('Escape');await poll(async()=>!await page.getByRole('menu').count(),'native answer menu closed');
    await poll(()=>answer.evaluate(e=>document.activeElement===e),'answer origin after menu');
    checks.push('Response embedded native Eva link opens the correct person menu and Escape restores the answer; no menu business leaf executed');
   }
   await answer.focus();const body=page.waitForResponse(r=>new URL(r.url()).pathname==='/body');
   await page.evaluate(()=>FM.core.refresh(false,true));await body;await settled();
   assert.equal(await answer.getAttribute('id'),id);assert.ok(await answer.evaluate(e=>document.activeElement===e));
   // Retain the actual old production handle only to validate it after the
   // native turn changes; do not manufacture another UI element or command.
   const captured=await evaluate('var a=System.AppDomain.CurrentDomain.GetAssemblies()[0];foreach(var x in System.AppDomain.CurrentDomain.GetAssemblies())if(x.GetName().Name=="FM27Access.Plugin"&&x.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'")a=x;var t=a.GetType("FM27Access.Plugin.Server.ServerHandles");var hs=(System.Collections.IDictionary)t.GetField("_handles",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);var h=hs["'+id+'"];System.AppDomain.CurrentDomain.SetData("'+key+'",h);var args=new object[]{null};Console.WriteLine("valid="+h.GetType().GetMethod("TryValidate").Invoke(h,args));');
   assert.match(captured,/valid=True/);
   if(i===2){
    assert.equal(await question().getByRole('button',{name:'Phallon Tullis-Joyce',exact:true}).count(),1);
    await question().focus();const before=await page.locator('#fmroot').innerHTML();
    const description=page.waitForResponse(r=>new URL(r.url()).pathname==='/describe');
    const spoken=page.waitForResponse(r=>new URL(r.url()).pathname==='/speak');
    await page.keyboard.press('Alt+i');const text=await(await description).text();assert.equal((await spoken).status(),200);
    assert.equal(text,await question().innerText());assert.equal(await page.locator('#fmroot').innerHTML(),before);
    checks.push('Focused rich native question retains its real player link and original Alt+I describe/speak without DOM changes');
    await audit('Native rich question and response controls');
   }
   await activate(answer);
   if(i<2)await poll(()=>progress(i+2).count(),'native question '+(i+2));
   else await poll(()=>page.getByText('Terminou',{exact:true}).count(),'native press finished state');
   await settled();await focusedQuestion('new question/final speech focus');
   const guard=await evaluate('var h=System.AppDomain.CurrentDomain.GetData("'+key+'");var args=new object[]{null};Console.WriteLine("valid="+h.GetType().GetMethod("TryValidate").Invoke(h,args));Console.WriteLine("reason="+args[0]);System.AppDomain.CurrentDomain.SetData("'+key+'",null);');
   assert.match(guard,/valid=False/);
   assert.match(guard,i===2?/conversation answer changed|no longer on screen/:/conversation answer changed/);
   const old=await fetchText('/activate?id='+encodeURIComponent(id)+'&screen='+encodeURIComponent(screen));
   assert.match(old,/Unknown element|conversation answer changed|screen.*changed|stale/i);
   if(i<2)assert.equal(await progress(i+2).count(),1);else assert.equal(await page.getByText('Terminou',{exact:true}).count(),1);
   stale.push({turn:i+1,guard,request:old});
  }
  checks.push('All three native answers progress once; each new question/final speech receives focus; neutral refresh retains the same command id/focus');
  checks.push('Each retained old native handle fails validation and each old HTTP id is refused without advancing the next turn');
  await audit('Native completed press conference');
  await activate(page.getByRole('button',{name:'Finalizar Conferência de Imprensa',exact:true}));
  await poll(async()=>!await page.getByRole('heading',{name:'Conferência de Imprensa',exact:true}).count()
   &&await page.getByRole('combobox',{name:'FM Menu',exact:true}).count(),'native career return');
  await settled();const destination=await page.title();
  assert.ok(await page.locator('#fmroot').innerText().then(t=>t.includes('Mensagens')||t.includes('Portal')));
  checks.push('Native finish returns to its actual career parent');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(audits.flatMap(x=>x.violations),[]);
  const report={success:true,mvid,checks,audits,stale,destination,jsErrors:c.errors,requestFailures:c.requestFailures,manualNVDA:false};
  const output=path.resolve(__dirname,'../../artifacts/tests/web/conversation.json');fs.mkdirSync(path.dirname(output),{recursive:true});fs.writeFileSync(output,JSON.stringify(report,null,2));
  console.log(JSON.stringify(report,null,2));
 }catch(error){
  const output=path.resolve(__dirname,'../../artifacts/tests/web/conversation-failure.json');fs.mkdirSync(path.dirname(output),{recursive:true});
  const browser=await page.evaluate(()=>({focus:document.activeElement?.outerHTML.slice(0,500),waiting:FM.conversation.waiting,trace:window.qaConversationTrace}));
  fs.writeFileSync(output,JSON.stringify({success:false,error:String(error.stack||error),mvid,checks,audits,stale,browser,jsErrors:c.errors,requestFailures:c.requestFailures},null,2));throw error;
 }finally{await evaluate('System.AppDomain.CurrentDomain.SetData("'+key+'",null);').catch(()=>{});await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
