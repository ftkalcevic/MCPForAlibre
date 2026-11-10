<#
.SYNOPSIS
    Draws MCPForAlibre.ico (16, 24, 32, 48, 64 and 256 px, PNG-compressed entries) with System.Drawing.

.DESCRIPTION
    A blue rounded square with a white plug-and-socket link, the server's tray and window icon
    and the add-on's icon in Alibre. Run it again after changing the drawing:
        powershell -ExecutionPolicy Bypass -File tools\MakeIcon.ps1
#>
param([string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\MCPForAlibre.Server\MCPForAlibre.ico'))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function DrawIcon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 64.0

    # Background: blue rounded square, lighter at the top.
    $bg = RoundedRect (1 * $s) (1 * $s) (62 * $s) (62 * $s) (12 * $s)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(0, $size)),
        [System.Drawing.Color]::FromArgb(255, 52, 132, 219), [System.Drawing.Color]::FromArgb(255, 20, 72, 150))
    $g.FillPath($brush, $bg)

    $white = [System.Drawing.Brushes]::White
    $orange = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 160, 40))
    if ($size -le 16) {
        # Small: a white plug body with two prongs and an orange socket dot.
        $g.FillRectangle($white, 3, 6, 6, 5)
        $g.FillRectangle($white, 9, 7, 2, 1)
        $g.FillRectangle($white, 9, 9, 2, 1)
        $g.FillEllipse($orange, 11, 6, 4, 5)
    }
    else {
        # Plug on the left: body, cable and two prongs.
        $penW = [Math]::Max(1.5, 4 * $s)
        $cable = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penW)
        $cable.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLine($cable, 6 * $s, 46 * $s, 14 * $s, 38 * $s)
        $body = RoundedRect (12 * $s) (22 * $s) (16 * $s) (20 * $s) (4 * $s)
        $g.FillPath($white, $body)
        $g.FillRectangle($white, 28 * $s, 25 * $s, 8 * $s, 4 * $s)
        $g.FillRectangle($white, 28 * $s, 35 * $s, 8 * $s, 4 * $s)
        # Socket on the right, in orange, with two slots.
        $sock = RoundedRect (38 * $s) (18 * $s) (20 * $s) (28 * $s) (5 * $s)
        $g.FillPath($orange, $sock)
        $slot = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 20, 72, 150))
        $g.FillRectangle($slot, 38 * $s, 25 * $s, 7 * $s, 4 * $s)
        $g.FillRectangle($slot, 38 * $s, 35 * $s, 7 * $s, 4 * $s)
    }
    $g.Dispose()
    return $bmp
}

# Sizes under 256 as classic 32-bit DIBs (BITMAPINFOHEADER, BGRA rows bottom-up, then the AND mask),
# which every icon reader handles; 256 as PNG, as Windows expects.
function DibBytes([System.Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    $w.Write([uint32]40); $w.Write([int32]$n); $w.Write([int32](2 * $n))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0); $w.Write([uint32]0)
    $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $n - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $n; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($n / 32.0) * 4)
    for ($y = $n - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskRow
        for ($x = 0; $x -lt $n; $x++) {
            if ($bmp.GetPixel($x, $y).A -eq 0) { $row[[int][Math]::Floor($x / 8)] = $row[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) }
        }
        $w.Write($row)
    }
    $w.Flush()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$pngs = foreach ($size in $sizes) {
    $bmp = DrawIcon $size
    if ($size -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bytes = $ms.ToArray()
    }
    else {
        $bytes = DibBytes $bmp
    }
    $bmp.Dispose()
    , $bytes
}

# ICO: header, one directory entry per image, then the PNG data.
$file = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($file)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
$w.Flush()
[System.IO.File]::WriteAllBytes($Out, $file.ToArray())
Write-Host "Wrote $Out"
