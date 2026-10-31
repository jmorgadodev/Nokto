[CmdletBinding()]
param([string]$DotnetPath = 'dotnet', [string]$IsccPath, [switch]$NoRestore, [switch]$PortableOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = [string]($props.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version de release invalida.' }
$output = Join-Path $root "artifacts\release-$version"
$stage = Join-Path $root ('artifacts\.release-build-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage,$output -Force | Out-Null
try {
    $restoreArgs = @(); if ($NoRestore) { $restoreArgs += '--no-restore' }
    & $DotnetPath publish (Join-Path $root 'src\Nokto.UI\Nokto.UI.csproj') -c Release -m:1 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $stage @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw 'La compilacion de release fallo.' }
    $exe = Join-Path $stage 'Nokto.exe'
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($info.FileVersion -ne "$version.0") { throw "Version binaria inesperada: $($info.FileVersion)" }
    if ((Get-ChildItem -LiteralPath $stage -File).Where({ $_.Name -notin @('Nokto.exe','Nokto.pdb') }).Count) { throw 'Contenido inesperado en el payload de archivo unico.' }
    $portable = Join-Path $output 'portable-payload'
    $installed = Join-Path $output 'installer-payload'
    New-Item -ItemType Directory -Path $portable,$installed -Force | Out-Null
    # Only copy the allowlisted build products. Never copy from the personal DEV portable.
    foreach ($payload in @($portable,$installed)) {
        Copy-Item -LiteralPath $exe -Destination (Join-Path $payload 'Nokto.exe') -Force
        Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $payload -Force
        Copy-Item -LiteralPath (Join-Path $root 'ThirdPartyNotices') -Destination $payload -Recurse -Force
        if (Test-Path -LiteralPath (Join-Path $payload 'data')) { throw 'No se permite data en el paquete publico.' }
    }
    New-Item -ItemType File -Path (Join-Path $portable 'portable.lock') -Force | Out-Null
    New-Item -ItemType File -Path (Join-Path $installed 'installed.lock') -Force | Out-Null
    Copy-Item -LiteralPath $exe -Destination (Join-Path $output 'Nokto-Portable-x64.exe') -Force
    $zip = Join-Path $output "Nokto-v$version-Portable-x64.zip"
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zip -Force
    if (!$PortableOnly) {
        if (!$IsccPath) {
            $candidate = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
            if (Test-Path -LiteralPath $candidate) { $IsccPath = $candidate }
            else { $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue; if ($command) { $IsccPath = $command.Source } }
        }
        if (!$IsccPath -or !(Test-Path -LiteralPath $IsccPath)) { throw 'Falta ISCC.exe (Inno Setup). Payload listo; no se genero ni se publico un instalador.' }
        & $IsccPath "/DNoktoVersion=$version" "/DPublishDir=$installed" "/DReleaseDir=$output" (Join-Path $root 'build\inno-setup\nokto-setup.iss')
        if ($LASTEXITCODE -ne 0) { throw 'La compilacion del instalador fallo.' }
    }
    $assets = @(Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Name -match '^Nokto-.+\.(exe|zip)$' })
    $lines = foreach ($asset in ($assets | Sort-Object Name)) { (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $asset.Name }
    Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS') -Value $lines -Encoding ascii
    Write-Output "Paquetes $version listos en $output"
} finally {
    $absolute = [IO.Path]::GetFullPath($stage)
    $prefix = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (!$absolute.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $absolute -Leaf).StartsWith('.release-build-')) { throw 'Staging fuera del proyecto.' }
    if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force }
}
