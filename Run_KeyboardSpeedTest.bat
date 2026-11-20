@echo off
rem Keyboard Speed Test - compare two keyboards
rem Starts the app from source (src*.cs) with the built-in Windows PowerShell 5.1. / Chay ung dung tu ma nguon.
rem The console window is hidden; errors are shown in a message box. / Loi se hien trong hop thoai.
set "SCRIPT=%~dp0KeyboardSpeedTest.ps1"
start "" powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%SCRIPT%"
