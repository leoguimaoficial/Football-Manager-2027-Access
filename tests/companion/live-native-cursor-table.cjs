// Real Unity/Prism navigation through the original Home, T and Ctrl+Alt+arrow
// NavigationManager operations. No native control activation, Save or simulation.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..');
const checkpoint='FM27Access QA Dialog Regression 20261110 Responsibilities Restored 20261110104840158';
(async()=>{
 const c=await require('./original-client.cjs')({networkDiagnostics:true}),{page,activate,poll,settled}=c;
 const report={at:new Date().toISOString(),success:false,checkpoint,steps:[],nativeBusinessCommandsSubmitted:0,saveSubmitted:false,simulationAdvanced:false,manualNVDA:false};
 const output=path.join(root,'artifacts/tests/web/native-cursor-table.json');
 async function native(action='read'){
  const code=`
var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;
var nav=System.AppDomain.CurrentDomain.GetAssemblies()[0];
foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.GetName().Name=="FM27Access.Navigation")nav=a;
var manager=nav.GetType("FM27Access.Navigation.NavigationManager");
var finder=manager.GetProperty("TableContextFinder",f).GetValue(null,null);
if(finder==null)throw new System.InvalidOperationException("Actual native table context finder is not registered");
var action="${action}";
if(action=="home")manager.GetMethod("JumpToFirst",f).Invoke(null,null);
else if(action=="table")manager.GetMethod("JumpToNextContainer",f).Invoke(null,new object[]{finder,true,"no more tables"});
else if(action!="read")manager.GetMethod("MoveTableCell",f).Invoke(null,new object[]{System.Enum.Parse(nav.GetType("FM27Access.Navigation.NavigationDirection"),action)});
var current=manager.GetMethod("GetCurrentElement",f).Invoke(null,null);
if(current==null)throw new System.InvalidOperationException("Actual navigation cursor is absent");
var context=finder.GetType().GetMethod("Invoke").Invoke(finder,new object[]{current});
int row=-1;int column=-1;
if(context!=null){
 var backing=current.GetType().GetProperty("BackingElement",f).GetValue(current,null);
 var position=context.GetType().GetMethod("FindVirtualisedItemInfo",f).Invoke(context,new object[]{backing});
 row=(int)position.GetType().GetField("Item1").GetValue(position);
 var content=position.GetType().GetField("Item2").GetValue(position);
 if(content!=null)column=(int)context.GetType().GetMethod("FindColumnIndex",f).Invoke(context,new object[]{content,backing});
}
var text=(string)current.GetType().GetMethod("GetAnnouncement",f).Invoke(current,null);
System.Console.WriteLine("STATE|"+(context==null?"none":context.GetType().Name)+"|"+row+"|"+column+"|"+text.Replace("|"," ").Replace("\\r"," ").Replace("\\n"," "));
`;
  const reply=await page.evaluate(async code=>await(await fetch('/eval',{method:'POST',body:code})).text(),code);
  assert.doesNotMatch(reply,/\[compile\]|\[exception\]|error CS/i,'Actual native navigation code must execute');
  const line=reply.split(/\r?\n/).find(line=>line.startsWith('STATE|'));assert.ok(line,reply);
  const [,context,row,column,...text]=line.split('|');const state={action,context,row:Number(row),column:Number(column),text:text.join('|')};report.steps.push(state);return state;
 }
 async function observed(predicate,label){let state;await poll(async()=>{state=await native();return predicate(state);},label);return state;}
 try{
  report.health=JSON.parse(await c.fetchText('/health'));assert.equal(report.health.developerTools,true);
  await c.dropdown('FM Menu');await c.choose('Gravar Jogo Como');const actual=await page.getByRole('textbox',{name:'File name',exact:true}).inputValue();await activate(page.getByRole('button',{name:'Cancelar',exact:true}));assert.equal(actual,checkpoint);
  const uid='1381513472',entry=page.locator('#fmnav-tree [role=treeitem][data-uid="'+uid+'"][data-obj="2000104296"]').first(),search=page.getByRole('textbox',{name:'Search all screens',exact:true});
  await search.fill(await entry.locator(':scope > span').textContent());await entry.focus();const response=page.waitForResponse(r=>new URL(r.url()).pathname==='/navigator/go');response.catch(()=>{});
  await page.keyboard.press('Enter');assert.match(await(await response).text(),/^Opening /);
  await poll(()=>page.evaluate(uid=>FMTOKEN.split('\x1f').some(p=>p===uid||p.startsWith(uid+'#')),uid),'actual first-team native panel');await settled();await search.fill('');await settled();
  await native('home');let initial=await native('table');assert.equal(initial.context,'TableContext');
  if(initial.row<0){await native('Down');initial=await observed(s=>s.row>=0&&s.column>=0,'actual header to native data row');}
  assert.ok(initial.row>=0&&initial.column>=0);report.initial=initial;
  await native('Right');const right=await observed(s=>s.row===initial.row&&s.column>initial.column,'native cursor moves to next real column');
  await native('Left');const left=await observed(s=>s.row===initial.row&&s.column===initial.column,'native cursor returns to its original column');assert.equal(left.text,initial.text);
  await native('Down');const down=await observed(s=>s.row>initial.row&&s.column===initial.column,'native cursor moves to next real player row');
  await native('Up');const up=await observed(s=>s.row===initial.row&&s.column===initial.column,'native cursor returns to its original player row');assert.equal(up.text,initial.text);
  report.result={initial,right,left,down,up};
  assert.equal(JSON.parse(await c.fetchText('/health')).coreMvid,report.health.coreMvid);assert.deepEqual(c.errors,[]);assert.deepEqual(c.requestFailures,[]);assert.deepEqual(c.network.failures,[]);report.success=true;
  console.log(JSON.stringify({success:true,module:report.health.coreMvid,initial,right,down,restored:up}));
 }catch(error){report.failure=error.stack;throw error;}
 finally{report.errors=c.errors;report.requestFailures=c.requestFailures;report.network=c.network;fs.writeFileSync(output,JSON.stringify(report,null,2));await c.browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
