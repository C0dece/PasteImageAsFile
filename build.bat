@echo off
chcp 65001 >nul 2>&1

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "dist" mkdir dist

echo Compiling PasteImageAsFile...
set DOTNET_DIR=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
if not exist "%DOTNET_DIR%\csc.exe" set DOTNET_DIR=C:\Windows\Microsoft.NET\Framework\v4.0.30319

%CSC% /target:winexe /optimize+ /platform:anycpu /reference:"%DOTNET_DIR%\System.Xaml.dll" /reference:"%DOTNET_DIR%\WPF\WindowsBase.dll" /reference:"%DOTNET_DIR%\WPF\PresentationCore.dll" /win32icon:assets\app.ico /out:dist\PasteImageAsFile.exe src\*.cs

if %errorlevel% neq 0 (
    echo Build FAILED.
    exit /b 1
)

echo.
echo Build OK: dist\PasteImageAsFile.exe
echo.

:: Copy icon next to exe for runtime loading
copy /Y assets\app.ico dist\app.ico >nul

echo Done.
