param([Parameter(Mandatory)][string]$GamePath)
. "$PSScriptRoot/Common.ps1"
Push-Location $ProjectRoot
try { Invoke-DotNet -Arguments @('run','--project','tools/game-inspect','--','inspect',$GamePath) }
finally { Pop-Location }
