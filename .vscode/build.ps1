$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\PDF-umbenenen.csproj' | Resolve-Path
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'

if (-not (Test-Path $vswhere)) {
    Write-Error 'Visual Studio Build Tools nicht gefunden (vswhere fehlt).'
    exit 1
}

$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild -or -not (Test-Path $msbuild)) {
    Write-Error 'MSBuild.exe nicht gefunden.'
    exit 1
}

& $msbuild $project /p:Configuration=Debug /p:Platform=AnyCPU /v:minimal /nologo
exit $LASTEXITCODE
