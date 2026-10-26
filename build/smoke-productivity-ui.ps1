[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$src=Join-Path $root 'artifacts\Nokto-Dev-Portable-x64\Nokto.Dev.exe'
$folder=Join-Path $env:TEMP ('nokto-qa-art-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path (Join-Path $folder 'data') | Out-Null
Copy-Item -LiteralPath $src -Destination (Join-Path $folder 'Nokto.Dev.exe')
New-Item -ItemType File -Path (Join-Path $folder 'portable.lock') | Out-Null
$data=@{island=@{enabled=$true;style='Eyes';clockVisibility='Expand';showClock=$true;startupMode='Island';startupModeConfigured=$true;followCursor=$true;naturalBlink=$true;animationRateMode='60';reducedMotion=$false;expandOnHover=$true;showAudioControls=$true;showBatteryStatus=$true;showNetworkStatus=$false}; settings=@{language='es';theme='Dark';startMinimizedToTray=$false;checkUpdatesOnStartup=$false;startInWorkMode=$false};lanServer=@{enabled=$false}}
[IO.File]::WriteAllText((Join-Path $folder 'data\config.json'),($data|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class QaWindows {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p,IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h,StringBuilder b,int len);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int left,top,right,bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out Pt p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [StructLayout(LayoutKind.Sequential)] public struct Pt {public int x,y;}
  public static IntPtr Find(uint target) {
    IntPtr result=IntPtr.Zero;
    EnumWindows((h,l)=>{
      uint pid;GetWindowThreadProcessId(h,out pid);
      if(pid==target && IsWindowVisible(h)) {
        var b=new StringBuilder(128);GetWindowText(h,b,b.Capacity);
        if(b.ToString().Contains("Nokto Island")) result=h;
      }
      return true;
    },IntPtr.Zero);
    return result;
  }
}
'@
function Capture($h,$name) {
  $r=New-Object QaWindows+Rect
  if(-not [QaWindows]::GetWindowRect($h,[ref]$r)){throw 'GetWindowRect failed'}
  $w=[Math]::Min([Math]::Max($r.right-$r.left,10),900)
  $hgt=[Math]::Min([Math]::Max($r.bottom-$r.top,10),950)
  $bmp=New-Object System.Drawing.Bitmap($w,$hgt)
  $graphics=[System.Drawing.Graphics]::FromImage($bmp)
  try{$graphics.CopyFromScreen($r.left,$r.top,0,0,$bmp.Size);$bmp.Save((Join-Path $root ('artifacts\qa-island-'+$name+'.png')),[System.Drawing.Imaging.ImageFormat]::Png)}
  finally{$graphics.Dispose();$bmp.Dispose()}
  Write-Host ('[CAPTURE] '+$name+' '+$w+'x'+$hgt+' top='+$r.top+' left='+$r.left)
  return $r
}
$oldCursor=New-Object QaWindows+Pt
[void][QaWindows]::GetCursorPos([ref]$oldCursor)
[void][QaWindows]::SetCursorPos(4,700)
$p=Start-Process -FilePath (Join-Path $folder 'Nokto.Dev.exe') -WorkingDirectory $folder -PassThru
try{
  Start-Sleep -Seconds 11
  $p.Refresh()
  if($p.HasExited){throw ('La app salio con '+$p.ExitCode)}
  $h=[QaWindows]::Find([uint32]$p.Id)
  if($h -eq [IntPtr]::Zero){throw 'No se encontro ventana Nokto Island'}
  $rect=Capture $h 'compact'
  [void][QaWindows]::SetCursorPos([int](($rect.left+$rect.right)/2),[int]($rect.top+20))
  Start-Sleep -Milliseconds 900
  $rect=Capture $h 'expanded'
  $ui=[System.Windows.Automation.AutomationElement]::FromHandle($h)
  $test=$ui.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'Probar ojos'))
  if($null -eq $test){ throw 'No aparece el boton Probar ojos al expandir.' }
  $test.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 170
  [void](Capture $h 'gaze-left')
  Start-Sleep -Milliseconds 610
  [void](Capture $h 'gaze-right')
  $scope=[System.Windows.Automation.TreeScope]::Descendants
  function FindAutomation([string]$id) {
    return $ui.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new(
      [System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
  }
  $toggle=FindAutomation 'IslandProductivityToggle'
  if ($null -eq $toggle) {throw 'No se encontró entrada de productividad'}
  $toggle.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 650
  [void](Capture $h 'productivity')
  $timer=FindAutomation 'IslandTimer5'
  if ($null -eq $timer) {throw 'No aparece temporizador de 5 minutos'}
  $timer.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 280
  $saved=Get-Content -Raw (Join-Path $folder 'data\config.json') | ConvertFrom-Json
  if ($null -eq $saved.island.productivity.timerDeadlineUtc) {throw 'Timer no persistido'}
  Write-Output '[PASS] Timer 5m inicializado y guardado.'
  $stopwatch=FindAutomation 'IslandStopwatch'
  if ($null -eq $stopwatch) {throw 'No aparece el cronometro'}
  $stopwatch.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 230
  $saved=Get-Content -Raw (Join-Path $folder 'data\config.json') | ConvertFrom-Json
  if ($null -eq $saved.island.productivity.stopwatchStartedUtc) {throw 'Cronometro no persistido'}
  Write-Output '[PASS] Cronometro iniciado y guardado.'
  $pomo=FindAutomation 'IslandPomodoro'
  if ($null -eq $pomo) {throw 'No aparece Pomodoro'}
  $pomo.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 250
  $saved=Get-Content -Raw (Join-Path $folder 'data\config.json') | ConvertFrom-Json
  if ($null -eq $saved.island.productivity.pomodoroDeadlineUtc) {throw 'Pomodoro no persistido'}
  Write-Output '[PASS] Pomodoro iniciado y guardado.'

  $task=FindAutomation 'IslandTaskInput'
  $add=FindAutomation 'IslandAddTask'
  if($null -eq $task -or $null -eq $add){throw 'Faltan campos de tareas'}
  $task.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Tarea automatizada DEV')
  $add.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 230
  $saved=Get-Content -Raw (Join-Path $folder 'data\config.json') | ConvertFrom-Json
  if (@($saved.island.productivity.tasks).Count -ne 1) {throw 'No se guardo la tarea'}
  Write-Output '[PASS] Tarea creada y persistida.'

  $alarm=FindAutomation 'IslandAlarmTime'
  $addAlarm=FindAutomation 'IslandAddAlarm'
  if($null -eq $alarm -or $null -eq $addAlarm){throw 'Faltan controles de alarma'}
  $alarm.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('23:59')
  $addAlarm.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 250
  $saved=Get-Content -Raw (Join-Path $folder 'data\config.json') | ConvertFrom-Json
  if (@($saved.island.productivity.alarms).Count -ne 1) {throw 'No se guardo la alarma'}
  Write-Output '[PASS] Alarma HH:mm creada y persistida en DEV temporal.'
}
finally{
  [void][QaWindows]::SetCursorPos($oldCursor.x,$oldCursor.y)
  $p.Refresh()
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force}
  Start-Sleep -Milliseconds 450
  Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
}
