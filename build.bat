@echo off
chcp 65001 >nul 2>&1

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "dist" mkdir dist

echo Compiling PasteImageAsFile...
%CSC% /target:winexe /optimize+ /platform:anycpu /win32icon:assets\app.ico /out:dist\PasteImageAsFile.exe src\*.cs

if %errorlevel% neq 0 (
    echo Build FAILED.
    pause
    exit /b 1
)

echo.
echo Build OK: dist\PasteImageAsFile.exe
echo.

:: Copy icon next to exe for runtime loading
copy /Y assets\app.ico dist\app.ico >nul

echo Done.
