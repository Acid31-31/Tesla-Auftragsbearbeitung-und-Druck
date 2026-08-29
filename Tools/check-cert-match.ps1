$exe = Join-Path $PSScriptRoot "..\bin\Release\DOK-V01.exe"
$cer = Join-Path $PSScriptRoot "..\CodeSigning.cer"
if (Test-Path $exe) {
    $c = [System.Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromSignedFile($exe)
    Write-Host "EXE Subject: $($c.Subject)"
    Write-Host "EXE Thumb:   $($c.Thumbprint)"
}
if (Test-Path $cer) {
    $t = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cer)
    Write-Host "CER Subject: $($t.Subject)"
    Write-Host "CER Thumb:   $($t.Thumbprint)"
}
