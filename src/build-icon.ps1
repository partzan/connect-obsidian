Add-Type -AssemblyName System.Drawing

function New-Color([string]$hex) {
    $r = [Convert]::ToInt32($hex.Substring(0, 2), 16)
    $g = [Convert]::ToInt32($hex.Substring(2, 2), 16)
    $b = [Convert]::ToInt32($hex.Substring(4, 2), 16)
    return [System.Drawing.Color]::FromArgb(255, $r, $g, $b)
}

$outline = New-Color "100818"
$deep = New-Color "1E1033"
$mid = New-Color "4C1D95"
$facet = New-Color "7C3AED"
$edge = New-Color "C4B5FD"
$glint = New-Color "F8F7FF"
$bezel = New-Color "022C22"
$frame = New-Color "166534"
$screen = New-Color "02110C"
$text = New-Color "4ADE80"
$cursor = New-Color "ECFDF5"
$stand = New-Color "064E3B"

$source = New-Object System.Drawing.Bitmap 32, 32, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

function Set-Pix([int]$x, [int]$y, [System.Drawing.Color]$color) {
    if ($x -ge 0 -and $x -lt 32 -and $y -ge 0 -and $y -lt 32) {
        $source.SetPixel($x, $y, $color)
    }
}

function Fill-Rect([int]$x1, [int]$y1, [int]$x2, [int]$y2, [System.Drawing.Color]$color) {
    for ($y = $y1; $y -le $y2; $y++) {
        for ($x = $x1; $x -le $x2; $x++) {
            Set-Pix $x $y $color
        }
    }
}

function Test-Inside($polygon, [double]$x, [double]$y) {
    $inside = $false
    $j = $polygon.Count - 1
    for ($i = 0; $i -lt $polygon.Count; $i++) {
        $yi = [double]$polygon[$i][1]
        $yj = [double]$polygon[$j][1]
        $xi = [double]$polygon[$i][0]
        $xj = [double]$polygon[$j][0]
        $cross = (($yi -gt $y) -ne ($yj -gt $y))
        if ($cross) {
            $hit = ($x -lt (($xj - $xi) * ($y - $yi) / (($yj - $yi) + 0.0000001) + $xi))
            if ($hit) { $inside = -not $inside }
        }
        $j = $i
    }
    return $inside
}

# Right half: green display terminal.
Fill-Rect 16 4 31 22 $bezel
Fill-Rect 18 6 29 19 $screen
Fill-Rect 19 8 26 9 $text
Fill-Rect 19 12 24 13 $text
Fill-Rect 19 16 22 17 $text
Set-Pix 24 16 $cursor
Set-Pix 24 17 $cursor
Set-Pix 25 16 $cursor
Set-Pix 25 17 $cursor
Fill-Rect 22 23 26 24 $stand
Fill-Rect 18 25 29 27 $stand
# Inner bezel lip so the screen reads as a monitor.
for ($x = 17; $x -le 30; $x++) { Set-Pix $x 5 $frame; Set-Pix $x 20 $frame }
for ($y = 5; $y -le 20; $y++) { Set-Pix 17 $y $frame; Set-Pix 30 $y $frame }

$rock = @(
    @(3, 10), @(8, 4), @(14, 7), @(15, 14), @(13, 22), @(14, 27), @(7, 29), @(2, 24), @(1, 16)
)
$shine = @(
    @(5, 9), @(10, 6), @(12, 10), @(8, 14), @(4, 13)
)

for ($y = 0; $y -lt 32; $y++) {
    for ($x = 0; $x -lt 15; $x++) {
        $pointX = $x + 0.5
        $pointY = $y + 0.5
        if (Test-Inside $shine $pointX $pointY) {
            Set-Pix $x $y $facet
        }
        elseif (Test-Inside $rock $pointX $pointY) {
            if (($x + $y) -lt 16) { Set-Pix $x $y $mid }
            else { Set-Pix $x $y $deep }
        }
    }
}

Set-Pix 8 7 $edge
Set-Pix 9 7 $edge
Set-Pix 9 8 $glint
Set-Pix 10 8 $edge

for ($y = 0; $y -lt 32; $y++) {
    for ($x = 0; $x -lt 32; $x++) {
        $pixel = $source.GetPixel($x, $y)
        if ($pixel.A -eq 0) { continue }
        $touchesEmpty = $false
        foreach ($pair in @(@(-1, 0), @(1, 0), @(0, -1), @(0, 1))) {
            $nx = $x + $pair[0]
            $ny = $y + $pair[1]
            if ($nx -lt 0 -or $ny -lt 0 -or $nx -ge 32 -or $ny -ge 32 -or $source.GetPixel($nx, $ny).A -eq 0) {
                $touchesEmpty = $true
            }
        }
        if ($touchesEmpty -and $x -lt 16) { Set-Pix $x $y $outline }
    }
}

function Scale-Nearest([System.Drawing.Bitmap]$bitmap, [int]$size) {
    $scaled = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt $size; $y++) {
        $sy = [Math]::Floor($y * $bitmap.Height / $size)
        for ($x = 0; $x -lt $size; $x++) {
            $sx = [Math]::Floor($x * $bitmap.Width / $size)
            $scaled.SetPixel($x, $y, $bitmap.GetPixel($sx, $sy))
        }
    }
    return $scaled
}

$preview = Scale-Nearest $source 256
$preview.Save((Join-Path $PSScriptRoot "icon-preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

function Get-DibBytes([System.Drawing.Bitmap]$bitmap) {
    $width = $bitmap.Width
    $height = $bitmap.Height
    $maskStride = [Math]::Floor(($width + 31) / 32) * 4
    $header = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($header)
    $writer.Write([UInt32]40)
    $writer.Write([Int32]$width)
    $writer.Write([Int32]($height * 2))
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32](($width * $height * 4) + ($maskStride * $height)))
    $writer.Write([Int32]0)
    $writer.Write([Int32]0)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]0)
    for ($y = $height - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $width; $x++) {
            $pixel = $bitmap.GetPixel($x, $y)
            $writer.Write([byte]$pixel.B)
            $writer.Write([byte]$pixel.G)
            $writer.Write([byte]$pixel.R)
            $writer.Write([byte]$pixel.A)
        }
    }
    for ($y = $height - 1; $y -ge 0; $y--) {
        $bit = 0
        $value = 0
        for ($x = 0; $x -lt $width; $x++) {
            $transparent = $bitmap.GetPixel($x, $y).A -lt 128
            if ($transparent) { $value = $value -bor (1 -shl (7 - $bit)) }
            $bit++
            if ($bit -eq 8) {
                $writer.Write([byte]$value)
                $bit = 0
                $value = 0
            }
        }
        if ($bit -ne 0) { $writer.Write([byte]$value) }
        $written = [Math]::Ceiling($width / 8.0)
        while ($written -lt $maskStride) {
            $writer.Write([byte]0)
            $written++
        }
    }
    $writer.Flush()
    $bytes = $header.ToArray()
    $writer.Dispose()
    $header.Dispose()
    return $bytes
}

$images = @(
    (Scale-Nearest $source 16),
    $source,
    (Scale-Nearest $source 48)
)
$dibs = @()
foreach ($image in $images) { $dibs += ,(Get-DibBytes $image) }

$icoPath = Join-Path $PSScriptRoot "app.ico"
$stream = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$dibs.Count)
$offset = 6 + (16 * $dibs.Count)
for ($i = 0; $i -lt $dibs.Count; $i++) {
    $size = $images[$i].Width
    $widthByte = [byte]($(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write($widthByte)
    $writer.Write($widthByte)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$dibs[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $dibs[$i].Length
}
$writeBytes = [System.IO.BinaryWriter].GetMethod("Write", [Type[]]@([byte[]]))
foreach ($dib in $dibs) { [void]$writeBytes.Invoke($writer, (,([byte[]]$dib))) }
$writer.Flush()
$writer.Dispose()
$stream.Dispose()
foreach ($image in $images) {
    if (-not [object]::ReferenceEquals($image, $source)) { $image.Dispose() }
}
$source.Dispose()
Write-Output "wrote $icoPath"
