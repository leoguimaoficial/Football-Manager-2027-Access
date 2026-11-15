#Requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$engine=([xml](Get-Content -LiteralPath "$PSScriptRoot/InstallerVersion.props" -Raw)).Project.PropertyGroup.InstallerEngineVersion
$repo='leoguimaoficial/Football-Manager-2027-Access'
$headers=@{'User-Agent'='FM27Access-Build';Accept='application/vnd.github+json'}
New-Item -ItemType Directory -Force $OutputPath | Out-Null
for($page=1;$page -le 100;$page++){
    $releases=@(Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases?per_page=100&page=$page" -Headers $headers)
    foreach($release in $releases){
        if($release.draft -or $release.prerelease){continue}
        $metadata=@($release.assets | Where-Object name -eq 'FM27Access-Installer.json')
        $binary=@($release.assets | Where-Object name -eq 'FM27Access-Installer.exe')
        if($metadata.Count -ne 1 -or $binary.Count -ne 1){continue}
        $prefix="https://github.com/$repo/releases/download/$($release.tag_name)/"
        if($metadata[0].browser_download_url -ne $prefix+'FM27Access-Installer.json' -or $binary[0].browser_download_url -ne $prefix+'FM27Access-Installer.exe'){throw 'Installer asset origin mismatch.'}
        $index=Invoke-RestMethod -Uri $metadata[0].browser_download_url -Headers $headers
        if($index.version -ne $engine){continue}
        if($index.schema -ne 1 -or $index.sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or $index.size -le 0 -or $index.size -gt 256MB -or
            $index.size -ne $binary[0].size -or $index.url -ne $binary[0].browser_download_url){throw 'Invalid existing official installer metadata.'}
        $exe=Join-Path $OutputPath 'FM27Access-Installer.exe'
        Invoke-WebRequest -Uri $binary[0].browser_download_url -OutFile $exe -Headers $headers
        if((Get-FileHash -LiteralPath $exe).Hash -ne $index.sha256 -or (Get-Item -LiteralPath $exe).Length -ne $index.size){throw 'Reused installer integrity failure.'}
        Write-Output "Reused official installer engine $engine from $($release.tag_name); no engine rebuild."
        return
    }
    if($releases.Count -lt 100){break}
    if($page -eq 100){throw 'Installer catalog pagination limit reached.'}
}
# The first engine, or a deliberately incremented new engine version, needs a build.
& "$PSScriptRoot/build-installer.ps1" -OutputPath $OutputPath
