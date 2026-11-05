# Builds Game.Rules and copies the DLL into the Unity project.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Game.Rules/Game.Rules.csproj'
$output = Join-Path $root 'src/Game.Rules/bin/Release/netstandard2.1'
$target = Join-Path $root 'client/Assets/Plugins/Game.Rules'

dotnet build $project -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Game.Rules build failed.' }

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item (Join-Path $output 'Game.Rules.dll') $target -Force
Write-Output "Copied Game.Rules.dll to $target"
