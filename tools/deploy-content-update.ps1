# Run manually on the host after reviewing the staged APK and content release.
# Without -Deploy this only validates the prepared artifacts.
param([switch]$Deploy)
$ErrorActionPreference = 'Stop'
$deployRoot = Split-Path -Parent $PSScriptRoot
$deployLiveServer = Join-Path $deployRoot 'server/Game.Server/bin/Release/net10.0'
$deployStageServer = Join-Path $deployRoot 'client/Builds/ContentUpdateServer'
$deployServerDll = Join-Path $deployLiveServer 'Game.Server.dll'
$deployApk = Join-Path $deployRoot 'client/Builds/AndroidRelease/downloads/DaKiemOnline-5.apk'
$deployRelease = Get-Content -LiteralPath (Join-Path $deployRoot 'client/Builds/AndroidRelease/updates/android.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$deployAndroid = Get-Content -LiteralPath (Join-Path $deployRoot 'client/Builds/ContentRelease/android/content/android/current.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$deployWindows = Get-Content -LiteralPath (Join-Path $deployRoot 'client/Builds/ContentRelease/windows/content/windows/current.json') -Encoding UTF8 -Raw | ConvertFrom-Json
if ($deployRelease.versionCode -ne 5 -or $deployRelease.versionName -ne '1.2.0' -or $deployAndroid.version -ne 1 -or $deployWindows.version -ne 1) { throw 'Unexpected staged release versions.' }
if ($deployAndroid.rulesHash -ne $deployWindows.rulesHash) { throw 'Android and Windows rules differ.' }
if ((Get-FileHash -LiteralPath $deployApk -Algorithm SHA256).Hash.ToLowerInvariant() -ne $deployRelease.sha256) { throw 'Staged APK checksum mismatch.' }
if (-not (Test-Path -LiteralPath (Join-Path $deployStageServer 'Game.Server.dll'))) { throw 'Staged server is missing.' }
$deployBefore = Invoke-RestMethod 'http://127.0.0.1:7777/status' -TimeoutSec 10
if ($deployBefore.database.kind -ne 'postgres') { throw 'The running server is not using PostgreSQL.' }
Write-Output 'Prepared: APK 1.2.0, Android data 1, Windows data 1, matching rule fingerprints.'
if (-not $Deploy) { Write-Output 'Validation only. Add -Deploy to restart the server and publish.'; return }

function Stop-OwnedGameServer {
    $deployListeners = @(Get-NetTCPConnection -LocalPort 7777 -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
    foreach ($deployProcessId in $deployListeners) {
        $deployProcessInfo = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $deployProcessId)
        if (-not $deployProcessInfo.CommandLine -or $deployProcessInfo.CommandLine.IndexOf($deployServerDll,[StringComparison]::OrdinalIgnoreCase) -lt 0) { throw 'Port 7777 belongs to an unexpected process.' }
        $deployProcess = Get-Process -Id $deployProcessId -ErrorAction Stop
        $null = $deployProcess.CloseMainWindow()
        if (-not $deployProcess.WaitForExit(3000)) {
            $deployProcessInfo = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $deployProcessId)
            if ($deployProcessInfo.CommandLine.IndexOf($deployServerDll,[StringComparison]::OrdinalIgnoreCase) -lt 0) { throw 'The server process changed.' }
            Stop-Process -Id $deployProcessId -ErrorAction Stop
            if (-not $deployProcess.WaitForExit(10000)) { throw 'The owned server did not exit.' }
        }
    }
}

function Wait-ContentServer([string]$ExpectedHash) {
    for ($deployAttempt = 0; $deployAttempt -lt 40; $deployAttempt++) {
        try {
            $deployHealth = Invoke-RestMethod 'http://127.0.0.1:7777/status' -TimeoutSec 2
            if ($deployHealth.database.kind -eq 'postgres' -and $deployHealth.rulesHash -eq $ExpectedHash -and $deployHealth.database.characters -ge $deployBefore.database.characters) { return $deployHealth }
        } catch { }
        Start-Sleep -Seconds 1
    }
    throw 'The new server did not pass its health and rules checks.'
}

if ($deployBefore.rulesHash -ne $deployAndroid.rulesHash) {
    if ($deployBefore.players -ne 0) { throw 'Players are connected. Close the game and retry during an idle window.' }
    $deployBackup = Join-Path $deployRoot ('client/Logs/content-update/server-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $null = New-Item -ItemType Directory -Force -Path $deployBackup
    Get-ChildItem -LiteralPath $deployLiveServer -File | Where-Object Name -ne 'appsettings.Local.json' | Copy-Item -Destination $deployBackup
    $deployBefore | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $deployBackup 'status-before.json') -Encoding UTF8
    Stop-ScheduledTask -TaskName 'DaKiemOnline-WebServer'
    try {
        Stop-OwnedGameServer
        Get-ChildItem -LiteralPath $deployStageServer -File | Where-Object Name -ne 'appsettings.Local.json' | Copy-Item -Destination $deployLiveServer -Force
        Start-ScheduledTask -TaskName 'DaKiemOnline-WebServer'
        $null = Wait-ContentServer $deployAndroid.rulesHash
    } catch {
        $deployFailure = $_
        Stop-ScheduledTask -TaskName 'DaKiemOnline-WebServer'
        Stop-OwnedGameServer
        Get-ChildItem -LiteralPath $deployBackup -File | Where-Object Name -ne 'status-before.json' | Copy-Item -Destination $deployLiveServer -Force
        Start-ScheduledTask -TaskName 'DaKiemOnline-WebServer'
        throw $deployFailure
    } finally {
        if ((Get-ScheduledTask -TaskName 'DaKiemOnline-WebServer').State -ne 'Running') { Start-ScheduledTask -TaskName 'DaKiemOnline-WebServer' }
    }
}

$deployPublicStatus = Invoke-RestMethod 'https://dakiem.online/status' -TimeoutSec 20
if ($deployPublicStatus.rulesHash -ne $deployAndroid.rulesHash) { throw 'The public tunnel is not serving the expected server.' }
& (Join-Path $PSScriptRoot 'publish-content.ps1') -Platform android -Version 1 -Notes $deployAndroid.notes -Publish
& (Join-Path $PSScriptRoot 'publish-content.ps1') -Platform windows -Version 1 -Notes $deployWindows.notes -Publish
& (Join-Path $PSScriptRoot 'publish-android-update.ps1') -PreviousApk (Join-Path $deployRoot 'client/Builds/Web/downloads/DaKiemOnline-4.apk') -Publish
foreach ($deployPlatform in @('android','windows')) {
    $deployPublic = Invoke-RestMethod ('https://dakiem.online/content/' + $deployPlatform + '/current.json?check=' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()) -TimeoutSec 20
    if ($deployPublic.version -ne 1 -or $deployPublic.rulesHash -ne $deployAndroid.rulesHash) { throw 'Public content verification failed.' }
}
$deployPublicApk = Invoke-RestMethod ('https://dakiem.online/updates/android.json?check=' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()) -TimeoutSec 20
if ($deployPublicApk.versionCode -ne 5 -or $deployPublicApk.sha256 -ne $deployRelease.sha256) { throw 'Public APK manifest verification failed.' }
Write-Output 'Deployed: https://dakiem.online/downloads/DaKiemOnline-5.apk; in-game data release 1.'
