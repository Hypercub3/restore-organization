param([Parameter(Mandatory=$true)][string]$ScriptPath)
$ErrorActionPreference='Stop'
# Compile and run only the code embedded in a generated script, in a stock PS 5.1 process.
# This fixture needs no generator executable or resource files.
$env:RO_SELF=$ScriptPath
$text=[IO.File]::ReadAllText($ScriptPath)
$base64=($text -split '\r?\n:RO_PAYLOAD\r?\n',2)[1]
$memory=New-Object IO.MemoryStream(,[Convert]::FromBase64String($base64))
$gzip=New-Object IO.Compression.GZipStream($memory,[IO.Compression.CompressionMode]::Decompress)
$reader=New-Object IO.StreamReader($gzip,[Text.Encoding]::UTF8)
$payload=$reader.ReadToEnd()
$reader.Dispose()
$prefix=($payload -split '\r?\nif\(\$env:RO_HEADLESS',2)[0]
. ([scriptblock]::Create($prefix))
$source=([regex]::Match($payload,"FromBase64String\('([^']+)'\)")).Groups[1].Value
$code=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($source))
$snapshot=[RestoreOrganization.Engine]::ReadSnapshot($json)
$plan=[RestoreOrganization.Engine]::Preview($root,$snapshot,$null)
[RestoreOrganization.Engine]::Restore($plan, {param($saved,$target) [RestoreOrganization.Engine]::WriteScript($saved,$target,$code)},$null) | Out-Null
if(-not [IO.File]::Exists([IO.Path]::Combine($root,'Books\one.epub'))) {throw 'Embedded restore did not restore the saved location.'}
$undo=@(Get-ChildItem -LiteralPath $root -Filter 'Undo - *.cmd')
if($undo.Count -ne 1){throw 'Independent undo script missing.'}
$undoText=[IO.File]::ReadAllText($undo[0].FullName)
$undoMemory=New-Object IO.MemoryStream(,[Convert]::FromBase64String(($undoText -split '\r?\n:RO_PAYLOAD\r?\n',2)[1]))
$undoGzip=New-Object IO.Compression.GZipStream($undoMemory,[IO.Compression.CompressionMode]::Decompress)
$undoReader=New-Object IO.StreamReader($undoGzip,[Text.Encoding]::UTF8)
$undoPayload=$undoReader.ReadToEnd();$undoReader.Dispose()
$matches=[regex]::Matches($undoPayload,"FromBase64String\('([^']+)'\)")
$undoJson=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($matches[1].Groups[1].Value))
$undoSnapshot=[RestoreOrganization.Engine]::ReadSnapshot($undoJson)
$undoPlan=[RestoreOrganization.Engine]::Preview($root,$undoSnapshot,$null)
[RestoreOrganization.Engine]::Restore($undoPlan,{param($saved,$target) [RestoreOrganization.Engine]::WriteScript($saved,$target,$code)},$null) | Out-Null
if(-not [IO.File]::Exists([IO.Path]::Combine($root,'copied.epub'))) {throw 'Embedded undo did not restore the preceding arrangement.'}
'PASS: independent embedded restore and undo under Windows PowerShell 5.1'

if([IO.Directory]::Exists([IO.Path]::Combine($root,'.restore-organization'))) {throw 'Embedded restore or undo left a recovery folder after successful completion.'}
