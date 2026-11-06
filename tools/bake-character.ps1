# Cuts the separated character parts to size and writes them into the Unity project.
# Input: a folder of partNN.png from the split sheet. Each single-part line below is:
#   output name, source part, scale X, scale Y, pixels cut from the top, pixels cut from the bottom.
# Cutting removes the ellipse caps the image tool draws at each joint.
# Arms and legs are joined into one continuous image so no seam shows when they bend.
param(
    [Parameter(Mandatory)][string]$PartsDir,
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'client/Assets/Resources/Character/Base'),
    [double]$BakeScale = 0.3,
    [int]$SmoothRows = 20,
    [int]$ShoulderFadeRows = 16,
    [int]$JoinFadeRows = 5
)

Add-Type -AssemblyName System.Drawing

$singleParts = @'
head        part01 1    1    0  0
hair_front  part02 1    1    0  0
hair_back   part03 1    1    0  0
tail_base   part04 1    1    0  0
tail_tip    part05 1    1    0  0
torso       part06 0.85 0.85 0  14
shorts      part07 0.62 0.72 24 0
hand_near   part12 0.72 0.72 16 0
hand_far    part13 0.72 0.72 16 0
foot        part18 0.6  0.6  22 0
'@

# Output name, upper part, lower part, scale, cuts for both parts, overlap in layout pixels.
$limbs = @'
arm part08 part10 0.72 22 16 18 14 8
leg part14 part16 0.6  18 16 20 14 9
'@

function Read-Part($file, $top, $bottom) {
    $image = [System.Drawing.Image]::FromFile((Join-Path $PartsDir "$file.png"))
    return @{ Image = $image; Rect = (New-Object System.Drawing.Rectangle 0, ([int]$top), $image.Width, ($image.Height - [int]$top - [int]$bottom)) }
}

function New-Canvas($width, $height) {
    $bitmap = New-Object System.Drawing.Bitmap ([int]$width), ([int]$height), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = 'HighQualityBicubic'
    $graphics.PixelOffsetMode = 'HighQuality'
    return @{ Bitmap = $bitmap; Graphics = $graphics }
}

function Save-Canvas($canvas, $name) {
    $canvas.Graphics.Dispose()
    $canvas.Bitmap.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output ("{0,-11} {1}x{2}" -f $name, $canvas.Bitmap.Width, $canvas.Bitmap.Height)
    $canvas.Bitmap.Dispose()
}

# The upper and lower limb are drawn at different widths, which leaves a step where they join.
# This evens out the edges row by row so the outline runs smoothly through the elbow or knee.
function Smooth-LimbEdges($bitmap) {
    $width = $bitmap.Width
    $height = $bitmap.Height
    $left = New-Object 'double[]' $height
    $right = New-Object 'double[]' $height

    for ($y = 0; $y -lt $height; $y++) {
        $left[$y] = -1
        for ($x = 0; $x -lt $width; $x++) {
            if ($bitmap.GetPixel($x, $y).A -gt 100) {
                if ($left[$y] -lt 0) { $left[$y] = $x }
                $right[$y] = $x
            }
        }
    }

    $output = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    for ($y = 0; $y -lt $height; $y++) {
        if ($left[$y] -lt 0) { continue }

        $sumLeft = 0.0; $sumRight = 0.0; $count = 0
        for ($row = [Math]::Max(0, $y - $SmoothRows); $row -le [Math]::Min($height - 1, $y + $SmoothRows); $row++) {
            if ($left[$row] -ge 0) { $sumLeft += $left[$row]; $sumRight += $right[$row]; $count++ }
        }

        $newLeft = $sumLeft / $count
        $newRight = $sumRight / $count
        $span = [Math]::Max(1.0, $newRight - $newLeft)

        for ($x = [Math]::Max(0, [int][Math]::Floor($newLeft) - 1); $x -le [Math]::Min($width - 1, [int][Math]::Ceiling($newRight) + 1); $x++) {
            $along = ($x - $newLeft) / $span
            $sourceX = [int][Math]::Round($left[$y] + $along * ($right[$y] - $left[$y]))
            if ($sourceX -ge 0 -and $sourceX -lt $width) {
                $output.SetPixel($x, $y, $bitmap.GetPixel($sourceX, $y))
            }
        }
    }

    return $output
}

# Fades the top of a part so it melts into the part underneath instead of ending on a hard cut.
function Fade-Top($bitmap, $rows) {
    for ($y = 0; $y -lt [Math]::Min($rows, $bitmap.Height); $y++) {
        $keep = ($y + 1) / ($rows + 1)
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            $pixel = $bitmap.GetPixel($x, $y)
            $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb([int]($pixel.A * $keep), $pixel.R, $pixel.G, $pixel.B))
        }
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

foreach ($old in 'arm_upper', 'forearm', 'thigh', 'shin') {
    foreach ($file in "$old.png", "$old.png.meta") {
        $path = Join-Path $OutDir $file
        if (Test-Path $path) { Remove-Item $path -Confirm:$false }
    }
}

foreach ($line in $singleParts -split "`r?`n") {
    if ($line.Trim().Length -eq 0) { continue }

    $name, $part, $sx, $sy, $top, $bottom = $line -split '\s+'
    $piece = Read-Part $part $top $bottom
    $width = [Math]::Max(1, [Math]::Round($piece.Rect.Width * [double]$sx * $BakeScale))
    $height = [Math]::Max(1, [Math]::Round($piece.Rect.Height * [double]$sy * $BakeScale))
    $canvas = New-Canvas $width $height

    $canvas.Graphics.DrawImage($piece.Image, (New-Object System.Drawing.Rectangle 0, 0, ([int]$width), ([int]$height)), $piece.Rect, [System.Drawing.GraphicsUnit]::Pixel)
    $piece.Image.Dispose()
    if ($name -match '^(hand_|foot)') { Fade-Top $canvas.Bitmap $JoinFadeRows }
    Save-Canvas $canvas $name
}

foreach ($line in $limbs -split "`r?`n") {
    if ($line.Trim().Length -eq 0) { continue }

    $name, $upperPart, $lowerPart, $scale, $upperTop, $upperBottom, $lowerTop, $lowerBottom, $overlap = $line -split '\s+'
    $upper = Read-Part $upperPart $upperTop $upperBottom
    $lower = Read-Part $lowerPart $lowerTop $lowerBottom
    $factor = [double]$scale

    $upperWidth = [Math]::Round($upper.Rect.Width * $factor)
    $upperHeight = [Math]::Round($upper.Rect.Height * $factor)
    $lowerWidth = [Math]::Round($lower.Rect.Width * $factor)
    $lowerHeight = [Math]::Round($lower.Rect.Height * $factor)
    $shared = [Math]::Round([double]$overlap)
    $width = [Math]::Max($upperWidth, $lowerWidth)
    $height = $upperHeight + $lowerHeight - $shared
    $canvas = New-Canvas $width $height

    $upperDestination = New-Object System.Drawing.Rectangle ([int](($width - $upperWidth) / 2)), 0, ([int]$upperWidth), ([int]$upperHeight)
    $lowerDestination = New-Object System.Drawing.Rectangle ([int](($width - $lowerWidth) / 2)), ([int]($upperHeight - $shared)), ([int]$lowerWidth), ([int]$lowerHeight)
    $canvas.Graphics.DrawImage($upper.Image, $upperDestination, $upper.Rect, [System.Drawing.GraphicsUnit]::Pixel)
    $canvas.Graphics.DrawImage($lower.Image, $lowerDestination, $lower.Rect, [System.Drawing.GraphicsUnit]::Pixel)
    $upper.Image.Dispose()
    $lower.Image.Dispose()
    $canvas.Graphics.Dispose()
    $smooth = Smooth-LimbEdges $canvas.Bitmap
    $canvas.Bitmap.Dispose()

    # Smoothing runs at full size; shrinking afterwards keeps the outline clean.
    $small = New-Canvas ([Math]::Round($smooth.Width * $BakeScale)) ([Math]::Round($smooth.Height * $BakeScale))
    $small.Graphics.DrawImage($smooth, 0, 0, $small.Bitmap.Width, $small.Bitmap.Height)
    $smooth.Dispose()
    if ($name -eq 'arm') { Fade-Top $small.Bitmap $ShoulderFadeRows }
    Save-Canvas $small $name
}
