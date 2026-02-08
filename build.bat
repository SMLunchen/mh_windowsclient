@echo off
echo ========================================
echo Meshhessen Client - Build Script
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
dotnet publish MeshhessenClient\MeshhessenClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o publish
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
echo %CD%\publish\MeshhessenClient.exe
echo.
echo File size:
dir publish\MeshhessenClient.exe | find "MeshhessenClient.exe"
echo.
pause
