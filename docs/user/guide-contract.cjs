const crypto = require('node:crypto');
const digest = value => crypto.createHash('sha256').update(JSON.stringify(value)).digest('hex');

// Stable content units preserve one source paragraph, heading, list item or
// table cell per translation, instead of accepting merely the same section keys.
function units(section, table = false) {
  const [title, ...rest] = section.split('|');
  const body = rest.join('|'), result = [{prefix:'',text:title,suffix:'|',kind:'title'}];
  if(table) {
    const rows=body.split('\n');
    rows.forEach((row,index)=>row.split('|').forEach((cell,column)=>result.push({
      prefix:'',text:cell,suffix:column===0?'|':index===rows.length-1?'':'\n',
      kind:index===0?'column':column===0?'key':'cell',literal:index>0&&column===0
    })));
  } else {
    const blocks=body.split(/\n\s*\n/);
    blocks.forEach((block,index)=>{
      const lines=block.split('\n');
      const heading=block.startsWith('### '),list=lines.every(line=>/^(?:- |\d+\. )/.test(line));
      (list?lines:[block]).forEach((line,lineIndex)=>{
        const prefix=heading?'### ':list?line.match(/^(?:- |\d+\. )/)[0]:'';
        result.push({prefix,text:line.slice(prefix.length),suffix:lineIndex<(list?lines.length:1)-1?'\n':index<blocks.length-1?'\n\n':'',kind:heading?'heading':list?prefix.startsWith('-')?'bullet':'step':'paragraph'});
      });
    });
  }
  return result;
}
function validateLocale(source, target, code) {
  if(Object.keys(source).sort().join()!==Object.keys(target).sort().join())throw Error(`${code}: section inventory differs`);
  for(const id of Object.keys(source)) {
    const a=units(source[id],id==='shortcuts'||id==='match'),b=units(target[id],id==='shortcuts'||id==='match');
    if(a.length!==b.length)throw Error(`${code}/${id}: ${b.length} content units; expected ${a.length}`);
    for(let i=0;i<a.length;i++) {
      if(a[i].kind!==b[i].kind||a[i].prefix!==b[i].prefix||!b[i].text.trim())throw Error(`${code}/${id}/${i}: structure or text missing`);
      if(a[i].literal&&a[i].text!==b[i].text)throw Error(`${code}/${id}/${i}: keyboard shortcut changed`);
      const links=text=>[...text.matchAll(/\]\((https:\/\/[^\s()]+)\)/g)].map(m=>m[1]).sort().join('|');
      if(links(a[i].text)!==links(b[i].text))throw Error(`${code}/${id}/${i}: source link changed`);
      const keys=text=>[...text.matchAll(/`([^`]+)`/g)].map(m=>m[1]).sort().join('|');
      if(keys(a[i].text)!==keys(b[i].text))throw Error(`${code}/${id}/${i}: inline keyboard notation changed`);
    }
  }
}
function validateCatalog(languages,metadata) {
  const source=languages.find(l=>l.code==='en')?.guide;
  if(!source||metadata.schema!==1||metadata.sourceHash!==digest(source))throw Error('Guide translations need the current canonical source');
  if(Object.keys(metadata.locales).sort().join()!==languages.map(l=>l.code).sort().join())throw Error('Guide translation inventory differs');
  for(const {code,guide} of languages) {
    validateLocale(source,guide,code);
    const stamp=metadata.locales[code];
    if(stamp.sourceHash!==metadata.sourceHash||stamp.translationHash!==digest(guide))throw Error(`${code}: translation/source revision is stale`);
  }
}
module.exports={digest,units,validateLocale,validateCatalog};
