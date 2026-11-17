@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-web-server.ps1" -WithTunnel
if errorlevel 1 (
    echo Could not start the web server. See the error above.
    pause
    exit /b 1
)
powershell.exe -NoProfile -Command "$webTask = Get-ScheduledTask -TaskName 'DaKiemOnline-WebServer' -ErrorAction SilentlyContinue; if ($webTask) { if ($webTask.State -eq 'Disabled') { Enable-ScheduledTask -TaskName 'DaKiemOnline-WebServer' | Out-Null }; Start-ScheduledTask -TaskName 'DaKiemOnline-WebServer' }"
echo Da Kiem Online: https://dakiem.online/
