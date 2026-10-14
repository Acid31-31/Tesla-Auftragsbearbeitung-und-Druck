param(
    [string]$Root = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

$ErrorActionPreference = "Stop"
Set-Location $Root
Add-Type -AssemblyName System.Drawing

function New-RoundedRectPath([System.Drawing.Rectangle]$rect, [int]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($radius * 2, [Math]::Min($rect.Width, $rect.Height))
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Get-LogoPixel([System.Drawing.Color]$c) {
    if ($c.A -lt 16) { return $null }
    $brightness = ($c.R + $c.G + $c.B) / 3
    $isTeal = $c.G -gt 95 -and ($c.G - $c.R) -gt 12 -and ($c.G - $c.B) -gt 12
    $isGreen = $c.G -gt 80 -and ($c.G - $c.R) -gt 8
    $isWhite = $brightness -gt 195
    if ($isTeal -or $isGreen -or $isWhite) { return $c }
    return $null
}

function Test-LogoPixel([System.Drawing.Color]$c) {
    return $null -ne (Get-LogoPixel $c)
}

function Get-ContentBounds([System.Drawing.Bitmap]$bitmap) {
    $minX = $bitmap.Width; $minY = $bitmap.Height; $maxX = 0; $maxY = 0
    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            if (-not (Test-LogoPixel $bitmap.GetPixel($x, $y))) { continue }
            if ($x -lt $minX) { $minX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
    if ($maxX -le $minX -or $maxY -le $minY) {
        throw "Logo-Inhalt im AppLogo nicht gefunden."
    }
    return [pscustomobject]@{
        X = $minX; Y = $minY
        Width = $maxX - $minX + 1
        Height = $maxY - $minY + 1
    }
}

$logoPath = Join-Path $Root "Assets\AppLogo.png"
$pngPath = Join-Path $Root "Assets\AppIcon.png"
$iconPath = Join-Path $Root "Assets\AppIcon.ico"
$makeIcoProject = Join-Path $Root "Tools\MakeIco\MakeIco.csproj"

if (-not (Test-Path $logoPath)) { throw "AppLogo fehlt: $logoPath" }

$size = 512
$cornerRadius = [int]($size * 0.26)
$logoInset = [int]($size * 0.12)

$src = [System.Drawing.Image]::FromFile($logoPath)
try {
    $cropSize = [Math]::Min($src.Width, $src.Height)
    $sourceCrop = New-Object System.Drawing.Rectangle 0, 0, $cropSize, $cropSize

    $scan = New-Object System.Drawing.Bitmap $cropSize, $cropSize, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sg = [System.Drawing.Graphics]::FromImage($scan)
    $sg.Clear([System.Drawing.Color]::Transparent)
    $sg.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $cropSize, $cropSize), $sourceCrop, [System.Drawing.GraphicsUnit]::Pixel)
    $sg.Dispose()

    for ($y = 0; $y -lt $cropSize; $y++) {
        for ($x = 0; $x -lt $cropSize; $x++) {
            $logoPixel = Get-LogoPixel $scan.GetPixel($x, $y)
            if ($null -eq $logoPixel) {
                $scan.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
            }
        }
    }

    $bounds = Get-ContentBounds $scan
    $pad = [Math]::Max(2, [int]([Math]::Max($bounds.Width, $bounds.Height) * 0.03))
    $x = [Math]::Max(0, $bounds.X - $pad)
    $y = [Math]::Max(0, $bounds.Y - $pad)
    $w = [Math]::Min($cropSize - $x, $bounds.Width + 2 * $pad)
    $h = [Math]::Min($cropSize - $y, $bounds.Height + 2 * $pad)
    $crop = New-Object System.Drawing.Rectangle $x, $y, $w, $h

    $logoOnly = New-Object System.Drawing.Bitmap $crop.Width, $crop.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $lg = [System.Drawing.Graphics]::FromImage($logoOnly)
    $lg.Clear([System.Drawing.Color]::Transparent)
    $lg.DrawImage($scan, (New-Object System.Drawing.Rectangle 0, 0, $crop.Width, $crop.Height), $crop, [System.Drawing.GraphicsUnit]::Pixel)
    $lg.Dispose()
    $scan.Dispose()

    $canvas = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $bgRect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $bgPath = New-RoundedRectPath $bgRect $cornerRadius
    $g.FillPath([System.Drawing.Brushes]::Black, $bgPath)
    $bgPath.Dispose()

    $inner = $size - 2 * $logoInset
    $scale = [Math]::Min($inner / $logoOnly.Width, $inner / $logoOnly.Height)
    $drawW = [int]($logoOnly.Width * $scale)
    $drawH = [int]($logoOnly.Height * $scale)
    $drawX = [int](($size - $drawW) / 2)
    $drawY = [int](($size - $drawH) / 2)
    $dest = New-Object System.Drawing.Rectangle $drawX, $drawY, $drawW, $drawH
    $g.DrawImage($logoOnly, $dest)

    $logoOnly.Dispose()
    $g.Dispose()

    if (Test-Path $pngPath) { Remove-Item $pngPath -Force }
    $canvas.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Dispose()
    Write-Host "AppIcon.png: $pngPath"
}
finally {
    $src.Dispose()
}

dotnet run --project $makeIcoProject -c Release -- "$pngPath" "$iconPath"
if ($LASTEXITCODE -ne 0) { throw "MakeIco fehlgeschlagen." }
Write-Host "AppIcon.ico: $iconPath"
