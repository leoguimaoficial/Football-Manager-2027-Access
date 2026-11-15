// Produces one offline HTML file; translations are maintained separately from mod binaries.
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const resources = path.join(root, 'installer/FM27Access.Installer/Resources');
const ids = ['intro','requirements','install','manual','settings','reader','navigation','controls','shortcuts','career','gameplay','match','maintenance','report'];
const escape = value => value.replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;');
const inline = value => escape(value)
  .replace(/\[([^\]\n]+)\]\((https:\/\/[^\s()]+)\)/g,'<a href="$2">$1</a>')
  .replace(/`([^`]+)`/g,'<kbd>$1</kbd>');
const chrome = JSON.parse(fs.readFileSync(path.join(__dirname,'chrome.json'),'utf8'));
function renderBody(value, table) {
  if(table){const rows=value.trim().split('\n').map(line=>line.split('|'));const [head,...body]=rows;
    return `<div class="scroll" tabindex="0" role="region" aria-label="${escape(table)}"><table><caption>${escape(table)}</caption><thead><tr>${head.map(h=>`<th scope="col">${inline(h)}</th>`).join('')}</tr></thead><tbody>${body.map(([key,...description])=>`<tr><th scope="row">${inline(key)}</th><td>${inline(description.join('|'))}</td></tr>`).join('')}</tbody></table></div>`;}
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
  if (!chrome[code] || chrome[code].length !== 3) throw Error(`Incomplete guide navigation: ${code}`);
  return {code,ui,guide};
}).sort((a,b)=>a.code==='en'?-1:b.code==='en'?1:a.code.localeCompare(b.code));
const sections = languages.map(({code,ui,guide})=>`<article data-language="${code}" lang="${code}"${code==='ar'?' dir="rtl"':''}${code==='en'?'':' hidden'}>
<header class="hero"><p class="eyebrow">FOOTBALL MANAGER · ACCESS</p><h1>FM27 Access — ${escape(ui.guide)}</h1><p class="edition">${escape(guide.intro.split('|')[0])}</p></header>
<nav class="contents" aria-label="${escape(chrome[code][1])}"><p class="contents-title">${escape(chrome[code][1])}</p><ol>${ids.map(id=>`<li><a href="#${id}">${escape(guide[id].split('|')[0])}</a></li>`).join('')}</ol></nav>
${ids.map(id=>{const [title,...body]=guide[id].split('|');return `<section data-section="${id}"${code==='en'?` id="${id}"`:''}><h2 tabindex="-1">${escape(title)}</h2>${renderBody(body.join('|'),id==='shortcuts'||id==='match'?title:null)}</section>`;}).join('\n')}
<footer><a href="#document">${escape(chrome[code][2])}</a><a href="https://github.com/${'leoguimaoficial/Football-Manager-2027-Access'}/issues">GitHub Issues — FM27 Access</a></footer></article>`).join('\n');
const html=`<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>FM27 Access — User guide</title>
<style>
:root{color-scheme:light dark;--bg:#eef3f1;--paper:#fff;--ink:#182c28;--muted:#4e635d;--line:#bdcec7;--accent:#176348;--soft:#e6f2eb;--hero:#123a30;--hero-ink:#f2fff7;--link:#075a46;--focus:#9c3d00}
@media(prefers-color-scheme:dark){:root{--bg:#101b19;--paper:#182723;--ink:#edf5f0;--muted:#b9cec3;--line:#4b685c;--accent:#8de5b0;--soft:#233c31;--hero:#1b3e31;--link:#9debc3;--focus:#ffcc80}}
*{box-sizing:border-box}html{scroll-behavior:auto}body{font:1.075rem/1.75 system-ui,"Segoe UI",sans-serif;margin:0;color:var(--ink);background:var(--bg);overflow-wrap:anywhere}a{color:var(--link);text-underline-offset:.18em;text-decoration-thickness:.08em;overflow-wrap:anywhere}a:hover{text-decoration-thickness:.15em}select{font:inherit;max-width:100%;min-height:2.75rem;padding:.3rem .7rem;color:var(--ink);background:var(--paper);border:1px solid var(--line);border-radius:.4rem}:focus-visible{outline:3px solid var(--focus);outline-offset:4px}[hidden]{display:none!important}main{max-width:68rem;margin:0 auto;padding:0 1.5rem 4rem}h1,h2,h3{line-height:1.3;text-wrap:balance}h1{font-size:clamp(1.9rem,4vw,3rem);margin:.4rem 0 1rem;max-width:24ch}h2{font-size:1.6rem;color:var(--accent);margin:0 0 1.4rem;padding-bottom:.6rem;border-bottom:2px solid var(--line)}h3{font-size:1.2rem;margin:2rem 0 .7rem}p,li{max-width:76ch}p{margin:1rem 0}li{padding-inline-start:.2rem;margin:.55rem 0}section{background:var(--paper);border:1px solid var(--line);border-radius:.8rem;margin:1.5rem 0;padding:clamp(1.15rem,4vw,2.5rem);scroll-margin-top:1.5rem;overflow-wrap:anywhere}section>p:first-of-type{font-size:1.1em}section ul,section ol{padding-inline-start:1.7rem}.hero{background:var(--hero);color:var(--hero-ink);border-radius:1rem;padding:clamp(1.4rem,5vw,3rem);border-top:5px solid #8de5b0;margin:.5rem 0 1.5rem}.eyebrow{letter-spacing:.12em;font-size:.8rem;font-weight:700;margin:0}.edition{margin:0;font-size:1.1rem}.contents{background:var(--paper);border:1px solid var(--line);border-radius:.8rem;padding:1.4rem 1.7rem}.contents-title{font-weight:700;font-size:1.2rem;margin:0 0 .8rem}.contents ol{columns:2;column-gap:3rem;margin:0;padding-inline-start:1.7rem}.contents li{break-inside:avoid;padding-inline-start:.3rem;margin:0 0 .35rem}.contents a{display:inline-block;padding:.35rem 0}.contents li::marker{color:var(--accent);font-weight:700}kbd{font: .92em ui-monospace,Consolas,monospace;background:var(--soft);border:1px solid var(--line);border-bottom-width:2px;border-radius:.3rem;padding:.08rem .3rem;overflow-wrap:anywhere}.scroll{max-width:100%;overflow-x:auto;margin:1rem 0}table{border-collapse:collapse;width:100%;font-size:.98rem}caption{text-align:start;font-weight:600;margin-bottom:.7rem;color:var(--muted)}th,td{text-align:start;vertical-align:top;border-bottom:1px solid var(--line);padding:.8rem}thead th{background:var(--soft)}tbody tr:nth-child(even){background:var(--soft)}tbody th{min-width:8rem}#language-control{max-width:68rem;margin:auto;padding:1.5rem;display:flex;align-items:center;justify-content:flex-end;gap:.8rem;flex-wrap:wrap}#language-control label{font-weight:600}.skip{position:absolute;inset-inline-start:1rem;top:-10rem;background:var(--paper);color:var(--ink);padding:.7rem 1rem;border:2px solid var(--accent);z-index:2}.skip:focus{top:1rem}footer{padding:1rem .3rem;display:flex;justify-content:space-between;gap:1.5rem;flex-wrap:wrap;border-top:1px solid var(--line);font-size:.95rem}
@media(max-width:38rem){main{padding-inline:.8rem}.contents ol{columns:1}h2{font-size:1.4rem}th,td{padding:.6rem}#language-control{justify-content:flex-start;padding:1rem}}
@media(forced-colors:active){body,section,.contents,.hero,select{background:Canvas;color:CanvasText;border-color:CanvasText}a{color:LinkText}h2,.contents li::marker{color:CanvasText}:focus-visible{outline-color:Highlight}kbd,thead th,tbody tr:nth-child(even){background:Canvas;color:CanvasText;border-color:CanvasText}}
@media print{body,section,.hero,.contents{background:white;color:black;border-color:#888}#language-control,.skip,footer{display:none}main{max-width:none;padding:0}section{padding:1rem 0;border:0;border-radius:0}h2{color:black;break-after:avoid}h3{break-after:avoid}.contents ol{columns:2}a{color:black}.scroll{overflow:visible}tr{break-inside:avoid}}
</style></head>
<body><a class="skip" href="#document">${escape(chrome.en[0])}</a><div id="language-control"><label for="language">Language / Idioma</label> <select id="language">${languages.map(({code,ui})=>`<option value="${code}" lang="${code}">${escape(ui.languageName)}</option>`).join('')}</select></div><main id="document" tabindex="-1">${sections}</main>
<script>
(()=>{'use strict';const select=document.getElementById('language'),articles=[...document.querySelectorAll('article[data-language]')];
const available=articles.map(a=>a.dataset.language), ui=${JSON.stringify(Object.fromEntries(languages.map(({code,ui})=>[code,{language:ui.language,guide:ui.guide,skip:chrome[code][0]}])))};
function resolve(value){if(!value)return null;if(available.includes(value))return value;const n=value.split('-')[0];if(n==='pt')return 'pt-PT';if(n==='zh')return 'zh';if(n==='no'||n==='nn')return 'nb';return available.includes(n)?n:null;}
function show(code){const hash=location.hash.slice(1);for(const article of articles){article.hidden=article.dataset.language!==code;for(const section of article.querySelectorAll('[data-section]')){if(article.hidden)section.removeAttribute('id');else section.id=section.dataset.section;}}document.documentElement.lang=code;document.documentElement.dir=code==='ar'?'rtl':'ltr';document.title='FM27 Access — '+ui[code].guide;document.querySelector('label[for=language]').textContent=ui[code].language;document.querySelector('.skip').textContent=ui[code].skip;select.value=code;if(hash)document.getElementById(hash)?.scrollIntoView();}
let stored;try{stored=localStorage.getItem('fm27-guide-language');}catch{}const requested=new URLSearchParams(location.search).get('lang');show(resolve(requested)||resolve(stored)||(navigator.languages||[navigator.language]).map(resolve).find(Boolean)||'en');
select.addEventListener('change',()=>{show(select.value);try{localStorage.setItem('fm27-guide-language',select.value);}catch{}});
for(const a of document.querySelectorAll('nav a'))a.addEventListener('click',()=>{setTimeout(()=>document.getElementById(a.hash.slice(1))?.querySelector('h2')?.focus(),0);});
for(const a of document.querySelectorAll('a[href="#document"]'))a.addEventListener('click',()=>{document.getElementById('document').focus();});
})();
</script></body></html>\n`;
fs.writeFileSync(path.join(__dirname,'FM27Access-UserGuide.html'),html);
console.log(`Built offline guide: ${languages.length} languages, ${ids.length} sections each.`);
