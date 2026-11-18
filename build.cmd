@echo off
setlocal
cd /d "%~dp0"

echo TLOUP1 portable dosyalari hazirlaniyor...
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Build-Portable.ps1
if errorlevel 1 goto :fail

echo Bitti: portable\TLOUP1\TLOUP1.exe
goto :eof

:fail
echo.
echo Islem basarisiz oldu.
exit /b 1
