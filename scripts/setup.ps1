param([switch]$Loader)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolsPath=Join-Path $root '.tools'
New-Item -ItemType Directory -Force $toolsPath | Out-Null
$sdk=Join-Path $toolsPath 'dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk) -or -not ((& $sdk --list-sdks) -match '^10\.0\.403 ')) {
    $installer=Join-Path $toolsPath 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Version '10.0.403' -InstallDir (Join-Path $toolsPath 'dotnet') -NoPath
    if ($LASTEXITCODE -ne 0) { throw 'SDK installation failed.' }
}
if ($Loader) {
    $zip=Join-Path $toolsPath 'bepinex-793.zip'
    $loaderPath=Join-Path $toolsPath 'bepinex-793'
    $expected='5B63D7D0E92A2448CCD13B738FF95FF1CC1026C6D718555D4DF8E6C824953AC4'
    if (-not (Test-Path -LiteralPath $zip)) {
        Invoke-WebRequest 'https://builds.bepinex.dev/projects/bepinex_be/793/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793%2Bdbc82f0.zip' -OutFile $zip
    }
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) { throw 'Unexpected BepInEx hash. Download preserved for diagnostics; not installed.' }
    if (-not (Test-Path -LiteralPath $loaderPath)) { Expand-Archive -LiteralPath $zip -DestinationPath $loaderPath }
    Write-Output "BepInEx 793 prepared at $loaderPath. Game installation is a separate step."
}
Write-Output 'Local SDK ready; no game files changed.'
