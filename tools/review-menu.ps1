# Renders the main menu, character, quests, settings and bag screens with sample data into pictures.
# Close the Unity Editor first.
param([string]$OutputDirectory = 'client/Logs/menu-review')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$null = New-Item -ItemType Directory -Force -Path $output
$arguments = @('-batchmode', '-quit', '-projectPath', "`"$root/client`"", '-executeMethod', 'DaKiem.Client.Editor.MenuScreenReview.Run', '-menuReviewOut', "`"$output`"", '-logFile', "`"$output/editor.log`"")
$process = Start-Process 'C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Unity.exe' -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "Menu review failed: $output/editor.log" }
Write-Output "Menu review: $output"
