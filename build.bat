@echo off
rem Builds Save Elon! with the C# compiler that comes with Windows. Nothing is downloaded.
echo Building Save Elon!...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 (
    echo.
    echo Build failed, sorry. Please open an issue with the text above.
    pause
    exit /b 1
)
copy /y "%~dp0bin\SaveElon.exe" "%~dp0SaveElon.exe" > nul
echo.
echo Done! SaveElon.exe is now right next to build.bat. Have fun!
pause
