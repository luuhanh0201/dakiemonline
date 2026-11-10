# Runs the game server on port 7777 for every network: LAN, tunnel and the web build.
# Extra arguments go to the server, e.g. -- --fake-latency-ms 75 to try prediction on a fast LAN.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet run --project (Join-Path $root 'server/Game.Server') -c Release -- @args
