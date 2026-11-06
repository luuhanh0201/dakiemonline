# Builds the Windows version into client/Builds/PC. Close the Unity Editor first.
param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe',
    [string]$LogFile = (Join-Path $env:TEMP 'dakiem-pc-build.log')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'sync-rules.ps1')

$arguments = @(
    '-batchmode', '-nographics', '-buildTarget', 'Win64',
    '-projectPath', "`"$(Join-Path $root 'client')`"",
    '-executeMethod', 'DaKiem.Client.Editor.PcBuild.Run',
    '-logFile', "`"$LogFile`""
)

$process = Start-Process -FilePath $Unity -ArgumentList $arguments -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "PC build failed, see $LogFile" }

Write-Output "Built $(Join-Path $root 'client/Builds/PC/DaKiemOnline.exe')"
