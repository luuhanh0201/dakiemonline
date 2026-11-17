param([ValidateSet('android','windows')][string]$Platform = 'android')
$ErrorActionPreference = 'Stop'
$contentRepo = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'sync-rules.ps1')
$contentTarget = if ($Platform -eq 'android') { 'Android' } else { 'Win64' }
$contentLog = Join-Path $contentRepo ('client/Logs/content-' + $Platform + '-build.log')
$contentArgs = @('-batchmode','-quit','-buildTarget',$contentTarget,'-projectPath',('"' + (Join-Path $contentRepo 'client') + '"'),'-executeMethod','DaKiem.Client.Editor.ContentBuild.Run','-logFile',('"' + $contentLog + '"'))
$contentProcess = Start-Process 'C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Unity.exe' -ArgumentList $contentArgs -WindowStyle Hidden -Wait -PassThru
if ($contentProcess.ExitCode -ne 0) { throw ('Content export failed: ' + $contentLog) }
Write-Output ('Content ready: client/Builds/ContentSources/' + $Platform)
