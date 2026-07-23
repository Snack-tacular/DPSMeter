@echo off
echo Building DPS Meter...
cd /d "%~dp0"
dotnet build DpsMeter.csproj -c Release
if %ERRORLEVEL% EQU 0 (
    echo Copying to BepInEx plugins...
    copy /Y "bin\Release\netstandard2.1\DpsMeter.dll" "..\BepInEx\plugins\DpsMeter\DpsMeter.dll"
    echo Done! Restart Sineus Arena to load the updated plugin.
) else (
    echo Build FAILED. See errors above.
)
pause
