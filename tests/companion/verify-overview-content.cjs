// Checks saved DOM reported by the sole production Companion against its own
// native text census. Offline parsing only; no second live game UI client.
// Usage: node tests/companion/verify-overview-content.cjs <artifact prefix>
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
(async()=>{
 const prefix=process.argv[2];assert.ok(prefix,'Supply the actual capture prefix');
 const results=[],browser=await chromium.launch({channel:'msedge',headless:true});
 try{
  const page=await browser.newPage();await page.route('**/*',r=>r.abort());
  const captures=JSON.parse(fs.readFileSync(prefix+'-inventory.json','utf8'));
  assert.equal(captures.length,6,'All six main menu overviews captured');
  for(const capture of captures){
   const key=capture.name.replace(/[^a-z0-9]+/g,'-');
   const html=fs.readFileSync(prefix+'-'+key+'.html','utf8');
   await page.setContent('<!doctype html><html lang="pt-PT"><head><title>Captured native overview</title></head><body><main>'+html+'</main></body></html>');
   assert.equal(await page.locator('h1 button,h2 button,h3 button,h4 button,h5 button,h6 button').count(),0,'Informational headings only');
   const duplicates=await page.locator('[id]').evaluateAll(xs=>xs.map(x=>x.id).filter((x,i,all)=>all.indexOf(x)!==i));assert.deepEqual(duplicates,[],'Distinct title/action ids');
   assert.doesNotMatch(await page.locator('main').innerText(),/unlabeled icon button|unknown control|(?:CompIcon|Icon), image/);
   const native=fs.readFileSync(prefix+'-'+key+'-text.txt','utf8').split(/\r?\n/).filter(l=>l.startsWith('TEXT|')).map(l=>{
    const p=l.split('|');return {name:p[2],text:Buffer.from(p[3],'base64').toString().replace(/<[^>]*>/g,''),path:p[4]};
   });
   const titles=native.filter(t=>t.path.includes('/title-link-button/'));
   for(const t of titles){assert.equal(await page.getByRole('heading',{name:t.text,exact:true,level:3}).count(),1,'Native title '+t.text);assert.ok(await page.getByRole('button',{name:t.text,exact:true}).count(),'Preserved title command '+t.text);}
   if(capture.name==='plantel'){
    assert.equal(await page.getByRole('heading',{name:'Equipa Principal',level:3,exact:true}).count(),1);
    assert.equal(await page.getByRole('button',{name:'DINÂMICA, Alt I for more info',exact:true}).count(),1);
    assert.equal(await page.getByRole('heading',{name:'DINÂMICA',exact:true}).count(),0);
    assert.doesNotMatch(await page.locator('main').innerText(),/Muito bom/);
    assert.equal(await page.locator('tbody tr').count(),32,'Complete native senior roster');
   }
   if(capture.name==='treino'){
    const calendar=page.locator('table').first();
    for(const [day,team] of [['11','AFS'],['18','LSC'],['25','CLU'],['29','GALA'],['1 Ago','SCUT']]){
     const cell=calendar.locator('td').filter({has:page.locator('div').filter({hasText:new RegExp('^'+day+'$')})});
     assert.equal(await cell.getByRole('button',{name:team,exact:true}).count(),1,'Fixture opponent in actual day '+day);
    }
    assert.ok((await calendar.innerText()).includes('Treino físico ligeiro'),'Late session content recovered');
    const agenda=html.slice(html.indexOf('>AGENDA</h3>'));
    const windowed=agenda.includes('>Show more items</button>');
    for(const caption of native.filter(t=>t.path.includes('/FixtureSchedule/')&&t.path.includes('/ClubLink/')))
     assert.ok(agenda.includes('>'+caption.text+'</button>')||windowed,'Native agenda opponent or explicit original paging '+caption.text);
    assert.equal(await page.getByRole('combobox',{name:'Julho de 2026',exact:true}).count(),1,'Month caption remains the accessible dropdown name');
    assert.ok(!html.includes('<!--fm-provisional-->'),'Final calendar completeness receipt');
   }
   results.push({name:capture.name,nativeTitles:titles.map(t=>t.text),passed:true});
  }
 }finally{await browser.close();}
 fs.writeFileSync(prefix+'-checks.json',JSON.stringify({at:new Date().toISOString(),scope:'Saved actual Companion DOM/native census; no keyboard/manual NVDA claim',results},null,2));
 console.log(JSON.stringify({passed:results.length,overviews:results.map(x=>x.name)}));
})().catch(e=>{console.error(e.message);process.exitCode=1});
