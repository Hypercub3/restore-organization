@echo off
setlocal DisableDelayedExpansion
set "app=%~dp0dist\Restore Organization.exe"
if not exist "%app%" (
    echo Restore Organization.exe is missing from the dist folder.
    echo Keep this launcher and the complete dist folder together.
    pause
    exit /b 1
)
start "" /D "%~dp0dist" "%app%"
