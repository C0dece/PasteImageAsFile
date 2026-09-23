Add-Type -AssemblyName System.Drawing

$srcPath = $args[0]
$dstPng = $args[1]
$dstIco = $args[2]

$src = [System.Drawing.Image]::FromFile($srcPath)
$size = $src.Width

# Создаем базовый 512x512 или размер источника с прозрачными скругленными углами
$bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

# Скругленный прямоугольник (радиус ~18% от размера)
$radius = [int]($size * 0.18)
$d = $radius * 2
$rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)

$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
$path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
$path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
$path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
$path.CloseFigure()

$g.SetClip($path)
$g.DrawImage($src, 0, 0, $size, $size)
$g.Dispose()
$path.Dispose()

# Сохраняем PNG для GitHub
$bmp.Save($dstPng, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "PNG saved: $dstPng"

# Размеры для ICO: стандартный набор Windows (16, 24, 32, 48, 64, 128, 256)
$sizes = @(16, 24, 32, 48, 64, 128, 256)

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$bmpDataList = New-Object System.Collections.ArrayList

foreach ($s in $sizes) {
    $scaled = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sg = [System.Drawing.Graphics]::FromImage($scaled)
    $sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $sg.Clear([System.Drawing.Color]::Transparent)
    $sg.DrawImage($bmp, 0, 0, $s, $s)
    $sg.Dispose()

    $lockRect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $bmpData = $scaled.LockBits($lockRect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = [Math]::Abs($bmpData.Stride)
    $bytes = New-Object byte[] ($stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($bmpData.Scan0, $bytes, 0, $bytes.Length)
    $scaled.UnlockBits($bmpData)

    # ICO BMP data is bottom-up
    $flipped = New-Object byte[] ($bytes.Length)
    for ($row = 0; $row -lt $s; $row++) {
        $srcOff = $row * $stride
        $dstOff = ($s - 1 - $row) * $stride
        [Array]::Copy($bytes, $srcOff, $flipped, $dstOff, $stride)
    }

    # AND mask (1 bit per pixel, padded to 32 bits per row)
    $andMaskRowBytes = [int]([Math]::Ceiling($s / 32.0)) * 4
    $andMaskSize = $andMaskRowBytes * $s
    $andMask = New-Object byte[] $andMaskSize
    # Заполняем 0 (все пиксели видимы или определяются альфа-каналом 32bpp)

    $dibMs = New-Object System.IO.MemoryStream
    $dibBw = New-Object System.IO.BinaryWriter($dibMs)

    # BITMAPINFOHEADER
    $dibBw.Write([uint32]40)
    $dibBw.Write([int32]$s)
    $dibBw.Write([int32]($s * 2))
    $dibBw.Write([uint16]1)
    $dibBw.Write([uint16]32)
    $dibBw.Write([uint32]0)
    $dibBw.Write([uint32]($flipped.Length + $andMaskSize))
    $dibBw.Write([int32]0)
    $dibBw.Write([int32]0)
    $dibBw.Write([uint32]0)
    $dibBw.Write([uint32]0)

    $dibBw.Write($flipped)
    $dibBw.Write($andMask)
    $dibBw.Flush()
    $dibData = $dibMs.ToArray()
    $null = $bmpDataList.Add($dibData)
    $dibBw.Dispose()
    $dibMs.Dispose()

    # Для 256 в ICONDIRENTRY ширина и высота записываются как 0
    $iconDim = if ($s -ge 256) { 0 } else { [byte]$s }
    $bw.Write([byte]$iconDim)
    $bw.Write([byte]$iconDim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$dibData.Length)
    $bw.Write([uint32]0)

    $scaled.Dispose()
}

$headerSize = 6 + 16 * $sizes.Count
$offset = $headerSize
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $ms.Position = 6 + 16 * $i + 12
    $bw.Write([uint32]$offset)
    $offset += $bmpDataList[$i].Length
}

$ms.Position = $ms.Length
foreach ($dd in $bmpDataList) { $bw.Write($dd) }

[System.IO.File]::WriteAllBytes($dstIco, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()
$bmp.Dispose()
$src.Dispose()

Write-Host "ICO saved: $dstIco ($($sizes.Count) sizes including 256x256, transparent corners)"
