// Read-only inventory of the already running production WebView2 document.
// Never opens/navigates a page, requests /body, consumes SSE or executes a game
// action. Inventory presence is explicitly not functional action validation.
const fs=require('node:fs'),path=require('node:path'),{createRequire}=require('node:module');
const requireTests=createRequire(path.resolve(__dirname,'../tests/companion/package.json'));
const {chromium}=requireTests('playwright');
const root=path.resolve(__dirname,'..'),endpoint=process.env.FM27_COMPANION_CDP||'http://127.0.0.1:9227';
const address=new URL(endpoint);if(address.protocol!=='http:'||address.hostname!=='127.0.0.1')throw Error('Only the known local production debug endpoint is allowed');
const destination=path.resolve(root,process.argv[2]||'artifacts/action-inventory-'+Date.now()+'.json');
if(!destination.startsWith(path.join(root,'artifacts')+path.sep)||fs.existsSync(destination))throw Error('Use a new artifact filename inside this workspace');
(async()=>{
 const browser=await chromium.connectOverCDP(endpoint);
 try{
  const pages=browser.contexts().flatMap(c=>c.pages()).filter(p=>p.url().startsWith('http://127.0.0.1:8787/'));
  if(pages.length!==1)throw Error('Expected the sole existing production page');const page=pages[0];
  const before=await page.evaluate(()=>({token:FMTOKEN,title:document.title,refreshing:FM.core.refreshing,pending:FM.core.pending}));
  const session=await page.context().newCDPSession(page),dom=await session.send('DOM.getDocument',{depth:-1}),ax=await session.send('Accessibility.getFullAXTree');
  const names=new Map(ax.nodes.filter(n=>!n.ignored).map(n=>[n.backendDOMNodeId,n]));let fmroot;
  function find(node){const attrs=node.attributes||[],id=attrs[attrs.indexOf('id')+1];if(attrs.includes('id')&&id==='fmroot')fmroot=node;for(const child of node.children||[])find(child);}find(dom.root);if(!fmroot)throw Error('Production fmroot is absent');
  const controls=[];
  function collect(node){const attrs={};for(let i=0;i<(node.attributes||[]).length;i+=2)attrs[node.attributes[i]]=node.attributes[i+1];
   if(['button','select','input','textarea'].includes(node.localName)||node.localName==='a'&&attrs.href||['button','menuitem','treeitem','tab','combobox'].includes(attrs.role)){
    const a=names.get(node.backendNodeId),properties=Object.fromEntries((a?.properties||[]).filter(p=>['disabled','focusable','expanded','selected','checked'].includes(p.name)).map(p=>[p.name,p.value?.value]));
    const handlers=Object.fromEntries(Object.entries(attrs).filter(([k])=>/^on[a-z]+$/.test(k))),nativeBindings=Object.fromEntries(Object.entries(attrs).filter(([k])=>/^data-(?:dd|date|search-dd|fm-)/.test(k))),htmlDisabled=Object.hasOwn(attrs,'disabled');
    controls.push({id:attrs.id||null,tag:node.localName,role:a?.role?.value||attrs.role||null,name:a?.name?.value||'',inAccessibilityTree:!!a,states:properties,htmlDisabled,handlers,nativeBindings,expectedNativeHandle:/^e[0-9a-f]+(?:-\d+)?$/.test(attrs.id||'')&&!htmlDisabled&&(Object.keys(handlers).length>0||Object.keys(nativeBindings).length>0),nativeMapping:null,functionalValidation:'Not executed by this inventory'});
   }for(const child of node.children||[])collect(child);
  }collect(fmroot);
  const mvid=await page.evaluate(async()=>{const r=await fetch('/health');if(!r.ok)throw Error('Health HTTP '+r.status);return(await r.json()).coreMvid;});
  const ids=controls.map(c=>c.id).filter(id=>/^e[0-9a-f]+(?:-\d+)?$/.test(id||''));
  const code='foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="'+mvid+'"){var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;var sh=a.GetType("FM27Access.Plugin.Server.ServerHandles");var handles=(System.Collections.IDictionary)sh.GetField("_handles",f).GetValue(null);foreach(string id in new[]{'+ids.map(id=>'"'+id+'"').join(',')+'}){var h=handles[id];if(h==null){System.Console.WriteLine("ACTION|"+id+"|missing");continue;}var af=h.GetType().GetField("El",f);var ad=af==null?null:af.GetValue(h);var el=ad==null?null:(UnityEngine.UIElements.VisualElement)sh.GetMethod("NativeElement",f).Invoke(null,new object[]{ad});string context="";foreach(string name in new[]{"CalendarIdentity","NativeRowIdentity","MessageId","ConversationContext"}){var field=h.GetType().GetField(name,f);var value=field==null?null:field.GetValue(h);if(value!=null)context+=name+"="+value+";";}System.Console.WriteLine("ACTION|"+id+"|"+h.GetType().Name+"|"+(ad==null?"":ad.GetType().Name)+"|"+(el==null?"":el.GetIl2CppType().FullName)+"|"+(el==null?"":el.enabledInHierarchy.ToString())+"|"+System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(context)));}}';
  let native='';if(ids.length)native=await page.evaluate(async code=>{const r=await fetch('/eval',{method:'POST',body:code});if(!r.ok)throw Error('Metadata HTTP '+r.status);return r.text();},code);
  if(/\[exception\]|\[compile\].*error/.test(native))throw Error('Native inventory metadata read failed: '+native);
  const mappings=new Map(native.split(/\r?\n/).filter(l=>l.startsWith('ACTION|')).map(line=>{const p=line.split('|');return[p[1],{handle:p[2],adapter:p[3]||null,nativeType:p[4]||null,nativeEnabled:p[5]?p[5]==='True':null,retainedContext:p[6]?Buffer.from(p[6],'base64').toString('utf8'):null,scope:'Metadata only; does not realize/activate a native row or establish a result'}];}));
  for(const c of controls)if(c.id)c.nativeMapping=mappings.get(c.id)||null;
  const after=await page.evaluate(()=>({token:FMTOKEN,title:document.title,refreshing:FM.core.refreshing,pending:FM.core.pending}));
  const report={at:new Date().toISOString(),mvid,before,after,sameDocument:before.token===after.token&&before.title===after.title,scope:'Current DOM/computed accessibility and registered native-handle metadata only; not an exhaustive action inventory or functional pass',nativeActionsExecuted:0,bodyRequested:false,summary:{controls:controls.length,inAccessibilityTree:controls.filter(c=>c.inAccessibilityTree).length,unnamed:controls.filter(c=>c.inAccessibilityTree&&!c.name).length,registeredHandles:controls.filter(c=>c.nativeMapping&&c.nativeMapping.handle!=='missing').length,missingRenderedHandles:controls.filter(c=>c.expectedNativeHandle&&c.nativeMapping?.handle==='missing').length},controls};
  fs.writeFileSync(destination,JSON.stringify(report,null,2),{flag:'wx'});console.log(JSON.stringify({file:destination,sameDocument:report.sameDocument,...report.summary}));
 }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
