// Produces one offline HTML file; translations are maintained separately from mod binaries.
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const resources = path.join(root, 'installer/FM27Access.Installer/Resources');
const ids = ['intro','requirements','install','manual','settings','reader','navigation','controls','shortcuts','career','gameplay','match','maintenance','report'];
const escape = value => value.replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;');
const inline = value => escape(value).replace(/`([^`]+)`/g,'<kbd>$1</kbd>');
function renderBody(value, table) {
  if(table){const rows=value.trim().split('\n').map(line=>line.split('|'));const [head,...body]=rows;
    return `<div class="scroll"><table><thead><tr>${head.map(h=>`<th scope="col">${inline(h)}</th>`).join('')}</tr></thead><tbody>${body.map(([key,...description])=>`<tr><th scope="row">${inline(key)}</th><td>${inline(description.join('|'))}</td></tr>`).join('')}</tbody></table></div>`;}
  return value.split(/\n\s*\n/).map(block=>{
    if(block.startsWith('### '))return `<h3>${escape(block.slice(4))}</h3>`;
    const lines=block.split('\n');
    if(lines.every(l=>/^- /.test(l)))return `<ul>${lines.map(l=>`<li>${inline(l.slice(2))}</li>`).join('')}</ul>`;
    if(lines.every(l=>/^\d+\. /.test(l)))return `<ol>${lines.map(l=>`<li>${inline(l.replace(/^\d+\. /,''))}</li>`).join('')}</ol>`;
    return `<p>${inline(block)}</p>`;
  }).join('\n');
}
const languages = fs.readdirSync(resources).filter(n=>n.endsWith('.json')).map(n=>{
  const code=n.slice(0,-5), ui=JSON.parse(fs.readFileSync(path.join(resources,n),'utf8').replace(/^\uFEFF/,''));
  const source=path.join(__dirname,'locales',n);
  const guide=JSON.parse(fs.readFileSync(source,'utf8').replace(/^\uFEFF/,''));
  if (Object.keys(guide).sort().join() !== [...ids].sort().join()) throw Error(`Incomplete guide: ${code}`);
  return {code,ui,guide};
}).sort((a,b)=>a.code==='en'?-1:b.code==='en'?1:a.code.localeCompare(b.code));
const sections = languages.map(({code,ui,guide})=>`<article data-language="${code}" lang="${code}"${code==='ar'?' dir="rtl"':''}${code==='en'?'':' hidden'}>
<h1>FM27 Access — ${escape(ui.guide)}</h1>
<nav aria-label="${escape(ui.guide)}"><ul>${ids.map(id=>`<li><a href="#${id}">${escape(guide[id].split('|')[0])}</a></li>`).join('')}</ul></nav>
${ids.map(id=>{const [title,...body]=guide[id].split('|');return `<section data-section="${id}"${code==='en'?` id="${id}"`:''}><h2 tabindex="-1">${escape(title)}</h2>${renderBody(body.join('|'),id==='shortcuts'||id==='match')}</section>`;}).join('\n')}
<p><a href="https://github.com/${'leoguimaoficial/Football-Manager-2027-Access'}/issues">GitHub Issues — FM27 Access</a></p></article>`).join('\n');
const html=`<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>FM27 Access — User guide</title>
<style>:root{color-scheme:light dark}*{box-sizing:border-box}body{font:1.1rem/1.6 system-ui,sans-serif;max-width:62rem;margin:auto;padding:1.5rem;color:CanvasText;background:Canvas}a{color:LinkText}select{font:inherit;max-width:100%}:focus-visible{outline:3px solid Highlight;outline-offset:3px}[hidden]{display:none!important}h1,h2{line-height:1.3}h2{margin-top:2.5rem;border-bottom:2px solid;padding-bottom:.35rem}section{scroll-margin-top:1rem}nav{border:1px solid;padding:1rem;border-radius:.5rem}nav ul{columns:2;column-gap:2rem;margin:0;padding-inline-start:1.2rem}nav li{break-inside:avoid;margin:.35rem 0}p{max-width:78ch}kbd{font-family:ui-monospace,monospace;border:1px solid;border-radius:.2rem;padding:.05rem .25rem}.scroll{overflow-x:auto}table{border-collapse:collapse;width:100%}th,td{text-align:start;vertical-align:top;border-bottom:1px solid;padding:.6rem}tbody th{white-space:nowrap}#language-control{margin-bottom:1.5rem}@media(max-width:32rem){nav ul{columns:1}}@media print{#language-control{display:none}}</style></head>
<body><div id="language-control"><label for="language">Language / Idioma</label> <select id="language">${languages.map(({code,ui})=>`<option value="${code}" lang="${code}">${escape(ui.languageName)}</option>`).join('')}</select></div><main>${sections}</main>
<script>
(()=>{'use strict';const select=document.getElementById('language'),articles=[...document.querySelectorAll('article[data-language]')];
const available=articles.map(a=>a.dataset.language), ui=${JSON.stringify(Object.fromEntries(languages.map(({code,ui})=>[code,{language:ui.language,guide:ui.guide}])))};
function resolve(value){if(!value)return null;if(available.includes(value))return value;const n=value.split('-')[0];if(n==='pt')return 'pt-PT';if(n==='zh')return 'zh';if(n==='no'||n==='nn')return 'nb';return available.includes(n)?n:null;}
function show(code){const hash=location.hash.slice(1);for(const article of articles){article.hidden=article.dataset.language!==code;for(const section of article.querySelectorAll('[data-section]')){if(article.hidden)section.removeAttribute('id');else section.id=section.dataset.section;}}document.documentElement.lang=code;document.documentElement.dir=code==='ar'?'rtl':'ltr';document.title='FM27 Access — '+ui[code].guide;document.querySelector('label[for=language]').textContent=ui[code].language;select.value=code;if(hash)document.getElementById(hash)?.scrollIntoView();}
let stored;try{stored=localStorage.getItem('fm27-guide-language');}catch{}const requested=new URLSearchParams(location.search).get('lang');show(resolve(requested)||resolve(stored)||(navigator.languages||[navigator.language]).map(resolve).find(Boolean)||'en');
select.addEventListener('change',()=>{show(select.value);try{localStorage.setItem('fm27-guide-language',select.value);}catch{}});
for(const a of document.querySelectorAll('nav a'))a.addEventListener('click',()=>{setTimeout(()=>document.getElementById(a.hash.slice(1))?.querySelector('h2')?.focus(),0);});
})();
</script></body></html>\n`;
fs.writeFileSync(path.join(__dirname,'FM27Access-UserGuide.html'),html);
console.log(`Built offline guide: ${languages.length} languages, ${ids.length} sections each.`);
