// Existing Alt+number route, actual sole companion and Prism history. Men's career only.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const base='http://127.0.0.1:8787',pause=ms=>new Promise(r=>setTimeout(r,ms));
(async()=>{
 const health=await fetch(base+'/health').then(r=>r.json());assert.equal(health.developerTools,true);assert.equal(health.screen,'Live');
 const cookie=(await fetch(base+'/',{method:'HEAD'})).headers.get('set-cookie')?.split(';')[0];assert.ok(cookie);
 async function request(route,post=false){const r=await fetch(base+route,{method:post?'POST':'GET',headers:{Cookie:cookie}});assert.ok(r.ok,`HTTP ${r.status}`);return r.text();}
 const checks=[];
 const expected={1:'Mensagens',2:'Squad',3:'Tácticas',4:'Base de dados de Jogadores',5:'Equipa Técnica',6:'Agenda'};
 for(const digit of [2,3,4,5,6,1]){
  const start=Number((await request('/speech?since=0')).match(/^cursor: (\d+)/)[1]);
  const response=await request('/matchchord?digit='+digit,true);assert.equal(response,'','Native shortcut must confirm its real destination');
  const deadline=Date.now()+7000;let spoken=[];
  do{await pause(150);spoken=(await request('/speech?since='+start)).split('\n').filter(line=>/^\d+:/.test(line));if(spoken.length)break;}while(Date.now()<deadline);
  await pause(500);spoken=(await request('/speech?since='+start)).split('\n').filter(line=>/^\d+:/.test(line));
  assert.ok(spoken.length,`Alt+${digit} arrived without its title`);
  assert.ok(spoken.every(line=>!/, (button|image|tab|dropdown)\b/.test(line)),`Alt+${digit} automatically read a control`);
  // The server can confirm the native landing before the sole companion's
  // pending body commits. Observe that real commit; do not label an earlier
  // /rendered snapshot as this destination or resend the navigation command.
  let headings=[];const documentDeadline=Date.now()+12000;
  do{const html=await request('/rendered');headings=[...html.matchAll(/<h[1-6][^>]*>(.*?)<\/h[1-6]>/g)].map(m=>m[1].replace(/<[^>]+>/g,''));if(headings.some(h=>h.includes(expected[digit])))break;await pause(150);}while(Date.now()<documentDeadline);
  assert.ok(headings.some(h=>h.includes(expected[digit])),`Companion did not commit Alt+${digit}'s actual destination`);
  checks.push({digit,spoken,headings:headings.slice(0,8)});
 }
 const evidence={at:new Date().toISOString(),health,checks,manualNvdaEvidence:false,physicalAltNumberKeysTested:false,savesIssued:0,simulationIssued:0,businessCommandsIssued:0};
 const output=process.argv[2]||path.resolve(__dirname,'../../artifacts/native-auto-speech/shortcut-title-speech.json');fs.mkdirSync(path.dirname(output),{recursive:true});fs.writeFileSync(output,JSON.stringify(evidence,null,2));console.log(`PASS: all six Alt-number command paths announce titles without automatic controls; returned to Messages. ${output}`);
})().catch(e=>{console.error(e.stack||e);process.exitCode=1;});
