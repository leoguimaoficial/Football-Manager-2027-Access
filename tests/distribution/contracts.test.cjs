const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const root=path.resolve(__dirname,'../..'),read=p=>fs.readFileSync(path.join(root,p),'utf8').replace(/^\uFEFF/,'');
const resources='installer/FM27Access.Installer/Resources';
test('Every installer language has complete real resources and a matching guide',()=>{
  const keys=Object.keys(JSON.parse(read(`${resources}/en.json`))).sort();
  const files=fs.readdirSync(path.join(root,resources)); assert.equal(files.length,20);
  for(const file of files){
    const ui=JSON.parse(read(`${resources}/${file}`));assert.deepEqual(Object.keys(ui).sort(),keys,file);
    for(const value of Object.values(ui)){assert.equal(typeof value,'string');assert.ok(value.trim());assert.doesNotMatch(value,/TODO|PLACEHOLDER|\{[0-9]\}/);}
    const guide=JSON.parse(read(`docs/user/locales/${file}`));assert.equal(Object.keys(guide).length,14);
    for(const value of Object.values(guide)){assert.ok(value.includes('|'));assert.ok(value.split('|').slice(1).join('|').length>40);}
  }
});
test('Offline guide is reproducible and has no external script or media dependencies',()=>{
  const before=read('docs/user/FM27Access-UserGuide.html');cp.execFileSync(process.execPath,[path.join(root,'docs/user/build-guide.cjs')]);
  assert.equal(read('docs/user/FM27Access-UserGuide.html'),before);
  assert.doesNotMatch(before,/<script[^>]+src=|<link[^>]+stylesheet|<iframe|<img|<video|<audio/i);
  assert.equal((before.match(/<article data-language=/g)||[]).length,20);
  assert.equal((before.match(/data-section=/g)||[]).length,280);
  assert.doesNotMatch(before,/native API|interop|qualification|not published|not been published|in development|em desenvolvimento|ainda não foi publicada/i);
  assert.equal((before.match(/<table>/g)||[]).length,40);
});
test('Release publication is manual, approved and uses selected original artifacts',()=>{
  const workflow=read('.github/workflows/release.yml');
  assert.match(workflow,/workflow_dispatch:/);assert.doesNotMatch(workflow,/^  (push|pull_request):/m);
  assert.match(workflow,/environment: fm27-release/);assert.match(workflow,/required_reviewers/);
  assert.match(workflow,/head_sha !== sha/);assert.match(workflow,/run.conclusion !== 'success'/);assert.match(workflow,/createRef/);
  assert.match(workflow,/draft:true/);assert.match(workflow,/uploadReleaseAsset/);assert.match(workflow,/draft:false/);
  assert.doesNotMatch(workflow,/dotnet (build|publish)|package-distribution/);
  const ci=read('.github/workflows/ci.yml');assert.match(ci,/github.event_name != 'pull_request'/);assert.match(ci,/retention-days: 90/);
});
test('Installer engine version and mod version have separate central sources',()=>{
  assert.match(read('Directory.Build.props'),/<Version>\d+\.\d+\.\d+(?:-[^<]+)?<\/Version>/);
  assert.match(read('installer/InstallerVersion.props'),/<InstallerEngineVersion>\d+\.\d+\.\d+<\/InstallerEngineVersion>/);
  assert.match(read('installer/InstallerVersion.props'),/<Version>\$\(InstallerEngineVersion\)<\/Version>/);
  assert.match(read('installer/FM27Access.Installer.Core/Distribution.cs'),/Assembly.GetName\(\).Version/);
  assert.match(read('installer/FM27Access.Installer/FM27Access.Installer.csproj'),/<AssemblyName>FM27Access-Installer<\/AssemblyName>/);
});
