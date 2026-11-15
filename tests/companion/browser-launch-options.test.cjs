const {test}=require('node:test'),assert=require('node:assert/strict');
const {mergeDisabledFeatures}=require('./browser-launch-options.cjs');
test('Loopback mitigation preserves every framework feature in one effective switch',()=>{
 const defaults=['--headless','--disable-features=Translate,PaintHolding,msForceBrowserSignIn'];
 const requested=['--disable-features=TcpPortRandomizationWin','--log-net-log=local-test'];
 const originalDefaults=[...defaults],originalRequested=[...requested];
 const plan=mergeDisabledFeatures(defaults,requested);
 assert.deepEqual(plan.ignoreDefaultArgs,['--disable-features=Translate,PaintHolding,msForceBrowserSignIn']);
 assert.deepEqual(plan.args,['--disable-features=Translate,PaintHolding,msForceBrowserSignIn,TcpPortRandomizationWin','--log-net-log=local-test']);
 assert.deepEqual(defaults,originalDefaults);assert.deepEqual(requested,originalRequested);
});
test('An upstream default mitigation is retained once without dropping other defaults',()=>{
 const plan=mergeDisabledFeatures(['--disable-features=PaintHolding,TcpPortRandomizationWin'],['--disable-features=TcpPortRandomizationWin']);
 assert.deepEqual(plan.profile.disabledFeatures,['PaintHolding','TcpPortRandomizationWin']);assert.equal(plan.args.length,1);
});
test('An installed framework without this default switch still receives the explicit mitigation',()=>{
 const plan=mergeDisabledFeatures(['--headless'],['--disable-features=TcpPortRandomizationWin']);
 assert.deepEqual(plan.ignoreDefaultArgs,[]);assert.deepEqual(plan.args,['--disable-features=TcpPortRandomizationWin']);
});
