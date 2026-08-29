param(
    [string]$SourcePng = ".\Assets\AppIcon.png",
    [string]$IconPath = ".\Assets\AppIcon.ico",
    [string]$LogoPath = ".\Assets\AppLogo.png",
    [double]$FillScale = 1.0,
    [double]$ContentMarginRatio = 0.015
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function Test-IconContentPixel([System.Drawing.Color]$color) {
    if ($color.A -le 16) { return $false }
    return ($color.R -gt 24 -or $color.G -gt 24 -or $color.B -gt 24)
}

function Get-TrimmedSquareCrop([System.Drawing.Bitmap]$bitmap, [double]$marginRatio) {
    $minX = $bitmap.Width
    $minY = $bitmap.Height
    $maxX = 0
    $maxY = 0
    $found = $false

    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            if (-not (Test-IconContentPixel ($bitmap.GetPixel($x, $y)))) { continue }
            $found = $true
            if ($x -lt $minX) { $minX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }

    if (-not $found) {
        return Get-IconSourceCrop $bitmap
    }

    $contentWidth = $maxX - $minX + 1
    $contentHeight = $maxY - $minY + 1
    $margin = [Math]::Max(1, [int]([Math]::Max($contentWidth, $contentHeight) * $marginRatio))
    $minX = [Math]::Max(0, $minX - $margin)
    $minY = [Math]::Max(0, $minY - $margin)
    $maxX = [Math]::Min($bitmap.Width - 1, $maxX + $margin)
    $maxY = [Math]::Min($bitmap.Height - 1, $maxY + $margin)

    $cropWidth = $maxX - $minX + 1
    $cropHeight = $maxY - $minY + 1
    $side = [Math]::Max($cropWidth, $cropHeight)
    $centerX = $minX + ($cropWidth / 2.0)
    $centerY = $minY + ($cropHeight / 2.0)
    $squareX = [Math]::Max(0, [int]($centerX - ($side / 2.0)))
    $squareY = [Math]::Max(0, [int]($centerY - ($side / 2.0)))
    if ($squareX + $side -gt $bitmap.Width) { $squareX = $bitmap.Width - $side }
    if ($squareY + $side -gt $bitmap.Height) { $squareY = $bitmap.Height - $side }
    if ($squareX -lt 0) { $squareX = 0 }
    if ($squareY -lt 0) { $squareY = 0 }
    $side = [Math]::Min($side, [Math]::Min($bitmap.Width - $squareX, $bitmap.Height - $squareY))

    $bmp = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $srcRect = New-Object System.Drawing.Rectangle $squareX, $squareY, $side, $side
    $destRect = New-Object System.Drawing.Rectangle 0, 0, $side, $side
    $g.DrawImage($bitmap, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $bmp
}

function Get-IconSourceCrop([System.Drawing.Image]$image) {
    $side = [Math]::Min($image.Width, $image.Height)
    $bmp = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $destRect = New-Object System.Drawing.Rectangle 0, 0, $side, $side
    $srcRect = New-Object System.Drawing.Rectangle 0, 0, $side, $side
    $g.DrawImage($image, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $bmp
}

function New-SquareBitmap([System.Drawing.Image]$image, [int]$size, [double]$fillScale) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $scale = [Math]::Min($size / $image.Width, $size / $image.Height) * $fillScale
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
        # PNG-in-ICO: planes and bitCount must be 0 (Windows spec)
        $writer.Write([uint16]0)
        $writer.Write([uint16]0)
        $writer.Write([uint32]$images[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }

    foreach ($image in $images) {
        $writer.Write($image)
    }

    [System.IO.File]::WriteAllBytes($outputPath + ".tmp", $fileStream.ToArray())
    $writer.Dispose()
    $fileStream.Dispose()
    if (Test-Path $outputPath) { Remove-Item $outputPath -Force -ErrorAction SilentlyContinue }
    Move-Item ($outputPath + ".tmp") $outputPath -Force
}

$source = [System.Drawing.Bitmap]::FromFile((Resolve-Path $SourcePng))
try {
    $iconSource = Get-TrimmedSquareCrop $source $ContentMarginRatio
    try {
        $bitmaps = @(16, 24, 32, 48, 64, 128, 256) | ForEach-Object { New-SquareBitmap $iconSource $_ $FillScale }
        Save-ClassicIcon $bitmaps $IconPath
        foreach ($bmp in $bitmaps) { $bmp.Dispose() }
    }
    finally {
        $iconSource.Dispose()
    }
}
finally {
    $source.Dispose()
}

Write-Host "Icon erstellt: $IconPath"
Write-Host "Logo unveraendert: $LogoPath"
