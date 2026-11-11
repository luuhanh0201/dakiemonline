# Cuts generated UI art: removes the flat magenta (#FF00FF) background and saves each separate
# piece as its own PNG, numbered left to right, top to bottom.
#   tools/cut-assets.ps1 -In docs/giao-dien-goc/anh4_nutchinh.png -OutDir build/cut/anh4
# With -KeyOnly the image is only made transparent, not split (for full-frame background layers).
param(
    [Parameter(Mandatory)] [string]$In,
    [Parameter(Mandatory)] [string]$OutDir,
    [int]$MinPixels = 400,
    [int]$Pad = 2,
    # Pieces this close (px) are one piece: icons drawn in parts, a pupil inside an eye.
    [int]$Merge = 0,
    # green for art that is itself pink, such as cherry blossom, drawn on #00FF00 instead.
    [ValidateSet('magenta', 'green')] [string]$Key = 'magenta',
    [switch]$KeyOnly,
    # How far from a clear pixel a magenta cast is taken out.
    [int]$SpillReach = 4,
    # Writes each piece's place in the source image as JSON.
    [string]$Manifest
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

if (-not ('AssetCutter4' -as [type])) {
Add-Type -ReferencedAssemblies PresentationCore, WindowsBase, System.Xaml -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

public static class AssetCutter4
{
    public static byte[] Load(string path, out int width, out int height)
    {
        using (var stream = File.OpenRead(path))
        {
            BitmapSource frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            width = bgra.PixelWidth;
            height = bgra.PixelHeight;
            var pixels = new byte[width * height * 4];
            bgra.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }
    }

    public static void Save(string path, byte[] pixels, int width, int height)
    {
        BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }
    }

    // Magenta is strong red and blue with little green. A pixel's share of magenta becomes its
    // transparency, and the magenta is taken back out of its colour, so edges keep no pink fringe.
    public static void KeyMagenta(byte[] p)
    {
        Key(p, false);
    }

    // The same for a #00FF00 background: green over both red and blue is the background's share.
    public static void Key(byte[] p, bool green)
    {
        for (int i = 0; i < p.Length; i += 4)
        {
            float b = p[i] / 255f, g = p[i + 1] / 255f, r = p[i + 2] / 255f;
            float magenta = green ? g - Math.Max(r, b) : Math.Min(r, b) - g;

            if (magenta <= 0.25f)
            {
                continue;
            }

            float share = Math.Min(1f, (magenta - 0.25f) / 0.55f);

            if (share >= 0.95f)
            {
                p[i] = p[i + 1] = p[i + 2] = p[i + 3] = 0;
                continue;
            }

            float keep = 1f - share;
            float nb = (green ? b : b - share) / keep;
            float ng = (green ? g - share : g) / keep;
            float nr = (green ? r : r - share) / keep;

            // Taking magenta out of a dark outline can leave green on top; an edge never gets a hue
            // of the opposite key, so the third channel stays at most the larger of the other two.
            ng = Math.Min(ng, Math.Max(nr, nb));

            p[i] = Clamp(nb);
            p[i + 1] = Clamp(ng);
            p[i + 2] = Clamp(nr);
            p[i + 3] = (byte)(255 * keep);
        }
    }

    private static byte Clamp(float v)
    {
        return (byte)(Math.Max(0f, Math.Min(1f, v)) * 255f);
    }

    // Takes the background colour that is left in drawn pixels after keying: a green cast between
    // blossoms, a pink cast on hair. Green is taken out everywhere, since art keyed on green has no
    // green of its own; magenta only within reach pixels of a clear one, so purple clouds and hair
    // keep their colour inside.
    public static void Despill(byte[] p, int width, int height, bool green, int reach)
    {
        bool[] near = null;

        if (!green)
        {
            near = new bool[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (p[(y * width + x) * 4 + 3] > 8)
                    {
                        continue;
                    }

                    for (int dy = -reach; dy <= reach; dy++)
                    {
                        for (int dx = -reach; dx <= reach; dx++)
                        {
                            int nx = x + dx, ny = y + dy;

                            if (nx >= 0 && ny >= 0 && nx < width && ny < height)
                            {
                                near[ny * width + nx] = true;
                            }
                        }
                    }
                }
            }
        }

        for (int i = 0, n = 0; i < p.Length; i += 4, n++)
        {
            if (p[i + 3] == 0)
            {
                continue;
            }

            byte b = p[i], g = p[i + 1], r = p[i + 2];

            if (green)
            {
                p[i + 1] = Math.Min(g, Math.Max(r, b));
            }
            else if (near[n])
            {
                // Magenta in a pixel is the part of red and blue above green; bring both down to the
                // larger of green and the other channel's natural share.
                int spill = Math.Min(r, b) - g;

                if (spill > 0)
                {
                    p[i] = (byte)(b - spill);
                    p[i + 2] = (byte)(r - spill);
                }
            }
        }
    }

    // Connected pieces of non-transparent pixels, as [x, y, w, h], in reading order.
    public static List<int[]> Pieces(byte[] p, int width, int height, int minPixels)
    {
        var seen = new bool[width * height];
        var pieces = new List<int[]>();
        var stack = new Stack<int>();

        for (int start = 0; start < width * height; start++)
        {
            if (seen[start] || p[start * 4 + 3] < 24)
            {
                continue;
            }

            int minX = width, minY = height, maxX = 0, maxY = 0, count = 0;
            stack.Push(start);
            seen[start] = true;

            while (stack.Count > 0)
            {
                int at = stack.Pop();
                int x = at % width, y = at / width;
                count++;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;

                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }

                        int next = ny * width + nx;

                        if (!seen[next] && p[next * 4 + 3] >= 24)
                        {
                            seen[next] = true;
                            stack.Push(next);
                        }
                    }
                }
            }

            if (count >= minPixels)
            {
                pieces.Add(new[] { minX, minY, maxX - minX + 1, maxY - minY + 1 });
            }
        }

        return pieces;
    }

    // Joins pieces whose boxes come within the gap of each other, such as an icon drawn in parts.
    public static List<int[]> Merge(List<int[]> pieces, int gap)
    {
        bool joined = true;

        while (joined)
        {
            joined = false;

            for (int i = 0; i < pieces.Count && !joined; i++)
            {
                for (int j = i + 1; j < pieces.Count && !joined; j++)
                {
                    int[] a = pieces[i], b = pieces[j];

                    if (a[0] - gap <= b[0] + b[2] && b[0] - gap <= a[0] + a[2] && a[1] - gap <= b[1] + b[3] && b[1] - gap <= a[1] + a[3])
                    {
                        int x0 = Math.Min(a[0], b[0]), y0 = Math.Min(a[1], b[1]);
                        int x1 = Math.Max(a[0] + a[2], b[0] + b[2]), y1 = Math.Max(a[1] + a[3], b[1] + b[3]);
                        pieces[i] = new[] { x0, y0, x1 - x0, y1 - y0 };
                        pieces.RemoveAt(j);
                        joined = true;
                    }
                }
            }
        }

        return pieces;
    }

    public static List<int[]> Order(List<int[]> pieces)
    {
        // Rows first: pieces whose tops are within a third of the shorter height share a row.
        pieces.Sort((a, b) => Math.Abs(a[1] - b[1]) < Math.Min(a[3], b[3]) / 3 ? a[0].CompareTo(b[0]) : a[1].CompareTo(b[1]));
        return pieces;
    }

    public static byte[] Crop(byte[] p, int width, int height, int[] box, int pad, out int w, out int h)
    {
        int x0 = Math.Max(0, box[0] - pad), y0 = Math.Max(0, box[1] - pad);
        int x1 = Math.Min(width, box[0] + box[2] + pad), y1 = Math.Min(height, box[1] + box[3] + pad);
        w = x1 - x0;
        h = y1 - y0;
        var o = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            Buffer.BlockCopy(p, ((y0 + y) * width + x0) * 4, o, y * w * 4, w * 4);
        }

        return o;
    }
}
"@
}

$inPath = (Resolve-Path $In).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outPath = (Resolve-Path $OutDir).Path
$width = 0; $height = 0
$pixels = [AssetCutter4]::Load($inPath, [ref]$width, [ref]$height)
[AssetCutter4]::Key($pixels, $Key -eq 'green')
[AssetCutter4]::Despill($pixels, $width, $height, $Key -eq 'green', $SpillReach)
$name = [System.IO.Path]::GetFileNameWithoutExtension($inPath)

if ($KeyOnly) {
    [AssetCutter4]::Save((Join-Path $outPath "$name.png"), $pixels, $width, $height)
    Write-Output "$name.png ${width}x${height}"
    return
}

$pieces = [AssetCutter4]::Order([AssetCutter4]::Merge([AssetCutter4]::Pieces($pixels, $width, $height, $MinPixels), $Merge))
$index = 0

$entries = @()

foreach ($box in $pieces) {
    $index++
    $w = 0; $h = 0
    $crop = [AssetCutter4]::Crop($pixels, $width, $height, $box, $Pad, [ref]$w, [ref]$h)
    $file = Join-Path $outPath ('{0}_{1:D2}.png' -f $name, $index)
    [AssetCutter4]::Save($file, $crop, $w, $h)
    $x = [Math]::Max(0, $box[0] - $Pad); $y = [Math]::Max(0, $box[1] - $Pad)
    $entries += [ordered]@{ name = ('{0}_{1:D2}' -f $name, $index); x = $x; y = $y; w = $w; h = $h }
    Write-Output ('{0}_{1:D2}.png at {2},{3} size {4}x{5}' -f $name, $index, $x, $y, $w, $h)
}

# Where each piece sat in the source, top-left origin, so layered art can be put back in place.
if ($Manifest) {
    [ordered]@{ source = $name; width = $width; height = $height; pieces = $entries } | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $Manifest
}
