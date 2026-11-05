# Builds the Android APK into client/Builds/Android. Close the Unity Editor first.
param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe',
    [string]$LogFile = (Join-Path $env:TEMP 'dakiem-android-build.log')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'sync-rules.ps1')

# Gradle failed with "Unable to establish loopback connection" using the default temp folder.
$buildTemp = Join-Path $root 'client/Builds/tmp'
New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null
$env:TEMP = $buildTemp
$env:TMP = $buildTemp
$env:JAVA_TOOL_OPTIONS = "-Djdk.net.unixdomain.tmpdir=$buildTemp -Djava.io.tmpdir=$buildTemp"

$arguments = @(
    '-batchmode', '-nographics', '-buildTarget', 'Android',
    '-projectPath', "`"$(Join-Path $root 'client')`"",
    '-executeMethod', 'DaKiem.Client.Editor.AndroidBuild.Run',
    '-logFile', "`"$LogFile`""
)

$process = Start-Process -FilePath $Unity -ArgumentList $arguments -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Android build failed, see $LogFile" }

Write-Output "Built $(Join-Path $root 'client/Builds/Android/DaKiemOnline.apk')"
