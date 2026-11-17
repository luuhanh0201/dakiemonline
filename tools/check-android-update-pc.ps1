param([int]$Width = 1170, [int]$Height = 540)
$ErrorActionPreference = 'Stop'
$checkRoot = Split-Path -Parent $PSScriptRoot
$checkOutput = Join-Path $checkRoot ('client/Logs/android-update/pc-' + $Width + 'x' + $Height)
$null = New-Item -ItemType Directory -Force -Path $checkOutput
$checkArguments = @('-android-update-smoke', ('"' + $checkOutput + '"'), '-screen-width', $Width, '-screen-height', $Height, '-logFile', ('"' + (Join-Path $checkOutput 'player.log') + '"'))
$checkPlayer = Start-Process -FilePath (Join-Path $checkRoot 'client/Builds/PC/DaKiemOnline.exe') -ArgumentList $checkArguments -WindowStyle Hidden -PassThru
if (-not $checkPlayer.WaitForExit(60000)) { throw 'APK update UI check timed out; inspect the player log.' }
if ($checkPlayer.ExitCode -ne 0) { throw 'APK update UI check failed; inspect checks.txt.' }
$checkResult = Get-Content -LiteralPath (Join-Path $checkOutput 'checks.txt') -Raw
if ($checkResult -notlike 'RESULT PASS:*') { throw $checkResult }
Write-Output ($Width.ToString() + 'x' + $Height + ': ' + $checkResult.Trim())
