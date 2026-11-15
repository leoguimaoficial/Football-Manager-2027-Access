// Real native Preferences language changes; restores the original Portuguese
// selection and every Quick-mode value. No native setters, Save or simulation.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true,initScript:()=>{
  window.qaLanguageSources=0;window.qaLanguageOpens=0;window.qaLanguageErrors=0;const Original=window.EventSource;
  window.EventSource=class extends Original{constructor(...args){super(...args);qaLanguageSources++;
   this.addEventListener('open',()=>qaLanguageOpens++);this.addEventListener('error',()=>qaLanguageErrors++);}};
 }}),{page,poll,activate}=c;
 const checks=[],audits=[],observations=[],out=path.resolve(__dirname,'../../artifacts/tests/web/document-language.json');
 let original=null,restorationRequired=false;
 const button=name=>page.getByRole('button',{name,exact:true});
 async function native(){const raw=await page.evaluate(async()=>await(await fetch('/eval',{method:'POST',body:
  'System.Console.WriteLine("LANG|"+SI.Translation.TranslationManager.CurrentLanguage);System.Console.WriteLine("CULTURE|"+SI.Translation.TranslationManager.s_defaultCulture.Name);'})).text());
  assert.doesNotMatch(raw,/\[compile\]|\[exception\]/);const match=raw.match(/^LANG\|([^\r\n]+)/m);assert.ok(match,raw);return{resource:match[1],raw};}
 async function snapshot(){return page.locator('#fmroot').evaluate(e=>({
  tabs:[...e.querySelectorAll('[role=tab][aria-selected=true]')].map(x=>x.textContent),
  selects:[...e.querySelectorAll('select')].map(x=>({name:x.getAttribute('aria-label')||x.labels?.[0]?.textContent,value:x.options[x.selectedIndex]?.textContent})).filter(x=>x.name&&!['World','FM Menu'].includes(x.name)),
  checks:[...e.querySelectorAll('input[type=checkbox]')].map(x=>({name:x.labels?.[0]?.textContent,checked:x.checked}))}));}
 async function preferences(){await c.dropdown('FM Menu');const choices=await page.locator('select[data-dd][data-loaded] option').allTextContents();
  const target=choices.find(x=>/^(Preferences|Preferências|Preferencias)$/.test(x));assert.ok(target,'Actual native preferences action');await c.choose(target);
  await poll(()=>page.locator('select').evaluateAll(es=>es.some(x=>['Idioma','Language'].includes(x.getAttribute('aria-label')))),'native preferences language control');await c.settled();}
 async function change(label,tag,resource){const field=await page.locator('select').evaluateAll(es=>es.map(x=>x.getAttribute('aria-label')).find(x=>['Idioma','Language'].includes(x)));
  assert.ok(field);await c.dropdown(field);const choices=await page.locator('select[data-dd][data-loaded] option').allTextContents();assert.ok(choices.includes(label),'Actual native option exists');
  await c.choose(label);const confirm=page.getByRole('button',{name:/^(Confirm|Confirmar)$/,exact:true});assert.equal(await confirm.count(),1);await activate(confirm);
  await poll(()=>page.evaluate(tag=>document.documentElement.lang===tag,tag),'live document language follows native confirmation');
  const actual=await native();assert.equal(actual.resource,resource);await c.settled();
  const body=await page.evaluate(async()=>{const r=await fetch('/body');return{status:r.status,language:r.headers.get('X-FM-Language')};});assert.equal(body.status,200);assert.equal(body.language,tag);
  const stream=await page.evaluate(()=>({sources:qaLanguageSources,opens:qaLanguageOpens,errors:qaLanguageErrors}));
  assert.deepEqual(stream,{sources:1,opens:1,errors:0},'Language changes retain the sole original EventSource without a reconnect or error');
  observations.push({label,tag,native:actual,body,stream,title:await page.title()});
  const a=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();audits.push({name:label,violations:a.violations.map(v=>({id:v.id,targets:v.nodes.map(n=>n.target)}))});assert.deepEqual(audits.at(-1).violations,[]);
 }
 try{
  const mvid=JSON.parse(await c.fetchText('/health')).coreMvid;assert.equal((await native()).resource,'portuguese','This qualification starts in the original Portuguese QA environment');
  await poll(()=>page.evaluate(()=>qaLanguageOpens===1),'initial actual SSE open');
  assert.equal(await page.evaluate(()=>document.documentElement.lang),'pt');
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const file=page.getByRole('textbox',{name:'File name',exact:true});await poll(()=>file.count(),'owned QA file identity');
  const ownedSave=await file.inputValue();assert.match(ownedSave,/^FM27Access QA Dialog Regression /);await activate(button('Cancelar'));
  await preferences();original=await snapshot();assert.ok(original.tabs.includes('Rápido'),'Original native Quick preferences');
  checks.push('Fresh actual Portuguese shell and native QA identity agree before any language change');
  restorationRequired=true;
  await change('English','en','english');checks.push('Actual English confirmation updates the existing document and body metadata without reconnecting SSE');
  await preferences();await change('English (US)','en-US','english (us)');checks.push('Actual native US English resource retains its standard regional tag');
  await preferences();await change('Español latinoamericano','es-419','spanish (latam)');checks.push('Actual native Latin American Spanish resource retains its standard regional tag');
  await preferences();await change('Português','pt','portuguese');await preferences();assert.deepEqual(await snapshot(),original);
  await activate(button('Close'));await c.settled();restorationRequired=false;
  const index=await c.fetchText('/');assert.ok(index.startsWith('<!doctype html><html lang="pt">'),'Subsequent full shell also has the restored native language');
  checks.push('Original Portuguese, selected tab, all Quick values/check states and full-page language restored');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);
  fs.writeFileSync(out,JSON.stringify({success:true,mvid,ownedSave,checks,audits,observations,original,restorationRequired,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false},null,2));
  console.log('PASS: actual native document language, regional variants, one SSE and original preference inverse.');
 }catch(error){fs.writeFileSync(out,JSON.stringify({success:false,error:error.stack,checks,audits,observations,original,restorationRequired,errors:c.errors,requestFailures:c.requestFailures,networkFailures:c.network.failures},null,2));throw error;}
 finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
