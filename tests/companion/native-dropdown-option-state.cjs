// Debug-only, read-only existing native menu rows. The raw tree/enable/opacity
// observation is independent of the ordinary dropdown HTML builder.
const assert=require('node:assert/strict');
module.exports=async(c,mvid,owner)=>{
 assert.match(mvid,/^[0-9a-f-]{36}$/);assert.match(owner,/^[-\w:.]+$/);
 const code=`
var f=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;
System.Reflection.Assembly core=null;foreach(var a in System.AppDomain.CurrentDomain.GetAssemblies())if(a.ManifestModule.ModuleVersionId.ToString()=="${mvid}"&&a.GetType("FM27Access.Plugin.CoreEntry").GetField("_bridge",f).GetValue(null)!=null)core=a;
if(core==null)throw new System.InvalidOperationException("Current active core is absent");
var sessions=core.GetType("FM27Access.Plugin.Server.DropdownSessionManager");
if((string)sessions.GetField("_ownerId",f).GetValue(null)!="${owner}")throw new System.InvalidOperationException("Native menu owner changed");
var levels=(System.Collections.IList)sessions.GetField("_levels",f).GetValue(null);if(levels==null||levels.Count!=1)throw new System.InvalidOperationException("Require the actual root menu");
var level=levels[0];var adapter=level.GetType().GetField("Adapter",f).GetValue(level);
var adapterType=core.GetType("FM27Access.Plugin.Adapters.DropdownAdapter");
System.Console.WriteLine("SELECTED_INDEX|"+adapterType.GetMethod("GetSelectedIndex",f).Invoke(adapter,null));
var labels=(System.Collections.IEnumerable)adapterType.GetMethod("ReadChoices",f).Invoke(adapter,null);int labelIndex=0;foreach(var label in labels)System.Console.WriteLine("LABEL|"+(labelIndex++)+"|"+label);
var disabled=(bool[])adapterType.GetMethod("BuildIsDisabledArray",f).Invoke(adapter,null);for(int i=0;i<disabled.Length;i++)System.Console.WriteLine("GUARD_DISABLED|"+i+"|"+disabled[i]);
var rows=(System.Collections.IEnumerable)adapterType.GetMethod("GetRowRoots",f).Invoke(adapter,null);
var printer=core.GetType("FM27Access.Plugin.Diagnostics.TreeDump");var optionsType=core.GetType("FM27Access.Plugin.Diagnostics.TreeDump+Options");var options=System.Activator.CreateInstance(optionsType,true);
optionsType.GetField("MaxNodes",f).SetValue(options,400);optionsType.GetField("MaxDepth",f).SetValue(options,40);optionsType.GetField("ShowEnabled",f).SetValue(options,true);optionsType.GetField("ShowOpacity",f).SetValue(options,true);optionsType.GetField("ShowUss",f).SetValue(options,true);
int rowIndex=0;foreach(var row in rows){System.Console.WriteLine("RAW_ROW|"+rowIndex+"|self="+row.GetType().GetProperty("enabledSelf",f).GetValue(row,null)+"|hierarchy="+row.GetType().GetProperty("enabledInHierarchy",f).GetValue(row,null));var sb=new System.Text.StringBuilder();printer.GetMethod("Write",f).Invoke(null,new object[]{row,sb,options});System.Console.WriteLine(sb.ToString());rowIndex++;}
System.Console.WriteLine("RAW_ROW_COUNT|"+rowIndex);
`;
 const raw=await c.page.evaluate(async code=>(await fetch('/eval',{method:'POST',body:code})).text(),code);
 assert.doesNotMatch(raw,/\[exception\]|\[compile\].*error|error CS\d+/i,'Independent existing native menu rows must be readable');
 return{raw,labels:raw.split(/\r?\n/).filter(l=>l.startsWith('LABEL|')).map(l=>l.split('|').slice(2).join('|')),
  guardDisabled:raw.split(/\r?\n/).filter(l=>l.startsWith('GUARD_DISABLED|')).map(l=>({index:Number(l.split('|')[1]),disabled:l.endsWith('|True')})),
  selectedIndex:Number(raw.split(/\r?\n/).find(l=>l.startsWith('SELECTED_INDEX|'))?.split('|')[1]),
  rows:raw.split(/\r?\n/).filter(l=>l.startsWith('RAW_ROW|'))};
};
