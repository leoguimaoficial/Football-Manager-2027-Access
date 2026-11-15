// Live regression against the sole installed companion, with the FM window unfocused.
// Main-menu dialogs only: no career load, preferences edit, save or simulation.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const origin='http://127.0.0.1:8787';
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));
(async()=>{
 const health=await fetch(origin+'/health').then(r=>r.json());
 assert.equal(health.screen,'Live');assert.equal(health.developerTools,true,'Speech evidence requires an explicit diagnostic build');
 const head=await fetch(origin+'/',{method:'HEAD'}),cookie=head.headers.get('set-cookie')?.split(';')[0];
 assert.ok(cookie,'Original server session required');
 async function request(route,post=false,body=''){
  const response=await fetch(origin+route,{method:post?'POST':'GET',headers:{Cookie:cookie},...(post?{body}:{})});
  assert.ok(response.ok,`HTTP ${response.status}: ${route.split('?')[0]}`);return response.text();
 }
 const speech=()=>request('/speech?since=0');
 const cursor=async()=>Number((await speech()).match(/^cursor: (\d+)/)[1]);
 const lines=async since=>(await request('/speech?since='+since)).split('\n').filter(line=>/^\d+:/.test(line));
 const focused=async()=>{
  const result=await request('/eval',true,'System.Console.WriteLine("FOCUSED="+UnityEngine.Application.isFocused);');
  assert.match(result,/FOCUSED=False/,'The real FM window must remain unfocused');
 };
 async function waitFor(test){
  const until=Date.now()+10000;
  do{const html=await request('/rendered');if(test(html))return html;await pause(100);}while(Date.now()<until);
  throw Error('The actual companion did not report the expected document');
 }
 const buttons=html=>[...html.matchAll(/<button\b([^>]*)>([\s\S]*?)<\/button>/g)].map(m=>({id:m[1].match(/\bid="([^"]+)"/)?.[1],attributes:m[1],text:m[2].replace(/<[^>]+>/g,' ').trim()}));
 async function activate(button){assert.ok(button?.id,'Unique current native button required');const reply=await request('/activate?id='+encodeURIComponent(button.id),true);assert.match(reply,/^Activated /);}
 let menu=await waitFor(html=>buttons(html).some(b=>b.text==='Preferências')&&buttons(html).some(b=>b.text==='Quais são as novidades?'));
 await focused();
 const checks=[];
 for(let cycle=0;cycle<2;cycle++){
  for(const dialog of [{open:'Preferências',title:'Preferências e Acessibilidade',close:'Close',native:/Close/},{open:'Quais são as novidades?',title:'O que há de novo no FM27?',close:'Entendi',native:/image/}]){
   await focused();const start=await cursor();
   await activate(buttons(menu).find(b=>b.text===dialog.open));
   const html=await waitFor(value=>value.includes(dialog.title)&&buttons(value).some(b=>b.text===dialog.close));
   await pause(900);await focused();
   const opening=await lines(start);
   assert.ok(opening.some(line=>line.endsWith(': '+dialog.title)),`Native title missing for ${dialog.open}`);
   assert.ok(opening.every(line=>line.endsWith(': '+dialog.title)),`An automatic control was read while opening ${dialog.open}`);
   const nativeCursor=await request('/cursor',true,'read');assert.match(nativeCursor,dialog.native,'Native cursor must still follow the actual panel');
   const closeStart=await cursor();await activate(buttons(html).find(b=>b.text===dialog.close));
   menu=await waitFor(value=>buttons(value).some(b=>b.text==='Preferências')&&buttons(value).some(b=>b.text==='Quais são as novidades?'));
   await pause(900);const returning=await lines(closeStart);
   assert.ok(returning.length>0,'The restored native page title must still speak');
   assert.ok(returning.every(line=>!/, (button|image)\b/.test(line)),'An automatic control was read on return');
   checks.push({cycle:cycle+1,dialog:dialog.open,titleSpeech:opening.length,automaticControlSpeech:0,returnTitleSpeech:returning.length,nativeCursorStillFollows:true,reportedCompanionContent:true});
  }
 }
 const create=buttons(menu).find(b=>b.text.startsWith('CRIAR NOVO JOGO'));
 assert.match(create.text,/Alt I/,'Inherited description hint remains in the original label');
 const description=await request('/describe?id='+encodeURIComponent(create.id),true);assert.ok(description.length>80,'Actual long native help required');
 const helpStart=await cursor();await request('/speak?text='+encodeURIComponent(description),true);await pause(300);
 assert.ok((await lines(helpStart)).length>0,'Requested description still reaches SpeechPipeline');
 const explicitStart=await cursor();const last=await request('/cursor',true,'last');await pause(300);
 assert.ok((await lines(explicitStart)).length>0,'Explicit native cursor navigation must still speak');
 checks.push({descriptionProtocolPreserved:true,explicitNativeCursorSpeechPreserved:true,nativeLast:last.trim()});
 const evidence={at:new Date().toISOString(),health,checks,groups:checks.length,manualNvdaEvidence:false,actualAltIKeyTested:false,careerLoaded:false,savesChanged:false,preferencesChanged:false};
 const output=process.argv[2]||path.resolve(__dirname,'../../artifacts/native-auto-speech/live-debug.json');fs.mkdirSync(path.dirname(output),{recursive:true});fs.writeFileSync(output,JSON.stringify(evidence,null,2));
 console.log(`PASS: ${checks.length} groups; four dialog open/return cycles announce titles without controls; requested help and native cursor still speak. ${output}`);
})().catch(error=>{console.error(error.stack||error);process.exitCode=1;});
