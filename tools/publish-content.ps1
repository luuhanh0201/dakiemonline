param(
    [ValidateSet('android','windows')][string]$Platform = 'android',
    [Parameter(Mandatory = $true)][ValidateRange(1,2147483647)][int]$Version,
    [string]$Notes = 'Cập nhật tài nguyên game.',
    [switch]$AssetsOnly,
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
$contentRepo = Split-Path -Parent $PSScriptRoot
$contentSource = Join-Path $contentRepo ('client/Builds/ContentSources/' + $Platform)
$contentManifest = Get-Content -LiteralPath (Join-Path $contentSource 'manifest.json') -Encoding UTF8 -Raw | ConvertFrom-Json
if ($contentManifest.format -ne 1 -or $contentManifest.contract -ne 'dakiem-resources-v1' -or $contentManifest.platform -ne $Platform) { throw 'Unsupported content source.' }
$contentManifest.version = $Version
$contentManifest.notes = $Notes
$contentStage = Join-Path $contentRepo ('client/Builds/ContentRelease/' + $Platform)
$contentStageBlobs = Join-Path $contentStage 'content/blobs'
$contentStageManifest = Join-Path $contentStage ('content/' + $Platform + '/current.json')
$null = New-Item -ItemType Directory -Force -Path $contentStageBlobs,(Split-Path -Parent $contentStageManifest)
$contentKeys = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($contentFile in $contentManifest.files) {
    if (-not $contentKeys.Add($contentFile.key) -or $contentFile.sha256 -cnotmatch '^[0-9a-f]{64}$' -or $contentFile.kind -notin @('image','text','config')) { throw 'Invalid content entry.' }
    $contentBlob = Join-Path $contentSource ('blobs/' + $contentFile.sha256 + '.bin')
    if ((Get-Item -LiteralPath $contentBlob).Length -ne $contentFile.size -or (Get-FileHash -LiteralPath $contentBlob -Algorithm SHA256).Hash.ToLowerInvariant() -ne $contentFile.sha256) { throw ('Corrupted source payload: ' + $contentFile.key) }
    $contentStagedBlob = Join-Path $contentStageBlobs ($contentFile.sha256 + '.bin')
    if (-not (Test-Path -LiteralPath $contentStagedBlob) -or (Get-Item -LiteralPath $contentStagedBlob).Length -ne $contentFile.size -or (Get-FileHash -LiteralPath $contentStagedBlob -Algorithm SHA256).Hash.ToLowerInvariant() -ne $contentFile.sha256) {
        Copy-Item -LiteralPath $contentBlob -Destination $contentStagedBlob -Force
    }
}
$contentJson = $contentManifest | ConvertTo-Json -Depth 32 -Compress
[IO.File]::WriteAllText($contentStageManifest, $contentJson, (New-Object Text.UTF8Encoding($false)))
if ($AssetsOnly) {
    # An older server can serve art/interface updates without a restart. Shared gameplay
    # configuration must remain byte-for-byte identical to the shipped native baseline.
    if ($Platform -eq 'android') {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $contentNativeApk = Join-Path $contentRepo 'client/Builds/Android/DaKiemOnline.apk'
        $contentArchive = [IO.Compression.ZipFile]::OpenRead($contentNativeApk)
        try {
            $contentBaselineEntry = $contentArchive.GetEntry('assets/content/baseline.json')
            if (-not $contentBaselineEntry) { throw 'The native APK has no content baseline.' }
            $contentBaselineReader = New-Object IO.StreamReader($contentBaselineEntry.Open())
            try { $contentBaseline = $contentBaselineReader.ReadToEnd() | ConvertFrom-Json }
            finally { $contentBaselineReader.Dispose() }
        } finally { $contentArchive.Dispose() }
    } else {
        $contentBaseline = Get-Content -LiteralPath (Join-Path $contentRepo 'client/Builds/PC/DaKiemOnline_Data/StreamingAssets/content/baseline.json') -Encoding UTF8 -Raw | ConvertFrom-Json
    }
    if ($contentBaseline.platform -ne $Platform -or $contentBaseline.buildCode -ne $contentManifest.buildCode -or $contentBaseline.rulesHash -ne $contentManifest.rulesHash) { throw 'Assets-only publication cannot change native gameplay rules.' }
    $contentSharedConfigs = @($contentBaseline.files | Where-Object { $_.kind -eq 'config' -and $_.key -ne 'config/login-scene.json' })
    $contentCandidateConfigs = @($contentManifest.files | Where-Object { $_.kind -eq 'config' -and $_.key -ne 'config/login-scene.json' })
    if ($contentSharedConfigs.Count -ne $contentCandidateConfigs.Count -or $contentSharedConfigs.Count -eq 0) { throw 'Assets-only publication changed the gameplay configuration list.' }
    foreach ($contentSharedConfig in $contentSharedConfigs) {
        $contentCandidateConfig = @($contentCandidateConfigs | Where-Object key -CEQ $contentSharedConfig.key)
        if ($contentCandidateConfig.Count -ne 1 -or $contentCandidateConfig[0].sha256 -ne $contentSharedConfig.sha256 -or $contentCandidateConfig[0].size -ne $contentSharedConfig.size) { throw 'Assets-only publication changed a gameplay configuration payload.' }
    }
    Write-Output 'Assets-only validation passed: shared gameplay configuration matches the native application.'
}
if ($Publish) {
    $contentStatus = Invoke-RestMethod 'https://dakiem.online/status' -TimeoutSec 20
    if (-not $AssetsOnly -and $contentStatus.rulesHash -ne $contentManifest.rulesHash) { throw 'Deploy the server with matching rule/config data before publishing this content, or use -AssetsOnly for unchanged native gameplay data.' }
    if ($AssetsOnly -and $contentStatus.rulesHash -and $contentStatus.rulesHash -ne $contentManifest.rulesHash) { throw 'The server reports different gameplay rules; deploy matching data first.' }
    $contentLive = Join-Path $contentRepo 'client/Builds/Web/content'
    $contentLiveManifest = Join-Path $contentLive ($Platform + '/current.json')
    if (Test-Path -LiteralPath $contentLiveManifest) {
        $contentOldJson = Get-Content -LiteralPath $contentLiveManifest -Encoding UTF8 -Raw
        $contentOld = $contentOldJson | ConvertFrom-Json
        if ($contentOld.version -gt $Version -or ($contentOld.version -eq $Version -and $contentOldJson -ne $contentJson)) { throw 'Content release numbers must increase; never reuse a version for different data.' }
    }
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $contentLive 'blobs'),(Split-Path -Parent $contentLiveManifest)
    foreach ($contentFile in $contentManifest.files) {
        $contentLiveBlob = Join-Path $contentLive ('blobs/' + $contentFile.sha256 + '.bin')
        if (Test-Path -LiteralPath $contentLiveBlob) {
            if ((Get-FileHash -LiteralPath $contentLiveBlob -Algorithm SHA256).Hash.ToLowerInvariant() -ne $contentFile.sha256) { throw 'An immutable public content file has been corrupted.' }
        } else { Copy-Item -LiteralPath (Join-Path $contentStageBlobs ($contentFile.sha256 + '.bin')) -Destination $contentLiveBlob }
    }
    Copy-Item -LiteralPath $contentStageManifest -Destination ($contentLiveManifest + '.tmp') -Force
    if (Test-Path -LiteralPath $contentLiveManifest) { [IO.File]::Replace(($contentLiveManifest + '.tmp'), $contentLiveManifest, $null) }
    else { [IO.File]::Move(($contentLiveManifest + '.tmp'), $contentLiveManifest) }
    Write-Output ('Published https://dakiem.online/content/' + $Platform + '/current.json')
} else { Write-Output ('Prepared content release: ' + $contentStage) }
[PSCustomObject]@{Platform=$Platform;Version=$Version;Files=$contentManifest.files.Count;TotalBytes=($contentManifest.files | Measure-Object -Property size -Sum).Sum;MinClient=$contentManifest.minClientCode} | ConvertTo-Json -Compress
