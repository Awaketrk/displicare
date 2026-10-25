@echo off
setlocal
set VERSION=%1
if "%VERSION%"=="" (
    echo Usage: build-release.bat ^<version^> [-Force]   e.g. build-release.bat 2.1.0
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1" -Version "%VERSION%" %2 %3
endlocal
