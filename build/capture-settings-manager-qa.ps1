$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $env:TEMP ('nokto-settings-qa-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path (Join-Path $folder 'data') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'artifacts\Nokto-Dev-Portable-miradas\Nokto.Dev.exe') -Destination (Join-Path $folder 'Nokto.Dev.exe')
New-Item -ItemType File -Path (Join-Path $folder 'portable.lock') | Out-Null
$config = @{island=@{enabled=$false;startupMode='Dashboard';startupModeConfigured=$true};settings=@{language='es';theme='Dark';checkUpdatesOnStartup=$false;startMinimizedToTray=$false;startInWorkMode=$false};lanServer=@{enabled=$false}}
[IO.File]::WriteAllText((Join-Path $folder 'data\config.json'),($config | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class QaMain {
 public delegate bool EnumProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc,IntPtr l);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h,StringBuilder buf,int length);
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int left,top,right,bottom;}
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 public static IntPtr Find(uint pid) {
  IntPtr found=IntPtr.Zero;
  EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p);
   if(p==pid && IsWindowVisible(h)){
     var name=new StringBuilder(160);GetWindowText(h,name,name.Capacity);
     if(name.ToString().Contains("Nokto 2.0 DEV"))found=h;
   }
   return true;},IntPtr.Zero);
  return found;
 }
}
'@
function SaveShot([IntPtr]$handle,[string]$tag){
 $rect=New-Object QaMain+Rect
 if(-not [QaMain]::GetWindowRect($handle,[ref]$rect)){throw "WindowRect"}
 $width=[Math]::Min(1500,[Math]::Max(10,$rect.right-$rect.left))
 $height=[Math]::Min(925,[Math]::Max(10,$rect.bottom-$rect.top))
 $bmp=New-Object Drawing.Bitmap($width,$height)
 $graphics=[Drawing.Graphics]::FromImage($bmp)
 try{$graphics.CopyFromScreen($rect.left,$rect.top,0,0,$bmp.Size);$bmp.Save((Join-Path $root ("artifacts\qa-"+$tag+".png")),[Drawing.Imaging.ImageFormat]::Png)}
 finally{$graphics.Dispose();$bmp.Dispose()}
 Write-Output ("[SHOT] "+$tag+": "+$width+"x"+$height)
}
$proc=Start-Process -FilePath (Join-Path $folder 'Nokto.Dev.exe') -WorkingDirectory $folder -PassThru
try {
 Start-Sleep -Seconds 10
 $proc.Refresh()
 if($proc.HasExited){throw "Nokto closed"}
 $handle=[QaMain]::Find([uint32]$proc.Id)
 if($handle -eq [IntPtr]::Zero){throw "MainWindow missing"}
 $main=[System.Windows.Automation.AutomationElement]::FromHandle($handle)
 $tabs=@($main.FindAll([System.Windows.Automation.TreeScope]::Descendants,
 [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,
 [System.Windows.Automation.ControlType]::TabItem)))
 $island=@($tabs|Where-Object{$_.Current.Name -like "*Isla Din*"}|Select-Object -First 1)
 if($island.Count -ne 1){throw "Island tab missing"}
 $island[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Start-Sleep -Seconds 1
 SaveShot $handle 'settings-grid'
 $routines=@($tabs|Where-Object{$_.Current.Name -like "*Rutinas*"}|Select-Object -First 1)
 if($routines.Count -ne 1){throw "Routines tab missing"}
 $routines[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Start-Sleep -Milliseconds 750
 $inner=@($main.FindAll([System.Windows.Automation.TreeScope]::Descendants,
 [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,
 [System.Windows.Automation.ControlType]::TabItem)))
 $manager=@($inner|Where-Object{$_.Current.Name -like "*PRODUCTIVIDAD*"}|Select-Object -First 1)
 if($manager.Count -ne 1){throw "Productivity manager tab missing"}
 $manager[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Start-Sleep -Milliseconds 950
 SaveShot $handle 'manager-productivity'
 $check=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
 [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'ManagerTimer5'))
 if($null -eq $check){throw 'Manager timer controls missing'}
 $check.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 Start-Sleep -Milliseconds 300
 $cfg=Get-Content -Raw (Join-Path $folder 'data\config.json') | ConvertFrom-Json
 if($null -eq $cfg.island.productivity.timerDeadlineUtc){throw 'Manager timer failed to save.'}
 $taskInput=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
   [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'ManagerTaskInput'))
 $taskBtn=$main.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
   [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'ManagerAddTask'))
 if($null -eq $taskInput -or $null -eq $taskBtn){throw 'Manager task controls missing'}
 $taskInput.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Tarea QA aislada')
 $taskBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 Start-Sleep -Milliseconds 300
 $cfg=Get-Content -Raw (Join-Path $folder 'data\config.json') | ConvertFrom-Json
 if(@($cfg.island.productivity.tasks).Count -ne 1){throw 'Manager task failed to save'}
 Write-Output '[PASS] Gestor: temporizador y tarea guardados desde la nueva pestaña, solamente en datos temporales.'
} finally {
 $proc.Refresh()
 if(-not $proc.HasExited){Stop-Process -Id $proc.Id -Force}
 Start-Sleep -Milliseconds 450
 Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
}
