# Starts the existing web build with the persistent database. -Watch restores a stopped server.
param([switch]$Watch, [switch]$WithTunnel)
$ErrorActionPreference = 'Stop'
$webRoot = Split-Path -Parent $PSScriptRoot
$webExe = Join-Path $webRoot 'server/Game.Server/bin/Release/net10.0/Game.Server.exe'
$webBuild = Join-Path $webRoot 'client/Builds/Web'
$webLogs = Join-Path $webRoot 'client/Logs/web-server'
$webSettings = Join-Path $webRoot 'server/Game.Server/appsettings.Local.json'
if ($WithTunnel) {
    $webTunnel = Get-Service -Name Cloudflared -ErrorAction Stop
    if ($webTunnel.Status -ne 'Running') { Start-Service -Name Cloudflared -ErrorAction Stop }
}
if (-not (Test-Path -LiteralPath $webExe)) { throw 'Build the Release server first.' }
if (-not (Test-Path -LiteralPath (Join-Path $webBuild 'index.html'))) { throw 'Build the web client first.' }
if (-not (Test-Path -LiteralPath $webSettings)) { throw 'Persistent database settings are missing.' }
$null = New-Item -ItemType Directory -Force -Path $webLogs
$webMutex = New-Object Threading.Mutex($false, 'Local\DaKiemOnline.WebServer7777')
$webOwnsMutex = $false
try {
    try { $webOwnsMutex = $webMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $webOwnsMutex = $true }
    if (-not $webOwnsMutex) { return }
    do {
        try {
            $webHealthy = $false
            try {
                $webStatus = Invoke-RestMethod 'http://127.0.0.1:7777/status' -TimeoutSec 5
                if ($webStatus.database.kind -ne 'postgres' -or $null -eq $webStatus.channels) {
                    throw 'Port 7777 is not serving the expected persistent game server.'
                }
                $webHealthy = $true
            } catch {
                if (Get-NetTCPConnection -LocalPort 7777 -State Listen -ErrorAction SilentlyContinue) {
                    throw 'Port 7777 is occupied but the game health check failed; no second server started.'
                }
            }
            if (-not $webHealthy) {
                $webDb = (Get-Content -LiteralPath $webSettings -Raw | ConvertFrom-Json).db
                if ([string]::IsNullOrWhiteSpace($webDb) -or $webDb -eq 'memory' -or $webDb.Contains('DIEN_MAT_KHAU_VAO_DAY') -or $env:DAKIEM_DB -eq 'memory') {
                    throw 'A real database connection must be configured before starting the public server.'
                }
                $webStamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
                $webArgs = @('--urls', 'http://0.0.0.0:7777', '--config', ('"' + (Join-Path $webRoot 'config') + '"'), '--web', ('"' + $webBuild + '"'), '--Logging:EventLog:LogLevel:Default', 'None')
                $webLauncher = $webExe
                $webPrivateRuntime = Join-Path $webRoot '.runtime/dotnet/dotnet.exe'
                if (Test-Path -LiteralPath $webPrivateRuntime) {
                    $webLauncher = $webPrivateRuntime
                    $webArgs = @('"' + (Join-Path (Split-Path -Parent $webExe) 'Game.Server.dll') + '"') + $webArgs
                }
                $webProcess = Start-Process -FilePath $webLauncher -WorkingDirectory (Split-Path -Parent $webSettings) -ArgumentList $webArgs -WindowStyle Hidden -RedirectStandardOutput (Join-Path $webLogs ($webStamp + '.stdout.log')) -RedirectStandardError (Join-Path $webLogs ($webStamp + '.stderr.log')) -PassThru
                for ($webAttempt = 0; $webAttempt -lt 60; $webAttempt++) {
                    if ($webProcess.HasExited) { throw 'Game server exited; inspect client/Logs/web-server.' }
                    try {
                        $webStatus = Invoke-RestMethod 'http://127.0.0.1:7777/status' -TimeoutSec 2
                        $webHealthy = $webStatus.database.kind -eq 'postgres'
                        if ($webHealthy) { break }
                    } catch { }
                    Start-Sleep -Milliseconds 500
                }
                if (-not $webHealthy) { throw 'Game server did not become ready; inspect client/Logs/web-server.' }
                Add-Content -LiteralPath (Join-Path $webLogs 'supervisor.log') -Value ((Get-Date -Format o) + ' Started server PID ' + $webProcess.Id)
            }
            if (-not $Watch) { Write-Output 'Web server ready: http://localhost:7777/ (PostgreSQL)' }
        } catch {
            Add-Content -LiteralPath (Join-Path $webLogs 'supervisor.log') -Value ((Get-Date -Format o) + ' ' + $_.Exception.Message)
            if (-not $Watch) { throw }
        }
        if ($Watch) { Start-Sleep -Seconds 10 }
    } while ($Watch)
} finally {
    if ($webOwnsMutex) { $webMutex.ReleaseMutex() }
    $webMutex.Dispose()
}
