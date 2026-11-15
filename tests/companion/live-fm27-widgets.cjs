const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const{default:AxeBuilder}=require('@axe-core/playwright');
(async()=>{const c=await require('./original-client.cjs')(),{page,poll,activate,settled,fetchText}=c;const checks=[];let tables;
 async function close(){const native=page.locator('button[data-dialog-close][id]'),x=page.getByRole('button',{name:'Close',exact:true}),cancel=page.getByRole('button',{name:'Cancelar',exact:true});if(await native.count())await activate(native.first());else if(await x.count())await activate(x.first());else if(await cancel.count())await activate(cancel.first());else{const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/history');await page.getByRole('button',{name:'Back',exact:true}).focus();await page.keyboard.press('Enter');const r=await response;assert.equal(r.status(),200);assert.ok(!/not available/i.test(await r.text()));await settled();}}
 try{
  await page.route('**/speak?*',route=>route.fulfill({status:200,body:''}));
  assert.equal(await page.title(),'MainMenu');
  if(!process.argv.includes('--biography-only')){
  for(const label of['Quais são as novidades?','FMFC']){
   console.log('opening',label);const control=page.getByRole('button',{name:label,exact:true}),origin=await control.getAttribute('id');await activate(control);await poll(async()=>await page.title()!=='MainMenu','native card');await settled();console.log('opened',await page.title());assert.equal(await page.getByRole('button',{name:'Preferências',exact:true}).count(),0);
   if(label==='Quais são as novidades?'){
    assert.equal(await page.title(),'O que há de novo no FM27?');
    assert.ok(!(await page.locator('#fmroot').innerText()).includes('TintGradient'));
    assert.equal(await page.locator('#fmroot h3').count(),4);
    checks.push('News retains its native title and four section headings without decorative TintGradient images');
   }
   if(label==='FMFC'){
    assert.equal(await page.title(),'Junte-se à Equipa');
    assert.equal(await page.getByRole('heading',{name:'Junte-se à Equipa',exact:true}).count(),1);
    assert.doesNotMatch(await page.locator('#fmroot').innerText(),/FMFCJoinSquadCard/);
   }
   await close();await poll(async()=>await page.title()==='MainMenu','main title restored');assert.equal(await page.evaluate(()=>document.activeElement?.id),origin);checks.push(label+': native card opens, closes and returns to origin');
  }
  await activate(page.getByRole('button',{name:'Preferências',exact:true}));await poll(()=>page.getByRole('tab',{name:'Detalhado',exact:true}).count(),'preferences');
  await activate(page.getByRole('tab',{name:'Detalhado',exact:true}));await poll(()=>page.getByRole('tab',{name:'Ecrã',exact:true}).count(),'categories');await activate(page.getByRole('tab',{name:'Ecrã',exact:true}));await poll(()=>page.getByRole('button',{name:'Editar cores',exact:true}).count(),'edit colours');
  const oldColourMode=await page.getByRole('combobox',{name:'Cores Personalizadas',exact:true}).locator('option:checked').textContent();
  await c.dropdown('Cores Personalizadas');await c.choose('Personalizado');await poll(()=>page.getByRole('button',{name:'Editar cores',exact:true}).isEnabled(),'native custom-colour dependency');
  await activate(page.getByRole('button',{name:'Editar cores',exact:true}));
  await poll(async()=>await page.locator('table').count()===3,'three semantic colour tables');console.log('tables ready');
  tables=await page.locator('table').evaluateAll(ts=>ts.map(t=>({columns:t.querySelectorAll('thead th').length,rows:t.querySelectorAll('tbody tr').length,rowHeaders:t.querySelectorAll('tbody th[scope=row]').length})));
  assert.deepEqual(tables.map(t=>t.columns),[5,2,2]);assert.ok(tables.every(t=>t.rows>0&&t.rows===t.rowHeaders));assert.equal(await page.getByRole('button',{name:'unlabeled icon button',exact:true}).count(),0);
  const row=page.locator('table').first().locator('tbody tr').first();await activate(row.getByRole('button').first());await poll(()=>page.getByRole('radio').count(),'native palette');await settled();
  const current=page.locator('#fmroot p').filter({hasText:/^Selected colour: #[0-9A-F]{8}$/});assert.equal(await current.count(),1);const before=await current.textContent();
  const labels=await page.getByRole('radio').evaluateAll(rs=>rs.map(r=>r.labels?.[0]?.textContent?.trim()||r.getAttribute('aria-label')));assert.ok(labels.length>0);
  const target=labels.find(label=>label&&!before.includes(label));assert.ok(target);const targetRadio=page.getByRole('radio',{name:target,exact:true});await activate(targetRadio,'Space');await poll(async()=>(await current.textContent()).includes(target),'native colour read-back');
  checks.push('FM27 colour form consolidated with original cell renderer: 3 tables, column/row headers and real controls');checks.push('Native palette selection uses original radio-group HTML; selected colour updates through LiveValues');
  await close();await poll(async()=>await page.locator('table').count()===3,'colour preferences after cancel');await close();await poll(()=>page.getByRole('tab',{name:'Ecrã',exact:true}).count(),'preferences after colour close');
  await c.dropdown('Cores Personalizadas');await c.choose(oldColourMode);await poll(async()=>await page.getByRole('combobox',{name:'Cores Personalizadas',exact:true}).locator('option:checked').textContent()===oldColourMode,'native colour preset restored');
  await close();await poll(async()=>await page.title()==='MainMenu','returned main menu');
  }
  await activate(page.getByRole('button',{name:'Perfis de Treinador',exact:true}));
  const owned=page.getByRole('radio',{name:/^QA Portal, Access,/});
  await poll(()=>owned.count(),'owned QA profile; never edit a user profile');
  const profileIndex=await page.getByRole('radio').evaluateAll(rs=>rs.findIndex(r=>r.labels?.[0]?.textContent?.trim().startsWith('QA Portal, Access,')));
  assert.ok(profileIndex>=0);await activate(page.getByRole('button',{name:'Edit',exact:true}).nth(profileIndex));
  await poll(()=>page.getByRole('button',{name:'DETALHES BIOGRÁFICOS',exact:true}).count(),'native profile edit areas');
  await activate(page.getByRole('button',{name:'DETALHES BIOGRÁFICOS',exact:true}));
  await poll(()=>page.getByRole('tab',{name:'Nacionalidades',exact:true}).count(),'biography tabs');
  await activate(page.getByRole('tab',{name:'Nacionalidades',exact:true}));
  const place=page.getByRole('combobox',{name:'Local de Nascimento',exact:true});
  await poll(()=>place.count(),'native search input');
  // This dedicated QA profile was saved with Porto Alegre. On a cold profile
  // load its native text can arrive after the initial form structure, so an
  // early empty browser property is not its committed saved value.
  await poll(async()=>await place.inputValue()==='Porto Alegre (RS)','saved QA birthplace bound');
  const savedPlace=await place.inputValue();
  assert.equal(await page.getByText('Edição da biografia',{exact:true}).count(),1);
  assert.doesNotMatch(await page.locator('#fmroot').innerText(),/Appearance editing not available/);
  const browse=await c.dropdown('Browse Local de Nascimento'),browseId=await browse.getAttribute('id');
  async function expand(select,label){
   assert.ok((await select.locator('option').allTextContents()).includes(label),'Missing native submenu '+label+': '+JSON.stringify(await select.locator('option').allTextContents()));
   await select.selectOption({label});const opened=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/expand');
   await page.keyboard.press('Enter');assert.match(await(await opened).text(),/^<option/);
   return page.locator('select[data-chain]').last();
  }
  const letters=await expand(browse,'Brasil, submenu'),cities=await expand(letters,'P, submenu');
  assert.ok((await cities.locator('option').allTextContents()).includes('Porto Alegre (RS)'));
  assert.equal(await browse.getAttribute('data-loaded'),'1');
  await c.cancelDropdown();assert.equal(await place.inputValue(),savedPlace);assert.equal(await page.evaluate(()=>document.activeElement.id),browseId);
  await c.dropdown('Browse Local de Nascimento');
  const more=await browse.locator('option[data-more]').count();assert.equal(more,1);
  await browse.selectOption({index:(await browse.locator('option').count())-1});
  const continuation=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/more');await page.keyboard.press('Enter');assert.match(await(await continuation).text(),/^<option/);
  await poll(async()=>!await browse.locator('option[data-more]').count(),'all native countries');assert.equal(await browse.locator('option').count(),228);
  const lastLetters=await expand(browse,'Zimbabué, submenu'),lastCities=await expand(lastLetters,'H, submenu');
  assert.deepEqual(await lastCities.locator('option').allTextContents(),['Harare','Hwange']);
  const back=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/collapse');await page.keyboard.press('ArrowLeft');assert.equal((await back).status(),200);
  await poll(async()=>await page.locator('select[data-chain]').count()===1,'one submenu level back');
  const committedCities=await expand(page.locator('select[data-chain]').last(),'H, submenu');
  async function commitLeaf(select,label){
   await select.selectOption({label});const committed=page.waitForResponse(r=>new URL(r.url()).pathname==='/dropdown/select');
   await page.keyboard.press('Enter');const response=await committed;assert.equal(response.status(),200);assert.doesNotMatch(await response.text(),/unavailable|not found|failed/i);
   await poll(async()=>!await page.locator('select[data-chain]').count(),'native leaf selection closes');
   await poll(async()=>await place.inputValue()===label,'native leaf read-back '+label);await settled();
  }
  await commitLeaf(committedCities,'Harare');
  const restoreRoot=await c.dropdown('Browse Local de Nascimento');
  const restoreLetters=await expand(restoreRoot,'Brasil, submenu');
  await commitLeaf(await expand(restoreLetters,'P, submenu'),savedPlace);
  await c.dropdown('Browse Local de Nascimento');
  await c.cancelDropdown();assert.equal(await place.inputValue(),savedPlace);assert.equal(await page.evaluate(()=>document.activeElement.id),browseId);
  const biographyAudit=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();assert.deepEqual(biographyAudit.violations,[]);
  checks.push('Native searchable full-list chevrons reuse the original hierarchical select: Brazil/P exposes Porto Alegre, without a false screen departure');
  checks.push('Full-list pagination reaches all 228 countries; Zimbabue/H exposes Harare/Hwange, Left returns a level, native Harare commit and Brazil/Porto Alegre restoration update the bound field; Escape preserves the saved birthplace and origin focus, without saving the QA profile');
  await activate(page.getByRole('button',{name:'Cancelar',exact:true}));await poll(()=>page.getByRole('button',{name:'Menu Principal',exact:true}).count(),'profile selection restored');
  await activate(page.getByRole('button',{name:'Menu Principal',exact:true}));await poll(async()=>await page.title()==='MainMenu','main menu after cancelling biography');
  assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);const output={success:true,checks,tables,audits:[{name:'QA biography native full-list controls',violations:[]}],errors:c.errors,requestFailures:c.requestFailures,manualNVDA:false,privacyAndAuthenticationSubmitted:false,colourConfirmed:false,profileSaved:false};fs.writeFileSync(path.resolve(__dirname,'../../artifacts/tests/web/live-fm27-widgets.json'),JSON.stringify(output,null,2));console.log(JSON.stringify(output));
 }finally{await c.browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
