[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$original = Join-Path $root 'artifacts\Nokto-Dev-Portable-x64\Nokto.Dev.exe'
if (!(Test-Path $original)) { throw 'Ejecutable DEV ausente.' }
if (Get-Process -Name 'Nokto.Dev' -ErrorAction SilentlyContinue) {
    throw 'Cierra Nokto.Dev antes de esta prueba; nunca se cerrara otra instancia.'
}
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class WindowProbe {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc fn, IntPtr data);
  public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr data);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder b, int len);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  public static string[] ForProcess(uint pid) {
    var list = new List<string>();
    EnumWindows((h, arg) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) {
        var b = new StringBuilder(300); GetWindowText(h, b, b.Capacity);
        list.Add(b.ToString());
      }
      return true;
    }, IntPtr.Zero);
    return list.ToArray();
  }
}
'@
$testDir = Join-Path $env:TEMP ('nokto-v2-island-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $testDir 'data') -Force | Out-Null
Copy-Item -LiteralPath $original -Destination (Join-Path $testDir 'Nokto.Dev.exe')
New-Item -ItemType File -Path (Join-Path $testDir 'portable.lock') | Out-Null
$exe = Join-Path $testDir 'Nokto.Dev.exe'
foreach ($scenario in @('Island','Dashboard','Both','Tray')) {
  $islandEnabled = $scenario -eq 'Island' -or $scenario -eq 'Both'
  $cfg = @{ settings = @{ language = 'es'; theme='Dark'; minimizeToTrayOnClose=$true; startMinimizedToTray=$false; startInWorkMode=$false; checkUpdatesOnStartup=$false; panicHotkey='Ctrl+Alt+Shift+F18'; micMuteHotkey='Ctrl+Alt+Shift+F7'; audioMuteHotkey='Ctrl+Alt+Shift+F8' }; island = @{ enabled=$islandEnabled; style='Eyes'; startupMode=$scenario; startupModeConfigured=$true; showClock=$true }; lanServer=@{enabled=$false} }
  [IO.File]::WriteAllText((Join-Path $testDir 'data\config.json'), ($cfg | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
  $p = Start-Process -FilePath $exe -WorkingDirectory $testDir -PassThru
  try {
    Start-Sleep -Seconds 6
    $p.Refresh()
    if ($p.HasExited) { throw ('Se cerro Nokto DEV inesperadamente en ' + $scenario + ': ' + $p.ExitCode) }
    $titles = [WindowProbe]::ForProcess([uint32]$p.Id)
    $island = [bool](@($titles | Where-Object { $_ -like '*Nokto Island*' }).Count)
    $dashboard = [bool](@($titles | Where-Object { $_ -like '*Nokto 2.0 DEV*' }).Count)
    if ($scenario -eq 'Island' -and (!$island -or $dashboard)) { throw ('Fallo modo Isla: ' + ($titles -join ', ')) }
    if ($scenario -eq 'Dashboard' -and (!$dashboard -or $island)) { throw ('Fallo modo Dashboard: ' + ($titles -join ', ')) }
    if ($scenario -eq 'Both' -and (!$dashboard -or !$island)) { throw ('Fallo modo Ambos: ' + ($titles -join ', ')) }
    if ($scenario -eq 'Tray' -and ($island -or $dashboard)) { throw ('Fallo modo Bandeja: ' + ($titles -join ', ')) }
    if ($scenario -eq 'Dashboard') {
      $main = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
      $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
      $tabs = $main.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
      $islandTab = @($tabs | Where-Object { $_.Current.Name -like '*Isla Din*' })[0]
      if ($null -eq $islandTab) { throw ('No se encontro la quinta pestaña. Pestañas: ' + (($tabs | ForEach-Object { $_.Current.Name }) -join ', ')) }
      $islandTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
      Start-Sleep -Milliseconds 300
      $checkCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'Mostrar la isla flotante')
      $check = $main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$checkCondition)
      if ($null -eq $check) { throw 'Pestaña existe pero no contiene el control de activación.' }
      $check.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
      Start-Sleep -Seconds 1
      $saved = Get-Content -Raw (Join-Path $testDir 'data\config.json') | ConvertFrom-Json
      if (!$saved.island.enabled) { throw 'El control de isla no guardo enabled=true en config.json.' }
      $visibleNow = [WindowProbe]::ForProcess([uint32]$p.Id)
      if (-not @($visibleNow | Where-Object { $_ -like '*Nokto Island*' }).Count) {
        throw 'Al activar la isla desde la pestaña no apareció la ventana.'
      }
      Write-Output '[PASS] Quinta pestaña seleccionada; control funcional y persistencia verificada.'
    }
    Write-Output ('[PASS] ' + $scenario + ': island=' + $island + ', dashboard=' + $dashboard + ', RAM=' + [math]::Round($p.WorkingSet64/1MB,1) + ' MB')
  } finally {
    $p.Refresh()
    if (!$p.HasExited) { Stop-Process -Id $p.Id -Force }
    Start-Sleep -Milliseconds 800
  }
}
Write-Output ('[PASS] Pruebas aisladas en ' + $testDir)
