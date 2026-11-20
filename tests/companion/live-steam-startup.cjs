// Real production document and native main-menu actions. No career or save is loaded.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true});
 const {page,poll,settled,activate}=c;
 const report={at:new Date().toISOString(),success:false,checks:[],audits:[],careerLoaded:false,saveSubmitted:false,manualNVDA:false};
 const output=path.resolve(__dirname,'../../artifacts/steam-investigation');fs.mkdirSync(output,{recursive:true});
 async function audit(label){
  const result=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  report.audits.push({label,violations:result.violations});assert.deepEqual(result.violations,[],label);
  assert.equal(await page.locator('h1 button,h2 button,h3 button,h4 button,h5 button,h6 button').count(),0);
 }
 async function menu(){await poll(async()=>await page.title()==='MainMenu','actual native MainMenu');await settled();}
 try{
  const version=fs.readFileSync(path.resolve(__dirname,'../../Directory.Build.props'),'utf8').match(/<Version>([^<]+)<\/Version>/)?.[1];
  assert.ok(version,'Central mod version');
  report.health=JSON.parse(await c.fetchText('/health'));assert.equal(report.health.developerTools,false);assert.equal(report.health.version,version);
  // The game's own update notice can be the first foreground after a patch.
  // Observe and dismiss its actual native acknowledgement before testing MainMenu.
  if(await page.title()!=='MainMenu'&&await page.getByRole('button',{name:'Entendi',exact:true}).count()===1){
   report.startupNotice=await page.title();await audit('Native post-update notice');
   await activate(page.getByRole('button',{name:'Entendi',exact:true}));
   report.checks.push('Native post-update notice acknowledged through its own control');
  }
  await menu();assert.equal(await page.locator('html').getAttribute('data-fm-development'),'false');
  report.menuButtons=await page.getByRole('button').allTextContents();
  assert.ok(report.menuButtons.includes('CRIAR NOVO JOGO, Alt I for more info'));
  assert.ok(report.menuButtons.some(text=>text.startsWith('CARREGAR JOGO, ')));
  await audit('Steam MainMenu');report.checks.push('Native MainMenu retains complete FM26 labels, descriptions and informational headings');
  for(const [open,close] of [['Quais são as novidades?','Entendi'],['Preferências','Close']]){
   const opener=page.getByRole('button',{name:open,exact:true}),origin=await opener.getAttribute('id');
   await activate(opener);await poll(async()=>await page.title()!=='MainMenu','native '+open);await settled();
   report.checks.push('Native destination: '+await page.title());await audit(open);
   await activate(page.getByRole('button',{name:close,exact:true}));await menu();
   assert.equal(await page.evaluate(()=>document.activeElement?.id),origin,'exact originating control focus');
  }
  const load=page.getByRole('button',{name:/^CARREGAR JOGO, /}),origin=await load.getAttribute('id');
  await activate(load);await poll(async()=>await page.title()!=='MainMenu','native load dialog');await settled();
  report.loadDialogTitle=await page.title();await audit('Steam load dialog');
  await activate(page.getByRole('button',{name:'Close',exact:true}));await menu();
  assert.equal(await page.evaluate(()=>document.activeElement?.id),origin);
  report.checks.push('Actual load dialog cancels without selecting/loading a save and returns exact focus');
  for(const [route,method] of [['/eval','POST'],['/reload','POST'],['/gui','GET'],['/rendered/report','POST']]){
   const status=await page.evaluate(async({route,method})=>(await fetch(route,{method})).status,{route,method});assert.equal(status,404,route);
  }
  report.checks.push('Production developer routes remain absent');
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,report.health.coreMvid);
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  report.success=true;
 }catch(error){report.error=error.stack;fs.writeFileSync(path.join(output,'steam-startup-failure.html'),await page.content().catch(()=>''));throw error;}
 finally{
  report.errors=c.errors;report.requestFailures=c.requestFailures;report.networkFailures=c.network.failures;
  fs.writeFileSync(path.join(output,'steam-startup-result.json'),JSON.stringify(report,null,2));await c.browser.close();
  console.log(JSON.stringify({success:report.success,checks:report.checks,audits:report.audits.length}));
 }
})().catch(error=>{console.error(error);process.exitCode=1;});
