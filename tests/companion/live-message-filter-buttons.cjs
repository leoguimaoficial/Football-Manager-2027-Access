// Actual sole-companion/native inbox regression. Does not open/read a message,
// change game business data, save or advance time. Starts on the men's inbox.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const base='http://127.0.0.1:8787',pause=ms=>new Promise(r=>setTimeout(r,ms));
(async()=>{
 const health=await fetch(base+'/health').then(r=>r.json());assert.equal(health.developerTools,true);
 const cookie=(await fetch(base+'/',{method:'HEAD'})).headers.get('set-cookie')?.split(';')[0];assert.ok(cookie);
 async function request(route,post=false,body=''){
  const r=await fetch(base+route,{method:post?'POST':'GET',headers:{Cookie:cookie},...(post?{body}:{})});assert.ok(r.ok,`HTTP ${r.status}`);return r.text();
 }
 function filters(html){
  const bar=html.match(/<div role="region"[^>]*>(?:(?!<\/div>)[\s\S])*data-fm-msg-unread-filter(?:(?!<\/div>)[\s\S])*<\/div>/)?.[0];assert.ok(bar,'Native unread filter bar required');
  assert.doesNotMatch(bar,/role="(?:tab|tablist)"/);
  const list=[...bar.matchAll(/<button\b([^>]*)>([^]*?)<\/button>/g)].map(m=>({id:m[1].match(/\bid="([^"]+)"/)[1],attributes:m[1],label:m[2],pressed:/aria-pressed="true"/.test(m[1])}));
  assert.equal(list.length,4);assert.equal(list.filter(b=>b.pressed).length,1);assert.ok(list.every(b=>/aria-pressed="(?:true|false)"/.test(b.attributes)));
  return list;
 }
 async function oracle(id){
  // Byte-loaded retired cores share the assembly name. typeof(CoreEntry) in an
  // evaluator can bind an older one; qualify this readonly oracle by live MVID.
  const code=`System.Reflection.Assembly live=null; foreach(System.Reflection.Assembly candidate in System.AppDomain.CurrentDomain.GetAssemblies()){if(candidate.GetName().Name=="FM27Access.Plugin" && candidate.ManifestModule.ModuleVersionId.ToString()=="${health.coreMvid}") live=candidate;} var ht=live.GetType("FM27Access.Plugin.Server.ServerHandles"); var hs=(System.Collections.IDictionary)ht.GetField("_handles",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null); var hh=hs["${id}"]; if(hh==null) throw new Exception("Rendered native filter handle unavailable"); var aa=hh.GetType().GetField("El").GetValue(hh); var ve=(UnityEngine.UIElements.VisualElement)live.GetType("FM27Access.Plugin.Adapters.PluginAdapterBase").GetProperty("BackingElement",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(aa,null); var bg=ve; while(bg!=null && bg.GetIl2CppType().Name!="ButtonGroup") bg=bg.hierarchy.parent; if(bg==null) throw new Exception("No native filter group"); var gg=new SI.Bindable.ButtonGroup(bg.Pointer); System.Console.WriteLine("INDEX="+gg.GetSelectedIndex()+" STYLE="+gg.SelectedStyle+" SELECTED="+ve.ClassListContains(gg.SelectedStyle));`;
  const result=await request('/eval',true,code);assert.doesNotMatch(result,/\[compile\]|\[exception\]|\[host error\]/);assert.match(result,/SELECTED=True/);return result.trim();
 }
 // Close only the mod-owned reading view, once, before testing its inbox filters.
 await request('/message/close',true);
 let html=await request('/rendered');
 const inboxDeadline=Date.now()+10000;
 while(!html.includes('data-fm-msg-unread-filter')&&Date.now()<inboxDeadline){await pause(100);html=await request('/rendered');}
 const initial=filters(html),checks=[];
 for(const label of ['Novo','Tarefas','Por Ler','Tudo']){
  const before=filters(html),button=before.find(b=>b.label.startsWith(label));assert.ok(button);
  assert.match(await request('/activate?id='+button.id,true),/^Activated /);
  const until=Date.now()+10000;let selected;
  do{await pause(100);html=await request('/rendered');const current=filters(html);selected=current.find(b=>b.label.startsWith(label)&&b.pressed);if(selected)break;}while(Date.now()<until);
  assert.ok(selected,`Native selection did not reach the actual companion: ${label}`);
  const raw=await oracle(selected.id);
  if(label==='Por Ler')assert.match(selected.attributes,/data-fm-selected/,'Inherited Unread return marker lost');
  checks.push({filter:selected.label,ordinaryButton:true,pressed:true,nativeSelection:raw});
 }
 const evidence={at:new Date().toISOString(),health,checks,initial:initial.map(b=>({label:b.label,pressed:b.pressed})),manualNvdaEvidence:false,messagesOpened:0,savesIssued:0,simulationIssued:0};
 const out=process.argv[2]||path.resolve(__dirname,'../../artifacts/native-auto-speech/message-filter-buttons.json');fs.mkdirSync(path.dirname(out),{recursive:true});fs.writeFileSync(out,JSON.stringify(evidence,null,2));console.log(`PASS: four ordinary native filter buttons, exclusive pressed state, raw native selection and Unread return marker. ${out}`);
})().catch(error=>{console.error(error.stack||error);process.exitCode=1;});
