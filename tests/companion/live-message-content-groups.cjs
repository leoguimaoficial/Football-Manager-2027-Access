// Real native commands and the sole Companion's reported DOM. The auxiliary
// browser parses captured markup only; it never opens a second live game UI.
// This does not qualify frontend key injection or manual NVDA operation.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const base='http://127.0.0.1:8787',out=path.resolve(__dirname,'../../artifacts/message-text-contract');
fs.mkdirSync(out,{recursive:true});
(async()=>{
 const handshake=await fetch(base+'/',{method:'HEAD'});assert.ok(handshake.ok);
 const cookie=handshake.headers.get('set-cookie').split(';')[0];
 async function request(route,method='GET'){
  const response=await fetch(base+route,{method,headers:{Cookie:cookie},signal:AbortSignal.timeout(25000)}),text=await response.text();
  assert.ok(response.ok,route+': '+text);return text;
 }
 const browser=await chromium.launch({channel:'msedge',headless:true}),page=await browser.newPage();
 const report={at:new Date().toISOString(),success:false,checks:[],messages:[],manualNVDA:false,frontendKeyboard:false,secondLiveGameDocument:false,businessActions:false,save:false,simulation:false};
 async function observe(test){
  const deadline=Date.now()+30000;let last='',same=0;
  while(Date.now()<deadline){
   const html=await request('/rendered');
   if(html===last)same++;else{last=html;same=0;}
   if(same>=2&&test(html)){
    await page.setContent('<main lang="pt-PT">'+html+'</main>');return html;
   }
   await new Promise(r=>setTimeout(r,200));
  }
  fs.writeFileSync(path.join(out,'failure.html'),last);throw Error('Companion document did not reach expected state');
 }
 const isMessage=(id)=>html=>html.includes('data-fm-message-view-id="'+id+'"');
 async function close(){
  assert.equal(await request('/message/close','POST'),'');
  return observe(html=>!html.includes('data-fm-message-view-id='));
 }
 async function filter(label){
  const button=page.locator('button[aria-pressed]').filter({hasText:new RegExp('^'+label+'(?: \\(|$)')});
  assert.equal(await button.count(),1);const id=await button.getAttribute('id');
  assert.match(await request('/activate?id='+id,'POST'),/^Activated /);
  return observe(html=>html.includes('id="'+id+'" aria-pressed="true"'));
 }
 async function open(id){
  const row=page.locator('tr[data-fm-message-id="'+id+'"] button[onclick^="FM.msgview.open"]');
  assert.equal(await row.count(),1);const handle=await row.getAttribute('id');
  assert.equal(await request('/message/open?id='+handle,'POST'),'');return observe(isMessage(id));
 }
 async function groups(){
  assert.deepEqual(await page.locator('h3').allTextContents(),['Objectivo','Assuntos','Pessoal importante']);
  const lists=page.locator('main > ul');assert.equal(await lists.count(),2);
  assert.deepEqual(await lists.nth(0).locator(':scope > li').allTextContents(),['Capitães','Melhor 11','Candidatos à equipa principal','Lesões actuais']);
  assert.deepEqual(await lists.nth(1).locator(':scope > li').evaluateAll(rows=>rows.map(row=>({name:row.querySelector('button')?.textContent,description:row.querySelector('p:last-child')?.textContent}))),[
   {name:'Diogo Costa',description:'guarda-redes de 26 anos'},
   {name:'Tcherno Jamanca',description:'avançado de 16 anos'},
   {name:'Cláudio Ramos',description:'guarda-redes de 34 anos'}]);
  assert.equal(await page.locator('h1 button,h2 button,h3 button,h4 button,h5 button,h6 button').count(),0);
  assert.equal(await lists.nth(0).locator('button').count(),0,'Native topics are not person destinations');
  const ids=await page.locator('[id]').evaluateAll(elements=>elements.map(e=>e.id));assert.equal(new Set(ids).size,ids.length);
 }
 try{
  report.health=JSON.parse(await request('/health'));assert.equal(report.health.pid,2676);assert.equal(report.health.developerTools,true);
  const reported=await observe(isMessage('7'));
  fs.writeFileSync(path.join(out,'responsibilities.html'),reported);
  const originalCommands=await page.locator('button').allTextContents();
  assert.deepEqual(originalCommands.slice(0,-1),['Close message','Rui Correia','Lino Godinho','Paulo Araújo','Declinar Introdução']);
  assert.match(originalCommands[originalCommands.length-1],/^(Começar Introdução|Continuar introdução)$/);
  assert.equal(await page.locator('[data-fm-msg-body] button,[data-fm-msg-body] a').count(),0);
  assert.match(await page.locator('[data-fm-msg-body]').textContent(),/^Olá, QA Portal\. Sou o Lino Godinho, Treinador Adjunto Principal aqui do FC Porto\./);
  assert.equal(await page.locator('h2 + p > button[data-fm-message-view-id="7"]').count(),1,'No extracted club button before Close');
  assert.equal(await page.locator('main > ul').nth(1).locator('li button').count(),3,'Important people retain their real links');
  report.checks.push('Reported prose/title/sender use FM26 readonly text and original spacing; important-person links and response commands remain');
  await close();await filter('Tudo');await open('8');
  const initial=await observe(isMessage('8'));await groups();
  fs.writeFileSync(path.join(out,'squad.html'),initial);
  assert.equal(await page.locator('[data-fm-msg-body] button,[data-fm-msg-body] a').count(),0);
  assert.equal(await page.locator('h2 + p > button[data-fm-message-view-id="8"]').count(),1);
  assert.match(initial,/<!--fm-focus:[^>]+-->/);assert.ok(initial.includes('data-fm-msg-body'));
  const native=await request('/gui?textlen=2000');fs.writeFileSync(path.join(out,'squad-native.txt'),native);
  assert.ok(native.includes('name="topic_block"')&&native.includes('name="squad_header"')&&native.includes('name="squad_block"'));
  report.checks.push('Native independent topics/people groups rendered as headings and two semantic lists');
  const originalPeople=await page.locator('main > ul').nth(1).locator('button').evaluateAll(es=>es.map(e=>({id:e.id,name:e.textContent})));
  for(const person of originalPeople){
   assert.match(await request('/activate?id='+person.id,'POST'),/^Activated /);
   const profile=await observe(html=>!isMessage('8')(html)&&html.includes(person.name));
   const tree=await request('/gui?textlen=250');assert.match(tree,/Panel name="PlayerReport"/);
   fs.writeFileSync(path.join(out,'profile-'+person.id+'.html'),profile);
   assert.equal(await request('/history?dir=back','POST'),'');
   await observe(isMessage('8'));await groups();
  }
  report.checks.push('Each of the three actual player links opens PlayerReport and native Back restores message groups');
  await close();await filter('Tudo');
  const originalRows=await page.locator('tr[data-fm-message-id]').evaluateAll(rs=>rs.map(r=>({id:r.dataset.fmMessageId,text:r.textContent,status:r.cells[0].textContent})));
  assert.equal(originalRows.length,11);assert.ok(originalRows.every(r=>!/(^|,\s*)unread(,|$)/.test(r.status)));
  for(const row of originalRows){
   const html=await open(row.id);
   fs.writeFileSync(path.join(out,'message-'+row.id+'.html'),html);
   const tree=await request('/gui?textlen=2000');fs.writeFileSync(path.join(out,'message-'+row.id+'-native.txt'),tree);
   const info=await page.locator('main').evaluate(main=>({
    headings:[...main.querySelectorAll('h2,h3,h4')].map(e=>e.textContent),
    lists:[...main.children].filter(e=>e.tagName==='UL').map(e=>[...e.children].map(li=>li.textContent)),
    body:main.querySelector('[data-fm-msg-body]')?.textContent,
    tables:main.querySelectorAll('table').length,
    buttons:main.querySelectorAll('button').length
   }));
   assert.ok(info.body?.trim());assert.equal(await page.locator('h1 button,h2 button,h3 button,h4 button,h5 button,h6 button').count(),0);
   assert.equal(await page.locator('[data-fm-msg-body] button,[data-fm-msg-body] a').count(),0,'Ordinary message prose stays readonly');
   if(tree.includes('name="purpose_block"')&&tree.includes('name="topic_block"')){
    assert.ok(info.headings.includes('Objectivo'));assert.ok(info.headings.includes('Assuntos'));assert.ok(info.lists.length>=1);
   }
   if(row.id==='8')await groups();
   if(row.id==='133'){
    const tableLinks=page.locator('table tbody button');assert.equal(await tableLinks.count(),18);
    const club=tableLinks.filter({hasText:/^Benfica$/});assert.equal(await club.count(),1);
    const clubId=await club.getAttribute('id');assert.match(await request('/activate?id='+clubId,'POST'),/^Activated /);
    const clubHtml=await observe(html=>!isMessage('133')(html)&&html.includes('Benfica'));
    assert.match(await request('/gui?textlen=250'),/Panel name="ClubSite"/);
    fs.writeFileSync(path.join(out,'actual-table-club.html'),clubHtml);
    assert.equal(await request('/history?dir=back','POST'),'');await observe(isMessage('133'));
    assert.equal(await page.locator('table tbody button').count(),18);
    report.checks.push('All18 native table-row club links remain; actual Benfica destination and native Back preserve the message table');
   }
   report.messages.push({id:row.id,...info});await close();
  }
  const after=await page.locator('tr[data-fm-message-id]').evaluateAll(rs=>rs.map(r=>({id:r.dataset.fmMessageId,text:r.textContent,status:r.cells[0].textContent})));
  assert.deepEqual(after,originalRows);
  report.checks.push('All 11 native messages open/close with bodies, action semantics and original read-list metadata intact');
  await filter('Tarefas');await open('7');
  assert.deepEqual(await page.locator('button').allTextContents(),originalCommands);
  report.checks.push('Original Tasks filter and responsibilities message restored');report.success=true;
 }catch(error){report.failure=error.stack;throw error;}
 finally{fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));await browser.close();}
 console.log(JSON.stringify({success:report.success,checks:report.checks,messages:report.messages.map(m=>({id:m.id,headings:m.headings,lists:m.lists,tables:m.tables}))}));
})().catch(error=>{console.error(error);process.exitCode=1;});
