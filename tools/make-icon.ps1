<#
  Builds the program icon (a multi-size .ico) from the logo picture.

  usage:  powershell -File tools\make-icon.ps1 [-Logo cctv-logo.png] [-Out src\CctvBackup.App\Assets\app.ico]

  What it does to the picture:
    1. a white / empty background becomes transparent,
    2. the empty margin around the drawing is cropped away (otherwise it is unreadable at 16 or 32 pixels),
    3. the result is centred on a square and drawn at 16, 24, 32, 48, 64, 128 and 256 pixels.
#>
param(
    [string]$Logo = (Join-Path (Split-Path $PSScriptRoot -Parent) 'cctv-logo.png'),
    [string]$Out = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\CctvBackup.App\Assets\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $Logo)) { throw "Logo not found: $Logo" }
$src = New-Object System.Drawing.Bitmap $Logo

# ---- 1. make the background transparent (white or already transparent) and find the drawing's bounds ----
$w = $src.Width; $h = $src.Height
$rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
$data = $src.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bytes = New-Object byte[] ($data.Stride * $h)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$minX = $w; $minY = $h; $maxX = -1; $maxY = -1
for ($y = 0; $y -lt $h; $y++) {
    $row = $y * $data.Stride
    for ($x = 0; $x -lt $w; $x++) {
        $i = $row + $x * 4
        $b = $bytes[$i]; $g = $bytes[$i + 1]; $r = $bytes[$i + 2]; $a = $bytes[$i + 3]
        $isBackground = ($a -lt 16) -or ($r -ge 248 -and $g -ge 248 -and $b -ge 248)
        if ($isBackground) {
            $bytes[$i] = 0; $bytes[$i + 1] = 0; $bytes[$i + 2] = 0; $bytes[$i + 3] = 0
        } else {
            if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
[System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
$src.UnlockBits($data)
if ($maxX -lt 0) { throw 'The logo has no visible drawing.' }

# ---- 2. crop to the drawing with a small margin, centred on a square ----
$cw = $maxX - $minX + 1; $ch = $maxY - $minY + 1
$side = [int]([Math]::Max($cw, $ch) * 1.10)
$square = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($square)
$g.Clear([System.Drawing.Color]::Transparent)
$g.DrawImage($src, [int](($side - $cw) / 2), [int](($side - $ch) / 2),
    (New-Object System.Drawing.Rectangle $minX, $minY, $cw, $ch), [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose(); $src.Dispose()

# ---- 3. render each size and write the .ico (done in C#: byte handling is safer there) ----
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IcoWriter
{
    static Bitmap Render(Bitmap src, int size)
    {
        var r = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(r))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(src, 0, 0, size, size);
        }
        return r;
    }

    // Classic 32-bit DIB entry: most compatible for the small sizes.
    static byte[] ToDib(Bitmap bmp)
    {
        int s = bmp.Width;
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(40); bw.Write(s); bw.Write(s * 2); bw.Write((short)1); bw.Write((short)32);
        bw.Write(0); bw.Write(s * s * 4); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
        var d = bmp.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var buf = new byte[d.Stride * s];
        System.Runtime.InteropServices.Marshal.Copy(d.Scan0, buf, 0, buf.Length);
        bmp.UnlockBits(d);
        for (int y = s - 1; y >= 0; y--) bw.Write(buf, y * d.Stride, s * 4);   // bottom-up rows
        bw.Write(new byte[((s + 31) / 32) * 4 * s]);                            // AND mask: alpha does the work
        bw.Flush();
        return ms.ToArray();
    }

    static byte[] ToPng(Bitmap bmp)
    {
        var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static string Write(Bitmap square, string path, int[] sizes)
    {
        var images = new List<byte[]>();
        foreach (int s in sizes)
        {
            using (var r = Render(square, s)) images.Add(s <= 48 ? ToDib(r) : ToPng(r));
        }

        using (var fs = File.Create(path))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)images.Count);
            int offset = 6 + 16 * images.Count;
            for (int i = 0; i < images.Count; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                bw.Write(dim); bw.Write(dim); bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((short)1); bw.Write((short)32); bw.Write(images[i].Length); bw.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) bw.Write(img);
        }
        return path;
    }
}
"@

$sizes = [int[]](16, 24, 32, 48, 64, 128, 256)
New-Item -ItemType Directory -Force -Path (Split-Path $Out -Parent) | Out-Null
[void][IcoWriter]::Write($square, $Out, $sizes)
$square.Dispose()

"Icon written: $Out ($((Get-Item $Out).Length) bytes, sizes: $($sizes -join ', '))"
"Drawing bounds in the logo: x $minX-$maxX, y $minY-$maxY"