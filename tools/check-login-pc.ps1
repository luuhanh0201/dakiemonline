$ErrorActionPreference = 'Stop'
$checkRoot = Split-Path -Parent $PSScriptRoot
$checkOutput = Join-Path $checkRoot 'client/Logs/login-ui-pc'
$null = New-Item -ItemType Directory -Force -Path $checkOutput
& (Join-Path $PSScriptRoot 'build-pc.ps1') -LogFile (Join-Path $checkOutput 'build.log')
$checkArgs = @('-account-ui-smoke',$checkOutput,'-screen-width','1170','-screen-height','540','-logFile',(Join-Path $checkOutput 'player.log'))
$checkProcess = Start-Process (Join-Path $checkRoot 'client/Builds/PC/DaKiemOnline.exe') -ArgumentList $checkArgs -WindowStyle Hidden -PassThru
if (-not $checkProcess.WaitForExit(60000)) { throw "UI check timed out; PID $($checkProcess.Id), see $checkOutput/player.log" }
if ($checkProcess.ExitCode -ne 0) { throw "UI check failed: $checkOutput/checks.txt" }
if (Select-String -Path (Join-Path $checkOutput 'player.log') -Pattern 'is not supported or has been stripped|Missing UI art:' -Quiet) { throw "Rendering resource warning: $checkOutput/player.log" }
Get-Content -Encoding UTF8 (Join-Path $checkOutput 'checks.txt')
