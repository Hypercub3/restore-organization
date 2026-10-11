$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$files=@('Restore Organization.exe','Restore Organization.exe.config','CertificateTrust.exe','ContextMenu.cer','ContextMenu.ps1')
foreach($file in $files){if(-not(Test-Path -LiteralPath (Join-Path 'dist' $file))){throw "Missing release file: $file"}}
foreach($arch in @('x64','x86','arm64')){
 foreach($file in @('SnapshotCommand.dll','ContextMenu.msix')){
  if(-not(Test-Path -LiteralPath (Join-Path ('dist\architectures\'+$arch) $file))){throw "Missing $arch component: $file"}
 }
}
$release=Join-Path $PSScriptRoot ('build\release-'+[Guid]::NewGuid().ToString('N')+'\Restore Organization')
New-Item -ItemType Directory -Path $release -Force | Out-Null
foreach($file in $files){Copy-Item -LiteralPath (Join-Path 'dist' $file) -Destination (Join-Path $release $file) -Force}
Copy-Item -LiteralPath 'README.md' -Destination 'dist\README.md' -Force
Copy-Item -LiteralPath 'README.md' -Destination (Join-Path $release 'README.md') -Force
Copy-Item -LiteralPath 'dist\Assets' -Destination (Join-Path $release 'Assets') -Recurse -Force
foreach($arch in @('x64','x86','arm64')){
 $destination=Join-Path $release ('architectures\'+$arch)
 New-Item -ItemType Directory -Path $destination -Force | Out-Null
 foreach($file in @('SnapshotCommand.dll','ContextMenu.msix')){
  Copy-Item -LiteralPath (Join-Path ('dist\architectures\'+$arch) $file) -Destination (Join-Path $destination $file)
 }
}
$allowed=@($files)+'README.md'
Get-ChildItem -LiteralPath $release -File | Where-Object {$_.Name -notin $allowed} | ForEach-Object {throw "Unexpected release file: $($_.Name)"}
$zip=Join-Path $PSScriptRoot 'Restore Organization - Windows.zip'
Compress-Archive -LiteralPath $release -DestinationPath $zip -Force
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  Restore Organization - Windows.zip" | Set-Content -LiteralPath 'Restore Organization - Windows.sha256' -Encoding ASCII
Write-Output "Packaged: $zip"
