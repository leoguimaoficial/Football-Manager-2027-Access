#Requires -Version 7.2
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

function Get-InteropContractHash([string]$Path) {
    # Generated DLL timestamps/MVIDs vary by generation. Compare the complete
    # declaration/signature shape, not those machine-specific PE bytes. This is
    # deliberately conservative: token-order differences still require review.
    $stream=[IO.File]::OpenRead($Path)
    $pe=[Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $reader=[Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $rows=[Collections.Generic.List[string]]::new()
        $rows.Add('fm27-interop-metadata-shape-v1')
        $assembly=$reader.GetAssemblyDefinition()
        $rows.Add('SELF|'+$reader.GetString($assembly.Name)+'|'+$assembly.Version+'|'+[int]$assembly.Flags)
        function Add-Generics($Handles) {
            foreach($handle in $Handles){
                $generic=$reader.GetGenericParameter($handle)
                $rows.Add('G|'+$generic.Index+'|'+[int]$generic.Attributes)
                foreach($constraint in $generic.GetConstraints()){$rows.Add('GC|'+[Reflection.Metadata.Ecma335.MetadataTokens]::GetToken($reader.GetGenericParameterConstraint($constraint).Type))}
            }
        }
        foreach($handle in $reader.AssemblyReferences){
            $reference=$reader.GetAssemblyReference($handle)
            $rows.Add('A|'+$reader.GetString($reference.Name)+'|'+$reference.Version)
        }
        # Include reference ordering because signature blobs encode those tokens.
        foreach($handle in $reader.TypeReferences){
            $type=$reader.GetTypeReference($handle)
            $rows.Add('R|'+$reader.GetString($type.Namespace)+'|'+$reader.GetString($type.Name)+'|'+[Reflection.Metadata.Ecma335.MetadataTokens]::GetToken($type.ResolutionScope))
        }
        foreach($handle in $reader.TypeDefinitions){
            $type=$reader.GetTypeDefinition($handle)
            $layout=$type.GetLayout()
            $rows.Add('T|'+$reader.GetString($type.Namespace)+'|'+$reader.GetString($type.Name)+'|'+[int]$type.Attributes+'|'+[Reflection.Metadata.Ecma335.MetadataTokens]::GetToken($type.BaseType)+'|'+$layout.PackingSize+'|'+$layout.Size)
            $rows.Add('N|'+[Reflection.Metadata.Ecma335.MetadataTokens]::GetToken([Reflection.Metadata.EntityHandle]($type.GetDeclaringType())))
            Add-Generics $type.GetGenericParameters()
            foreach($interface in $type.GetInterfaceImplementations()){$rows.Add('I|'+[Reflection.Metadata.Ecma335.MetadataTokens]::GetToken($reader.GetInterfaceImplementation($interface).Interface))}
            foreach($member in $type.GetFields()){
                $field=$reader.GetFieldDefinition($member)
                $rows.Add('F|'+$reader.GetString($field.Name)+'|'+[int]$field.Attributes+'|'+[Convert]::ToBase64String($reader.GetBlobBytes($field.Signature)))
                $rows.Add('FO|'+$field.GetOffset())
                $constant=$field.GetDefaultValue()
                if(!$constant.IsNil){$value=$reader.GetConstant($constant);$rows.Add('C|'+$value.TypeCode+'|'+[Convert]::ToBase64String($reader.GetBlobBytes($value.Value)))}
            }
            foreach($member in $type.GetMethods()){
                $method=$reader.GetMethodDefinition($member)
                $rows.Add('M|'+$reader.GetString($method.Name)+'|'+[int]$method.Attributes+'|'+[int]$method.ImplAttributes+'|'+[Convert]::ToBase64String($reader.GetBlobBytes($method.Signature)))
                Add-Generics $method.GetGenericParameters()
            }
            foreach($member in $type.GetProperties()){
                $property=$reader.GetPropertyDefinition($member)
                $rows.Add('P|'+$reader.GetString($property.Name)+'|'+[int]$property.Attributes+'|'+[Convert]::ToBase64String($reader.GetBlobBytes($property.Signature)))
            }
            foreach($member in $type.GetEvents()){
                $event=$reader.GetEventDefinition($member)
                $rows.Add('E|'+$reader.GetString($event.Name)+'|'+[int]$event.Attributes+'|'+[Reflection.Metadata.Ecma335.MetadataTokens]::GetToken($event.Type))
            }
        }
        foreach($handle in $reader.CustomAttributes){
            $attribute=$reader.GetCustomAttribute($handle)
            $rows.Add('CA|'+[Reflection.Metadata.Ecma335.MetadataTokens]::GetToken($attribute.Parent)+'|'+[Reflection.Metadata.Ecma335.MetadataTokens]::GetToken($attribute.Constructor)+'|'+[Convert]::ToBase64String($reader.GetBlobBytes($attribute.Value)))
        }
        $bytes=[Text.Encoding]::UTF8.GetBytes([string]::Join("`n",$rows))
        return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    } finally {$pe.Dispose();$stream.Dispose()}
}
Export-ModuleMember -Function Get-InteropContractHash
