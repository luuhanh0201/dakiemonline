# Publishes assets under a new URL so browser/CDN caches cannot mix different Unity builds.
param(
    [Parameter(Mandatory = $true)][string]$ReleaseId,
    [string]$BuildDirectory = 'client/Builds/WebStage',
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
if ($ReleaseId -notmatch '^[a-zA-Z0-9][a-zA-Z0-9-]{0,79}$') { throw 'Invalid web release ID.' }
$webRepo = Split-Path -Parent $PSScriptRoot
$webSource = if ([IO.Path]::IsPathRooted($BuildDirectory)) { [IO.Path]::GetFullPath($BuildDirectory) } else { [IO.Path]::GetFullPath((Join-Path $webRepo $BuildDirectory)) }
$webLive = Join-Path $webRepo 'client/Builds/Web'
$webRelease = Join-Path $webLive ('releases/' + $ReleaseId)
$webLoaders = @(Get-ChildItem -LiteralPath (Join-Path $webSource 'Build') -Filter '*.loader.js' -File)
if ($webLoaders.Count -ne 1) { throw 'Expected exactly one Unity web loader.' }
$webBuildName = $webLoaders[0].Name -replace '\.loader\.js$', ''
foreach ($webRequired in @('index.html', ('Build/' + $webBuildName + '.loader.js'), ('Build/' + $webBuildName + '.framework.js.gz'), ('Build/' + $webBuildName + '.wasm.gz'), ('Build/' + $webBuildName + '.data.gz'), 'StreamingAssets/config/map.house.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $webSource $webRequired))) { throw ('Missing web output: ' + $webRequired) }
}
$webPrefix = '/releases/' + $ReleaseId + '/'
$webIndex = Get-Content -LiteralPath (Join-Path $webSource 'index.html') -Raw -Encoding UTF8
if (-not $webIndex.Contains('Build/' + $webBuildName + '.loader.js') -or -not $webIndex.Contains('"StreamingAssets"')) { throw 'Unsupported web template; index was not published.' }
$webIndex = $webIndex.Replace('Build/', ($webPrefix + 'Build/')).Replace('"StreamingAssets"', ('"' + $webPrefix + 'StreamingAssets"')).Replace('TemplateData/', ($webPrefix + 'TemplateData/'))
$webReview = Join-Path $webRepo 'client/Logs/map-door-fix'
$null = New-Item -ItemType Directory -Path $webReview -Force
[IO.File]::WriteAllText((Join-Path $webReview ($ReleaseId + '-index.html')), $webIndex, (New-Object Text.UTF8Encoding($false)))
if ($Publish) {
    if (Test-Path -LiteralPath $webRelease) { throw 'This release directory already exists; use a new release ID.' }
    $null = New-Item -ItemType Directory -Path $webRelease -Force
    Copy-Item -LiteralPath (Join-Path $webSource 'Build'),(Join-Path $webSource 'StreamingAssets') -Destination $webRelease -Recurse
    if (Test-Path -LiteralPath (Join-Path $webSource 'TemplateData')) {
        Copy-Item -LiteralPath (Join-Path $webSource 'TemplateData') -Destination $webRelease -Recurse
    }
    Copy-Item -LiteralPath (Join-Path $webLive 'index.html') -Destination (Join-Path $webReview ('index-before-' + $ReleaseId + '.html'))
    [IO.File]::WriteAllText((Join-Path $webLive 'index.html.tmp'), $webIndex, (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath (Join-Path $webLive 'index.html.tmp') -Destination (Join-Path $webLive 'index.html') -Force
    Write-Output ('Published web release ' + $ReleaseId + ' at https://dakiem.online/')
} else { Write-Output ('Prepared web index for review: ' + (Join-Path $webReview ($ReleaseId + '-index.html'))) }
