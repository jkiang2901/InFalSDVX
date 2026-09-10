@echo off
title Building InFalSDVX...
echo ========================================================
echo   Compiling InFalSDVX using C# Compiler (csc.exe)
echo ========================================================
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /win32icon:app.ico /out:InFalSDVX.exe /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll InFalSDVX.cs
if %errorlevel% equ 0 (
    echo.
    echo [SUCCESS] File created successfully: InFalSDVX.exe
) else (
    echo.
    echo [ERROR] Build failed. Please check code errors.
)
echo ========================================================
