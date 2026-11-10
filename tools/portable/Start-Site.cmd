@echo off
setlocal
cd /d "%~dp0"
set ASPNETCORE_ENVIRONMENT=Production
set DOTNET_ENVIRONMENT=Production
echo.
echo PULSE - Local website
echo Address: http://localhost:5196/
echo Keep this window open while viewing the website.
echo Press Ctrl+C or close this window to stop the website.
echo.
if not exist "%~dp0FitnessCenter.Lite.exe" (
    echo Application not found. Extract the entire ZIP before starting.
    pause
    exit /b 1
)
start "" /b powershell.exe -NoProfile -WindowStyle Hidden -Command "$siteUrl='http://localhost:5196/'; for($attempt=0; $attempt -lt 30; $attempt++) { try { $response=Invoke-WebRequest -Uri $siteUrl -UseBasicParsing -TimeoutSec 1; if($response.StatusCode -eq 200) { Start-Process $siteUrl; exit } } catch {}; Start-Sleep -Milliseconds 300 }"
"%~dp0FitnessCenter.Lite.exe" --urls "http://localhost:5196"
if errorlevel 1 (
    echo.
    echo Could not start the website. Port 5196 may already be in use.
    echo Close another running copy and try again.
    pause
)
endlocal
