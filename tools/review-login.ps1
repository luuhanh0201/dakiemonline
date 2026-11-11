param([string]$OutputDirectory = 'client/Logs/login-ui-review')
$ErrorActionPreference = 'Stop'
$reviewRoot = Split-Path -Parent $PSScriptRoot
$reviewOutput = [IO.Path]::GetFullPath((Join-Path $reviewRoot $OutputDirectory))
$null = New-Item -ItemType Directory -Force -Path $reviewOutput
$reviewArgs = @('-batchmode','-quit','-projectPath',"$reviewRoot/client",'-executeMethod','DaKiem.Client.Editor.LoginScreenReview.Run','-loginReviewOut',$reviewOutput,'-logFile',"$reviewOutput/editor.log")
$reviewProcess = Start-Process 'C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Unity.exe' -ArgumentList $reviewArgs -WindowStyle Hidden -PassThru -Wait
if ($reviewProcess.ExitCode -ne 0) { throw "Login UI review failed: $reviewOutput/editor.log" }
Write-Output "Login UI review: $reviewOutput"
