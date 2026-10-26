[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $root 'artifacts\Nokto-Dev-Portable-x64'
$exe = Join-Path $folder 'Nokto.Dev.exe'
$config = Join-Path $folder 'data\config.json'
if (-not (Test-Path -LiteralPath $exe)) { throw 'No existe Nokto.Dev.exe.' }
if (Get-Process -Name 'Nokto.Dev' -ErrorAction SilentlyContinue) {
    throw 'Ya existe una instancia de Nokto.Dev. No se iniciara otra ni se detendra la existente.'
}
$child = Start-Process -FilePath $exe -ArgumentList '--tray' -WorkingDirectory $folder -PassThru
try {
    Start-Sleep -Seconds 6
    $child.Refresh()
    if ($child.HasExited) { throw ('Nokto.Dev termino inesperadamente: ' + $child.ExitCode) }
    if (-not (Test-Path -LiteralPath $config)) { throw 'No se creo config.json dentro del portable DEV.' }
    if (Test-Path -LiteralPath (Join-Path $folder 'crash.log')) { throw 'Se detecto crash.log en DEV.' }
    $mem = [Math]::Round($child.WorkingSet64 / 1MB, 1)
    Write-Output ('[PASS] Nokto.Dev iniciado en bandeja. PID=' + $child.Id)
    Write-Output ('[PASS] Configuracion aislada: ' + $config)
    Write-Output ('[INFO] Working Set tras 6 s: ' + $mem + ' MB; no es una medicion de rendimiento en reposo.')
}
finally {
    $child.Refresh()
    if (-not $child.HasExited) {
        Stop-Process -Id $child.Id -Force -ErrorAction SilentlyContinue
        Write-Output '[INFO] Se cerro solamente la instancia temporal creada para esta prueba.'
    }
}
