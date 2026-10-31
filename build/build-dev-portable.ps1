[CmdletBinding()]
param([string]$DotnetPath, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = if ($DotnetPath) { (Resolve-Path -LiteralPath $DotnetPath).Path } else { Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$destination = Join-Path $root 'artifacts\Nokto-Dev-Portable-miradas'
$target = Join-Path $destination 'Nokto.Dev.exe'
$stage = Join-Path $root ('artifacts\.nokto-build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
    $restoreArgs = @()
    if ($NoRestore) { $restoreArgs += '--no-restore' }
    & $dotnet publish (Join-Path $root 'src\Nokto.UI\Nokto.UI.csproj') -c Release -m:1 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:NuGetAudit=false -o $stage @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw 'Error al publicar Nokto Portable Dev.' }
    $built = Join-Path $stage 'Nokto.exe'
    if (-not (Test-Path -LiteralPath $built)) { throw 'No se genero Nokto.exe.' }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    if (Test-Path -LiteralPath $target) {
        try { $lock = [IO.File]::Open($target,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None); $lock.Dispose() }
        catch { throw 'Cierra Nokto.Dev completamente antes de actualizar el portable.' }
    }
    Copy-Item -LiteralPath $built -Destination (Join-Path $destination 'Nokto.Dev.nuevo.tmp') -Force
    if (Test-Path -LiteralPath $target) { [IO.File]::Replace((Join-Path $destination 'Nokto.Dev.nuevo.tmp'),$target,[NullString]::Value) }
    else { Move-Item -LiteralPath (Join-Path $destination 'Nokto.Dev.nuevo.tmp') -Destination $target }
    New-Item -ItemType File -Path (Join-Path $destination 'portable.lock') -Force | Out-Null
    $notices = Join-Path $destination 'ThirdPartyNotices'
    New-Item -ItemType Directory -Path $notices -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'ThirdPartyNotices\shazam-for-real.txt') -Destination $notices -Force
    Write-Output ('Portable DEV actualizado: ' + $target)
} finally {
    $resolved = (Resolve-Path -LiteralPath $stage).Path
    $tempRoot = (Resolve-Path -LiteralPath (Join-Path $root 'artifacts')).Path + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or -not (Split-Path $resolved -Leaf).StartsWith('.nokto-build-')) { throw 'Ruta temporal inesperada.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
