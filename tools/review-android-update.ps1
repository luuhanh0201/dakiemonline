$ErrorActionPreference = 'Stop'
$reviewRoot = Split-Path -Parent $PSScriptRoot
$reviewArgs = @('-batchmode','-quit','-buildTarget','Android','-projectPath',('"' + (Join-Path $reviewRoot 'client') + '"'),'-executeMethod','DaKiem.Client.Editor.AndroidUpdateReview.Run','-logFile',('"' + (Join-Path $reviewRoot 'client/Logs/android-update/preview.log') + '"'))
$reviewProcess = Start-Process 'C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Unity.exe' -ArgumentList $reviewArgs -WindowStyle Hidden -PassThru -Wait
if ($reviewProcess.ExitCode -ne 0) { throw 'Android update preview failed; inspect client/Logs/android-update/preview.log.' }
Write-Output 'Update preview: client/Logs/android-update/preview/'
