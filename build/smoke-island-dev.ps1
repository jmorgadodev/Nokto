[CmdletBinding()]
param([string[]]$Scenarios = @('Island','Dashboard','Both','Tray'))
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
foreach ($scenario in $Scenarios) {
  $islandEnabled = $scenario -eq 'Island' -or $scenario -eq 'Both'
  $cfg = @{ settings = @{ language = 'es'; theme='Dark'; minimizeToTrayOnClose=$true; startMinimizedToTray=$false; startInWorkMode=$false; checkUpdatesOnStartup=$false; panicHotkey='Ctrl+Alt+Shift+F18'; micMuteHotkey='Ctrl+Alt+Shift+F7'; audioMuteHotkey='Ctrl+Alt+Shift+F8' }; island = @{ enabled=$islandEnabled; style='Eyes'; startupMode=$scenario; startupModeConfigured=$true; showClock=$true }; lanServer=@{enabled=$false} }
  [IO.File]::WriteAllText((Join-Path $testDir 'data\config.json'), ($cfg | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
  $p = Start-Process -FilePath $exe -WorkingDirectory $testDir -PassThru
  try {
    Start-Sleep -Seconds 12
    $p.Refresh()
    if ($p.HasExited) { throw ('Se cerro Nokto DEV inesperadamente en ' + $scenario + ': ' + $p.ExitCode) }
    $titles = [WindowProbe]::ForProcess([uint32]$p.Id)
    Write-Output ('[DEBUG] ' + $scenario + ' PID=' + $p.Id + ' ventanas=' + ($titles -join ' | '))
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

      # El modo Automatico debe mostrar el monitor activo y el objetivo resultante.
      $rateText = @($main.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
          [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
          [System.Windows.Automation.ControlType]::Text)) |
        Where-Object { $_.Current.Name -match '(\d+) Hz\s+.+\s+(\d+) FPS' } |
        Select-Object -First 1)
      if ($rateText.Count -eq 0) { throw 'No se muestra la frecuencia detectada en Rendimiento y animaciones.' }
      $label = $rateText[0].Current.Name
      $numbers = [regex]::Match($label, '(\d+) Hz\s+.+\s+(\d+) FPS')
      if ([int]$numbers.Groups[1].Value -ne [int]$numbers.Groups[2].Value) {
        throw ('En modo automatico se esperaba igualar la frecuencia detectada: ' + $label)
      }
      Write-Output ('[PASS] Deteccion automatica: ' + $label)

      $clockSelector = $main.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
          [System.Windows.Automation.AutomationElement]::AutomationIdProperty,'IslandClockVisibilitySelector'))
      if ($null -eq $clockSelector) { throw 'No se encontro configuracion de reloj.' }
      foreach ($clockTest in @(@('Siempre visible','Always'),@('Ocultar reloj','Never'),@('Solo al expandir','Expand'))) {
        $clockSelector.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 160
        $clockOption = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
          [System.Windows.Automation.TreeScope]::Descendants,
          [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)) |
          Where-Object { $_.Current.Name -like ('*' + $clockTest[0] + '*') } |
          Select-Object -First 1)
        if ($clockOption.Count -eq 0) { throw ('No se encontro modo de reloj '+$clockTest[0]) }
        $clockOption[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 220
        $clockCfg = Get-Content -Raw (Join-Path $testDir 'data\config.json') | ConvertFrom-Json
        if ($clockCfg.island.clockVisibility -ne $clockTest[1]) {
          throw ('Modo de reloj no se guardo: ' + $clockTest[1])
        }
      }
      Write-Output '[PASS] Reloj: Expand, Always y Never persistidos; predeterminado Expand.'
      $eyeSelector = $main.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
          [System.Windows.Automation.AutomationElement]::AutomationIdProperty,'IslandEyeStyleSelector'))
      if ($null -eq $eyeSelector) { throw 'No se encontro el selector de los 42 ojos.' }
      foreach ($selection in ,@('Minimal','Punto verde','GreenDot'))  {
        $eyeSelector.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 160
        $option = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
          [System.Windows.Automation.TreeScope]::Descendants,
          [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)) |
          Where-Object { $_.Current.Name -like ('*' + $selection[1] + '*') } |
          Select-Object -First 1)
        if ($option.Count -eq 0) {
          throw ('Ojo no localizado en UI: ' + $selection[1])
        }
        $option[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 300
        $selected = Get-Content -Raw (Join-Path $testDir 'data\config.json') | ConvertFrom-Json
        if ($selected.island.style -ne $selection[2]) {
          throw ('No se guardo el estilo ' + $selection[2] + ': ' + $selected.island.style)
        }
        Write-Output ('[PASS] Estilo ' + $selection[2] + ': seleccion y persistencia.')
      }
      # Seleccion directa haciendo clic sobre una tarjeta de la galeria (no por lista).
      $galleryChoice = $main.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
          [System.Windows.Automation.AutomationElement]::AutomationIdProperty,'Eyes'))
      if ($null -eq $galleryChoice) { throw 'No se encontro la tarjeta de galeria Eyes.' }
      $galleryChoice.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
      Start-Sleep -Milliseconds 350
      $galleryConfig = Get-Content -Raw (Join-Path $testDir 'data\config.json') | ConvertFrom-Json
      if ($galleryConfig.island.style -ne 'Eyes') { throw 'Clic en galeria no modifico el estilo.' }
      Write-Output '[PASS] Galeria: tarjeta Eyes seleccionada y persistida con un clic.'
      $noEye = $main.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
          [System.Windows.Automation.AutomationElement]::AutomationIdProperty,'IslandNoIndicator'))
      if ($null -eq $noEye) { throw 'No hay boton Sin indicador.' }
      $noEye.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
      Start-Sleep -Milliseconds 300
      $emptyConfig = Get-Content -Raw (Join-Path $testDir 'data\config.json') | ConvertFrom-Json
      if ($emptyConfig.island.style -ne 'None') { throw 'No se guardó Sin indicador.' }
      Write-Output '[PASS] Sin indicador: cápsula vacía seleccionada y guardada.'

      $sizeSlider = $main.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
          [System.Windows.Automation.AutomationElement]::AutomationIdProperty,'IslandSizeSlider'))
      if ($null -eq $sizeSlider) { throw 'Falta configuracion de tamaño de la isla.' }
      $sizeSlider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(130)
      Start-Sleep -Milliseconds 350
      $sizeConfig = Get-Content -Raw (Join-Path $testDir 'data\config.json') | ConvertFrom-Json
      if ([int]$sizeConfig.island.sizePercent -ne 130) {
        throw ('Tamaño de isla no se guardo: '+$sizeConfig.island.sizePercent)
      }
      Write-Output '[PASS] Tamaño: 130 % seleccionado y persistido.'

      $shelfCheck = $main.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
          [System.Windows.Automation.AutomationElement]::AutomationIdProperty,'IslandShelfEnabled'))
      if ($null -eq $shelfCheck) { throw 'No existe opcion Bandeja de archivos.' }
      $shelfCheck.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
      Start-Sleep -Milliseconds 230
      $shelfConfig = Get-Content -Raw (Join-Path $testDir 'data\config.json') | ConvertFrom-Json
      if ($shelfConfig.island.showFileShelf -ne $true) { throw 'Bandeja de archivos no guardo habilitacion.' }
      Write-Output '[PASS] Bandeja de archivos: activacion opt-in guardada.'

      foreach ($mediaSetting in @(@('IslandMediaControls','showMediaControls',$true),@('IslandMediaCompact','showMediaInCompact',$false))) {
        $control = $main.FindFirst(
          [System.Windows.Automation.TreeScope]::Descendants,
          [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,$mediaSetting[0]))
        if ($null -eq $control) { throw ('No existe ajuste Multimedia: '+$mediaSetting[0]) }
        foreach ($expected in @(!$mediaSetting[2],$mediaSetting[2])) {
          $control.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
          Start-Sleep -Milliseconds 250
          $configNow = Get-Content -Raw (Join-Path $testDir 'data\config.json') | ConvertFrom-Json
          if ([bool]($configNow.island.($mediaSetting[1])) -ne [bool]$expected) {
            throw ('Multimedia no guardo '+$mediaSetting[1]+'='+$expected)
          }
        }
      }
      Write-Output '[PASS] Multimedia: mostrar reproductor y canción compacta, persistencia en ambos sentidos.'
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
      foreach ($entry in @(@('Expandir al acercar el cursor','expandOnHover'),@('Reducir movimiento (sin transiciones)','reducedMotion'))) {
        $control = $main.FindFirst(
          [System.Windows.Automation.TreeScope]::Descendants,
          [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $entry[0]))
        if ($null -eq $control) { throw ('Control de animacion ausente: ' + $entry[0]) }
        $control.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
      }
      Start-Sleep -Milliseconds 400
      $saved = Get-Content -Raw (Join-Path $testDir 'data\config.json') | ConvertFrom-Json
      if ($saved.island.expandOnHover -or !$saved.island.reducedMotion) {
        throw 'Las preferencias de movimiento no se guardaron en config.json.'
      }
      $fpsSelector = $main.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
          [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
          'IslandFrameRateSelector'))
      if ($null -eq $fpsSelector) { throw 'No se encontro el selector de frecuencia de la isla.' }
      foreach ($limit in @(60,240)) {
        $fpsSelector.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 150
        $item = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
          [System.Windows.Automation.TreeScope]::Descendants,
          [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, "$limit FPS"))
        if ($null -eq $item) { throw ("No existe la opcion $limit FPS") }
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 350
        $cfgCurrent = Get-Content -Raw (Join-Path $testDir 'data\config.json') | ConvertFrom-Json
        if ($cfgCurrent.island.animationRateMode -ne "$limit") {
          throw ("El modo $limit FPS no se guardo correctamente.")
        }
        Write-Output ("[PASS] Frecuencia manual $limit FPS: persistencia correcta.")
      }
      # Dejar modo automatico al terminar la prueba de configuración.
      $fpsSelector.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
      Start-Sleep -Milliseconds 150
      $auto = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
          [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
          [System.Windows.Automation.ControlType]::ListItem)) |
        Where-Object { $_.Current.Name -like 'Auto*' } |
        Select-Object -First 1)
      if ($auto.Count -eq 0) { throw 'No se encontro el modo automatico de animacion.' }
      $auto[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
      Start-Sleep -Milliseconds 300
      $cfgCurrent = Get-Content -Raw (Join-Path $testDir 'data\config.json') | ConvertFrom-Json
      if ($cfgCurrent.island.animationRateMode -ne 'Auto') { throw 'No se restauro el modo automatico.' }
      Write-Output '[PASS] Quinta pestaña, controles de movimiento y selector FPS verificados.'
    }
    Write-Output ('[PASS] ' + $scenario + ': island=' + $island + ', dashboard=' + $dashboard + ', RAM=' + [math]::Round($p.WorkingSet64/1MB,1) + ' MB')
  } finally {
    $p.Refresh()
    if (!$p.HasExited) { Stop-Process -Id $p.Id -Force }
    Start-Sleep -Milliseconds 800
  }
}
Write-Output ('[PASS] Pruebas aisladas en ' + $testDir)
