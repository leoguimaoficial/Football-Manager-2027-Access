#Requires -Version 7.2
$ErrorActionPreference='Stop'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('fm27-engine-reuse-'+[Guid]::NewGuid().ToString('N'))
$installer=Join-Path $fixture 'installer'
New-Item -ItemType Directory -Path $installer|Out-Null
Copy-Item -LiteralPath "$PSScriptRoot/../installer/get-distribution-installer.ps1","$PSScriptRoot/../installer/InstallerVersion.props" -Destination $installer
'throw "Fixture reached a new engine build."'|Set-Content -LiteralPath "$installer/build-installer.ps1"
$bytes=[byte[]](1,2,3)
$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
$engine=([xml](Get-Content "$installer/InstallerVersion.props" -Raw)).Project.PropertyGroup.InstallerEngineVersion
$prefix='https://github.com/leoguimaoficial/Football-Manager-2027-Access/releases/download/v0.1.0/'
$stable=[pscustomobject]@{tag_name='v0.1.0';draft=$false;prerelease=$false;assets=@(
    [pscustomobject]@{name='FM27Access-Installer.json';browser_download_url=$prefix+'FM27Access-Installer.json';size=99},
    [pscustomobject]@{name='FM27Access-Installer.exe';browser_download_url=$prefix+'FM27Access-Installer.exe';size=3})}
$fixturePages=@{}
$fixtureRequestedPages=[Collections.Generic.List[int]]::new()
function Invoke-RestMethod {
    param($Uri,$Headers)
    if($Uri -match '[?&]page=(\d+)$'){
        $page=[int]$Matches[1];$fixtureRequestedPages.Add($page)
        if(!$fixturePages.ContainsKey($page)){throw 'Unexpected fixture page.'}
        Write-Output -NoEnumerate $fixturePages[$page]
    }elseif($Uri -eq $prefix+'FM27Access-Installer.json'){
        [pscustomobject]@{schema=1;version=$engine;sha256=$hash;size=3;url=$prefix+'FM27Access-Installer.exe'}
    }else{throw 'Unexpected fixture request.'}
}
function Invoke-WebRequest {
    param($Uri,$OutFile,$Headers)
    if($Uri -ne $prefix+'FM27Access-Installer.exe'){throw 'Unexpected fixture download.'}
    [IO.File]::WriteAllBytes($OutFile,$bytes)
}
try {
    # Match actual Invoke-RestMethod JSON-array output, including a second release.
    $fixturePages=@{1=@([pscustomobject]@{draft=$false;prerelease=$true},$stable)}
    & "$installer/get-distribution-installer.ps1" -OutputPath "$fixture/multiple"|Out-Null
    if((Get-FileHash "$fixture/multiple/FM27Access-Installer.exe").Hash -ne $hash){throw 'Did not reuse the matching stable engine.'}
    # A full first page must not terminate pagination or treat flags as arrays.
    $fixtureRequestedPages.Clear()
    $fixturePages=@{1=@(1..100|ForEach-Object{[pscustomobject]@{draft=$true;prerelease=$false}});2=@($stable)}
    & "$installer/get-distribution-installer.ps1" -OutputPath "$fixture/paged"|Out-Null
    if(($fixtureRequestedPages -join ',') -ne '1,2'){throw 'Did not traverse the full release page.'}
    # An actually empty catalog must deliberately build the first engine.
    $fixturePages=@{1=@()}
    $built=$false
    try{& "$installer/get-distribution-installer.ps1" -OutputPath "$fixture/empty"|Out-Null}catch{if($_.Exception.Message -ne 'Fixture reached a new engine build.'){throw};$built=$true}
    if(!$built){throw 'Empty catalog did not build the initial engine.'}
    Write-Output 'PASS: real JSON-array shape, stable/preview filtering, 100-item pagination, exact existing engine reuse and first-engine fallback.'
}finally{
    $target=[IO.Path]::GetFullPath($fixture)
    $temp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
    if(!$target.StartsWith($temp,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($target) -notlike 'fm27-engine-reuse-*'){throw 'Unsafe fixture cleanup target.'}
    Remove-Item -LiteralPath $target -Recurse -Force
}
