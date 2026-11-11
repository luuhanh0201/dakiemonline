# Gives a cut-out layer the colours of the original picture: the shape (alpha) comes from the layer,
# the colour from the same pixel of the original. For a layer drawn in place over the original
# (docs/nen-dong), where the generated art mixed the key colour into its shading.
#   tools/recolor-layer.ps1 -Layer client/Assets/Resources/UI/Background/lop6_hoa.png -Original docs/nen-dong/goc.png
param(
    [Parameter(Mandatory)] [string]$Layer,
    [Parameter(Mandatory)] [string]$Original
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

function Read-Bgra([string]$path, [ref]$width, [ref]$height) {
    $stream = [System.IO.File]::OpenRead((Resolve-Path $path).Path)
    try {
        $frame = [System.Windows.Media.Imaging.BitmapDecoder]::Create($stream, 'None', 'OnLoad').Frames[0]
    } finally { $stream.Close() }
    $bgra = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap($frame, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0)
    $width.Value = $bgra.PixelWidth; $height.Value = $bgra.PixelHeight
    $pixels = New-Object byte[] ($bgra.PixelWidth * $bgra.PixelHeight * 4)
    $bgra.CopyPixels($pixels, $bgra.PixelWidth * 4, 0)
    return ,$pixels
}

$lw = 0; $lh = 0; $ow = 0; $oh = 0
$layerPixels = Read-Bgra $Layer ([ref]$lw) ([ref]$lh)
$originalPixels = Read-Bgra $Original ([ref]$ow) ([ref]$oh)

for ($y = 0; $y -lt [Math]::Min($lh, $oh); $y++) {
    for ($x = 0; $x -lt [Math]::Min($lw, $ow); $x++) {
        $i = ($y * $lw + $x) * 4
        if ($layerPixels[$i + 3] -eq 0) { continue }
        $j = ($y * $ow + $x) * 4
        $layerPixels[$i] = $originalPixels[$j]; $layerPixels[$i + 1] = $originalPixels[$j + 1]; $layerPixels[$i + 2] = $originalPixels[$j + 2]
    }
}

$image = [System.Windows.Media.Imaging.BitmapSource]::Create($lw, $lh, 96, 96, [System.Windows.Media.PixelFormats]::Bgra32, $null, $layerPixels, $lw * 4)
$encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($image))
$file = [System.IO.File]::Create((Resolve-Path $Layer).Path); $encoder.Save($file); $file.Close()
"recoloured $Layer ${lw}x${lh} from $Original"
