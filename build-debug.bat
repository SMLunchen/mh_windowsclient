@echo off
echo ========================================
echo Meshhessen Client - Debug Build
echo ========================================
echo.

echo [1/3] Restoring dependencies...
dotnet restore
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Restore failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [2/3] Building Debug configuration...
dotnet build -c Debug
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Build failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/3] Publishing Debug build...
dotnet publish -c Debug -r win-x64 --self-contained -o publish-debug
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Publish failed!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================
echo Build erfolgreich!
echo ========================================
echo.
echo Debug EXE: publish-debug\MeshhessenClient.exe
echo Log-Datei: publish-debug\meshhessen-client.log
echo.
echo HINWEIS: Debug-Build zeigt detaillierte Ausgaben in DebugView.
echo.
pause
