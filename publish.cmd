@echo off
rem Publishes LibTreeMapView into publish\win-x64 (or publish\win-x64-framework).
rem
rem   publish.cmd              self-contained build (no .NET runtime needed on the target)
rem   publish.cmd framework    smaller build that needs the .NET 10 desktop runtime
rem
rem (ASCII only: cmd.exe parses this file with the OEM code page.)

setlocal
cd /d "%~dp0"

set PROFILE=win-x64
if /i "%~1"=="framework" set PROFILE=win-x64-framework

echo Publishing with profile "%PROFILE%" ...
dotnet publish src\LibTreeMapView\LibTreeMapView.csproj ^
    -f net10.0-windows10.0.19041.0 ^
    -p:PublishProfile=%PROFILE% ^
    --nologo || exit /b 1

echo.
echo Output: %CD%\publish\%PROFILE%
echo Run it with:
echo     publish\%PROFILE%\LibTreeMapView.exe [path\to\your.lib]
echo.
echo To hand it to someone else, zip that folder:
echo     powershell -Command "Compress-Archive -Path publish\%PROFILE%\* -DestinationPath LibTreeMapView-%PROFILE%.zip -Force"
endlocal
