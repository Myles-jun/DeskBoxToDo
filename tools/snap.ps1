# Captures a running app window to PNG (physical pixels), moving it to a clear
# area and pulsing topmost first so the glass is not covered by other windows.
# Usage: powershell -ExecutionPolicy Bypass -File tools\snap.ps1 [-Out shot.png] [-X 1460] [-Y 120]
param(
    [string]$Process = 'DeskBoxToDo',
    [string]$Out = 'shots\shot.png',
    [string]$TitleLike = '',
    [int]$X = 1460,
    [int]$Y = 120,
    [switch]$NoMove
)

Add-Type -TypeDefinition @'
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public class SnapWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static IntPtr Find(string proc, string titleLike) {
    var pids = new HashSet<uint>(); foreach (var p in System.Diagnostics.Process.GetProcessesByName(proc)) pids.Add((uint)p.Id);
    IntPtr found = IntPtr.Zero;
    EnumWindows((h,l) => { uint pid; GetWindowThreadProcessId(h, out pid);
      if (!pids.Contains(pid) || !IsWindowVisible(h)) return true;
      var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      string t = sb.ToString();
      if (t.Length == 0) return true;
      if (titleLike.Length > 0 && t.IndexOf(titleLike, StringComparison.OrdinalIgnoreCase) < 0) return true;
      RECT r; GetWindowRect(h, out r); if (r.R-r.L < 80 || r.B-r.T < 80) return true;
      found = h; return false; }, IntPtr.Zero);
    return found; }
}
'@

[SnapWin]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
Add-Type -AssemblyName System.Drawing

$hwnd = [SnapWin]::Find($Process, $TitleLike)
if ($hwnd -eq [IntPtr]::Zero) { Write-Error "window not found: $Process / $TitleLike"; exit 2 }

if (-not $NoMove) {
    # HWND_TOPMOST without moving; glass over the wallpaper stays visible.
    [SnapWin]::SetWindowPos($hwnd, [IntPtr](-1), $X, $Y, 0, 0, 0x0001 -bor 0x0004) | Out-Null
    Start-Sleep -Milliseconds 700
}

$r = New-Object SnapWin+RECT
[SnapWin]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$w = $r.R - $r.L
$h = $r.B - $r.T

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $w, $h))
$g.Dispose()

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

if (-not $NoMove) {
    [SnapWin]::SetWindowPos($hwnd, [IntPtr](-2), 0, 0, 0, 0, 0x0001 -bor 0x0002 -bor 0x0004) | Out-Null
}

"$Out  ${w}x${h}  at=$($r.L),$($r.T)"
