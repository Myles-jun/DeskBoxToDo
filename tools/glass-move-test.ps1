param(
    [string]$OutDir = 'C:\Users\Myles\Documents\deepseek-harness\default-workspace\DeskBoxToDo'
)

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
public class Mover2 {
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

[Mover2]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Drawing

$zero = [IntPtr]::Zero

function Shot([string]$path, [int]$x, [int]$y, [int]$w = 520, [int]$ht = 470) {
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $ht))
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

function GlassMean([string]$path) {
    $b = [System.Drawing.Bitmap]::FromFile($path)
    $sum = 0; $n = 0
    for ($y = 400; $y -lt 460; $y += 4) {
        for ($x = 380; $x -lt 500; $x += 4) {
            $p = $b.GetPixel($x, $y)
            $sum += ($p.R + $p.G + $p.B) / 3
            $n++
        }
    }
    $b.Dispose()
    return [math]::Round($sum / $n, 1)
}

$stale = [Mover2]::FindFor('DeskBoxToDo')
if ($stale -ne $zero) {
    [Mover2]::PostMessage($stale, 0x0010, $zero, $zero) | Out-Null
    Start-Sleep -Milliseconds 400
}

Get-Process notepad -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null }
Start-Sleep -Milliseconds 300
Start-Process notepad.exe
Start-Sleep -Seconds 2
$np = [Mover2]::FindFor('notepad')
if ($np -ne $zero) {
    [Mover2]::SetWindowPos($np, $zero, 120, 120, 1000, 780, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 400
    Write-Output 'notepad-ready'
}

$exe = "$env:LOCALAPPDATA\Programs\DeskBoxToDo\DeskBoxToDo.exe"
$input = "$OutDir\_shot_input.txt"
Start-Process -FilePath $exe -ArgumentList @('--text-file', $input, '--preview', '--dry-run')
Start-Sleep -Seconds 2
$dialog = [Mover2]::FindFor('DeskBoxToDo')
if ($dialog -eq $zero) {
    Write-Output 'NO_DIALOG'
    exit 1
}

[Mover2]::SetWindowPos($dialog, $zero, 1900, 850, 0, 0, 0x0001 -bor 0x0004) | Out-Null
Start-Sleep -Milliseconds 700
Shot "$OutDir\_glass_dark.png" 1900 850

[Mover2]::SetWindowPos($dialog, $zero, 300, 300, 0, 0, 0x0001 -bor 0x0004) | Out-Null
Start-Sleep -Milliseconds 700
Shot "$OutDir\_glass_white.png" 300 300

[Mover2]::PostMessage($dialog, 0x0010, $zero, $zero) | Out-Null
Start-Sleep -Milliseconds 300
Get-Process notepad -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null }

Write-Output ('dark glass = ' + (GlassMean "$OutDir\_glass_dark.png"))
Write-Output ('white glass = ' + (GlassMean "$OutDir\_glass_white.png"))
