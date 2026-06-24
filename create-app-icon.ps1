param(
    [string]$SourcePng = ".\Assets\DOK-V01-logo-option-A-monogram.png",
    [string]$IconPath = ".\Assets\AppIcon.ico",
    [string]$LogoPath = ".\Assets\AppLogo.png"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-SquareBitmap([System.Drawing.Image]$image, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $scale = [Math]::Min($size / $image.Width, $size / $image.Height) * 0.92
    $w = [int]($image.Width * $scale)
    $h = [int]($image.Height * $scale)
    $x = [int](($size - $w) / 2)
    $y = [int](($size - $h) / 2)
    $g.DrawImage($image, $x, $y, $w, $h)
    $g.Dispose()
    return $bmp
}

function Save-ClassicIcon([System.Drawing.Bitmap[]]$bitmaps, [string]$outputPath) {
    $images = New-Object System.Collections.Generic.List[byte[]]
    foreach ($bitmap in $bitmaps) {
        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $images.Add($stream.ToArray())
        $stream.Dispose()
    }

    $fileStream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $fileStream
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$bitmaps.Length)
    $offset = 6 + (16 * $bitmaps.Length)

    for ($i = 0; $i -lt $bitmaps.Length; $i++) {
        $size = $bitmaps[$i].Width
        if ($size -ge 256) { $size = 0 }
        $writer.Write([byte]$size)
        $writer.Write([byte]$size)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }

    foreach ($image in $images) {
        $writer.Write($image)
    }

    [System.IO.File]::WriteAllBytes($outputPath, $fileStream.ToArray())
    $writer.Dispose()
    $fileStream.Dispose()
}

$source = [System.Drawing.Bitmap]::FromFile((Resolve-Path $SourcePng))
try {
    $logo = New-SquareBitmap $source 128
    $logo.Save($LogoPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $logo.Dispose()

    $bitmaps = @(16, 32, 48, 256) | ForEach-Object { New-SquareBitmap $source $_ }
    Save-ClassicIcon $bitmaps $IconPath
    foreach ($bmp in $bitmaps) { $bmp.Dispose() }
}
finally {
    $source.Dispose()
}

Write-Host "Icon erstellt: $IconPath"
Write-Host "Logo erstellt: $LogoPath"
