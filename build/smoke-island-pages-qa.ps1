[CmdletBinding()]
param([int]$SizePercent=100,[string]$Eye='Orbital')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$src=Join-Path $root 'artifacts\Nokto-Dev-Portable-x64\Nokto.Dev.exe'
$folder=Join-Path $env:TEMP ('nokto-qa-art-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path (Join-Path $folder 'data') | Out-Null
Copy-Item -LiteralPath $src -Destination (Join-Path $folder 'Nokto.Dev.exe')
New-Item -ItemType File -Path (Join-Path $folder 'portable.lock') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $folder 'QuickSample') | Out-Null
$data=@{island=@{enabled=$true;style=$Eye;sizePercent=$SizePercent;quickFolders=@((Join-Path $folder 'QuickSample'));showQuickActions=$true;enableWheelPages=$true;showClipboardActions=$true;showMainButton=$false;showHideButton=$false;clockVisibility='Expand';showClock=$true;startupMode='Island';startupModeConfigured=$true;followCursor=$true;naturalBlink=$true;animationRateMode='60';reducedMotion=$false;expandOnHover=$true;showAudioControls=$true;showBatteryStatus=$true;showNetworkStatus=$false}; settings=@{language='es';theme='Dark';startMinimizedToTray=$false;checkUpdatesOnStartup=$false;startInWorkMode=$false};lanServer=@{enabled=$false}}
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
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags,int dx,int dy,int data,UIntPtr extra);
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
  $hgt=[Math]::Min([Math]::Max($r.bottom-$r.top,10),850)
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
  $rect=Capture $h ('compact-'+$SizePercent)
  [void][QaWindows]::SetCursorPos([int](($rect.left+$rect.right)/2),[int]($rect.top+20))
  Start-Sleep -Milliseconds 900
  $rect=Capture $h ('expanded-'+$SizePercent)
  $ui=[System.Windows.Automation.AutomationElement]::FromHandle($h)
  [QaWindows]::mouse_event(0x0800,0,0,-120,[UIntPtr]::Zero)
  Start-Sleep -Milliseconds 390
  $wheelTitle=$ui.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'IslandPageTitle'))
  if($null -eq $wheelTitle -or $wheelTitle.Current.Name -notlike '*Sonido*'){ throw ('Rueda no cambió a Sonido: '+$wheelTitle.Current.Name) }
  [QaWindows]::mouse_event(0x0800,0,0,120,[UIntPtr]::Zero)
  Start-Sleep -Milliseconds 390
  if($wheelTitle.Current.Name -notlike '*Sonido*'){throw 'La rueda provocó salto dentro de Sonido'}
  Write-Output '[PASS] Rueda en Sonido: no cambia de página.'
  $previous=$ui.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'IslandPagePrevious'))
  $previous.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 250
  $next=$ui.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'IslandPageNext'))
  if($null -eq $next){throw 'No se encontro navegación entre paginas'}
  $next.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 470
  [void](Capture $h ('audio-'+$SizePercent))
  $slider=$ui.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'IslandVolumeSlider'))
  if($null -eq $slider){throw 'No se encontro barra de volumen'}
  Write-Output '[PASS] Pagina sonido y barra presentes (sin cambiar el volumen).'
  $next.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 470
  [void](Capture $h ('shortcuts-'+$SizePercent))
  foreach($key in @('IslandOpenExplorer','IslandAddFolder','IslandFolderList','IslandClipboardRead','IslandRoutinesButton')){
    $found=$ui.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$key))
    if($null -eq $found){throw ('Falta: '+$key)}
  }
  Write-Output '[PASS] Pagina accesos, rutinas, carpetas y portapapeles manual presentes.'
}
finally{
  [void][QaWindows]::SetCursorPos($oldCursor.x,$oldCursor.y)
  $p.Refresh()
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force}
  Start-Sleep -Milliseconds 450
  Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
}
