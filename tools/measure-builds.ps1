# Compares the builds in config/build-test.json and writes docs/T040-KET-QUA.md.
# Change config/progression.json or config/build-test.json, then run this again.
param(
    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs/T040-KET-QUA.md')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

dotnet run --project (Join-Path $root 'tools/Game.BuildTest') -c Release -- --config (Join-Path $root 'config') --out $Out
if ($LASTEXITCODE -ne 0) { throw 'Build test failed.' }
