param([switch]$IncludeShell)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
& '.\build.ps1' -SkipShell
$testArgs=@('/nologo','/target:exe','/platform:anycpu','/out:dist\EngineTests.exe','/reference:dist\Restore Organization.exe',
 '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','tests\EngineTests.cs')
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') @testArgs
if($LASTEXITCODE){throw 'Test compilation failed'}
& '.\dist\EngineTests.exe'
if($LASTEXITCODE){throw 'Engine tests failed'}
if($IncludeShell) {
 $gcc=Join-Path $PSScriptRoot '.tools\w64devkit\bin\g++.exe'
 $env:Path=(Split-Path -Parent $gcc)+';'+$env:Path
 & $gcc '-std=c++17' '-O2' '-static' '-static-libgcc' '-static-libstdc++' 'tests\ShellTests.cpp' '-o' 'build\ShellTests.exe' '-lole32' '-luuid' '-lshell32'
 if($LASTEXITCODE){throw 'Shell test compilation failed'}
 & '.\build\ShellTests.exe'
 if($LASTEXITCODE){throw 'Shell tests failed'}
}
