# Without -Publish this prepares a reviewable release only. Keep every published APK's versioned filename.
param(
    [switch]$Publish,
    [string]$Apk = 'client/Builds/Android/DaKiemOnline.apk',
    [string]$PreviousApk = ''
)
$ErrorActionPreference = 'Stop'
$releaseRoot = Split-Path -Parent $PSScriptRoot
$releaseSettings = Get-Content -LiteralPath (Join-Path $releaseRoot 'config/android-release.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$releaseApk = if ([IO.Path]::IsPathRooted($Apk)) { [IO.Path]::GetFullPath($Apk) } else { [IO.Path]::GetFullPath((Join-Path $releaseRoot $Apk)) }
$releaseSdk = 'C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Data/PlaybackEngines/AndroidPlayer'
$releaseBuildTools = Join-Path $releaseSdk 'SDK/build-tools/36.0.0'
$releaseJava = Join-Path $releaseSdk 'OpenJDK/bin/java.exe'
$releaseSigner = Join-Path $releaseBuildTools 'lib/apksigner.jar'
$releaseBadging = & (Join-Path $releaseBuildTools 'aapt.exe') dump badging $releaseApk
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect APK metadata.' }
$releasePackage = $releaseBadging | Where-Object { $_ -like 'package:*' } | Select-Object -First 1
if ($releasePackage -notmatch "name='([^']+)' versionCode='([0-9]+)' versionName='([^']+)'") { throw 'APK metadata is missing.' }
if ($Matches[1] -ne $releaseSettings.packageId -or [int]$Matches[2] -ne $releaseSettings.versionCode -or $Matches[3] -ne $releaseSettings.versionName) { throw 'APK and release settings have different versions or package IDs.' }
$releaseSignature = & $releaseJava -jar $releaseSigner verify --print-certs $releaseApk
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
if ($PreviousApk) {
    $releasePreviousPath = if ([IO.Path]::IsPathRooted($PreviousApk)) { [IO.Path]::GetFullPath($PreviousApk) } else { [IO.Path]::GetFullPath((Join-Path $releaseRoot $PreviousApk)) }
    $releasePreviousSignature = & $releaseJava -jar $releaseSigner verify --print-certs $releasePreviousPath
    if ($LASTEXITCODE -ne 0) { throw 'Previous APK signature verification failed.' }
    $releaseDigest = @($releaseSignature | Where-Object { $_ -match '^Signer #\d+ certificate SHA-256 digest:' })
    $releasePreviousDigest = @($releasePreviousSignature | Where-Object { $_ -match '^Signer #\d+ certificate SHA-256 digest:' })
    if (-not $releaseDigest.Count -or ($releaseDigest -join ',') -ne ($releasePreviousDigest -join ',')) { throw 'APK signing certificate changed; it cannot update the previous installation.' }
}
$releaseFilename = 'DaKiemOnline-' + $releaseSettings.versionCode + '.apk'
if ($releaseSettings.apkUrl -ne ('https://dakiem.online/downloads/' + $releaseFilename)) { throw 'Unexpected APK download URL.' }
$releaseStage = Join-Path $releaseRoot 'client/Builds/AndroidRelease'
$null = New-Item -ItemType Directory -Force -Path (Join-Path $releaseStage 'downloads'),(Join-Path $releaseStage 'updates')
$releaseHash = (Get-FileHash -LiteralPath $releaseApk -Algorithm SHA256).Hash.ToLowerInvariant()
$releaseSettings | Add-Member -MemberType NoteProperty -Name sha256 -Value $releaseHash -Force
$releaseSettings | Add-Member -MemberType NoteProperty -Name sizeBytes -Value (Get-Item -LiteralPath $releaseApk).Length -Force
$releaseStagedApk = Join-Path $releaseStage ('downloads/' + $releaseFilename)
Copy-Item -LiteralPath $releaseApk -Destination $releaseStagedApk -Force
$releaseManifest = Join-Path $releaseStage 'updates/android.json'
$releaseJson = $releaseSettings | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($releaseManifest, $releaseJson, (New-Object Text.UTF8Encoding($false)))
if ($Publish) {
    $releaseWeb = Join-Path $releaseRoot 'client/Builds/Web'
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $releaseWeb 'downloads'),(Join-Path $releaseWeb 'updates')
    $releasePublicApk = Join-Path $releaseWeb ('downloads/' + $releaseFilename)
    if ((Test-Path -LiteralPath $releasePublicApk) -and (Get-FileHash -LiteralPath $releasePublicApk -Algorithm SHA256).Hash.ToLowerInvariant() -ne $releaseHash) { throw 'This version already has a different published APK; increase versionCode.' }
    $releaseLiveManifest = Join-Path $releaseWeb 'updates/android.json'
    if (Test-Path -LiteralPath $releaseLiveManifest) {
        $releaseLive = Get-Content -LiteralPath $releaseLiveManifest -Encoding UTF8 -Raw | ConvertFrom-Json
        if ($releaseLive.versionCode -gt $releaseSettings.versionCode) { throw 'Refusing to publish an older release.' }
    }
    Copy-Item -LiteralPath $releaseStagedApk -Destination $releasePublicApk -Force
    Copy-Item -LiteralPath $releaseManifest -Destination ($releaseLiveManifest + '.tmp') -Force
    Move-Item -LiteralPath ($releaseLiveManifest + '.tmp') -Destination $releaseLiveManifest -Force
    Write-Output ('Published ' + $releaseSettings.apkUrl)
} else { Write-Output ('Prepared release for review: ' + $releaseStage) }
