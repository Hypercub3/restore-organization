$ErrorActionPreference='Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$scriptPath=Join-Path (Get-Location) 'dist\ContextMenu.ps1'
$ps64=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$checks=0
function Check([bool]$Condition,[string]$Description) {
 if(-not $Condition){throw "FAIL: $Description"}
 $script:checks++; "PASS: $Description"
}
$thumb=(& '.\dist\CertificateTrust.exe' --thumbprint | Out-String).Trim()
$cert=New-Object Security.Cryptography.X509Certificates.X509Certificate2 (Join-Path (Get-Location) 'dist\ContextMenu.cer')
Check ($thumb -eq $cert.Thumbprint) 'elevated helper pins the shipped public certificate'
& '.\dist\CertificateTrust.exe' --arbitrary-certificate
Check ($LASTEXITCODE -eq 2) 'helper rejects arbitrary operations'
$principal=New-Object Security.Principal.WindowsPrincipal ([Security.Principal.WindowsIdentity]::GetCurrent())
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
 & '.\dist\CertificateTrust.exe' --install
 Check ($LASTEXITCODE -eq 12) 'unelevated certificate import refuses mutation'
}
$output=& $ps64 -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $scriptPath -Action Validate 2>&1
Check ($LASTEXITCODE -eq 0) 'complete application validates under stock Windows PowerShell'
$ps32=Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
if(Test-Path -LiteralPath $ps32){
 $other=& $ps32 -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $scriptPath -Action Validate 2>&1
 Check ($LASTEXITCODE -eq 0 -and ($other -join ' ') -match 'architecture: x64') '32-bit installer detects native x64 Windows rather than process architecture'
}
# Load just the helper functions; stub Start-Process so error paths do not elevate or install anything.
$source=Get-Content -LiteralPath $scriptPath -Raw
$main=[regex]::Match($source,'(?m)^try \{')
Invoke-Expression $source.Substring(0,$main.Index)
function Start-Process {
 param($FilePath,$ArgumentList,$Verb,$WindowStyle,[switch]$PassThru,[switch]$Wait,$ErrorAction)
 Check ($Verb -eq 'RunAs' -and $WindowStyle -eq 'Hidden' -and $ArgumentList -eq '--install') 'installer requests narrowly scoped hidden UAC helper'
 throw [ComponentModel.Win32Exception]::new(1223)
}
$errorMessage=''
try { Invoke-CertificateTrust 'dummy.exe' '--install' } catch { $errorMessage=$_.Exception.Message }
Check ($errorMessage -match 'administrator prompt was cancelled') 'UAC cancellation explains the required administrator approval'
function Start-Process { param($FilePath,$ArgumentList,$Verb,$WindowStyle,[switch]$PassThru,[switch]$Wait,$ErrorAction); [pscustomobject]@{ExitCode=3} }
$note=Invoke-CertificateTrust 'dummy.exe' '--remove'
Check ($note -match 'another Windows account') 'certificate retention for other accounts is explained'
function Start-Process { param($FilePath,$ArgumentList,$Verb,$WindowStyle,[switch]$PassThru,[switch]$Wait,$ErrorAction); [pscustomobject]@{ExitCode=11} }
$errorMessage=''
try { Invoke-CertificateTrust 'dummy.exe' '--install' } catch {$errorMessage=$_.Exception.Message}
Check ($errorMessage -match 'not valid on this date') 'certificate validity errors explain clock and expired build'
Remove-Item Function:\Start-Process
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach($arch in @('x64','x86','arm64')){
 $file=Join-Path (Get-Location) ('dist\architectures\'+$arch+'\ContextMenu.msix')
 $signature=Get-AuthenticodeSignature -LiteralPath $file
 Check ($signature.SignerCertificate.Thumbprint -eq $thumb) "$arch package signer matches pinned certificate"
 $archive=[IO.Compression.ZipFile]::OpenRead($file)
 try {
  $reader=[IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
  try {$xml=[xml]$reader.ReadToEnd()} finally {$reader.Dispose()}
  Check ($xml.Package.Identity.ProcessorArchitecture -eq $arch) "$arch package manifest selects matching architecture"
  Check ($null -ne $archive.GetEntry('SnapshotCommand.dll') -and @($archive.Entries | Where-Object { [Uri]::UnescapeDataString($_.FullName) -eq 'Restore Organization.exe' }).Count -eq 1) "$arch package is complete without external-location registration"
 } finally {$archive.Dispose()}
 $binary=[IO.File]::ReadAllBytes((Join-Path (Get-Location) ('dist\architectures\'+$arch+'\SnapshotCommand.dll')))
 $pe=[BitConverter]::ToInt32($binary,0x3c);$machine=[BitConverter]::ToUInt16($binary,$pe+4)
 $expected=@{x64=0x8664;x86=0x14c;arm64=0xaa64}[$arch]
 Check ($machine -eq $expected) "$arch DLL contains matching native machine code"
}
$fixture=Join-Path (Get-Location) ('tests\work\installer-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
foreach($file in @('Restore Organization.exe','Restore Organization.exe.config','CertificateTrust.exe','ContextMenu.cer','ContextMenu.ps1')){
 Copy-Item -LiteralPath (Join-Path 'dist' $file) -Destination (Join-Path $fixture $file)
}
$ErrorActionPreference='Continue'
$output=& $ps64 -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $fixture 'ContextMenu.ps1') -Action Validate 2>&1
$ErrorActionPreference='Stop'
Check ($LASTEXITCODE -eq 1 -and ($output -join ' ') -match 'Extract the complete ZIP') 'incomplete extraction produces actionable error'
New-Item -ItemType Directory -Path (Join-Path $fixture 'architectures\x64') -Force | Out-Null
Copy-Item -LiteralPath 'dist\architectures\x64\SnapshotCommand.dll' -Destination (Join-Path $fixture 'architectures\x64\SnapshotCommand.dll')
[IO.File]::WriteAllText((Join-Path $fixture 'architectures\x64\ContextMenu.msix'),'invalid package')
$ErrorActionPreference='Continue'
$output=& $ps64 -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $fixture 'ContextMenu.ps1') -Action Validate 2>&1
$ErrorActionPreference='Stop'
Check ($LASTEXITCODE -eq 1 -and ($output -join ' ') -match 'signing certificate') 'unsigned or corrupt package rejected before trust is installed'
"PASS: $checks installer checks"
