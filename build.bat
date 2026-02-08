@echo off
echo ========================================
echo Meshtastic Windows Client - Build Script
echo ========================================
echo.

echo [1/3] Restoring NuGet packages...
dotnet restore
if %errorlevel% neq 0 (
    echo ERROR: NuGet restore failed!
    pause
    exit /b %errorlevel%
)

echo.
echo [2/3] Building Release configuration...
dotnet build -c Release
if %errorlevel% neq 0 (
    echo ERROR: Build failed!
    pause
    exit /b %errorlevel%
)

echo.
echo [3/3] Publishing standalone EXE...
dotnet publish MeshtasticClient\MeshtasticClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o publish
if %errorlevel% neq 0 (
    echo ERROR: Publish failed!
    pause
    exit /b %errorlevel%
)

echo.
echo ========================================
echo Build completed successfully!
echo ========================================
echo.
echo Executable location:
echo %CD%\publish\MeshtasticClient.exe
echo.
echo File size:
dir publish\MeshtasticClient.exe | find "MeshtasticClient.exe"
echo.
pause
