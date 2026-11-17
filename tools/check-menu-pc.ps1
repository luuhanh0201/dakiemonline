# Exercises the built Windows client against a disposable in-memory server.
param(
    [int]$Width = 1170,
    [int]$Height = 540,
    [int]$Port = 17778,
    [string]$OutputDirectory = 'client/Logs/menu-pc-check',
    [switch]$DoorsOnly
)
$ErrorActionPreference = 'Stop'
$checkRoot = Split-Path -Parent $PSScriptRoot
$checkOutput = [IO.Path]::GetFullPath((Join-Path $checkRoot $OutputDirectory))
$null = New-Item -ItemType Directory -Force -Path $checkOutput
$serverDll = Join-Path $checkRoot 'server/Game.Server/bin/Debug/net10.0/Game.Server.dll'
if (-not (Test-Path -LiteralPath $serverDll)) { throw 'Build the server first with dotnet build DaKiemOnline.slnx.' }
$listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($listener) { throw "Port $Port already has a listener; choose a free test port." }
$serverProcess = $null
$playerProcess = $null
try {
    $serverArgs = @("`"$serverDll`"", '--db', 'memory', '--urls', "http://127.0.0.1:$Port", '--config', "`"$checkRoot/config`"")
    $serverProcess = Start-Process dotnet -ArgumentList $serverArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $checkOutput 'server.log') -RedirectStandardError (Join-Path $checkOutput 'server-error.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($serverProcess.HasExited) { throw 'Test server exited; inspect server-error.log.' }
        try {
            $null = Invoke-WebRequest "http://127.0.0.1:$Port/status" -UseBasicParsing -TimeoutSec 1
            $ready = $true
            break
        } catch { Start-Sleep -Milliseconds 200 }
    }
    if (-not $ready) { throw 'Test server did not become ready.' }
    $playerArgs = @('-join', "ws://127.0.0.1:$Port/ws", '-menu-ui-smoke', "`"$checkOutput`"", '-screen-width', $Width, '-screen-height', $Height, '-logFile', "`"$checkOutput/player.log`"")
    if ($DoorsOnly) { $playerArgs += '-door-check-only' }
    $playerProcess = Start-Process (Join-Path $checkRoot 'client/Builds/PC/DaKiemOnline.exe') -ArgumentList $playerArgs -WindowStyle Hidden -PassThru
    if (-not $playerProcess.WaitForExit(60000)) { throw "Menu check timed out; see $checkOutput/player.log" }
    if ($playerProcess.ExitCode -ne 0) { throw "Menu check failed: $checkOutput/checks.txt" }
    $checks = Get-Content -Encoding UTF8 (Join-Path $checkOutput 'checks.txt')
    if (-not ($checks | Select-String '^RESULT PASS:')) { throw 'Menu check did not complete.' }
    if (Select-String -Path (Join-Path $checkOutput 'player.log') -Pattern 'is not supported or has been stripped|Missing UI art:|Missing menu figure:' -Quiet) { throw 'Missing rendering resource in player.log.' }
    $checks | Select-Object -Last 1
    Write-Output "Screenshots and logs: $checkOutput"
} finally {
    foreach ($ownedProcess in @($playerProcess, $serverProcess)) {
        if ($null -ne $ownedProcess -and -not $ownedProcess.HasExited) { Stop-Process -Id $ownedProcess.Id }
    }
}
