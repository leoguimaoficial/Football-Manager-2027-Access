// Real production-client regression. Run only in an explicitly named QA career;
// no fixtures, save deletion, simulation, profile edits or preference submission.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,poll,settled,activate,fetchText}=c;
 const mvid=JSON.parse(await fetchText('/health')).coreMvid;
 const checks=[],audits=[];
 const messageCheck={};
 async function portal(){await poll(()=>page.getByRole('heading',{name:'Portal',exact:true}).count(),'Portal');await settled();}
 async function goPortal(){
  await page.getByRole('textbox',{name:'Search all screens',exact:true}).fill('Visão Geral');
  await page.locator('[role=treeitem][data-filter="visão geral portal"]').focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');
  await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);await portal();
  await page.getByRole('textbox',{name:'Search all screens',exact:true}).fill('');await settled();
 }
 async function audit(name){console.log('audit',name);const result=await Promise.race([new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze(),new Promise((_,reject)=>setTimeout(()=>reject(Error('axe timeout: '+name)),30000))]);audits.push({name,violations:result.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});}
 async function cleanControls(){
  assert.equal(await page.getByRole('button',{name:'unlabeled icon button',exact:true}).count(),0);
  assert.deepEqual(await page.locator('[aria-label]').evaluateAll(xs=>xs.filter(e=>/unknown control|unlabeled icon button/.test(e.getAttribute('aria-label'))).map(e=>e.outerHTML)),[]);
  assert.doesNotMatch(await page.locator('#fmroot').innerText(),/unknown control|EntityTexture|may be incomplete/);
 }
 try{
  if(!await page.getByRole('heading',{name:'Portal',exact:true}).count())await goPortal();
  await portal();
  // A read-only Save As inspection proves ownership before consuming unread QA
  // messages. Cancel keeps the original save and all preferences untouched.
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');
  const name=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>name.count(),'Save As');
  const ownedSave=await name.inputValue();
  const filenameId=await name.getAttribute('id'),draftName=ownedSave+' focus preservation check';
  await name.fill(draftName);
  await page.evaluate(()=>FM.core.refresh(false,true));await settled();
  assert.equal(await name.inputValue(),draftName,'A neutral update must preserve the focused filename edit');
  assert.equal(await page.evaluate(()=>document.activeElement?.id),filenameId);
  await name.fill(ownedSave);await name.press('Tab');await settled();
  checks.push('Focused Save As text and caret target survive a neutral production-body refresh; original filename restored before cancellation');
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));await portal();
  assert.match(ownedSave,/^FM27Access QA /,'Use only an owned QA career');
  const holiday=page.locator('#fmnav-tree [role=treeitem][data-uid="135301632"]').first();
  await page.getByRole('textbox',{name:'Search all screens',exact:true}).fill(await holiday.locator(':scope > span').textContent());
  const holidayOrigin=await holiday.getAttribute('id');await holiday.focus();
  const holidayGo=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');
  assert.match(await(await holidayGo).text(),/^Opening /);
  await poll(()=>page.getByRole('spinbutton',{name:'Days on vacation',exact:true}).count(),'native holiday dialog');
  await settled();await audit('Holiday dialog');
  // Actual body/destination; vary only the content-name revision on return.
  // A modal must return to its filtered tree leaf despite names arriving while
  // it was open. No action is submitted with the simulated revision.
  await page.route('**/body?c=1',async route=>{
   const response=await route.fetch(),headers=response.headers();
   const token=decodeURIComponent(headers['x-fm-token'].replace(/\+/g,' '));
   const revision=Number(/\x1Fnames:(\d+)$/.exec(token)?.[1]||0)+1000;
   headers['x-fm-token']=encodeURIComponent(token.replace(/\x1Fnames:\d+$/,'')+'\x1Fnames:'+revision);
   await route.fulfill({response,headers});
  });
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));await portal();
  await poll(()=>page.evaluate(id=>document.activeElement?.id===id,holidayOrigin),'modal return preserves filtered menu origin across name revisions');
  await page.unroute('**/body?c=1');await page.evaluate(()=>FM.core.refresh());await settled();
  await page.getByRole('textbox',{name:'Search all screens',exact:true}).fill('');await settled();
  checks.push('Native holiday cancellation restores the exact filtered menu leaf across content-name revisions; current real protocol restored before other actions');
  const top=page.locator('#fmnav-tree > [role=treeitem] > span');
  assert.deepEqual(await top.allTextContents(),['PORTAL','PLANTEL','TÁCTICAS','RECRUTAMENTO','TREINO','CLUBE']);
  const headings=await page.locator('h3').allTextContents();
  for(const h of ['MENSAGENS','Agenda','Notícias','Estatísticas dos Jogadores','TREINO'])assert.ok(headings.includes(h),h);
  assert.equal(headings.filter(h=>h==='Classificação'||h==='Competição').length,1,'The actual league/competition tile retains its native heading');
  assert.deepEqual(await page.locator('h4').allTextContents(),['Seg','Ter','Qua','Qui','Sex','Sáb','Dom']);
  const league=page.locator('table').filter({has:page.getByRole('columnheader',{name:'Equipa',exact:true})});
  assert.equal(await league.count(),1);assert.deepEqual(await league.locator('thead th').allTextContents(),['Pos','Equipa','J','V','D','DG','Pts']);
  assert.ok(await league.locator('tbody tr').count()>0);
  assert.ok(await page.locator('table').count()>=3);await cleanControls();await audit('Portal');
  checks.push('Actual six primary categories; native dashboard/competition headings, weekday subheadings and semantic message/agenda/standings tables; populated extra native tables are retained');
  for(const label of ['World','Customize card: TREINO']){
   const dd=await c.dropdown(label);assert.ok(await dd.locator('option').count()>0);const id=await dd.getAttribute('id');
   await c.cancelDropdown();assert.equal(await page.evaluate(()=>document.activeElement.id),id);
  }
  checks.push('Native World and tile-option menus open/cancel through the original dropdown session and restore focus');
  const fm=page.getByRole('combobox',{name:'FM Menu',exact:true});
  const menuResponse=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/open');await page.keyboard.press('Alt+p');
  assert.equal((await menuResponse).status(),200);await poll(()=>fm.getAttribute('data-loaded'),'Alt+P menu');
  assert.ok((await fm.locator('option').allTextContents()).includes('Gravar Jogo Como'));await c.cancelDropdown();
  checks.push('Original Alt+P opens the existing native FM menu handle with Save, Save As and preferences actions');
  await c.dropdown('FM Menu');await c.choose('Preferências');
  await poll(()=>page.getByRole('tab',{name:'Detalhado',exact:true}).count(),'career preferences');
  const preferencesToken=await page.evaluate(()=>FMTOKEN);
  assert.match(await fetchText('/activate?id=obsolete-handle&screen='+encodeURIComponent(preferencesToken+'#obsolete-binding')),
   /^This screen changed\./,'Stale snapshot must be rejected before looking up or acting on a current native handle');
  await audit('Career preferences');await page.keyboard.press('Escape');await portal();
  checks.push('In-career FM menu, preferences mode tabs, Escape and return to Portal');
  await activate(page.getByRole('button',{name:'Pesquisar',exact:true}));
  const search=page.getByRole('textbox',{name:'Global search',exact:true});await poll(()=>search.count(),'global search field');
  await search.fill('Leicester');await search.press('Tab');
  await poll(()=>page.getByRole('button',{name:'Leicester, ENG, Clube',exact:true}).count(),'native results');
  await settled();await cleanControls();await audit('Global search');
  assert.equal(await search.inputValue(),'Leicester');await page.keyboard.press('Escape');await portal();
  checks.push('Global search uses the original text input, actual native query/results, no unnamed placeholders, and Escape to origin');
  await activate(page.getByRole('tab',{name:'Tudo',exact:true}));
  await poll(()=>page.locator('button[onclick*="msgview.open"]').count(),'all messages');
  const total=await page.locator('button[onclick*="msgview.open"]').count();assert.ok(total>1);
  // A compact Portal window and the full unread window need not contain the
  // same records. Establish the full native screen before verifying read state.
  await page.keyboard.press('Alt+1');await poll(()=>page.getByRole('heading',{name:'Mensagens',exact:true}).count(),'full native Messages');await settled();
  await activate(page.getByRole('tab',{name:'Tudo',exact:true}));await settled();
  const unread=page.getByRole('tab',{name:/^Por Ler \(/});await activate(unread);
  await poll(()=>page.locator('button[onclick*="msgview.open"]').count(),'unread messages');await settled();
  const before=await page.locator('button[onclick*="msgview.open"]').count();assert.ok(before>0);
  const unreadCount=async()=>Number((await page.getByRole('tab',{name:/^Por Ler \(/}).innerText()).match(/\((\d+)\)/)[1]);
  const unreadBefore=await unreadCount();assert.ok(unreadBefore>0);
  assert.equal(await unread.getAttribute('aria-selected'),'true');
  // Actual reports may share every visible metadata field. Use native identity,
  // not a coincidental subject/sender/time match across different realised windows.
  const candidates=await page.locator('button[onclick*="msgview.open"]').evaluateAll(buttons=>buttons.map(button=>{
   return{id:button.id,messageId:button.closest('tr').dataset.fmMessageId};
  }));
  const unique=candidates.find(candidate=>/^\d+$/.test(candidate.messageId)&&candidates.filter(other=>other.messageId===candidate.messageId).length===1);
  assert.ok(unique,'This read-state test requires an actual unique native message identity');
  const firstMessage=page.locator('#'+unique.id);
  const messageId=await firstMessage.locator('xpath=ancestor::tr').getAttribute('data-fm-message-id');
  assert.match(messageId,/^\d+$/,'Read-state verification requires the actual native message identity');
  const subject=await firstMessage.textContent();
  const cells=await firstMessage.locator('xpath=ancestor::tr').locator('td').allTextContents();
  const sender=cells[2],sent=cells[3];
  const selectedRow=()=>page.locator('tr[data-fm-message-id="'+messageId+'"]');
  assert.equal(await selectedRow().count(),1,'Selected native message identity is unique');
  // Opening Portal's compact inbox can navigate to the full Messages screen.
  // Its native realised window can grow (observed 9 -> 40), so row-count
  // equality/decrement is not a read-state contract across these contexts.
  Object.assign(messageCheck,{messageId,subject,sender,sent,initialAllRows:total,beforeRows:before,unreadBefore});
  await selectedRow().getByRole('button',{name:subject,exact:true}).focus();await page.keyboard.press('Enter');
  await poll(()=>page.getByRole('button',{name:'Close message',exact:true}).count(),'modal message');await settled();await cleanControls();await audit('Message view');
  // Embedded buttons insert whitespace into the browser's computed heading
  // name. The native text itself must remain exact, with one semantic heading
  // and all independently actionable native links preserved.
  assert.equal(await page.getByRole('heading').evaluateAll((headings,subject)=>headings.filter(h=>h.textContent===subject).length,subject),1,'Opened message subject is one heading, including native linked titles');
  const messageBody=page.locator('[data-fm-msg-body]').first();
  assert.equal(await messageBody.count(),1);await messageBody.focus();
  const bodyBeforeInfo=await page.locator('#fmroot').innerHTML();
  const descriptionReply=page.waitForResponse(r=>new URL(r.url()).pathname==='/describe');
  const spokenInfo=page.waitForResponse(r=>new URL(r.url()).pathname==='/speak');await page.keyboard.press('Alt+i');
  const description=await(await descriptionReply).text();await spokenInfo;
  assert.ok(description.trim());assert.doesNotMatch(description,/Unknown element|no longer on screen|could not|unavailable/i);
  assert.equal(await page.locator('#fmroot').innerHTML(),bodyBeforeInfo);
  checks.push('Alt+I resolves the focused read-only message body from the current snapshot and speaks without changing its DOM');
  await page.keyboard.press('Escape');
  await poll(async()=>!await page.getByRole('button',{name:'Close message',exact:true}).count()
   &&await selectedRow().count()===0
   &&await unreadCount()===unreadBefore-1,'read row removed and native unread count updated without reload');
  Object.assign(messageCheck,{afterRows:await page.locator('button[onclick*="msgview.open"]').count(),unreadAfter:await unreadCount()});
  assert.equal(await selectedRow().count(),0);
  await settled();
  if(unreadBefore>1){
   await poll(()=>page.evaluate(()=>document.activeElement?.getAttribute('onclick')?.includes('msgview.open')),'focus returns to next unread row after the final native refresh');
  }else{
   await poll(()=>page.evaluate(()=>document.activeElement?.hasAttribute('data-fm-msg-unread-filter')),'final unread message returns focus to its empty native filter');
   assert.equal(await page.getByRole('tab',{name:'Por Ler (0)',exact:true}).getAttribute('aria-selected'),'true');
  }
  await activate(page.getByRole('tab',{name:'Tudo',exact:true}));
  await poll(async()=>await page.getByRole('tab',{name:'Tudo',exact:true}).getAttribute('aria-selected')==='true','native All filter selected');
  // The unread window can reach an older record than the initial All window.
  // Keep FM26's native newer/older paging contract instead of treating that
  // realised window as the entire inbox or forcing a refresh/full expansion.
  const seenWindows=new Set(),nativeRow=page.locator('tr[data-fm-message-id="'+messageId+'"]');
  messageCheck.allPages=0;
  while(!await nativeRow.count()){
   const window=await page.locator('tr[data-fm-message-id]').evaluateAll(rs=>rs.map(r=>r.dataset.fmMessageId).join('|'));
   assert.ok(!seenWindows.has(window),'Native older-message paging must advance');seenWindows.add(window);
   const older=page.getByRole('button',{name:'Show older messages',exact:true});
   assert.equal(await older.count(),1,'The read record must remain reachable through the native inbox');
   await activate(older);messageCheck.allPages++;
   await poll(async()=>(await page.locator('tr[data-fm-message-id]').evaluateAll(rs=>rs.map(r=>r.dataset.fmMessageId).join('|')))!==window,'actual older-message window');await settled();
  }
  await poll(async()=>await selectedRow().count()===1
   &&await selectedRow().locator('td').nth(1).textContent()===subject
   &&await selectedRow().locator('td').nth(2).textContent()===sender,'same native read message and metadata in All without reload');
  const readRow=selectedRow();
  messageCheck.fullListSent=await readRow.locator('td').nth(3).innerText();
  assert.doesNotMatch(await readRow.locator('td').first().innerText(),/(^|,\s*)unread(,|$)/,'All must retain the actual read state');
  messageCheck.restoredAllRows=await page.locator('button[onclick*="msgview.open"]').count();
  checks.push('Original message table/status/category metadata, modal reading, Escape, unread-row removal, focus and filter updates without F5');
  await goPortal();
  for(const [digit,title] of [['1','Mensagens'],['2','Equipa Principal'],['3','Tácticas'],['4','Base de dados de Jogadoras'],['5','Equipa Técnica'],['6','Agenda']]){
   console.log('navigate Alt+'+digit);
   // A modifier shortcut must not prime the focused, closed native dropdown.
   await (digit==='1'?page.getByRole('combobox',{name:'FM Menu',exact:true}):page.getByRole('textbox',{name:'Search all screens',exact:true})).focus();
   const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/matchchord');await page.keyboard.press('Alt+'+digit);
   assert.equal(await(await response).text(),'');
   if(digit==='2'){
    await poll(async()=>await page.title()!=='Portal'&&(await fetchText('/gui')).startsWith('[0] Panel name="TeamSquadReport"'),'Alt+2 actual team report');
    assert.equal(await page.getByRole('heading',{name:await page.title(),exact:true}).count(),1);
   }else await poll(async()=>await page.title()===title,'Alt+'+digit+' destination');
   await settled();
   assert.match(await fetchText('/history'),/^index=\d+ count=\d+ /,'FM27 Current reference exposes a valid history position');
   if(digit==='1'){
    assert.equal(await page.getByRole('heading',{name:/^mensagens$/i}).count(),1,'The page and native card must not repeat the same Messages title');
    await activate(page.getByRole('button',{name:'Back',exact:true}));await portal();
    checks.push('The native title-bar Back button uses the same merged history path as the shortcut and returns to Portal');
   }
   await goPortal();
  }
  checks.push('All six original Alt+digit meanings resolve to observed FM27 destinations and return to Portal after each native panel load');
  for(const [caption,category] of [['TREINO','TrainingScreen'],['CLUBE','ClubScreen']]){
   console.log('primary category',caption);
   const entry=page.locator('#fmnav-tree > [role=treeitem]').filter({has:page.locator('span').filter({hasText:new RegExp('^'+caption+'$')})}).first();
   await entry.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');
   assert.match(await(await response).text(),/^Opening /);
   await poll(async()=>(await fetchText('/navigator/list')).startsWith('current: '+category+'\n')&&await page.title()!=='Portal','native '+caption+' category');
   if(await page.getByRole('button',{name:'Entendi',exact:true}).count()){
    assert.equal(await top.count(),0,'tutorial suppresses the background shell');
    await activate(page.getByRole('button',{name:'Entendi',exact:true}));
    await poll(async()=>await top.count()===6,'native tutorial acknowledged');
    checks.push(caption+': first-visit tutorial read and acknowledged through its actual native button');
   }
   await settled();assert.equal(await top.count(),6);await goPortal();
  }
  checks.push('FM27 Training and Club primary categories also open natively; the permanent shell remains available');
  await cleanControls();
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);assert.deepEqual(audits.flatMap(x=>x.violations),[]);
  const output={success:true,mvid,ownedSave,checks,audits,messageCheck,errors:c.errors,requestFailures:c.requestFailures,network:c.network,manualNVDA:false,simulationAdvanced:false};
  fs.mkdirSync(path.resolve(__dirname,'../../artifacts/tests/web'),{recursive:true});
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/live-career-shell.json'),JSON.stringify(output,null,2));console.log(JSON.stringify({success:true,mvid,checks,audits,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,manualNVDA:false,simulationAdvanced:false}));
 }catch(error){
  const failure={error:String(error.stack||error),mvid,messageCheck,errors:c.errors,requestFailures:c.requestFailures,network:c.network,requests:c.requests,
   browser:await page.evaluate(()=>({title:document.title,token:FMTOKEN,focus:document.activeElement?.outerHTML.slice(0,500),trace:window.qaInputTrace,refreshing:FM.core.refreshing,pending:FM.core.pending}))};
  fs.mkdirSync(path.resolve(__dirname,'../../artifacts/tests/web'),{recursive:true});
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/live-career-shell-failure.json'),JSON.stringify(failure,null,2));
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/live-career-shell-failure.html'),await page.locator('#fmroot').innerHTML());
  console.error(JSON.stringify(failure));throw error;
 }finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
