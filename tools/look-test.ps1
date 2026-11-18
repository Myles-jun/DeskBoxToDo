param(
    [string]$OutDir = 'C:\Users\Myles\Documents\deepseek-harness\default-workspace\DeskBoxToDo'
)

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
public class Looker {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static IntPtr FindFor(string name) {
    var pids = new HashSet<uint>();
    foreach (var p in Process.GetProcessesByName(name)) pids.Add((uint)p.Id);
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pids.Contains(pid) && IsWindowVisible(h)) {
        var sb = new StringBuilder(8); GetWindowText(h, sb, 8);
        if (sb.Length > 0) { found = h; return false; }
      }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
'@

[Looker]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Drawing
$zero = [IntPtr]::Zero

function ShootWindow([IntPtr]$hwnd, [string]$path) {
    $rect = New-Object Looker+RECT
    [Looker]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    $w = $rect.R - $rect.L
    $h = $rect.B - $rect.T
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.L, $rect.T, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Output ("shot " + $path + " " + $w + "x" + $h)
}

$stale = [Looker]::FindFor('DeskBoxToDo')
if ($stale -ne $zero) {
    [Looker]::PostMessage($stale, 0x0010, $zero, $zero) | Out-Null
    Start-Sleep -Milliseconds 400
}

Start-Process mspaint.exe
Start-Sleep -Seconds 3
$mp = [Looker]::FindFor('mspaint')
if ($mp -ne $zero) {
    [Looker]::SetWindowPos($mp, $zero, 60, 60, 1100, 850, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 600
}

$env:DESKBOX_TODO_NO_CAPTURE_EXCLUSION = '1'
$exe = "$env:LOCALAPPDATA\Programs\DeskBoxToDo\DeskBoxToDo.exe"
Start-Process -FilePath $exe -ArgumentList @('--text-file', "$OutDir\_shot_input.txt", '--preview', '--dry-run')
Start-Sleep -Seconds 3
$dialog = [Looker]::FindFor('DeskBoxToDo')
if ($dialog -eq $zero) { Write-Output 'NO_DIALOG'; exit 1 }

[Looker]::SetWindowPos($dialog, $zero, 420, 320, 0, 0, 0x0001 -bor 0x0004) | Out-Null
Start-Sleep -Milliseconds 900
ShootWindow $dialog "$OutDir\_look_white.png"

[Looker]::SetWindowPos($dialog, $zero, 1880, 880, 0, 0, 0x0001 -bor 0x0004) | Out-Null
Start-Sleep -Milliseconds 900
ShootWindow $dialog "$OutDir\_look_dark.png"

[Looker]::PostMessage($dialog, 0x0010, $zero, $zero) | Out-Null
Start-Sleep -Milliseconds 300
Get-Process mspaint -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
$env:DESKBOX_TODO_NO_CAPTURE_EXCLUSION = $null
Write-Output 'done'
