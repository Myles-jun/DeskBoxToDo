param(
    [string]$OutDir = 'C:\Users\Myles\Documents\deepseek-harness\default-workspace\DeskBoxToDo'
)

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
public class Mover4 {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
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

[Mover4]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Drawing
$zero = [IntPtr]::Zero

$stale = [Mover4]::FindFor('DeskBoxToDo')
if ($stale -ne $zero) {
    [Mover4]::PostMessage($stale, 0x0010, $zero, $zero) | Out-Null
    Start-Sleep -Milliseconds 400
}

Start-Process mspaint.exe
Start-Sleep -Seconds 3
$mp = [Mover4]::FindFor('mspaint')
if ($mp -ne $zero) {
    [Mover4]::SetWindowPos($mp, $zero, 60, 60, 1100, 850, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 500
    Write-Output 'canvas-ready'
}

$exe = "$env:LOCALAPPDATA\Programs\DeskBoxToDo\DeskBoxToDo.exe"
Start-Process -FilePath $exe -ArgumentList @('--text-file', "$OutDir\_shot_input.txt", '--preview', '--dry-run')
Start-Sleep -Seconds 2
$dialog = [Mover4]::FindFor('DeskBoxToDo')
if ($dialog -eq $zero) { Write-Output 'NO_DIALOG'; exit 1 }

Write-Output 'hold 3s at 400,300 (white canvas)'
[Mover4]::SetWindowPos($dialog, $zero, 400, 300, 0, 0, 0x0001 -bor 0x0004) | Out-Null
Start-Sleep -Seconds 3

Write-Output 'hold 3s at 1950,900 (dark desktop)'
[Mover4]::SetWindowPos($dialog, $zero, 1950, 900, 0, 0, 0x0001 -bor 0x0004) | Out-Null
Start-Sleep -Seconds 3

[Mover4]::PostMessage($dialog, 0x0010, $zero, $zero) | Out-Null
Start-Sleep -Milliseconds 300
Get-Process mspaint -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() }
Write-Output 'done'
