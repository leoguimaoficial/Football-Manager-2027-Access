const assert=require('node:assert/strict');
const prefix='--disable-features=';
// A second Chromium switch replaces the framework's first disabled-feature
// value. Discover this installed framework's defaults through its public owned
// BrowserServer process, close that blank probe, then supply one merged switch.
// No source parsing, dependency patching, fixed feature list or game connection.
function mergeDisabledFeatures(defaultArguments,requestedArguments){
 const defaults=defaultArguments.filter(a=>a.startsWith(prefix));
 const requested=requestedArguments.filter(a=>a.startsWith(prefix));
 assert.ok(requested.length,'A requested feature override is required');
 const features=[...new Set([...defaults,...requested].flatMap(a=>a.slice(prefix.length).split(',')).filter(Boolean))];
 return{ignoreDefaultArgs:defaults,args:[prefix+features.join(','),...requestedArguments.filter(a=>!a.startsWith(prefix))],
  profile:{defaultDisabledFeatures:defaults.flatMap(a=>a.slice(prefix.length).split(',')).filter(Boolean),disabledFeatures:features,defaultSwitchesReplaced:defaults.length}};
}
async function launchOptions(browserType,base,requestedArguments){
 const probe=await browserType.launchServer(base);let defaults;
 try{defaults=probe.process().spawnargs;assert.ok(Array.isArray(defaults));}
 finally{await probe.close();}
 const {profile,...options}=mergeDisabledFeatures(defaults,requestedArguments);
 return{options:{...base,...options},profile};
}
module.exports={mergeDisabledFeatures,launchOptions};
