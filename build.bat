@echo off
echo Building ATF Wyvern Mod...
dotnet build ATFWyvernMod.csproj -c Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build successful!
    echo DLL location: bin\Release\net472\ATFWyvernMod.dll
    echo.
    echo To install, copy the DLL to:
    echo C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option\BepInEx\plugins\
) else (
    echo.
    echo Build failed!
    pause
    exit /b 1
)

pause
