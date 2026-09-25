@echo off
chcp 65001 >nul
echo Building standalone exe (may take a few minutes the first time)...
echo.

dotnet publish -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o exe

if errorlevel 1 (
    echo.
    echo Something failed. If it complains about win-x64, try without --self-contained:
    echo    dotnet publish -c Release -o exe
    pause
    exit /b 1
)

echo.
echo Done. The exe is in: exe\AC2IconPatcher.exe
echo That file is standalone - it runs on machines without .NET installed.
echo.
pause
