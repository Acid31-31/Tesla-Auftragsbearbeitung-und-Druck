function Get-DokRevisionFromAssembly {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $assemblyInfo = Get-Content (Join-Path $Root "Properties\AssemblyInfo.cs") -Raw
    if ($assemblyInfo -match 'AssemblyVersion\("([^"]+)"\)') {
        $parts = $Matches[1].Split('.')
        $revision = 0
        if ($parts.Length -ge 4) {
            [void][int]::TryParse($parts[3], [ref]$revision)
        }
        if ($revision -le 0 -and $parts.Length -ge 3) {
            [void][int]::TryParse($parts[2], [ref]$revision)
        }

        $revLabel = "R$revision"
        return [pscustomobject]@{
            Revision       = $revision
            RevisionLabel  = $revLabel
            VersionTag     = "v$($parts[0]).$($parts[1])-$revLabel"
            ProductFolder  = "DOK-V01-$revLabel"
            UsbZipName     = "DOK-V01-$revLabel.zip"
            ReleaseZipName = "DOK-V01-Release-$revLabel.zip"
        }
    }

    throw "AssemblyVersion nicht gefunden in Properties\AssemblyInfo.cs"
}

function Copy-DokProgramPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseDir,
        [Parameter(Mandatory = $true)]
        [string]$Destination,
        [string]$ProjectRoot = ""
    )

    if (-not (Test-Path $ReleaseDir)) {
        throw "Release-Verzeichnis fehlt: $ReleaseDir"
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    Get-ChildItem $ReleaseDir -File | Where-Object {
        $_.Extension -in @('.exe', '.dll', '.config') -or
        $_.Name -in @('LICENSE_DE.txt', 'CodeSigning.cer', 'AppIcon.ico')
    } | ForEach-Object {
        Copy-Item $_.FullName -Destination (Join-Path $Destination $_.Name) -Force
    }

    foreach ($dirName in @('native', 'tesseract', 'Assets')) {
        $src = Join-Path $ReleaseDir $dirName
        if (Test-Path $src) {
            $destDir = Join-Path $Destination $dirName
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            Copy-Item (Join-Path $src '*') $destDir -Recurse -Force
        }
    }

    if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
        return
    }

    $pdfiumDest = Join-Path $Destination "native\win-x64\pdfium.dll"
    if (-not (Test-Path $pdfiumDest)) {
        $pdfiumCandidates = @(
            (Join-Path $ProjectRoot "native\win-x64\pdfium.dll"),
            (Join-Path $ReleaseDir "native\win-x64\pdfium.dll"),
            (Join-Path $ProjectRoot "packages\Docnet.Core.2.6.0\runtimes\win-x64\native\pdfium.dll")
        )
        $pdfiumSrc = $pdfiumCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($pdfiumSrc) {
            New-Item -ItemType Directory -Path (Split-Path $pdfiumDest) -Force | Out-Null
            Copy-Item $pdfiumSrc $pdfiumDest -Force
            Write-Host "pdfium.dll nachgeladen: $pdfiumSrc"
        }
    }

    $licenseSrc = Join-Path $ProjectRoot "LICENSE_DE.txt"
    if ((Test-Path $licenseSrc) -and -not (Test-Path (Join-Path $Destination "LICENSE_DE.txt"))) {
        Copy-Item $licenseSrc (Join-Path $Destination "LICENSE_DE.txt") -Force
    }

    $iconSrc = Join-Path $ProjectRoot "Assets\AppIcon.ico"
    if (Test-Path $iconSrc) {
        $assetsFolder = Join-Path $Destination "Assets"
        New-Item -ItemType Directory -Path $assetsFolder -Force | Out-Null
        Copy-Item $iconSrc (Join-Path $assetsFolder "AppIcon.ico") -Force
        Copy-Item $iconSrc (Join-Path $Destination "AppIcon.ico") -Force
    }

    $cerSrc = Join-Path $ReleaseDir "CodeSigning.cer"
    if (-not (Test-Path $cerSrc)) {
        $cerSrc = Join-Path $ProjectRoot "cert\DOK-CodeSigning.cer"
    }
    if (-not (Test-Path $cerSrc)) {
        $cerSrc = Join-Path $ProjectRoot "CodeSigning.cer"
    }
    if ((Test-Path $cerSrc) -and -not (Test-Path (Join-Path $Destination "CodeSigning.cer"))) {
        Copy-Item $cerSrc (Join-Path $Destination "CodeSigning.cer") -Force
    }
}

