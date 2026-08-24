@echo off
rem Builds the sample static library (out\sample.lib) and import library (out\import.lib).
rem Run it from a "x64 Native Tools Command Prompt for VS", or just double-click it:
rem vcvars64.bat is located automatically through vswhere.
rem (ASCII only: cmd.exe parses this file with the OEM code page.)

setlocal
cd /d "%~dp0"

if not defined VCINSTALLDIR (
    for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do (
        call "%%i\VC\Auxiliary\Build\vcvars64.bat" >nul
    )
)

if not defined VCINSTALLDIR (
    echo MSVC build tools were not found.
    exit /b 1
)

if not exist out mkdir out

rem alpha/beta: with debug info (/Zi), gamma: function-level COMDATs (/Gy)
cl /nologo /c /EHsc /Zi /GS- /Fo:out\ /Fd:out\ alpha.cpp beta.cpp || exit /b 1
cl /nologo /c /EHsc /Gy /GS- /Fo:out\ gamma.cpp || exit /b 1
lib /nologo /OUT:out\sample.lib out\alpha.obj out\beta.obj out\gamma.obj || exit /b 1
lib /nologo /def:sampledll.def /out:out\import.lib /machine:x64 || exit /b 1

echo.
echo Created out\sample.lib and out\import.lib
echo Drop one on the app window, or pass it on the command line:
echo     LibTreeMapView.exe "%CD%\out\sample.lib"
endlocal
