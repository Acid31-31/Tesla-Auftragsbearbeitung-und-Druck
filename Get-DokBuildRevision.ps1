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
