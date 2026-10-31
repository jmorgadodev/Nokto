[CmdletBinding()]
param([string]$DotnetPath = 'dotnet', [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$temp = Join-Path $root 'artifacts\qa-persistence-harness\temp'
$reports = Join-Path $root 'artifacts\qa-release'
New-Item -ItemType Directory -Path $temp,$reports,(Join-Path $root 'artifacts\Capturas-pruebas') -Force | Out-Null
$oldTemp = $env:TEMP; $oldTmp = $env:TMP
$env:TEMP = $temp; $env:TMP = $temp
Push-Location $root
try {
    $restoreArgs = @(); if ($NoRestore) { $restoreArgs += '--no-restore' }
    if (!$NoRestore) {
        & $DotnetPath restore (Join-Path $root 'Nokto.sln') --ignore-failed-sources -p:NuGetAudit=false -m:1
        if ($LASTEXITCODE -ne 0) { throw 'No se pudieron restaurar las dependencias disponibles para QA.' }
        $restoreArgs += '--no-restore'
    }
    foreach ($project in @('Nokto.ConsoleTest','Nokto.TrayTest','Nokto.ProductivityProbe','Nokto.WeatherProbe')) {
        & $DotnetPath build "tests/$project/$project.csproj" -c Release -m:1 @restoreArgs
        if ($LASTEXITCODE -ne 0) { throw "Build fallido: $project" }
    }
    $runs = @(
        @{ Project='Nokto.ConsoleTest'; Flags=@('--release-qa'); Name='core' },
        @{ Project='Nokto.ProductivityProbe'; Flags=@(); Name='productivity' },
        @{ Project='Nokto.WeatherProbe'; Flags=@(); Name='online-services-mocked' }
    )
    foreach ($flag in @('--media-refresh-probe','--island-interaction-safety-probe','--island-codex-probe','--island-notices-probe','--island-music-probe','--island-layout-probe','--island-harmony-probe','--island-composition-probe','--island-audio-presentation-probe')) {
        $runs += @{ Project='Nokto.TrayTest'; Flags=@($flag); Name=$flag.TrimStart('-') }
    }
    foreach ($run in $runs) {
        & $DotnetPath run --project "tests/$($run.Project)/$($run.Project).csproj" -c Release --no-build --no-restore -- @($run.Flags) 2>&1 | Tee-Object -FilePath (Join-Path $reports "$($run.Name).log")
        if ($LASTEXITCODE -ne 0) { throw "QA fallido: $($run.Name)" }
    }
    Set-Content -LiteralPath (Join-Path $reports 'passed.txt') -Value ('QA seguro completado ' + (Get-Date).ToString('o')) -Encoding utf8
} finally {
    Pop-Location
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp
}
