// Real paused-match settings, native loopback store and live Web Audio graph.
// Every edit is inverted; no career Continue, save, preview or game setting edit.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
async function run(c){
 const {page,poll,settled}=c,checks=[],audits=[],levels=['master','possession','playback','events','drones','pressure'],toggles=['wingdrones','pressureon'];
 const read=async()=>JSON.parse(await c.fetchText('/soundsettings'));
 let baseline,opened=false;
 async function change(key,keypress){
  const control=page.locator('#fmsnd_'+key);await control.focus();
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/soundsetting'&&new URL(r.url()).searchParams.get('key')===key);
  await control.press(keypress);const reply=await response;assert.equal(reply.status(),200);assert.equal(await reply.text(),'ok');
  assert.equal(await control.evaluate(e=>document.activeElement===e),true);
 }
 async function close(){
  await page.getByRole('button',{name:'Close sound cues settings',exact:true}).press('Enter');
  await poll(()=>page.getByRole('button',{name:'Sound cues settings',exact:true}).count(),'native preferences return');
  // FM26 deliberately resumes on closing this page, even when opened paused.
  // Preserve that production contract and explicitly restore the test's pause
  // through its actual inherited shortcut; don't assume a stationary clock.
  const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/matchchord');
  await page.keyboard.press('Alt+p');assert.equal(await(await response).text(),'pausing');
  await settled();
 }
 try{
  assert.equal(await page.getByRole('button',{name:'Play (match paused)',exact:true}).count(),1,'Start with a real paused match');
  const mvid=JSON.parse(await c.fetchText('/health')).coreMvid;baseline=await read();
  await c.activate(page.getByRole('button',{name:'Settings',exact:true}));
  await c.activate(page.getByRole('button',{name:'Sound cues settings',exact:true}));opened=true;
  // An inactive Web Audio branch can retain its unprocessed AudioParam value
  // while paused. Feed ZERO through the real playback bus so the DSP evaluates
  // its scheduled gain; this probe is silent and does not fake a match event.
  await page.evaluate(()=>{const source=FM.audio.ctx.createConstantSource();source.offset.value=0;source.connect(FM.audio.playbackBus);source.start();window.qaSoundGainProbe=source;});
  const changed={...baseline};
  for(const key of levels){
   assert.equal(Number(await page.locator('#fmsnd_'+key).inputValue()),baseline[key]);
   const direction=baseline[key]>=100?'ArrowLeft':'ArrowRight';await change(key,direction);
   changed[key]=Number(await page.locator('#fmsnd_'+key).inputValue());assert.notEqual(changed[key],baseline[key]);
   assert.equal(await page.locator('#fmsnd_'+key).getAttribute('aria-valuetext'),changed[key]+' percent');
  }
  for(const key of toggles){assert.equal(await page.locator('#fmsnd_'+key).isChecked(),baseline[key]);await change(key,'Space');changed[key]=!baseline[key];}
  assert.deepEqual(await read(),changed);checks.push('Six keyboard volume edits and two switches retain focus, announced values and actual server-store state');
  await poll(()=>page.evaluate(expected=>{
   const a=FM.audio;if(!a.ctx||a.ctx.state!=='running')return false;
   return [['master','master'],['possession','possessionGroup'],['playback','playbackBus'],['events','eventBus'],['drones','droneBus'],['pressure','pressureBus']].every(([key,node])=>{
    const enabled=key==='drones'?expected.wingdrones:key==='pressure'?expected.pressureon:true;
    const gain=enabled?Math.pow(expected[key]/100,2):0;
    return Math.abs(a[node].gain.value-gain)<0.002;
   });
  },changed),'actual live audio graph follows stored levels');checks.push('Running Web Audio bus gains follow all six levels and both independent switches');
  await close();await c.activate(page.getByRole('button',{name:'Sound cues settings',exact:true}));
  for(const key of levels)assert.equal(Number(await page.locator('#fmsnd_'+key).inputValue()),changed[key]);
  for(const key of toggles)assert.equal(await page.locator('#fmsnd_'+key).isChecked(),changed[key]);
  checks.push('Closing/reopening the real settings page retains every confirmed value');
  const scan=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name:'Real in-match sound settings',violations:scan.violations.map(v=>v.id)});assert.deepEqual(scan.violations,[]);
  for(const key of levels)await change(key,changed[key]<baseline[key]?'ArrowRight':'ArrowLeft');
  for(const key of toggles)await change(key,'Space');assert.deepEqual(await read(),baseline);
  await close();opened=false;await c.activate(page.getByRole('button',{name:'Close',exact:true}));
  await poll(()=>page.getByRole('button',{name:'Play (match paused)',exact:true}).count(),'same paused native match');await settled();
  checks.push('Exact original levels/switches restored through keyboard inverses; same native match remains paused');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  const report={success:true,mvid,checks,audits,baseline,changed,restored:await read(),jsErrors:c.errors,requestFailures:c.requestFailures,careerContinueSubmitted:false,playbackScope:'Inherited close resumes playback; actual Alt+P immediately restores the initial pause',manualNVDA:false};
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/sound-settings.json'),JSON.stringify(report,null,2));return report;
 }catch(error){
  // Restore only this test's settings through their real controls; never replay
  // an uncertain native gameplay command or modify files to imitate success.
  const restoration=[];
  if(opened&&baseline)for(const key of [...levels,...toggles])try{
   const control=page.locator('#fmsnd_'+key);if(!await control.count())continue;
   if(toggles.includes(key)){if(await control.isChecked()!==baseline[key])await change(key,'Space');}
   else for(let i=0;i<20&&Number(await control.inputValue())!==baseline[key];i++)await change(key,Number(await control.inputValue())<baseline[key]?'ArrowRight':'ArrowLeft');
   restoration.push(key);
  }catch(e){restoration.push({key,error:String(e)});}
  fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/sound-settings-failure.json'),JSON.stringify({success:false,error:String(error.stack||error),checks,audits,baseline,restoration,observed:await read().catch(()=>null),jsErrors:c.errors,requestFailures:c.requestFailures},null,2));throw error;
 }finally{
  await page.evaluate(()=>{if(window.qaSoundGainProbe){qaSoundGainProbe.stop();qaSoundGainProbe.disconnect();delete window.qaSoundGainProbe;}}).catch(()=>{});
 }
}
module.exports=run;
if(require.main===module)(async()=>{const c=await require('./original-client.cjs')({networkDiagnostics:true});try{console.log(JSON.stringify(await run(c),null,2));}finally{await c.browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
