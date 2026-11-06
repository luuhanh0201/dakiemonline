# Renders every equipment combination in every action into one image. Close the Unity Editor first.
param(
    [Parameter(Mandatory)][string]$Output,
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe',
    [string]$LogFile = (Join-Path $env:TEMP 'dakiem-gear-sheet.log')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$arguments = @(
    '-batchmode', '-quit', '-buildTarget', 'Win64',
    '-projectPath', "`"$(Join-Path $root 'client')`"",
    '-executeMethod', 'DaKiem.Client.Editor.GearSheet.Run',
    '-gearSheetOut', "`"$Output`"",
    '-logFile', "`"$LogFile`""
)

$process = Start-Process -FilePath $Unity -ArgumentList $arguments -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Gear sheet failed, see $LogFile" }

Write-Output "Wrote $Output"
