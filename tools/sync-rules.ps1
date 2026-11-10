# Builds Game.Rules and Game.Net and copies the DLLs and config files into the Unity project.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'client/Assets/Plugins/Game.Rules'
$configTarget = Join-Path $root 'client/Assets/StreamingAssets/config'

New-Item -ItemType Directory -Force -Path $target | Out-Null

foreach ($name in 'Game.Rules', 'Game.Net') {
    $project = Join-Path $root "src/$name/$name.csproj"
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "$name build failed." }

    $output = Join-Path $root "src/$name/bin/Release/netstandard2.1"
    Copy-Item (Join-Path $output "$name.dll") $target -Force
    Write-Output "Copied $name.dll to $target"
}

New-Item -ItemType Directory -Force -Path $configTarget | Out-Null
Copy-Item (Join-Path $root 'config/*.json') $configTarget -Force
Write-Output "Copied config files to $configTarget"
