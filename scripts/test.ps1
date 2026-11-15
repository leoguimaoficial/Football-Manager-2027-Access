param([switch]$Coverage,[ValidateSet('All','Runtime')][string]$Scope='All')
. "$PSScriptRoot/Common.ps1"
Push-Location $ProjectRoot
try {
    Write-Output "Game-independent unit/contract tests: $Scope. Live game results are recorded separately."
    $arguments=@('test','FM27Access.Tests','--nologo','--logger',"trx;LogFileName=tests-$Scope.trx",'--results-directory','artifacts/tests/latest')
    if($Scope -eq 'Runtime'){$arguments+=@('--filter','FullyQualifiedName~OriginalRuntimeTests')}
    if($Coverage){$arguments+=@('--collect','XPlat Code Coverage')}
    Invoke-DotNet -Arguments $arguments
    & "$PSScriptRoot/test-loader.ps1"
    & "$PSScriptRoot/test-deployment-profile.ps1"
    & "$PSScriptRoot/test-production-profile.ps1"
    & "$PSScriptRoot/test-player-package.ps1"
    & "$PSScriptRoot/test-package-retention.ps1"
    & "$PSScriptRoot/test-workspace-cleanup.ps1"
    # Profile fixtures deliberately run rejected native builds. Their expected
    # nonzero exit must not become this successful suite's process exit code.
    $global:LASTEXITCODE = 0
} finally { Pop-Location }
