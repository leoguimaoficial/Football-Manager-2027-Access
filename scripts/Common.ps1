$ErrorActionPreference='Stop'
$ProjectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$DotNet=Join-Path $ProjectRoot '.tools/dotnet/dotnet.exe'
if (Test-Path -LiteralPath $DotNet) { $env:DOTNET_ROOT=[IO.Path]::GetDirectoryName($DotNet) }
else {
    $DotNet=(Get-Command dotnet -ErrorAction Stop).Source
    if (-not (& $DotNet --list-sdks)) { throw 'Missing .NET SDK. Run scripts/setup.ps1.' }
}
function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & $DotNet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
