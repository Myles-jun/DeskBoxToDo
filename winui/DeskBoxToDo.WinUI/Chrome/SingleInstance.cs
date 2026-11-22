using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// One process per session. A second launch asks the running window to come
    /// forward; if the other process has no reachable window (hidden to tray or an
    /// older build with a broken tray) the user gets an explicit message instead of
    /// a silent no-op.
    /// </summary>
    internal static class SingleInstance
    {
        private const string MutexName = @"Local\DeskBoxToDo.WinUI.SingleInstance";
        private const uint WmShowWindowRequest = 0x0400 + 2;
        private const int SwShow = 5;
        private static Mutex _mutex;

        public static bool TryAcquire()
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            if (createdNew)
            {
                return true;
            }

            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        /// <summary>Brings the existing instance forward, or tells the user why it cannot.</summary>
        public static void NotifyExisting()
        {
            IntPtr hwnd = FindAppWindow();
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SwShow);
                PostMessage(hwnd, WmShowWindowRequest, IntPtr.Zero, IntPtr.Zero);
                SetForegroundWindow(hwnd);
                return;
            }

            MessageBox(IntPtr.Zero,
                "DeskBoxToDo 已在运行，但找不到它的窗口（可能被隐藏到托盘，或托盘图标没有创建成功）。\n\n" +
                "请在任务管理器里结束所有 DeskBoxToDo.exe，再重新启动。",
                "DeskBoxToDo",
                0x00000040 /* MB_ICONINFORMATION */);
        }

        private static IntPtr FindAppWindow()
        {
            var targetPids = new System.Collections.Generic.HashSet<uint>();
            string self = Process.GetCurrentProcess().ProcessName;
            uint selfPid = (uint)Environment.ProcessId;
            foreach (Process process in Process.GetProcessesByName(self))
            {
                if ((uint)process.Id != selfPid)
                {
                    targetPids.Add((uint)process.Id);
                }
            }

            if (targetPids.Count == 0)
            {
                return IntPtr.Zero;
            }

            IntPtr visible = IntPtr.Zero;
            IntPtr any = IntPtr.Zero;
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (!targetPids.Contains(pid))
                {
                    return true;
                }

                var title = new StringBuilder(256);
                GetWindowText(hwnd, title, 256);
                if (title.Length > 0)
                {
                    if (any == IntPtr.Zero)
                    {
                        any = hwnd;
                    }

                    if (IsWindowVisible(hwnd))
                    {
                        visible = hwnd;
                        return false;
                    }
                }

                return true;
            }, IntPtr.Zero);

            return visible != IntPtr.Zero ? visible : any;
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
    }
}
