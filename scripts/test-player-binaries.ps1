#Requires -Version 7.2
param([Parameter(Mandatory)][string]$PackageRoot)
$ErrorActionPreference='Stop'
$plugin=Join-Path $PackageRoot 'payload/BepInEx/plugins/FM27Access'
$binding=Get-Content -LiteralPath "$plugin/native-build.json" -Raw | ConvertFrom-Json
$manifest=Get-Content -LiteralPath (Join-Path $PackageRoot 'manifest.json') -Raw | ConvertFrom-Json
if($binding.Version -ne $manifest.native.version -or $binding.Assembly -ne ($manifest.native.files | Where-Object {$_.relative -eq 'GameAssembly.dll'}).sha256 -or $binding.Metadata -ne ($manifest.native.files | Where-Object {$_.relative -eq 'fm_Data/il2cpp_data/Metadata/global-metadata.dat'}).sha256){throw 'Runtime preflight is bound to a different native package.'}
if(!(Test-Path -LiteralPath "$plugin/companion/FM27AccessCompanion.exe")){throw 'Missing self-contained companion.'}
$runtime=Get-Content -LiteralPath "$plugin/companion/runtime-profile.json" -Raw | ConvertFrom-Json
if($runtime.runtimeOptions.tfm -ne 'net10.0' -or @($runtime.runtimeOptions.includedFrameworks | Where-Object {$_.version -ne '10.0.15'}).Count){throw 'Self-contained runtime differs from the qualified .NET10 profile.'}
foreach($pack in 'microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64'){
    if(!(Test-Path -LiteralPath "$plugin/licenses/$pack-10.0.15" -PathType Container)){throw "Resolved runtime license missing: $pack"}
}
if(Test-Path -LiteralPath "$plugin/mcs.dll"){throw 'Developer compiler shipped in player payload.'}
if(@(Get-ChildItem -LiteralPath $plugin -Recurse -File | Where-Object {$_.Extension -in '.pdb','.mdb'}).Count){throw 'Debug symbols shipped in player payload.'}
function Read-Metadata([string]$Path,[scriptblock]$Check){
    $stream=[IO.File]::OpenRead($Path)
    $pe=[Reflection.PortableExecutable.PEReader]::new($stream)
    try {& $Check ([Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe))}
    finally {$pe.Dispose();$stream.Dispose()}
}
function Get-MethodStrings($Metadata,$Method){
    if(!$Method.RelativeVirtualAddress){return}
    $bytes=[Reflection.Metadata.PEReaderExtensions]::GetMethodBody($pe,$Method.RelativeVirtualAddress).GetILBytes()
    $codes=@{}
    foreach($field in [Reflection.Emit.OpCodes].GetFields([Reflection.BindingFlags]'Public,Static')){
        $code=$field.GetValue($null);$codes[[int]$code.Value -band 0xffff]=$code
    }
    for($offset=0;$offset -lt $bytes.Length;){
        $value=[int]$bytes[$offset++];if($value -eq 0xfe){$value=0xfe00 -bor $bytes[$offset++]}
        $code=$codes[$value];if(!$code){throw 'Unknown IL opcode in production route audit.'}
        if($code.OperandType -eq [Reflection.Emit.OperandType]::InlineString){
            $token=[BitConverter]::ToInt32($bytes,$offset)
            $Metadata.GetUserString([Reflection.Metadata.Ecma335.MetadataTokens]::UserStringHandle($token -band 0xffffff))
        }
        $offset+=switch($code.OperandType.ToString()){
            'InlineNone' {0}
            {$_ -in 'ShortInlineBrTarget','ShortInlineI','ShortInlineVar'} {1}
            'InlineVar' {2}
            {$_ -in 'InlineI8','InlineR'} {8}
            'InlineSwitch' {4+4*[BitConverter]::ToInt32($bytes,$offset)}
            default {4}
        }
    }
}
Read-Metadata "$plugin/FM27Access.Plugin.dll" {
    param($metadata)
    foreach($handle in $metadata.AssemblyReferences){if($metadata.GetString($metadata.GetAssemblyReference($handle).Name) -eq 'mcs'){throw 'Release still references mcs.'}}
    $routesChecked=$false
    foreach($handle in $metadata.TypeDefinitions){
        $type=$metadata.GetTypeDefinition($handle);$name=$metadata.GetString($type.Name)
        if($name -in 'CSharpEvaluator','GameDriver','DateProbe','Introspect','PossessionTrace','TouchJournal','HttpRequestTrace','DebugTools','TreeDump','ClassificationDump'){throw "Developer-only type shipped in Release: $name"}
        if($name -eq 'EventLog' -and @($type.GetFields()).Count){throw 'Production event recorder retains diagnostic state.'}
        foreach($methodHandle in $type.GetMethods()){
            $method=$metadata.GetMethodDefinition($methodHandle);$methodName=$metadata.GetString($method.Name)
            if(($name -eq 'DevEndpoints' -and $methodName -in 'Screenshot','OnMainThread','Attach') -or
                ($name -eq 'CoreLoop' -and $methodName -match 'Diagnostics|^Diag0') -or
                ($name -eq 'ServerHandles' -and $methodName -in 'ProbeCell','ProbeFixtureRow','DescribeMenuState','DescribeMenuTrigger','DescribeAncestry') -or
                ($name -eq 'FixtureScheduleContext' -and $methodName -eq 'DumpRow')){throw "Developer-only method shipped in Release: $name.$methodName"}
            if($name -eq 'DevEndpoints' -and $methodName -eq 'RegisterRoutes'){
                $routes=@(Get-MethodStrings $metadata $method | Where-Object {$_ -like '/*'})
                if(($routes | Sort-Object) -join ',' -ne '/health,/navigator/go'){throw "Unexpected production developer route set: $($routes -join ',')"}
                $routesChecked=$true
            }
        }
    }
    if(!$routesChecked){throw 'Production route registration was not audited.'}
}
Read-Metadata "$plugin/FM27Access.Speech.dll" {
    param($metadata)
    $checked=$false
    foreach($handle in $metadata.TypeDefinitions){
        $type=$metadata.GetTypeDefinition($handle)
        if($metadata.GetString($type.Name) -ne 'BuildProfile'){continue}
        foreach($fieldHandle in $type.GetFields()){
            $field=$metadata.GetFieldDefinition($fieldHandle)
            if($metadata.GetString($field.Name) -ne 'DeveloperTools'){continue}
            $value=$metadata.GetConstant($field.GetDefaultValue())
            if($metadata.GetBlobBytes($value.Value)[0] -ne 0){throw 'Player speech assembly enables developer tools.'}
            $checked=$true
        }
    }
    if(!$checked){throw 'Missing immutable production build profile.'}
}
Read-Metadata (Join-Path $PSScriptRoot '../FM27Access.CompanionApp/bin/Release/net10.0-windows/win-x64/FM27AccessCompanion.dll') {
    param($metadata)
    $setterTokens=@()
    $memberRow=0
    foreach($handle in $metadata.MemberReferences){
        $memberRow++
        if($metadata.GetString($metadata.GetMemberReference($handle).Name) -eq 'set_AreDevToolsEnabled'){
            $setterTokens+=(0x0a000000 -bor $memberRow)
        }
    }
    $disabled=$false
    foreach($handle in $metadata.MethodDefinitions){
        $method=$metadata.GetMethodDefinition($handle)
        if($metadata.GetString($method.Name) -match 'WriteNavigationEvidence'){throw 'Companion retains DOM diagnostic reporter.'}
        $strings=@(Get-MethodStrings $metadata $method)
        if(@($strings | Where-Object {$_ -like '*navigationSuccess=*' -or $_ -like '*JSON.stringify({root:*'}).Count){throw 'Companion retains navigation diagnostic payload.'}
        if(!$method.RelativeVirtualAddress){continue}
        $bytes=[Reflection.Metadata.PEReaderExtensions]::GetMethodBody($pe,$method.RelativeVirtualAddress).GetILBytes()
        for($offset=1;$offset+4 -lt $bytes.Length;$offset++){
            if($bytes[$offset] -notin 0x28,0x6f){continue}
            if([BitConverter]::ToInt32($bytes,$offset+1) -notin $setterTokens){continue}
            if($bytes[$offset-1] -ne 0x16){throw 'Companion does not explicitly disable developer tools.'}
            $disabled=$true
        }
    }
    if(!$disabled){throw 'Companion developer-tools policy was not verified in compiled IL.'}
}
Write-Output 'PASS: compiled Release has developerTools=false; only health/navigation routes in DevEndpoints; no compiler, probes, startup self-tests, trace recorders or debug symbols.'
Write-Output 'PASS: compiled production companion disables WebView developer tools and contains no navigation/DOM diagnostic reporter.'
