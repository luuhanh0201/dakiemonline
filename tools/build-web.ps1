# Builds the web version into client/Builds/Web. Close the Unity Editor first.
# The game server serves that folder: dotnet run --project server/Game.Server, then open http://<pc>:7777/
param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe',
    [string]$LogFile = (Join-Path $env:TEMP 'dakiem-web-build.log')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'sync-rules.ps1')

$arguments = @(
    '-batchmode', '-nographics', '-buildTarget', 'WebGL',
    '-projectPath', "`"$(Join-Path $root 'client')`"",
    '-executeMethod', 'DaKiem.Client.Editor.WebBuild.Run',
    '-logFile', "`"$LogFile`""
)

$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Web build failed, see $LogFile" }

Write-Output "Built $(Join-Path $root 'client/Builds/Web')"
