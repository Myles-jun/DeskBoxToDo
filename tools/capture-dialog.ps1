param(
    [string]$ProcessName = 'DeskBoxToDo',
    [string]$Out = 'preview_dialog.png',
    [int]$TimeoutSeconds = 12,
    [switch]$KeepOpen
)

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
public class DlgCapture {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

  public static List<IntPtr> VisibleWindowsOf(string processName) {
    var pids = new HashSet<uint>();
    foreach (var p in Process.GetProcessesByName(processName)) pids.Add((uint)p.Id);
    var found = new List<IntPtr>();
    EnumWindows((h, l) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pids.Contains(pid) && IsWindowVisible(h)) {
        var sb = new StringBuilder(8); GetWindowText(h, sb, 8);
        if (sb.Length > 0) found.Add(h);
      }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
'@

[DlgCapture]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Drawing

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$wins = @()
while ((Get-Date) -lt $deadline) {
    $wins = [DlgCapture]::VisibleWindowsOf($ProcessName)
    if ($wins.Count -gt 0) { break }
    Start-Sleep -Milliseconds 200
}

if ($wins.Count -eq 0) {
    Write-Output "NO_WINDOW"
    exit 1
}

Write-Output ("WINDOWS " + $wins.Count)
$handle = $wins[0]
Start-Sleep -Milliseconds 800

$rect = New-Object DlgCapture+RECT
[DlgCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.R - $rect.L
$height = $rect.B - $rect.T
Write-Output ("RECT " + $rect.L + "," + $rect.T + " " + $width + "x" + $height)

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.L, $rect.T, 0, 0, (New-Object System.Drawing.Size $width, $height))
$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()
Write-Output ("SAVED " + $Out)

if (-not $KeepOpen) {
    foreach ($w in $wins) {
        [DlgCapture]::PostMessage($w, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    }
    Start-Sleep -Milliseconds 500
    Write-Output ("REMAINING " + ([DlgCapture]::VisibleWindowsOf($ProcessName)).Count)
}
