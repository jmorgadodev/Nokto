[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$dotnet='C:\Users\jorge\AppData\Local\Microsoft\dotnet\dotnet.exe'
$root=Split-Path -Parent $PSScriptRoot
& $dotnet run --project (Join-Path $root 'tests\Nokto.ProductivityProbe\Nokto.ProductivityProbe.csproj') -c Release --no-launch-profile -p:NuGetAudit=false
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'smoke-island-dev.ps1') -Scenarios Dashboard
exit $LASTEXITCODE
