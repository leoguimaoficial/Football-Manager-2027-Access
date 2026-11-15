// Actual Windows filesystem refusal, real paused native match and original UI.
// No exception mock, ACL changes, direct configuration writes or game commands.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {createHash}=require('node:crypto'),{spawn}=require('node:child_process');
const {default:AxeBuilder}=require('@axe-core/playwright');
async function run(c){
 const {page,poll,settled}=c,checks=[],audits=[],folder=path.resolve(__dirname,'../../artifacts/tests/web/sound-refusal-'+Date.now());
 fs.mkdirSync(folder,{recursive:true});
 let baseline,target,before,helper,helperError,opened=false;
 const read=async()=>JSON.parse(await c.fetchText('/soundsettings'));
 const cursor=async endpoint=>{const text=await c.fetchText(endpoint),match=text.match(/^cursor: (\d+)/);assert.ok(match,text.slice(0,100));return Number(match[1]);};
 const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
 async function level(key){const field=page.locator('#fmsnd_master');await field.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/soundsetting'&&new URL(r.url()).searchParams.get('key')==='master');await field.press(key);const reply=await response;assert.equal(reply.status(),200);return reply.text();}
 async function release(){
  if(!helper)return;const owned=helper;helper=null;fs.writeFileSync(path.join(folder,'release'),'release');
  try{await poll(()=>owned.exitCode!==null||owned.signalCode!==null,'owned readonly file lock released');}
  catch(error){owned.kill();throw error;}
  assert.equal(owned.exitCode,0,helperError||'Read lock helper failed');
  assert.ok(fs.existsSync(path.join(folder,'released')));
 }
 async function closePaused(){await c.activate(page.getByRole('button',{name:'Close sound cues settings',exact:true}));const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/matchchord');await page.keyboard.press('Alt+p');assert.equal(await(await response).text(),'pausing');await settled();}
 try{
  assert.equal(process.platform,'win32','This is an actual Windows file-sharing test');
  assert.equal(await page.getByRole('button',{name:'Play (match paused)',exact:true}).count(),1,'Start with an actual paused native match');
  const mvid=JSON.parse(await c.fetchText('/health')).coreMvid;
  const code=`foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}"){var t=a.GetType("FM27Access.Plugin.Server.SoundSettingsStore");System.Console.WriteLine(t.GetField("StorePath",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null));}`;
  const resolved=await page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);
  target=resolved.split(/\r?\n/).find(s=>/soundsettings\.json$/.test(s.trim()))?.trim();assert.ok(target,resolved);
  assert.equal(path.basename(target),'soundsettings.json');assert.equal(path.basename(path.dirname(target)),'FM27Access');
  before=fs.readFileSync(target);fs.writeFileSync(path.join(folder,'original-soundsettings.json'),before);
  baseline=await read();await c.activate(page.getByRole('button',{name:'Settings',exact:true}));await c.activate(page.getByRole('button',{name:'Sound cues settings',exact:true}));opened=true;
  let helperOutput='';helper=spawn('pwsh.exe',['-NoLogo','-NoProfile','-NonInteractive','-File',path.join(__dirname,'sound-settings-read-lock.ps1'),'-Target',target,'-Signals',folder],{windowsHide:true,stdio:['ignore','pipe','pipe']});
  helper.on('error',e=>helperError=String(e));helper.stdout.on('data',d=>helperOutput+=d);helper.stderr.on('data',d=>helperOutput+=d);
  await poll(()=>{if(helperError||helper.exitCode!==null)throw Error(helperError||helperOutput);return fs.existsSync(path.join(folder,'ready'));},'actual readonly mod file lock');
  const speechStart=await cursor('/speech'),logStart=await cursor('/log');
  const warning=await level(baseline.master>0?'ArrowLeft':'ArrowRight');assert.equal(warning,'The sound setting applies to this session, but could not be saved for the next session.');
  const changed=await read();assert.notEqual(changed.master,baseline.master);assert.deepEqual({...changed,master:baseline.master},baseline);
  await poll(()=>page.evaluate(v=>FM.audio.ctx.state==='running'&&Math.abs(FM.audio.master.gain.value-Math.pow(v/100,2))<0.002,changed.master),'actual session audio gain despite persistence refusal');
  assert.equal(await page.locator('#fmsnd_master').evaluate(e=>e===document.activeElement),true);assert.equal(hash(fs.readFileSync(target)),hash(before));
  checks.push('Actual readonly sharing lock refuses atomic replacement; original disk bytes preserved while real keyboard value/store/audio change and retain focus');
  await poll(async()=> (await c.fetchText('/speech?since='+speechStart)).includes(warning),'new asynchronous SpeechPipeline persistence warning');
  const nativeFault=await c.fetchText('/log?since='+logStart);assert.match(nativeFault,/sound settings save failed.*(?:UnauthorizedAccessException|IOException)/);
  await poll(()=>page.getByText(warning,{exact:true}).count(),'readable unsaved-settings notice');
  assert.doesNotMatch(await page.locator('#fmroot').innerText(),/Saved for next time/);
  await page.evaluate(()=>FM.core.refresh(false,true));await settled();assert.equal(await page.getByText(warning,{exact:true}).count(),1,'Actual server refresh retains the unsaved notice');
  await closePaused();await c.activate(page.getByRole('button',{name:'Sound cues settings',exact:true}));
  assert.equal(Number(await page.locator('#fmsnd_master').inputValue()),changed.master);assert.equal(await page.getByText(warning,{exact:true}).count(),1,'Reopening retains the actual unsaved state');
  const errorAudit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();assert.deepEqual(errorAudit.violations,[]);audits.push({name:'Actual persisted failure notice after refresh and reopening',violations:[]});
  const warningCount=(await c.fetchText('/speech?since='+speechStart)).split('\n').filter(line=>line.endsWith(': '+warning)).length;assert.equal(warningCount,1,'Exactly one NEW warning across refresh/reopening, not a previous run');
  checks.push('Explicit warning reaches SpeechPipeline once and remains readable through refresh/reopening, without claiming saved or adding a duplicate live region');
  await release();assert.equal(await level(changed.master<baseline.master?'ArrowRight':'ArrowLeft'),'ok');assert.deepEqual(await read(),baseline);assert.equal(hash(fs.readFileSync(target)),hash(before));
  await poll(async()=>await page.getByText(warning,{exact:true}).count()===0,'successful persistence clears the readable warning');
  assert.deepEqual(fs.readdirSync(path.dirname(target)).filter(n=>/^soundsettings\.json\..*\.tmp$/.test(n)),[]);checks.push('Released handle permits exact keyboard inverse and persistence, identical original bytes and no temporary siblings');
  await closePaused();await c.activate(page.getByRole('button',{name:'Sound cues settings',exact:true}));assert.equal(Number(await page.locator('#fmsnd_master').inputValue()),baseline.master);assert.deepEqual(await read(),baseline);
  const audit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();assert.deepEqual(audit.violations,[]);checks.push('Reopening retains all original values; actual settings page passes axe');
  audits.push({name:'Actual sound persistence restored reopening',violations:[]});
  await closePaused();opened=false;await c.activate(page.getByRole('button',{name:'Close',exact:true}));assert.equal(await page.getByRole('button',{name:'Play (match paused)',exact:true}).count(),1);checks.push('Same native historical playback remains paused after returning');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  const report={success:true,mvid,checks,audits,baseline,changed,restored:await read(),speechCursorBefore:speechStart,newWarningCount:warningCount,newNativeWriteFault:nativeFault.split('\n').filter(line=>line.includes('sound settings save failed')),diskHash:hash(fs.readFileSync(target)),originalDiskHash:hash(before),helperReleased:true,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/sound-persistence-failure.json'),JSON.stringify(report,null,2));return report;
 }catch(error){
  await release().catch(e=>helperError=String(e));
  let restored=false;try{if(opened&&baseline&&await page.locator('#fmsnd_master').count()){const observed=Number(await page.locator('#fmsnd_master').inputValue());if(observed!==baseline.master)assert.equal(await level(observed<baseline.master?'ArrowRight':'ArrowLeft'),'ok');restored=JSON.stringify(await read())===JSON.stringify(baseline);}}catch(e){helperError=String(e);}
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/sound-persistence-failure-failure.json'),JSON.stringify({success:false,error:String(error.stack||error),checks,baseline,restored,helperError,diskUnchanged:target&&before?hash(fs.readFileSync(target))===hash(before):null,errors:c.errors,requestFailures:c.requestFailures},null,2));throw error;
 }finally{await release();}
}
module.exports=run;
if(require.main===module)(async()=>{const c=await require('./original-client.cjs')({networkDiagnostics:true});try{console.log(JSON.stringify(await run(c),null,2));}finally{await c.browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
