[CmdletBinding(SupportsShouldProcess)]
param()
$ErrorActionPreference='Stop'
$workspaceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. "$PSScriptRoot/WorkspaceCleanup.ps1"
$artifactRoot=Join-Path $workspaceRoot 'artifacts'
if(-not(Test-Path -LiteralPath (Join-Path $artifactRoot 'current-verification.json'))){
    throw 'Consolidate current verification evidence before removing generated outputs.'
}
$targets=@(Get-WorkspaceCleanupTargets -Root $workspaceRoot)
$plan=[ordered]@{at=[DateTime]::UtcNow.ToString('o');root=$workspaceRoot;targets=$targets;
    preserved='Source, Git, FM26 baseline, pinned SDK10, Prism release DLL/legal notices, install receipts and consolidated verification';savesTouched=$false;installedGameTouched=$false}
$plan | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifactRoot 'workspace-cleanup-plan.json') -Encoding utf8
$removed=@(Remove-WorkspaceCleanupTargets -Root $workspaceRoot -Targets $targets -WhatIf:$WhatIfPreference)
$plan.removed=$removed
$plan.bytesRemoved=[long](($removed | Measure-Object bytes -Sum).Sum)
$plan | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $artifactRoot 'workspace-cleanup-latest.json') -Encoding utf8
Write-Output "Removed $($removed.Count) generated outputs; $([Math]::Round($plan.bytesRemoved/1GB,2)) GiB. Source, receipts, SDK10, installed game and saves preserved."
