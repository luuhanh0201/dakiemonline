param([string]$Atlas = 'docs/attack-art/torso-turn-atlas-v1.png')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$atlasImage = [Drawing.Bitmap]::FromFile((Join-Path $root $Atlas))
$references = @('Base/torso.png','Gear/armor_aovai_body.png','Gear/armor_giap_body.png')
$names = @('torso','armor_aovai','armor_giap')
function AlphaBounds($bitmap, $area) {
    $left=$area.Right; $top=$area.Bottom; $right=$area.X; $bottom=$area.Y
    for($y=$area.Y; $y -lt $area.Bottom; $y++) {
        for($x=$area.X; $x -lt $area.Right; $x++) {
            if($bitmap.GetPixel($x,$y).A -gt 20) {
                $left=[Math]::Min($left,$x); $right=[Math]::Max($right,$x)
                $top=[Math]::Min($top,$y); $bottom=[Math]::Max($bottom,$y)
            }
        }
    }
    return [Drawing.Rectangle]::FromLTRB($left,$top,$right+1,$bottom+1)
}
for($row=0; $row -lt 3; $row++) {
    $reference=[Drawing.Bitmap]::FromFile((Join-Path $root ('client/Assets/Resources/Character/'+$references[$row])))
    $destination=AlphaBounds $reference ([Drawing.Rectangle]::new(0,0,$reference.Width,$reference.Height))
    for($col=0; $col -lt 2; $col++) {
        $cell=[Drawing.Rectangle]::FromLTRB([int]($col*$atlasImage.Width/2),[int]($row*$atlasImage.Height/3),[int](($col+1)*$atlasImage.Width/2),[int](($row+1)*$atlasImage.Height/3))
        $crop=AlphaBounds $atlasImage $cell
        $output=[Drawing.Bitmap]::new($reference.Width,$reference.Height,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics=[Drawing.Graphics]::FromImage($output)
        $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($atlasImage,$destination,$crop,[Drawing.GraphicsUnit]::Pixel)
        $graphics.Dispose()
        $angle=if($col -eq 0){'open'}else{'closed'}
        $output.Save((Join-Path $root ('client/Assets/Resources/Character/Turns/'+$names[$row]+'_'+$angle+'.png')))
        $output.Dispose()
    }
    $reference.Dispose()
}
$atlasImage.Dispose()
