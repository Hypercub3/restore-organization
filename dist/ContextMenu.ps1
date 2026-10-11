param([ValidateSet('Enable','Disable','Status','Validate')][string]$Action = 'Status')
$ErrorActionPreference = 'Stop'
$packageName = 'Phill.RestoreOrganization.ContextMenu'
$base = Join-Path $env:LOCALAPPDATA 'RestoreOrganization'
$stateFile = Join-Path $base 'context-menu.json'

function Get-NativeArchitecture {
    if (-not ('RestoreOrganization.NativeSystem' -as [type])) {
        Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; namespace RestoreOrganization { public static class NativeSystem { [DllImport("kernel32.dll")] public static extern void GetNativeSystemInfo(IntPtr info); } }'
    }
    $buffer = [Runtime.InteropServices.Marshal]::AllocHGlobal(64)
    try {
        [RestoreOrganization.NativeSystem]::GetNativeSystemInfo($buffer)
        switch ([Runtime.InteropServices.Marshal]::ReadInt16($buffer)) {
            0 { return 'x86' }; 9 { return 'x64' }; 12 { return 'arm64' }
            default { throw 'This Windows processor architecture is not supported.' }
        }
    } finally { [Runtime.InteropServices.Marshal]::FreeHGlobal($buffer) }
}
function Invoke-CertificateTrust([string]$Helper,[string]$Operation) {
    try {
        $process = Start-Process -FilePath $Helper -ArgumentList $Operation -Verb RunAs -WindowStyle Hidden -PassThru -Wait -ErrorAction Stop
    } catch {
        if ($_.Exception.ToString() -match '1223|canceled|cancelled') {
            throw 'The Windows administrator prompt was cancelled. Enabling the menu requires administrator approval to trust its signing certificate on this computer.'
        }
        throw "Windows could not start the certificate installer: $($_.Exception.Message)"
    }
    switch ($process.ExitCode) {
        0 { return }
        3 { return 'Signing certificate retained because another Windows account still has the menu installed.' }
        11 { throw 'The signing certificate is not valid on this date. Check the computer clock or obtain a newer application build.' }
        12 { throw 'Certificate trust requires a Windows administrator account.' }
        13 { throw 'Windows could not check package registrations. Certificate trust was retained.' }
        14 { throw 'Windows prevented recording certificate ownership. Certificate installation was rolled back.' }
        default { throw "Windows prevented updating the computer Trusted People certificate store (helper exit code $($process.ExitCode)). Contact your administrator if this computer is managed." }
    }
}
function Remove-OwnedMachineTrust([string]$Helper,[string]$Thumb) {
    $marker = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\RestoreOrganization\ContextMenuCertificates\' + $Thumb
    if ((Test-Path -LiteralPath $marker) -and (Test-Path -LiteralPath ('Cert:\LocalMachine\TrustedPeople\' + $Thumb))) {
        Invoke-CertificateTrust $Helper '--remove'
    }
}
function Get-ReleaseInfo {
    if ([Environment]::OSVersion.Version.Build -lt 19041) { throw 'The optional context menu requires Windows 10 version 2004 or later, or Windows 11.' }
    $architecture = Get-NativeArchitecture
    $native = Join-Path $PSScriptRoot ('architectures\' + $architecture)
    $common = @('Restore Organization.exe','Restore Organization.exe.config','CertificateTrust.exe','ContextMenu.cer','ContextMenu.ps1')
    foreach ($file in $common) {
        if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $file) -PathType Leaf)) { throw "Required application file is missing: $file. Extract the complete ZIP first." }
    }
    foreach ($file in @('SnapshotCommand.dll','ContextMenu.msix')) {
        if (-not (Test-Path -LiteralPath (Join-Path $native $file) -PathType Leaf)) { throw "Required $architecture component is missing: $file. Extract the complete ZIP first." }
    }
    $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2 (Join-Path $PSScriptRoot 'ContextMenu.cer')
    $pinned = (& (Join-Path $PSScriptRoot 'CertificateTrust.exe') --thumbprint | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $pinned -ne $certificate.Thumbprint) { throw 'The public signing certificate does not match the certificate installer. Extract a complete matching application build.' }
    if ($certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -lt (Get-Date)) { throw 'The signing certificate is not valid on this date. Check the computer clock or obtain a newer application build.' }
    $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $native 'ContextMenu.msix')
    if (-not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $pinned) { throw 'The context-menu package has an unexpected or missing signing certificate.' }
    # UnknownError is allowed only before trust is installed; afterwards Windows must validate the signature.
    if ($signature.Status -notin @('Valid','UnknownError','NotTrusted')) { throw "The context-menu package signature is invalid: $($signature.StatusMessage)" }
    foreach ($asset in @('StoreLogo.png','Logo150.png','Logo44.png')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot ('Assets\' + $asset)) -PathType Leaf)) { throw "Required image is missing: $asset. Extract the complete ZIP first." }
    }
    return @{ Architecture=$architecture; Native=$native; Common=$common; Thumbprint=$pinned }
}
try {
    if ($Action -eq 'Status') {
        $existing = @(Get-AppxPackage -Name $packageName -ErrorAction Stop)
        if ($existing.Count) {
            if (Test-Path -LiteralPath $stateFile) {
                try {
                    $saved = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
                    if ($saved.Thumbprint -match '^[A-Fa-f0-9]{40}$' -and -not (Test-Path -LiteralPath ('Cert:\LocalMachine\TrustedPeople\' + $saved.Thumbprint))) {
                        'Menu registered, but certificate trust is missing. Click Enable context menu to repair it.'; exit 0
                    }
                } catch {}
            }
            'Enabled for this Windows account.'
        } else { 'Not enabled.' }
        exit 0
    }
    $oldState = $null
    if (Test-Path -LiteralPath $stateFile) {
        try { $oldState = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json } catch { Write-Warning 'Previous installation state is unreadable; Windows package registration will be checked directly.' }
    }
    if ($Action -eq 'Disable') {
        Get-AppxPackage -Name $packageName -ErrorAction Stop | Remove-AppxPackage -ErrorAction Stop
        if (Test-Path -LiteralPath 'HKCU:\Software\RestoreOrganization\ContextMenu') { Remove-Item -LiteralPath 'HKCU:\Software\RestoreOrganization\ContextMenu' -ErrorAction Stop }
        if (Test-Path -LiteralPath $stateFile) { Remove-Item -LiteralPath $stateFile -Force }
        $message = 'Context menu disabled. Existing restore scripts still work.'
        try {
            $helper = Join-Path $PSScriptRoot 'CertificateTrust.exe'
            if (Test-Path -LiteralPath $helper) {
                $thumb = (& $helper --thumbprint | Out-String).Trim()
                if ($thumb -match '^[A-Fa-f0-9]{40}$') { $note = Remove-OwnedMachineTrust $helper $thumb; if ($note) { $message += ' ' + $note } }
            } else { $message += ' The certificate installer is missing; signing certificate trust was retained.' }
        } catch { $message += ' The menu was removed, but signing certificate trust was retained: ' + $_.Exception.Message }
        $message
        exit 0
    }
    $release = Get-ReleaseInfo
    if ($Action -eq 'Validate') { "Application components and signing certificate checked. Native Windows architecture: $($release.Architecture)."; exit 0 }
    # Copy first: the menu continues to work after the extracted ZIP folder is moved or deleted.
    $target = Join-Path $base ('releases\' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($file in $release.Common) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $target $file) -ErrorAction Stop }
    foreach ($file in @('SnapshotCommand.dll','ContextMenu.msix')) { Copy-Item -LiteralPath (Join-Path $release.Native $file) -Destination (Join-Path $target $file) -ErrorAction Stop }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets') -Destination (Join-Path $target 'Assets') -Recurse -ErrorAction Stop
    # Keep native bundles so the installed creator also supports repair/re-enable.
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'architectures') -Destination (Join-Path $target 'architectures') -Recurse -ErrorAction Stop
    $certPath = 'Cert:\LocalMachine\TrustedPeople\' + $release.Thumbprint
    $helper = Join-Path $target 'CertificateTrust.exe'
    $hadPackage = @(Get-AppxPackage -Name $packageName -ErrorAction Stop).Count -gt 0
    $addedTrust = $false
    try {
        if (-not (Test-Path -LiteralPath $certPath)) {
            Invoke-CertificateTrust $helper '--install'
            $addedTrust = $true
        }
        $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $target 'ContextMenu.msix')
        if ($signature.Status -ne 'Valid') { throw "Windows cannot validate the signed package: $($signature.StatusMessage)" }
        # This process stays unelevated, so an administrator's credentials in UAC cannot redirect registration to that administrator.
        Add-AppxPackage -Path (Join-Path $target 'ContextMenu.msix') -ForceUpdateFromAnyVersion -ErrorAction Stop
        New-Item -Path 'HKCU:\Software\RestoreOrganization\ContextMenu' -Force | Out-Null
        New-ItemProperty -LiteralPath 'HKCU:\Software\RestoreOrganization\ContextMenu' -Name Location -Value $target -PropertyType String -Force | Out-Null
        @{ Thumbprint=$release.Thumbprint; Location=$target; Architecture=$release.Architecture; Version=2 } |
            ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
    } catch {
        $installError = $_.Exception.Message
        if ($addedTrust -and -not $hadPackage) {
            try { Remove-OwnedMachineTrust $helper $release.Thumbprint | Out-Null } catch { $installError += ' Certificate cleanup: ' + $_.Exception.Message }
        }
        throw $installError
    }
    # Clean up the old installer's redundant per-account trust, only when its state says it owned that entry.
    if ($oldState -and $oldState.AddedCertificate -and $oldState.Thumbprint -match '^[A-Fa-f0-9]{40}$') {
        $oldCert = 'Cert:\CurrentUser\TrustedPeople\' + $oldState.Thumbprint
        if (Test-Path -LiteralPath $oldCert) { Remove-Item -LiteralPath $oldCert -Force -ErrorAction SilentlyContinue }
    }
    'Context menu enabled for this Windows account. If Explorer has not refreshed, sign out and back in.'
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    [Console]::Error.WriteLine('The portable creator and generated restore scripts remain available. On managed PCs, an administrator may need to allow signed package installation.')
    exit 1
}
