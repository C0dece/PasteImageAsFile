Add-Type -AssemblyName System.Drawing

$srcPath = $args[0]
$dstPath = $args[1]

$src = [System.Drawing.Image]::FromFile($srcPath)

# Создаем ICO с BMP-данными (совместимо со старым csc.exe)
$sizes = @(16, 32, 48)

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

# ICO header
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$bmpDataList = New-Object System.Collections.ArrayList

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($src, $s, $s)

    # Convert to 32bpp ARGB raw DIB data
    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $bmpData = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = [Math]::Abs($bmpData.Stride)
    $bytes = New-Object byte[] ($stride * $s)
    [System.Runtime.InteropServices.Marshal]::Copy($bmpData.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($bmpData)

    # ICO BMP data is bottom-up, System.Drawing is top-down
    $flipped = New-Object byte[] ($bytes.Length)
    for ($row = 0; $row -lt $s; $row++) {
        $srcOffset = $row * $stride
        $dstOffset = ($s - 1 - $row) * $stride
        [Array]::Copy($bytes, $srcOffset, $flipped, $dstOffset, $stride)
    }

    # BITMAPINFOHEADER (40 bytes) + pixel data + AND mask
    $andMaskSize = (([Math]::Ceiling($s / 32.0)) * 4) * $s
    $andMask = New-Object byte[] $andMaskSize

    $dibMs = New-Object System.IO.MemoryStream
    $dibBw = New-Object System.IO.BinaryWriter($dibMs)

    # BITMAPINFOHEADER
    $dibBw.Write([uint32]40)        # biSize
    $dibBw.Write([int32]$s)         # biWidth
    $dibBw.Write([int32]($s * 2))   # biHeight (XOR + AND)
    $dibBw.Write([uint16]1)         # biPlanes
    $dibBw.Write([uint16]32)        # biBitCount
    $dibBw.Write([uint32]0)         # biCompression
    $dibBw.Write([uint32]($flipped.Length + $andMaskSize)) # biSizeImage
    $dibBw.Write([int32]0)          # biXPelsPerMeter
    $dibBw.Write([int32]0)          # biYPelsPerMeter
    $dibBw.Write([uint32]0)         # biClrUsed
    $dibBw.Write([uint32]0)         # biClrImportant

    $dibBw.Write($flipped)
    $dibBw.Write($andMask)

    $dibBw.Flush()
    $dibData = $dibMs.ToArray()
    $null = $bmpDataList.Add($dibData)

    $dibBw.Dispose()
    $dibMs.Dispose()

    # ICO directory entry
    $bw.Write([byte]$s)            # Width
    $bw.Write([byte]$s)            # Height
    $bw.Write([byte]0)             # Color palette
    $bw.Write([byte]0)             # Reserved
    $bw.Write([uint16]1)           # Color planes
    $bw.Write([uint16]32)          # Bits per pixel
    $bw.Write([uint32]$dibData.Length) # Data size
    $bw.Write([uint32]0)           # Offset placeholder

    $bmp.Dispose()
}

# Fix offsets
$headerSize = 6 + 16 * $sizes.Count
$offset = $headerSize

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $entryPos = 6 + 16 * $i + 12
    $ms.Position = $entryPos
    $bw.Write([uint32]$offset)
    $offset += $bmpDataList[$i].Length
}

# Write image data
$ms.Position = $ms.Length
foreach ($dibData in $bmpDataList) {
    $bw.Write($dibData)
}

[System.IO.File]::WriteAllBytes($dstPath, $ms.ToArray())

$bw.Dispose()
$ms.Dispose()
$src.Dispose()

Write-Host "ICO created: $dstPath ($($sizes.Count) sizes, BMP format)"
