@echo off
REM ========================================================
REM Build-Skript fuer GuardSwitch
REM
REM Voraussetzungen:
REM   - .NET 8 SDK installiert (https://dot.net)
REM   - Inno Setup 6 installiert (https://jrsoftware.org/isdl.php)
REM
REM Dieses Skript:
REM   1. Baut Service und Tray als self-contained Single-File
REM   2. Erstellt einen Windows-Installer (.exe)
REM ========================================================

setlocal
cd /d "%~dp0"

echo.
echo === [1/3] .NET-Projekte werden gebaut ===
echo.

dotnet publish src\WgAutoswitch.Service\WgAutoswitch.Service.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 goto :error

dotnet publish src\WgAutoswitch.Tray\WgAutoswitch.Tray.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 goto :error

echo.
echo === [2/3] Installer wird gebaut ===
echo.

REM Inno Setup Compiler suchen
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if "%ISCC%"=="" (
    echo FEHLER: Inno Setup 6 wurde nicht gefunden.
    echo Bitte installieren von: https://jrsoftware.org/isdl.php
    goto :error
)

"%ISCC%" installer\guardswitch.iss
if errorlevel 1 goto :error

echo.
echo === [3/3] Fertig ===
echo.
echo Der Installer liegt unter:
echo   installer\output\guardswitch-setup-1.0.0.exe
echo.
goto :end

:error
echo.
echo *** Build fehlgeschlagen ***
exit /b 1

:end
endlocal
