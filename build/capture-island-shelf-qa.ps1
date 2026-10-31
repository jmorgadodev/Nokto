[CmdletBinding()]
param([int]$SizePercent=100,[string]$Eye='Orbital')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$src=Join-Path $root 'artifacts\Nokto-Dev-Portable-miradas\Nokto.Dev.exe'
$folder=Join-Path $env:TEMP ('nokto-qa-art-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path (Join-Path $folder 'data') | Out-Null
Copy-Item -LiteralPath $src -Destination (Join-Path $folder 'Nokto.Dev.exe')
New-Item -ItemType File -Path (Join-Path $folder 'portable.lock') | Out-Null
Set-Content -LiteralPath (Join-Path $folder 'sample-shelf.txt') -Value 'Archivo sintético de prueba Nokto Dev' -Encoding UTF8
$data=@{island=@{enabled=$true;style=$Eye;sizePercent=$SizePercent;clockVisibility='Expand';showClock=$true;startupMode='Island';startupModeConfigured=$true;followCursor=$true;naturalBlink=$true;animationRateMode='60';reducedMotion=$false;expandOnHover=$true;showAudioControls=$true;showBatteryStatus=$true;showNetworkStatus=$false;showFileShelf=$true;shelfFiles=@((Join-Path $folder 'sample-shelf.txt'))}; settings=@{language='es';theme='Dark';startMinimizedToTray=$false;checkUpdatesOnStartup=$false;startInWorkMode=$false};lanServer=@{enabled=$false}}
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
  $hgt=[Math]::Min([Math]::Max($r.bottom-$r.top,10),550)
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
  $rect=Capture $h ('shelf-closed-'+$SizePercent)
  $ui=[System.Windows.Automation.AutomationElement]::FromHandle($h)
  $toggle=$ui.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
     [System.Windows.Automation.PropertyCondition]::new(
       [System.Windows.Automation.AutomationElement]::AutomationIdProperty,'IslandShelfToggle'))
  if($null -eq $toggle){throw 'No aparece bandeja opt-in en la isla.'}
  $toggle.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 700
  [void](Capture $h ('shelf-open-'+$SizePercent))
  $list=$ui.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
     [System.Windows.Automation.PropertyCondition]::new(
       [System.Windows.Automation.AutomationElement]::AutomationIdProperty,'IslandShelfList'))
  if($null -eq $list){throw 'Falta lista de referencias a archivos.'}
  Write-Output '[PASS] Bandeja de archivos en UI, activación y expansión.'
  $test=$ui.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'Probar ojos'))
  if($null -eq $test){ throw 'No aparece el boton Probar ojos al expandir.' }
  $test.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 170
  [void](Capture $h ('gaze-left-'+$SizePercent))
  Start-Sleep -Milliseconds 610
  [void](Capture $h ('gaze-right-'+$SizePercent))
  Write-Output '[PASS] Capturas de poses del boton Probar ojos generadas.'
}
finally{
  [void][QaWindows]::SetCursorPos($oldCursor.x,$oldCursor.y)
  $p.Refresh()
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force}
  Start-Sleep -Milliseconds 450
  Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
}
