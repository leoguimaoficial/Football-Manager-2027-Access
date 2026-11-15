$ErrorActionPreference='Stop'
. "$PSScriptRoot/GameReport.ps1"
$original=@{Store='Xbox';Architecture='x64';UnityVersion='6000.3.18f1';MetadataVersion=39;LoaderVersion='6.0.0-be.793+fixture';GameVersion='0.9.2409.0';AssemblyHash='FD0F8210FF0D52F5F3E5F1A4925FD2E3736A3DD167D2E17B355DC1C1173FE534';MetadataHash='AB3B75823275EC24CB8FC30FC428C7259F844CACA16A9A1BD650402D0CDF5420'}
$updated=$original.Clone();$updated.GameVersion='0.9.2435.0';$updated.AssemblyHash='3B393291208F1C06135100EA644911FD8CE9CCDDC93B9248A7F4C7A560B1F3BE';$updated.MetadataHash='118705D69DD8C981DDEEC4ED815AAB212E676A07AFC6F819D48511E833078BD1'
if((Get-ModDeploymentProfile ([pscustomobject]$original)).Version -ne $original.GameVersion){throw 'Original exact native profile rejected.'}
if((Get-ModDeploymentProfile ([pscustomobject]$updated)).Evidence -notmatch 'live qualification pending'){throw 'Qualification build incorrectly claims live validation.'}
$rejections=0
foreach($field in @('Store','Architecture','UnityVersion','MetadataVersion','LoaderVersion')){
    $invalid=$updated.Clone();$invalid[$field]=if($field -eq 'MetadataVersion'){40}else{'unexpected'}
    $rejected=$false;try{Get-ModDeploymentProfile ([pscustomobject]$invalid) | Out-Null}catch{$rejected=$true}
    if(-not $rejected){throw "Invalid native $field accepted."};$rejections++
}
foreach($variant in @(
    @{Store='Steam';GameVersion='123456789'},
    @{Store='Xbox';GameVersion='0.9.9999.0'},
    @{Store='Steam';GameVersion='123456789';AssemblyHash=('A'*64);MetadataHash=('B'*64)})){
    $compatible=$updated.Clone();foreach($key in $variant.Keys){$compatible[$key]=$variant[$key]}
    $result=Get-ModDeploymentProfile ([pscustomobject]$compatible)
    if($result.Version -ne $compatible.GameVersion -or $result.Evidence -notmatch 'pending'){throw 'Compatible variant label was refused or falsely qualified.'}
}
foreach($field in @('AssemblyHash','MetadataHash')){
    $mixed=$updated.Clone();$mixed[$field]=$original[$field];$rejected=$false
    try{Get-ModDeploymentProfile ([pscustomobject]$mixed) | Out-Null}catch{$rejected=$true}
    if(-not $rejected){throw "Mixed native version/$field accepted."};$rejections++
}
Write-Output "PASS: reference native pairs, compatible Steam/build labels and unqualified variants accepted for API checks; $rejections incompatible/mixed profile refusals."
