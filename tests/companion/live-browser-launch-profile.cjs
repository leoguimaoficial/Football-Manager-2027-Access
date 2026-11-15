// Real blank owned browsers only; no connection to the game or user profile.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright'),{launchOptions}=require('./browser-launch-options.cjs');
(async()=>{
 const executablePath=[process.env.LOCALAPPDATA,process.env['ProgramFiles(x86)'],process.env.ProgramFiles].filter(Boolean).map(p=>path.join(p,'Microsoft/Edge/Application/msedge.exe')).find(p=>fs.existsSync(p));assert.ok(executablePath);
 const report={at:new Date().toISOString(),success:false,gameConnected:false,nativeCommandsSubmitted:false};let server;
 try{
  const base={channel:'msedge',headless:true,executablePath},plan=await launchOptions(chromium,base,['--disable-features=TcpPortRandomizationWin']);report.profile=plan.profile;
  assert.ok(plan.profile.defaultDisabledFeatures.includes('msForceBrowserSignIn'),'Actual framework defaults were discovered, not invented');
  server=await chromium.launchServer(plan.options);const actual=server.process().spawnargs.filter(a=>a.startsWith('--disable-features='));assert.equal(actual.length,1);report.actualDisabledFeatures=actual[0].slice('--disable-features='.length).split(',');
  assert.deepEqual(report.actualDisabledFeatures,plan.profile.disabledFeatures);assert.ok(report.actualDisabledFeatures.includes('TcpPortRandomizationWin'));for(const feature of plan.profile.defaultDisabledFeatures)assert.ok(report.actualDisabledFeatures.includes(feature));
  report.success=true;
 }catch(e){report.failure=e.stack;throw e;}
 finally{if(server)await server.close();const file=path.resolve(__dirname,'../../artifacts/browser-launch-profile.json');fs.writeFileSync(file,JSON.stringify(report,null,2));console.log(JSON.stringify(report));}
})().catch(e=>{console.error(e);process.exitCode=1;});
