const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
const source=fs.readFileSync(require('node:path').join(__dirname,'../../FM27Access.Plugin/Server/app.js'),'utf8');
const report=source.slice(source.indexOf('var FMReport ='));

for(const development of [false,true]) test(`DOM reporting is ${development?'enabled in development':'absent in production'}`,()=>{
  let observers=0,listeners=0,timers=0;
  const context={document:{documentElement:{getAttribute:()=>String(development)},getElementById:()=>({}),addEventListener:()=>listeners++},MutationObserver:function(){observers++;this.observe=()=>{};},FM:{core:{}},setTimeout:()=>++timers};
  vm.createContext(context);vm.runInContext(report,context);
  context.FMReport.schedule();
  assert.equal(observers,development?1:0);
  assert.equal(listeners,development?2:0);
  assert.equal(timers,development?1:0);
});
