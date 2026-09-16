<#
    Builds the application icon from assets\icon.png.

    Steps:
      * finds the artwork bounds and fits it into a square with margins;
      * downscales to every size Windows takes from an .ico (16…256) by area
        averaging in linear light, restoring edge definition with an unsharp
        mask;
      * writes a multi-size .ico: frames up to 128 as BMP, 256 as PNG, which is
        what both Windows and Explorer read.

    There are no separate PNGs for in-app display: windows take a frame of the
    right size from the same .ico, see Controls\AppMark.cs.

    Kept in the repository so the icon can be rebuilt from source artwork
    rather than stored as an opaque binary.
#>
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\assets\icon.png'),
    [string]$IcoPath = (Join-Path $PSScriptRoot '..\src\MusicScanIntegrity.App\Assets\app.ico'),

    # Margin share on each side.
    #
    # Windows 11 margins are meant for glyph icons that need room to breathe.
    # This mark is a rounded plate that frames itself and fills the tile. At six
    # percent it looked visibly smaller than its taskbar neighbours and lost a
    # couple of pixels at 24 px. Two percent keeps the shadow off the edge.
    [double]$Margin = 0.02
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Downscaling is done in C#: a PowerShell loop over 512x512 pixels would take
# minutes and still need LockBits.
Add-Type -ReferencedAssemblies System.Drawing.Common, System.Drawing.Primitives @'
using System;
using System.Drawing;
using System.Drawing.Imaging;

public static class IconMath {
    // sRGB <-> linear light. Averaging must happen in linear light, or a thin
    // white line covering a quarter pixel darkens twice as much as it should.
    static readonly float[] ToLinear = new float[256];

    static IconMath() {
        for (int i = 0; i < 256; i++) {
            float c = i / 255f;
            ToLinear[i] = c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }

    static byte ToSrgb(float v) {
        if (v <= 0f) return 0;
        if (v >= 1f) return 255;
        float c = v <= 0.0031308f ? v * 12.92f : (float)(1.055 * Math.Pow(v, 1 / 2.4) - 0.055);
        return (byte)Math.Round(c * 255f);
    }

    static byte[] Read(Bitmap bmp, out int stride) {
        var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        stride = d.Stride;
        var bytes = new byte[d.Stride * bmp.Height];
        System.Runtime.InteropServices.Marshal.Copy(d.Scan0, bytes, 0, bytes.Length);
        bmp.UnlockBits(d);
        return bytes;
    }

    /// <summary>
    /// Area-averages the source region for each target pixel. Colour is
    /// alpha-weighted so transparent pixels do not bleed into the edge. The
    /// region is stretched to a square: the frame is square while the artwork
    /// is slightly wider than tall.
    /// </summary>
    public static Bitmap Area(Bitmap src, RectangleF from, int size, int inset) {
        int stride;
        var s = Read(src, out stride);
        int side = size - inset * 2;

        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        result.SetResolution(96, 96);
        var od = result.LockBits(new Rectangle(0, 0, size, size),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var o = new byte[od.Stride * size];

        float sx = from.Width / side, sy = from.Height / side;
        for (int y = 0; y < side; y++) {
            float y0 = from.Y + y * sy, y1 = y0 + sy;
            for (int x = 0; x < side; x++) {
                float x0 = from.X + x * sx, x1 = x0 + sx;
                float r = 0, g = 0, b = 0, a = 0, weight = 0;

                for (int py = (int)Math.Floor(y0); py < (int)Math.Ceiling(y1); py++) {
                    if (py < 0 || py >= src.Height) continue;
                    float cy = Math.Min(y1, py + 1) - Math.Max(y0, py);
                    if (cy <= 0) continue;

                    for (int px = (int)Math.Floor(x0); px < (int)Math.Ceiling(x1); px++) {
                        if (px < 0 || px >= src.Width) continue;
                        float cx = Math.Min(x1, px + 1) - Math.Max(x0, px);
                        if (cx <= 0) continue;

                        int i = py * stride + px * 4;
                        float part = cx * cy;
                        float alpha = s[i + 3] / 255f;
                        float wa = part * alpha;
                        b += ToLinear[s[i]] * wa;
                        g += ToLinear[s[i + 1]] * wa;
                        r += ToLinear[s[i + 2]] * wa;
                        a += wa;
                        weight += part;
                    }
                }

                int j = (y + inset) * od.Stride + (x + inset) * 4;
                if (weight <= 0 || a <= 0) { o[j + 3] = 0; continue; }
                o[j] = ToSrgb(b / a);
                o[j + 1] = ToSrgb(g / a);
                o[j + 2] = ToSrgb(r / a);
                o[j + 3] = (byte)Math.Round(Math.Min(1f, a / weight) * 255f);
            }
        }

        System.Runtime.InteropServices.Marshal.Copy(o, 0, od.Scan0, o.Length);
        result.UnlockBits(od);
        return result;
    }

    /// <summary>
    /// Unsharp mask restoring edge definition lost to averaging; applied in
    /// linear light, like the averaging.
    /// </summary>
    public static void Sharpen(Bitmap bmp, float amount) {
        if (amount <= 0) return;

        int stride;
        var s = Read(bmp, out stride);
        var o = (byte[])s.Clone();
        int w = bmp.Width, h = bmp.Height;

        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                int i = y * stride + x * 4;
                if (s[i + 3] == 0) continue;

                for (int c = 0; c < 3; c++) {
                    float blur = 0, weight = 0;
                    for (int dy = -1; dy <= 1; dy++) {
                        for (int dx = -1; dx <= 1; dx++) {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            float k = (dx == 0 && dy == 0) ? 4f : ((dx == 0 || dy == 0) ? 2f : 1f);
                            blur += ToLinear[s[ny * stride + nx * 4 + c]] * k;
                            weight += k;
                        }
                    }
                    float v = ToLinear[s[i + c]];
                    o[i + c] = ToSrgb(v + (v - blur / weight) * amount);
                }
            }
        }

        var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(o, 0, d.Scan0, o.Length);
        bmp.UnlockBits(d);
    }
}
'@

# Sizes Windows picks from an .ico: small ones (taskbar, title bar) and large
# ones (Explorer large icons).
$icoSizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256

function Get-ContentBounds {
    param([System.Drawing.Bitmap]$Bitmap)

    $rect = New-Object System.Drawing.Rectangle 0, 0, $Bitmap.Width, $Bitmap.Height
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $bytes = New-Object byte[] ($data.Stride * $Bitmap.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)

        $minX = $Bitmap.Width; $minY = $Bitmap.Height; $maxX = -1; $maxY = -1
        for ($y = 0; $y -lt $Bitmap.Height; $y++) {
            $row = $y * $data.Stride
            for ($x = 0; $x -lt $Bitmap.Width; $x++) {
                # The semi-transparent anti-aliased fringe does not count toward bounds.
                if ($bytes[$row + ($x * 4) + 3] -le 8) { continue }
                if ($x -lt $minX) { $minX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    finally {
        $Bitmap.UnlockBits($data)
    }

    if ($maxX -lt 0) { throw 'Рисунок пустой: непрозрачных точек нет.' }
    New-Object System.Drawing.Rectangle $minX, $minY, ($maxX - $minX + 1), ($maxY - $minY + 1)
}

function New-Canvas {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bmp.SetResolution(96, 96)
    $bmp
}

function New-Frame {
    <#
        Renders one frame: averages the artwork into a square with margins and
        sharpens the edges. Smaller frames get more sharpening: at 16 px a stroke
        is thinner than a pixel and would otherwise become a grey smear.
    #>
    param([System.Drawing.Bitmap]$Bitmap, [System.Drawing.RectangleF]$Bounds, [int]$Size)

    $inset = [int][Math]::Round($Size * $Margin)
    $frame = [IconMath]::Area($Bitmap, $Bounds, $Size, $inset)

    $amount = if ($Size -le 20) { 1.1 }
    elseif ($Size -le 32) { 0.9 }
    elseif ($Size -le 48) { 0.6 }
    elseif ($Size -le 96) { 0.3 }
    else { 0 }
    [IconMath]::Sharpen($frame, $amount)

    $frame
}

function Get-BgraBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $rect = New-Object System.Drawing.Rectangle 0, 0, $Bitmap.Width, $Bitmap.Height
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $bytes = New-Object byte[] ($Bitmap.Width * $Bitmap.Height * 4)
        for ($y = 0; $y -lt $Bitmap.Height; $y++) {
            $source = [IntPtr]::Add($data.Scan0, $y * $data.Stride)
            [System.Runtime.InteropServices.Marshal]::Copy(
                $source, $bytes, $y * $Bitmap.Width * 4, $Bitmap.Width * 4)
        }
        , $bytes
    }
    finally {
        $Bitmap.UnlockBits($data)
    }
}

function Get-DibFrame {
    <#
        An .ico frame as BMP: header, bottom-up pixels and the transparency
        mask. The mask is unused for 32-bit frames but must be present, or some
        programs cannot read the file.
    #>
    param([System.Drawing.Bitmap]$Bitmap)

    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $pixels = Get-BgraBytes -Bitmap $Bitmap

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $maskStride = [Math]::Floor((($w + 31) / 32)) * 4

        $writer.Write([uint32]40)          # header size
        $writer.Write([int32]$w)
        $writer.Write([int32]($h * 2))     # height including mask
        $writer.Write([uint16]1)           # planes
        $writer.Write([uint16]32)          # bits per pixel
        $writer.Write([uint32]0)           # no compression
        $writer.Write([uint32](($w * $h * 4) + ($maskStride * $h)))
        $writer.Write([int32]0); $writer.Write([int32]0)
        $writer.Write([uint32]0); $writer.Write([uint32]0)

        for ($y = $h - 1; $y -ge 0; $y--) {
            $writer.Write($pixels, $y * $w * 4, $w * 4)
        }

        $mask = New-Object byte[] ($maskStride * $h)
        $writer.Write($mask, 0, $mask.Length)
        $writer.Flush()

        , $stream.ToArray()
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

function Get-PngFrame {
    param([System.Drawing.Bitmap]$Bitmap)

    $stream = New-Object System.IO.MemoryStream
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        , $stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

# ── Build ──────────────────────────────────────────────────────────────
$Source = (Resolve-Path $Source).Path
Write-Host "исходный рисунок: $Source"

$master = [System.Drawing.Bitmap]::FromFile($Source)
try {
    $box = Get-ContentBounds -Bitmap $master
    Write-Host ("границы рисунка: {0},{1} {2}x{3}" -f $box.X, $box.Y, $box.Width, $box.Height)

    # Every frame is computed straight from the source artwork; an intermediate
    # image would only add blur.
    $bounds = New-Object System.Drawing.RectangleF $box.X, $box.Y, $box.Width, $box.Height

    $frames = @()
    foreach ($size in $icoSizes) {
        $image = New-Frame -Bitmap $master -Bounds $bounds -Size $size

        $frames += [pscustomobject]@{
            Size = $size
            Data = if ($size -ge 256) { Get-PngFrame -Bitmap $image } else { Get-DibFrame -Bitmap $image }
            Image = $image
        }
    }

    $directory = Split-Path -Parent $IcoPath
    if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }

    $stream = [System.IO.File]::Create($IcoPath)
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $writer.Write([uint16]0)                # reserved
        $writer.Write([uint16]1)                # type: icon
        $writer.Write([uint16]$frames.Count)

        $offset = 6 + (16 * $frames.Count)
        foreach ($frame in $frames) {
            $writer.Write([byte]($(if ($frame.Size -ge 256) { 0 } else { $frame.Size })))
            $writer.Write([byte]($(if ($frame.Size -ge 256) { 0 } else { $frame.Size })))
            $writer.Write([byte]0)              # palette colours: none
            $writer.Write([byte]0)              # reserved
            $writer.Write([uint16]1)            # planes
            $writer.Write([uint16]32)           # bits per pixel
            $writer.Write([uint32]$frame.Data.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Data.Length
        }

        foreach ($frame in $frames) {
            $writer.Write($frame.Data, 0, $frame.Data.Length)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }

    Write-Host ("собран {0}: {1} кадров, {2:N0} байт" -f (Split-Path -Leaf $IcoPath), $frames.Count, (Get-Item $IcoPath).Length)

    foreach ($frame in $frames) { $frame.Image.Dispose() }

}
finally {
    $master.Dispose()
}
