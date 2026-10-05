param(
    [Parameter(Mandatory = $true)][string]$PortableExe,
    [string]$ExpectedVersion = '1.0.2.0'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NoktoLifecycleNative {
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr window, int command);
}
'@
if (Get-Process -Name Nokto -ErrorAction SilentlyContinue) {
    throw 'La prueba aislada requiere que Nokto no este ejecutandose.'
}
$source = Get-Item -LiteralPath $PortableExe
if ($source.VersionInfo.FileVersion -ne $ExpectedVersion) { throw 'Version de portable inesperada.' }
$testDirectory = Join-Path ([IO.Path]::GetTempPath()) ('nokto-portable-lifecycle-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $testDirectory 'data') -Force | Out-Null
$testExe = Join-Path $testDirectory 'Nokto.exe'
Copy-Item -LiteralPath $source.FullName -Destination $testExe
$probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$probe.Start(); $testPort = $probe.LocalEndpoint.Port; $probe.Stop()
$testConfig = @{
    settings = @{
        theme = 'Slate'; minimizeToTrayOnClose = $true
        startMinimizedToTray = $false; startInWorkMode = $false
        panicHotkey = 'Ctrl+Alt+Shift+F18'
        micMuteHotkey = 'Ctrl+Alt+Shift+F7'; audioMuteHotkey = 'Ctrl+Alt+Shift+F8'
    }
    lanServer = @{ enabled = $true; port = $testPort }
}
function Save-TestConfig {
    $json = $testConfig | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText((Join-Path $testDirectory 'data\config.json'), $json, [Text.UTF8Encoding]::new($false))
}
function Wait-Window($process) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 100; $process.Refresh()
        if ($process.HasExited) { throw 'Nokto termino antes de mostrar su ventana.' }
    } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq [IntPtr]::Zero -or !$process.Responding) { throw 'Ventana no disponible o bloqueada.' }
}
function Wait-Lan {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        try {
            $response = Invoke-WebRequest "http://127.0.0.1:$testPort/" -UseBasicParsing -TimeoutSec 1
            if ($response.StatusCode -eq 200) { return }
        } catch { Start-Sleep -Milliseconds 100 }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'El servidor LAN de prueba no inicio.'
}
function Restore-ViaSecondLaunch($process) {
    $second = Start-Process -FilePath $testExe -WorkingDirectory $testDirectory -WindowStyle Hidden -PassThru
    if (!$second.WaitForExit(5000) -or $second.ExitCode -ne 0) { throw 'La segunda apertura no completo el IPC.' }
    Wait-Window $process
}
function Exit-UsingButton($process) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Salir de Nokto')
    $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $button) { throw 'No se encontro el boton Salir de Nokto.' }
    $invoke = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    try { $invoke.Invoke() }
    catch {
        # Shutdown destroys the UIA provider before it can return its COM reply.
        # Accept that disconnect only when the target process really exited cleanly.
        if (!$process.WaitForExit(2000) -or $process.ExitCode -ne 0) { throw }
    }
}
function Require-FullExit($process) {
    if (!$process.WaitForExit(8000)) { throw 'El proceso quedo vivo despues de salir.' }
    if ($process.ExitCode -ne 0) { throw "Codigo de salida inesperado: $($process.ExitCode)" }
    if (Test-Path -LiteralPath (Join-Path $testDirectory 'crash.log')) { throw 'Se genero un crash.log.' }
    # The next cycle must be able to use the same LAN port immediately.
    $reuse = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Any, $testPort)
    $reuse.Start(); $reuse.Stop()
}
function Verify-ManualPanel($process) {
    if ([version]$ExpectedVersion -lt [version]'1.0.4.0') { return }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $tabCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
    $tabs = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tabCondition)
    $manual = @($tabs | Where-Object { $_.Current.Name -like '*Control Manual*' })[0]
    if ($null -eq $manual) { throw 'No se encontro Control Manual en el portable.' }
    $manual.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 150
    $nowCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Ahora')
    $now = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nowCondition)
    if ($null -eq $now) { throw 'El portable no incluye la nueva condicion Ahora.' }
    $now.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 100
    $buttonCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
    $activate = @($buttons | Where-Object { $_.Current.Name -like '*Ejecutar ahora*' })[0]
    if ($null -eq $activate -or $activate.Current.IsEnabled) { throw 'Activacion inmediata o valores iniciales inesperados.' }
    Write-Output '[PASS] Portable: Control Manual nuevo, condicion Ahora y activacion bloqueada hasta elegir acciones.'
}
$active = $null
try {
    for ($cycle = 1; $cycle -le 3; $cycle++) {
        $testConfig.settings.minimizeToTrayOnClose = $cycle -ne 3
        Save-TestConfig
        $active = Start-Process -FilePath $testExe -WorkingDirectory $testDirectory -WindowStyle Hidden -PassThru
        Wait-Window $active
        Wait-Lan
        Verify-ManualPanel $active
        if ($cycle -eq 1) {
            $active.CloseMainWindow() | Out-Null
            Start-Sleep -Milliseconds 300; $active.Refresh()
            if ($active.HasExited -or $active.MainWindowHandle -ne [IntPtr]::Zero) { throw 'Cerrar hacia bandeja no oculto la ventana.' }
            Restore-ViaSecondLaunch $active
            Exit-UsingButton $active
        } elseif ($cycle -eq 2) {
            [NoktoLifecycleNative]::ShowWindowAsync($active.MainWindowHandle, 6) | Out-Null
            Start-Sleep -Milliseconds 300; $active.Refresh()
            if ($active.MainWindowHandle -ne [IntPtr]::Zero) { throw 'Minimizar no oculto la ventana.' }
            Restore-ViaSecondLaunch $active
            Exit-UsingButton $active
        } else {
            $active.CloseMainWindow() | Out-Null
        }
        Require-FullExit $active
        Write-Output "[PASS] Portable ciclo $cycle`: LAN activo, restauracion/cierre correcto, proceso finalizado y puerto reutilizable."
    }
    Write-Output "[PASS] Reapertura real del portable $ExpectedVersion; directorio aislado: $testDirectory"
} finally {
    if ($null -ne $active) {
        $active.Refresh()
        if (!$active.HasExited) { Stop-Process -Id $active.Id -Force }
    }
}
