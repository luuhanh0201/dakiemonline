# Bakes the equipment drawings into the fixed canvases the game reads from Resources/Character/Gear.
# Every file is named slot_item_part, so a new item only needs new lines here and no code change.
# Side parts use the rig's layout at the bake scale (layout pixel x 0.3). Front parts use the
# pixels of front_idle.png. Close the Unity Editor first if it holds the folder.
param(
    [string]$SourceDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs/gear-source'),
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'client/Assets/Resources/Character/Gear')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Canvas sizes and origins are shared with EquipmentView.
$HatSide = @{ W = 186; H = 140; X = 0; Y = -60 }
$BodySide = @{ W = 100; H = 100; X = 43; Y = 46 }
$Front = @{ W = 240; H = 340; X = 61; Y = 89 }
$Weapon = @{ W = 60; H = 216; GripX = 30; GripY = 176 }
$SleeveSize = @{ W = 20; H = 72 }
$LegSize = @{ W = 25; H = 86 }
$HipSize = @{ W = 41; H = 34 }
$CapeWidth = 27
$FistHeight = 21

$sources = @{}
function Source($name) {
    if (-not $sources.ContainsKey($name)) { $sources[$name] = New-Object System.Drawing.Bitmap (Join-Path $SourceDir "$name.png") }
    return $sources[$name]
}

function New-Canvas($w, $h) {
    $bitmap = New-Object System.Drawing.Bitmap ([int]$w), ([int]$h), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = 'HighQualityBicubic'
    $graphics.PixelOffsetMode = 'HighQuality'
    $graphics.SmoothingMode = 'AntiAlias'
    return @{ B = $bitmap; G = $graphics }
}

function Save-Canvas($canvas, $name) {
    $canvas.G.Dispose()
    $canvas.B.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.B.Dispose()
    Write-Output $name
}

# Draws a crop with its top middle at (x, y), turned around that point. Widen stretches it sideways.
function Place($canvas, $image, $crop, $x, $y, $height, $angle = 0, $widen = 1.0) {
    $width = $crop[2] * $height / $crop[3] * $widen
    $state = $canvas.G.Save()
    $canvas.G.TranslateTransform([single]$x, [single]$y)
    if ($angle -ne 0) { $canvas.G.RotateTransform([single]$angle) }
    $dest = New-Object System.Drawing.RectangleF (-$width / 2), 0, $width, $height
    $src = New-Object System.Drawing.RectangleF $crop[0], $crop[1], $crop[2], $crop[3]
    $canvas.G.DrawImage($image, $dest, $src, [System.Drawing.GraphicsUnit]::Pixel)
    $canvas.G.Restore($state)
}

# Draws a whole crop stretched to the canvas, for images laid along a limb.
function Fill($name, $crop, $size, $output) {
    $canvas = New-Canvas $size.W $size.H
    $dest = New-Object System.Drawing.RectangleF 0, 0, $size.W, $size.H
    $src = New-Object System.Drawing.RectangleF $crop[0], $crop[1], $crop[2], $crop[3]
    $canvas.G.DrawImage((Source $name), $dest, $src, [System.Drawing.GraphicsUnit]::Pixel)
    Save-Canvas $canvas $output
}

function Read-Pixels($bitmap) {
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bitmap.Width, $bitmap.Height
    $data = $bitmap.LockBits($rect, 'ReadOnly', 'Format32bppArgb')
    $pixels = New-Object byte[] ($data.Stride * $bitmap.Height)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bitmap.UnlockBits($data)
    return ,$pixels
}

function New-FromPixels([byte[]]$pixels, $width, $height) {
    $bitmap = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $width, $height
    $data = $bitmap.LockBits($rect, 'WriteOnly', 'Format32bppArgb')
    [Runtime.InteropServices.Marshal]::Copy($pixels, 0, $data.Scan0, $pixels.Length)
    $bitmap.UnlockBits($data)
    return $bitmap
}

# The armour's armhole is see-through in the drawing; closing it stops the body showing.
function Close-Hole($name, $seedX, $seedY) {
    $bitmap = Source $name
    $w = $bitmap.Width; $h = $bitmap.Height
    $p = Read-Pixels $bitmap
    $seen = New-Object bool[] ($w * $h)
    $stack = New-Object System.Collections.Generic.Stack[int]
    $stack.Push($seedY * $w + $seedX)
    while ($stack.Count -gt 0) {
        $at = $stack.Pop()
        if ($seen[$at]) { continue }
        $seen[$at] = $true
        if ($p[$at * 4 + 3] -gt 200) { continue }
        $p[$at * 4] = 62; $p[$at * 4 + 1] = 52; $p[$at * 4 + 2] = 58; $p[$at * 4 + 3] = 255
        $x = $at % $w
        if ($x -gt 0) { $stack.Push($at - 1) }
        if ($x -lt $w - 1) { $stack.Push($at + 1) }
        if ($at -ge $w) { $stack.Push($at - $w) }
        if ($at -lt $w * ($h - 1)) { $stack.Push($at + $w) }
    }
    $sources[$name] = New-FromPixels $p $w $h
    $bitmap.Dispose()
}

# The conical hat is seen slightly from below. Everything under its blue rim is the inside of the
# hat, which belongs behind the head, so it goes to its own image.
function Split-ConeHat($name, $backName) {
    $bitmap = Source $name
    $w = $bitmap.Width; $h = $bitmap.Height
    $p = Read-Pixels $bitmap
    $back = New-Object byte[] $p.Length
    for ($x = 0; $x -lt $w; $x++) {
        $last = -1
        for ($y = 0; $y -lt $h; $y++) {
            $i = ($y * $w + $x) * 4
            if ($p[$i + 3] -gt 128 -and $p[$i] -gt $p[$i + 2] + 50 -and $p[$i] -gt 100) { $last = $y }
        }
        if ($last -lt 0) { continue }
        $y = $last + 1
        while ($y -lt $h) {
            $i = ($y * $w + $x) * 4
            if ($p[$i + 3] -lt 128 -or ($p[$i] + $p[$i + 1] + $p[$i + 2]) -gt 200) { break }
            $y++
        }
        for (; $y -lt $h; $y++) {
            $i = ($y * $w + $x) * 4
            for ($c = 0; $c -lt 4; $c++) { $back[$i + $c] = $p[$i + $c]; $p[$i + $c] = 0 }
        }
    }
    $sources[$backName] = New-FromPixels $back $w $h
    $sources[$name] = New-FromPixels $p $w $h
    $bitmap.Dispose()
}

Close-Hole 'armor_giap_side' 490 500
Split-ConeHat 'hat_non' 'hat_non_inside'

$cone = @(261, 177, 1249, 533)
$coneHeight = 533 * 150 / 1249

# --- Hats ---------------------------------------------------------------------------------------
$c = New-Canvas $HatSide.W $HatSide.H
Place $c (Source 'hat_non') $cone (90 - $HatSide.X) (30 - $coneHeight - $HatSide.Y) $coneHeight
Save-Canvas $c 'hat_non_side'
$c = New-Canvas $HatSide.W $HatSide.H
Place $c (Source 'hat_non_inside') $cone (90 - $HatSide.X) (30 - $coneHeight - $HatSide.Y) $coneHeight
Save-Canvas $c 'hat_non_back'

$c = New-Canvas $HatSide.W $HatSide.H
$crop = @(241, 176, 1043, 640)
$height = 640 * 108 / 1043
Place $c (Source 'hat_mu_side') $crop (90 - $HatSide.X) (48 - $height - $HatSide.Y) $height
Save-Canvas $c 'hat_mu_side'

$c = New-Canvas $Front.W $Front.H
Place $c (Source 'hat_non') $cone (56 + $Front.X) (34 - $coneHeight + $Front.Y) $coneHeight
Save-Canvas $c 'hat_non_front'
$c = New-Canvas $Front.W $Front.H
Place $c (Source 'hat_non_inside') $cone (56 + $Front.X) (34 - $coneHeight + $Front.Y) $coneHeight
Save-Canvas $c 'hat_non_frontback'

$c = New-Canvas $Front.W $Front.H
$crop = @(227, 139, 1077, 715)
$height = 715 * 100 / 1077
Place $c (Source 'hat_mu_front') $crop (56 + $Front.X) (40 - $height + $Front.Y) $height
Save-Canvas $c 'hat_mu_front'

# --- Armour -------------------------------------------------------------------------------------
$armour = @(
    @{ Item = 'aovai'; SleevePart = 'sleeve'; Side = 'armor_aovai_side'; SideCrop = @(189, 66, 606, 1372); Front = 'armor_aovai_front'; Body = @(398, 27, 337, 622); Sleeve = @(1350, 27, 180, 622) },
    @{ Item = 'giap'; SleevePart = 'sleevehard'; Side = 'armor_giap_side'; SideCrop = @(262, 102, 487, 1332); Front = 'armor_giap_front'; Body = @(376, 87, 433, 725); Sleeve = @(1112, 87, 242, 725) }
)
foreach ($a in $armour) {
    $c = New-Canvas $BodySide.W $BodySide.H
    Place $c (Source $a.Side) $a.SideCrop (93 - $BodySide.X) (56 - $BodySide.Y) 84 0 1.3
    Save-Canvas $c "armor_$($a.Item)_body"

    Fill $a.Front $a.Sleeve $SleeveSize "armor_$($a.Item)_$($a.SleevePart)"

    $c = New-Canvas $Front.W $Front.H
    Place $c (Source $a.Front) $a.Sleeve (29 + $Front.X) (92 + $Front.Y) 78 18 1.15
    Place $c (Source $a.Front) $a.Sleeve (88 + $Front.X) (92 + $Front.Y) 78 -18 1.15
    Place $c (Source $a.Front) $a.Body (58 + $Front.X) (80 + $Front.Y) 92
    Save-Canvas $c "armor_$($a.Item)_front"
}

# --- Trousers -----------------------------------------------------------------------------------
Fill 'pants_vai_side' @(244, 185, 264, 225) $HipSize 'pants_vai_hip'
Fill 'pants_vai_side' @(917, 20, 241, 626) $LegSize 'pants_vai_leg'

$c = New-Canvas $Front.W $Front.H
Place $c (Source 'pants_vai_front') @(905, 29, 262, 609) (42 + $Front.X) (172 + $Front.Y) 68 0 1.15
Place $c (Source 'pants_vai_front') @(1459, 29, 262, 609) (74 + $Front.X) (172 + $Front.Y) 68 0 1.15
Place $c (Source 'pants_vai_front') @(248, 255, 383, 180) (58 + $Front.X) (158 + $Front.Y) 28
Save-Canvas $c 'pants_vai_front'

# --- Capes --------------------------------------------------------------------------------------
$capes = @(
    @{ Item = 'ngan'; Crop = @(289, 417, 446, 743); Length = 87; FrontHeight = 86; Widen = 1.9 },
    @{ Item = 'dai'; Crop = @(319, 73, 386, 1397); Length = 153; FrontHeight = 150; Widen = 2.4 }
)
foreach ($a in $capes) {
    Fill "cape_$($a.Item)" $a.Crop @{ W = $CapeWidth; H = $a.Length } "cape_$($a.Item)_strip"
    $c = New-Canvas $Front.W $Front.H
    Place $c (Source "cape_$($a.Item)") $a.Crop (58 + $Front.X) (94 + $Front.Y) $a.FrontHeight 0 $a.Widen
    Save-Canvas $c "cape_$($a.Item)_front"
}

# --- Weapons ------------------------------------------------------------------------------------
# The base hand is open; pressed shorter, its fingers read as curled around the handle.
$baseDir = Join-Path (Split-Path -Parent $OutDir) 'Base'
$open = New-Object System.Drawing.Bitmap (Join-Path $baseDir 'hand_near.png')
$c = New-Canvas $open.Width $FistHeight
$c.G.DrawImage($open, (New-Object System.Drawing.RectangleF 0, 0, $open.Width, $FistHeight))
$open.Dispose()
$c.G.Dispose()
$c.B.Save((Join-Path $baseDir 'hand_fist.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$c.B.Dispose()
Write-Output 'hand_fist'
$fist = New-Object System.Drawing.Bitmap (Join-Path $baseDir 'hand_fist.png')

# GripX and GripY are where the middle of the handle sits in the source drawing.
$weapons = @(
    @{ Item = 'kiem'; Crop = @(461, 82, 164, 1292); Height = 180; GripX = 542; GripY = 1210 },
    @{ Item = 'dao'; Crop = @(382, 63, 254, 1418); Height = 190; GripX = 497; GripY = 1230 }
)
foreach ($a in $weapons) {
    $image = Source "weapon_$($a.Item)"
    $k = $a.Height / $a.Crop[3]
    $width = $a.Crop[2] * $k
    $left = -($a.GripX - $a.Crop[0]) * $k
    $top = -($a.GripY - $a.Crop[1]) * $k
    $dest = New-Object System.Drawing.RectangleF $left, $top, $width, $a.Height
    $src = New-Object System.Drawing.RectangleF $a.Crop[0], $a.Crop[1], $a.Crop[2], $a.Crop[3]

    $c = New-Canvas $Weapon.W $Weapon.H
    $c.G.TranslateTransform($Weapon.GripX, $Weapon.GripY)
    $c.G.DrawImage($image, $dest, $src, [System.Drawing.GraphicsUnit]::Pixel)
    Save-Canvas $c "weapon_$($a.Item)_side"

    # Held upright in the hand on the right of the picture when the figure faces the camera.
    $c = New-Canvas $Front.W $Front.H
    $c.G.TranslateTransform((111 + $Front.X), (166 + $Front.Y))
    $c.G.RotateTransform(12)
    $c.G.DrawImage($image, $dest, $src, [System.Drawing.GraphicsUnit]::Pixel)
    $c.G.ResetTransform()
    $c.G.DrawImage($fist, (New-Object System.Drawing.RectangleF (111 + $Front.X - 11), (166 + $Front.Y - 11), 22, 22))
    Save-Canvas $c "weapon_$($a.Item)_front"
}
$fist.Dispose()

foreach ($image in $sources.Values) { $image.Dispose() }
