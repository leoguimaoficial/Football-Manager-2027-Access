// Actual male QA wizard through native production commands, observed in the sole
// already-running companion's reported DOM. This does not claim keyboard/NVDA.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
(async()=>{
 const base='http://127.0.0.1:8787',root=path.resolve(__dirname,'../..');
 const handshake=await fetch(base+'/',{method:'HEAD'});assert.equal(handshake.status,200);
 const cookie=handshake.headers.getSetCookie().map(c=>c.split(';')[0]).join('; ');
 const report={at:new Date().toISOString(),success:false,career:'FC Porto men',checks:[],observations:[],nativeCommandsSubmitted:0,confirmSubmitted:false,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false,keyboardTested:false,soleProductionCompanion:true};
 const read=async route=>{const r=await fetch(base+route,{headers:{Cookie:cookie}});assert.equal(r.status,200);return r.text();};
 const text=s=>s.replace(/<[^>]+>/g,'').replace(/&amp;/g,'&').replace(/&#39;/g,"'").replace(/&quot;/g,'"').trim();
 async function command(id){const screen=await read('/version');const r=await fetch(base+'/activate?id='+encodeURIComponent(id)+'&screen='+encodeURIComponent(screen),{method:'POST',headers:{Cookie:cookie}});report.nativeCommandsSubmitted++;assert.equal(r.status,200);assert.match(await r.text(),/^Activated /);}
 async function wait(check){const until=Date.now()+18000;do{const d=await read('/rendered');if(check(d))return d;await new Promise(r=>setTimeout(r,150));}while(Date.now()<until);throw Error('Actual native/companion state did not arrive');}
 const buttons=d=>[...d.matchAll(/<button\b([^>]*)>([\s\S]*?)<\/button>/g)].map(m=>({id:m[1].match(/\bid="([^"]+)"/)?.[1],label:text(m[2]),disabled:/\bdisabled\b/.test(m[1])}));
 const radios=d=>[...d.matchAll(/<label><input\b([^>]*type="radio"[^>]*)>([\s\S]*?)<\/label>/g)].map(m=>({id:m[1].match(/\bid="([^"]+)"/)[1],label:text(m[2]),checked:/\bchecked\b/.test(m[1])}));
 async function activate(label){const d=await read('/rendered'),b=buttons(d).filter(b=>b.label===label&&!b.disabled);assert.equal(b.length,1,label);await command(b[0].id);}
 function previews(d){return[...d.matchAll(/<p>(Com posse de bola|Sem posse da bola): <span id="([^"]+)">([^<]*)<\/span><\/p>/g)].map(m=>({phase:m[1],id:m[2],value:text(m[3])}));}
 async function observe(stage){
  const d=await wait(d=>previews(d).length===2),pairs=previews(d),native=await read('/subtree?name=regular-state&nth=1&count=1');
  const actual=[...native.matchAll(/\[3\] SIText(?: name="[^"]*")? visible=True text="([^"\r\n]*)"/g)].map(m=>m[1]);
  assert.equal(actual.length,2,'Actual two native phase-value sources');assert.deepEqual(pairs.map(p=>p.value),actual);
  assert.equal(new Set(pairs.map(p=>p.id)).size,2);assert.equal(pairs[0].phase,'Com posse de bola');assert.equal(pairs[1].phase,'Sem posse da bola');
  assert.ok(!/<h[1-6]\b[^>]*>(?:(?!<\/h[1-6]>)[\s\S])*<button\b/.test(d));
  report.observations.push({stage,pairs,radios:radios(d)});fs.writeFileSync(path.join(root,'artifacts/male-preview-'+stage+'.html'),d);return{d,pairs};
 }
 try{
  report.health=JSON.parse(await read('/health'));assert.equal(report.health.screen,'Live');
  let d=await read('/rendered');assert.ok(d.includes('>FC Porto<')&&d.includes('data-obj="1478"'),'Require the recorded male FC Porto QA career');
  assert.ok(d.includes('Escolher formação (sem posse da bola)'),'Begin on uncommitted native OOP wizard step');
  let choices=radios(d);assert.equal(choices.length,3);assert.ok(choices.every(r=>!r.checked),'Require an unchosen OOP draft; do not replay a committed action');
  const before=await observe('oop-empty');
  for(const index of [0,1,0]){
   const current=radios(await read('/rendered'));const target=current[index];await command(target.id);
   await wait(d=>radios(d).some(r=>r.id===target.id&&r.checked));
   const sample=await observe('oop-'+report.observations.length);assert.deepEqual(sample.pairs.map(p=>p.id),before.pairs.map(p=>p.id));
   assert.equal(sample.pairs[1].value,target.label.replace(/^Sugerido, /,''));assert.equal(sample.pairs[0].value,before.pairs[0].value);
  }
  report.checks.push('Both phases equal their own actual native SIText values; OOP change and inverse update the original span without changing IP');
  await activate('Previous step');await wait(d=>d.includes('Escolher formação (com posse da bola)'));
  const ipStart=await observe('ip-start'),ipChoices=radios(ipStart.d);assert.equal(ipChoices.filter(r=>r.checked).length,1);
  const original=ipChoices.findIndex(r=>r.checked),other=ipChoices.findIndex(r=>!r.checked);assert.ok(other>=0);
  for(const index of [other,original]){
   const target=radios(await read('/rendered'))[index];await command(target.id);await wait(d=>radios(d).some(r=>r.id===target.id&&r.checked));
   const sample=await observe('ip-'+report.observations.length);assert.equal(sample.pairs[0].value,target.label.replace(/^Sugerido, /,''));assert.deepEqual(sample.pairs.map(p=>p.id),before.pairs.map(p=>p.id));
  }
  report.checks.push('Unnamed selected IP value retains its own phase label through native change/inverse; original value identities remain stable');
  await activate('Cancelar');await wait(d=>d.includes('Active tactic, slot 1:')&&!d.includes('Criar Nova Táctica'));
  report.checks.push('Native Cancel leaves the existing active tactic and does not confirm a second tactic');report.success=true;
 }catch(error){report.failure=error.stack;throw error;}
 finally{fs.writeFileSync(path.join(root,'artifacts/male-preview-labels-regression.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({success:report.success,checks:report.checks.length,observations:report.observations.length,nativeCommands:report.nativeCommandsSubmitted}));}
})().catch(e=>{console.error(e);process.exitCode=1;});
