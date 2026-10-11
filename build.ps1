param([switch]$SkipShell)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler is required.' }
New-Item -ItemType Directory -Path build,dist -Force | Out-Null
$uiSource = [regex]::Replace((Get-Content -LiteralPath 'src\RestoreUI.cs' -Raw), '(?m)^using [^\r\n]+;\r?\n', '')
$runtimeSource = 'using System.Drawing; using System.Threading.Tasks; using System.Windows.Forms;' + [Environment]::NewLine + (Get-Content -LiteralPath 'src\Core.cs' -Raw) + [Environment]::NewLine + $uiSource
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'build\RestoreSource.cs.txt'),$runtimeSource,[Text.UTF8Encoding]::new($false))
$compilerArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/out:dist\Restore Organization.exe',
    '/win32manifest:src\app.manifest','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',
    '/resource:build\RestoreSource.cs.txt,RestoreSource','src\Core.cs','src\RestoreUI.cs','src\App.cs','src\FolderPicker.cs')
& $compiler @compilerArgs
if ($LASTEXITCODE) { throw 'Application compilation failed.' }
@'
<?xml version="1.0" encoding="utf-8"?>
<configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup>
<runtime><AppContextSwitchOverrides value="Switch.System.IO.UseLegacyPathHandling=false;Switch.System.IO.BlockLongPaths=false"/></runtime></configuration>
'@ | Set-Content -LiteralPath 'dist\Restore Organization.exe.config' -Encoding UTF8
Copy-Item -LiteralPath 'src\ContextMenu.ps1' -Destination 'dist\ContextMenu.ps1' -Force
if (-not $SkipShell) {
    $toolBin = Join-Path $PSScriptRoot '.tools\llvm-mingw-20261006-ucrt-x86_64\bin'
    $sdkBin = Join-Path $PSScriptRoot '.tools\sdk\bin\10.0.28000.0\x64'
    foreach ($tool in @((Join-Path $toolBin 'clang++.exe'),(Join-Path $sdkBin 'makeappx.exe'),(Join-Path $sdkBin 'signtool.exe'))) {
        if (-not (Test-Path -LiteralPath $tool)) { throw "Build tool missing: $tool. See docs\BUILD.md." }
    }
    $openssl = 'C:\Program Files\Git\usr\bin\openssl.exe'
    if (-not (Test-Path -LiteralPath '.tools\signing.pfx')) {
        & $openssl req -x509 -newkey rsa:2048 -sha256 -days 1825 -nodes -subj '/CN=Restore Organization Local' -addext 'basicConstraints=critical,CA:FALSE' -addext 'keyUsage=critical,digitalSignature' -addext 'extendedKeyUsage=codeSigning' -keyout '.tools\signing.key' -out '.tools\signing.pem'
        if ($LASTEXITCODE) { throw 'Signing certificate generation failed.' }
        & $openssl pkcs12 -export -out '.tools\signing.pfx' -inkey '.tools\signing.key' -in '.tools\signing.pem' -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1 -CSP 'Microsoft Enhanced RSA and AES Cryptographic Provider' -passout 'pass:local-build-only'
        if ($LASTEXITCODE) { throw 'PFX generation failed.' }
    }
    & $openssl x509 -in '.tools\signing.pem' -outform DER -out 'dist\ContextMenu.cer'
    if ($LASTEXITCODE) { throw 'Public certificate export failed.' }
    & $compiler '/nologo' '/target:exe' '/platform:anycpu' '/optimize+' '/out:dist\CertificateTrust.exe' '/resource:dist\ContextMenu.cer,MenuCertificate' 'src\CertificateTrust.cs'
    if ($LASTEXITCODE) { throw 'Certificate helper compilation failed.' }
    New-Item -ItemType Directory -Path 'build\package\Assets','dist\Assets' -Force | Out-Null
    Add-Type -AssemblyName System.Drawing
    foreach ($asset in @(@('StoreLogo.png',50),@('Logo150.png',150),@('Logo44.png',44))) {
        $bitmap = [Drawing.Bitmap]::new([int]$asset[1],[int]$asset[1])
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.Clear([Drawing.Color]::FromArgb(33,70,115))
        $pen = [Drawing.Pen]::new([Drawing.Color]::White,[single]($asset[1]/14))
        $graphics.DrawRectangle($pen,[single]($asset[1]*0.2),[single]($asset[1]*0.28),[single]($asset[1]*0.6),[single]($asset[1]*0.45))
        $bitmap.Save((Join-Path $PSScriptRoot ('build\package\Assets\' + $asset[0])),[Drawing.Imaging.ImageFormat]::Png)
        $pen.Dispose();$graphics.Dispose();$bitmap.Dispose()
        Copy-Item -LiteralPath ('build\package\Assets\' + $asset[0]) -Destination ('dist\Assets\' + $asset[0]) -Force
    }
    foreach ($variant in @(@('x64','x86_64'),@('x86','i686'),@('arm64','aarch64'))) {
        $arch=$variant[0];$triple=$variant[1]
        $native=Join-Path 'dist\architectures' $arch
        New-Item -ItemType Directory -Path $native -Force | Out-Null
        & (Join-Path $toolBin ($triple+'-w64-mingw32-clang++.exe')) '-std=c++17' '-O2' '-shared' '-static' '-static-libgcc' '-static-libstdc++' '-s' 'src\SnapshotCommand.cpp' '-o' (Join-Path $native 'SnapshotCommand.dll') '-lole32' '-luuid' '-lshlwapi' '-lshell32' '-ladvapi32' '-Wl,--kill-at'
        if ($LASTEXITCODE) { throw "$arch shell extension compilation failed." }
        $manifest=(Get-Content -LiteralPath 'src\AppxManifest.xml' -Raw).Replace('ProcessorArchitecture="x64"',('ProcessorArchitecture="'+$arch+'"'))
        [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'build\package\AppxManifest.xml'),$manifest,[Text.UTF8Encoding]::new($false))
        foreach ($payload in @('Restore Organization.exe','Restore Organization.exe.config')) { Copy-Item -LiteralPath (Join-Path 'dist' $payload) -Destination (Join-Path 'build\package' $payload) -Force }
        Copy-Item -LiteralPath (Join-Path $native 'SnapshotCommand.dll') -Destination 'build\package\SnapshotCommand.dll' -Force
        & (Join-Path $sdkBin 'makeappx.exe') pack /o /d 'build\package' /p (Join-Path $native 'ContextMenu.msix')
        if ($LASTEXITCODE) { throw "$arch MSIX packaging failed." }
        & (Join-Path $sdkBin 'signtool.exe') sign /fd SHA256 /f '.tools\signing.pfx' /p 'local-build-only' (Join-Path $native 'ContextMenu.msix')
        if ($LASTEXITCODE) { throw "$arch MSIX signing failed." }
    }
    # Root copies support existing native test tools. Installer chooses the native bundle explicitly.
    foreach($file in @('SnapshotCommand.dll','ContextMenu.msix')) { Copy-Item -LiteralPath (Join-Path 'dist\architectures\x64' $file) -Destination (Join-Path 'dist' $file) -Force }
}
if (Test-Path -LiteralPath 'README.md') { Copy-Item -LiteralPath 'README.md' -Destination 'dist\README.md' -Force }
Write-Output 'Build complete: dist\Restore Organization.exe'
