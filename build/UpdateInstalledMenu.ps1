$ErrorActionPreference='Stop'
Add-AppxPackage -Path (Join-Path $PSScriptRoot '..\dist\architectures\x64\ContextMenu.msix') -ForceApplicationShutdown -ForceUpdateFromAnyVersion -ErrorAction Stop
& (Join-Path $PSScriptRoot '..\dist\ContextMenu.ps1') -Action Enable
if($LASTEXITCODE){exit $LASTEXITCODE}
