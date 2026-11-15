const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..'),{validateLocale,validateCatalog,units}=require('../../docs/user/guide-contract.cjs');
const read=name=>JSON.parse(fs.readFileSync(path.join(root,'docs/user',name),'utf8'));
test('All twenty complete manuals track the same canonical content and revision',()=>{
  const languages=fs.readdirSync(path.join(root,'docs/user/locales')).map(name=>({code:name.replace('.json',''),guide:read('locales/'+name)}));
  assert.equal(languages.length,20);validateCatalog(languages,read('translations.json'));
  const source=languages.find(l=>l.code==='en').guide;
  assert.equal(Object.entries(source).reduce((n,[id,text])=>n+units(text,id==='shortcuts'||id==='match').length,0),257);
});
test('Translated manuals cannot omit paragraphs, list steps, links or shortcut identities',()=>{
  assert.throws(()=>validateLocale({intro:'Title|First.\n\nSecond.'},{intro:'Titre|Premier.'},'fr'),/content units/);
  assert.throws(()=>validateLocale({intro:'Title|1. First\n2. Second'},{intro:'Titre|1. Premier'},'fr'),/content units/);
  assert.throws(()=>validateLocale({intro:'Title|[Guide](https://example.com/guide)'},{intro:'Titre|Guide'},'fr'),/source link/);
  assert.throws(()=>validateLocale({match:'Title|Key|Action\nAlt+1|Read'},{match:'Titre|Touche|Action\nAlt+2|Lire'},'fr'),/shortcut/);
});
test('Even a same-shape source edit requires every language revision to be updated',()=>{
  const languages=fs.readdirSync(path.join(root,'docs/user/locales')).map(name=>({code:name.replace('.json',''),guide:read('locales/'+name)}));
  languages.find(l=>l.code==='en').guide.intro+=' Updated.';
  assert.throws(()=>validateCatalog(languages,read('translations.json')),/canonical source/);
});
