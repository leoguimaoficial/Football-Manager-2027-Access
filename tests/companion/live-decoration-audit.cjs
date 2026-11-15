// Read-only presentation audit of actual native menu destinations in the sole
// Companion. Equal captions are investigation leads, never a deduplication rule.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const {chromium}=require('playwright');
const endpoint=process.env.FM27_COMPANION_CDP;assert.ok(endpoint);assert.equal(new URL(endpoint).hostname,'127.0.0.1');
const out=path.resolve(__dirname,'../../artifacts/duplicate-audit');fs.mkdirSync(out,{recursive:true});
const opt=name=>process.argv.find(a=>a.startsWith('--'+name+'='))?.slice(name.length+3);
const start=Number(opt('start')||0),count=Number(opt('count')||1000),reportFile=path.join(out,'routes-'+start+'.json');
(async()=>{
 const browser=await chromium.connectOverCDP(endpoint),pages=browser.contexts().flatMap(c=>c.pages()).filter(p=>p.url().startsWith('http://127.0.0.1:8787/'));assert.equal(pages.length,1);const page=pages[0];
 const report={at:new Date().toISOString(),success:false,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false,captures:[],errors:[],observations:[]};page.on('pageerror',e=>report.errors.push(String(e)));
 const read=p=>page.evaluate(async p=>{const r=await fetch(p);if(!r.ok)throw Error('HTTP '+r.status);return r.text();},p);
 async function idle(){await page.waitForFunction(()=>!FM.core.refreshing&&!FM.core.pending,null,{timeout:30000});let last='',hits=0;for(let i=0;i<80;i++){const html=await page.locator('#fmroot').innerHTML();if(html===last)hits++;else{last=html;hits=0;}if(hits>=3&&await page.evaluate(()=>!FM.core.refreshing&&!FM.core.pending))return;await new Promise(r=>setTimeout(r,150));}throw Error('Native body did not settle');}
 function write(){fs.writeFileSync(reportFile,JSON.stringify(report,null,2));}
 async function capture(destination){
  // Classification may overlap a late native/body commit. Capture the actual
  // settled DOM afterwards; never attach a newer token to an older snapshot.
  const nativeAudit=await read('/audit');await idle();
  const snapshot=await page.locator('#fmroot').evaluate(root=>{
   const visible=e=>e.getClientRects().length>0&&!e.closest('[hidden],[aria-hidden=true]');
   const caption=e=>(e.getAttribute('aria-label')||e.textContent||'').trim().replace(/\s+/g,' ');
   const controls=[...root.querySelectorAll('button,a[href]')].filter(visible).map(e=>({id:e.id,caption:caption(e),menu:e.getAttribute('aria-haspopup'),disabled:e.disabled}));
   const adjacent=[];
   for(const p of root.querySelectorAll('p')){
    if(!visible(p)||!p.querySelector('button,a[href]'))continue;
    const next=p.nextElementSibling;
    if(next&&next.matches('p')&&visible(next)&&caption(p)&&caption(p)===caption(next))adjacent.push({caption:caption(p),first:p.outerHTML,second:next.outerHTML});
   }
   const cells=[];
   for(const cell of root.querySelectorAll('td,th')){
    const children=[...cell.querySelectorAll('button,a[href]')].filter(visible);
    const duplicates=children.filter((b,i)=>i&&caption(b)&&caption(b)===caption(children[i-1]));
    if(duplicates.length)cells.push({text:caption(cell),html:cell.outerHTML});
   }
   const media=[...root.querySelectorAll('img,[role=img],p')].filter(e=>visible(e)&&(e.matches('img,[role=img]')||/,(?:\s*)(?:image|imagem)\s*$/.test(e.textContent))).map(e=>({id:e.id,tag:e.tagName,caption:e.getAttribute('alt')||caption(e)}));
   return{title:document.title,token:FMTOKEN,controls,adjacent,cells,media,headings:[...root.querySelectorAll('h1,h2,h3,h4,h5,h6')].filter(visible).map(e=>e.textContent),tables:[...root.querySelectorAll('table')].map(t=>({headers:[...t.querySelectorAll('thead th')].map(x=>x.textContent),rows:t.querySelectorAll('tbody tr').length})),focus:document.activeElement.id};
  });
  return{destination,...snapshot,nativeAudit};
 }
 try{
  report.health=JSON.parse(await read('/health'));assert.equal(report.health.developerTools,true);await idle();
  const entries=await page.locator('#fmnav-tree [role=treeitem]').evaluateAll(bs=>bs.filter(b=>!b.hasAttribute('aria-expanded')).map(b=>({uid:b.dataset.uid,obj:b.dataset.obj??null,label:b.querySelector(':scope>span')?.textContent,path:b.dataset.filter,disabled:b.getAttribute('aria-disabled')==='true'})).filter((v,i,all)=>all.findIndex(x=>x.uid===v.uid&&x.obj===v.obj)===i));
  assert.ok(entries.length>0,'A busy/empty replacement is not an audited native menu');report.destinations=entries;write();
  for(const [offset,destination] of entries.slice(start,start+count).entries()){
   if(fs.existsSync(path.join(out,'STOP'))){report.observations.push('Cooperative investigation boundary after completed navigation');write();return;}
   report.current={index:start+offset,...destination};write();
   if(destination.disabled){report.captures.push({destination,notOpened:'Native disabled destination'});write();continue;}
   if(!await page.locator('#fmnav-tree').count()){await page.keyboard.press('Escape');await page.waitForSelector('#fmnav-tree',{timeout:20000});await idle();}
   const selector='#fmnav-tree [role=treeitem][data-uid="'+destination.uid+'"]'+(destination.obj===null?':not([data-obj])':'[data-obj="'+destination.obj+'"]');
   const entry=page.locator(selector).first();
   await page.getByRole('textbox',{name:'Search all screens',exact:true}).fill(await entry.locator(':scope>span').textContent());await entry.focus();
   const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');await page.keyboard.press('Enter');
   const reply=await(await response).text();assert.match(reply,/^Opening /);await idle();
   if(await page.getByRole('textbox',{name:'Search all screens',exact:true}).count()){await page.getByRole('textbox',{name:'Search all screens',exact:true}).fill('');await idle();}
   const current=await capture(destination);report.captures.push(current);write();
   console.log(JSON.stringify({index:start+offset,uid:destination.uid,obj:destination.obj,title:current.title,adjacent:current.adjacent.length,cells:current.cells.length,media:current.media.length}));
  }
  report.current=null;assert.deepEqual(report.errors,[]);report.success=true;write();
 }catch(error){report.failure=error.stack;report.lastState=await page.evaluate(()=>({title:document.title,token:FMTOKEN,body:document.getElementById('fmroot').innerText.slice(0,4000)})).catch(()=>null);write();throw error;}
 finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
