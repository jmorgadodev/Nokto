[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}
$existing = Get-Process -Name 'Nokto.Dev' -ErrorAction SilentlyContinue
if ($existing) {
    throw 'Cierra Nokto.Dev antes de recompilar el portable. No se terminara ningun proceso automaticamente.'
}
$destination = Join-Path $root 'artifacts\Nokto-Dev-Portable-x64'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
& $dotnet publish (Join-Path $root 'src\Nokto.UI\Nokto.UI.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:NuGetAudit=false -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Error al publicar Nokto Portable Dev.' }
$originalExe = Join-Path $destination 'Nokto.exe'
$devExe = Join-Path $destination 'Nokto.Dev.exe'
if (-not (Test-Path -LiteralPath $originalExe)) { throw 'No se genero Nokto.exe.' }
if (Test-Path -LiteralPath $devExe) { Remove-Item -LiteralPath $devExe -Force }
Move-Item -LiteralPath $originalExe -Destination $devExe
New-Item -ItemType File -Path (Join-Path $destination 'portable.lock') -Force | Out-Null
Write-Output ('Portable DEV preparado en: ' + $devExe)
Write-Output 'Iniciar manualmente Nokto.Dev.exe --tray para pruebas en bandeja.'
Write-Output 'Esta compilacion NO publica en GitHub ni se instala en Windows.'
