. "$PSScriptRoot/Common.ps1"
Invoke-DotNet -Arguments @('restore',(Join-Path $ProjectRoot FM27Access.sln))
