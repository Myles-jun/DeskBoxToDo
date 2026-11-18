using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>
    /// Short-lived foreground sampler. When the hotkey fires we sample whichever
    /// window owns the foreground for ~1.5s, so the log shows which other app
    /// also reacted to the chord (screenshot tools race us for Alt+letter).
    /// </summary>
    internal static class ForegroundProbe
    {
        public static void Sample(string reason)
        {
            var timer = new Timer { Interval = 200 };
            int ticks = 0;
            string last = null;
            timer.Tick += (sender, args) =>
            {
                ticks++;
                string current = DescribeForeground();
                if (!string.Equals(current, last, StringComparison.Ordinal))
                {
                    Log.Write(string.Format("foreground[{0}] +{1}ms {2}", reason, ticks * 200, current));
                    last = current;
                }

                if (ticks >= 8)
                {
                    timer.Stop();
                    timer.Dispose();
                }
            };
            timer.Start();
        }

        private static string DescribeForeground()
        {
            try
            {
                IntPtr handle = GetForegroundWindow();
                if (handle == IntPtr.Zero)
                {
                    return "(none)";
                }

                var title = new StringBuilder(256);
                GetWindowText(handle, title, 256);

                var className = new StringBuilder(256);
                GetClassName(handle, className, 256);

                uint pid;
                GetWindowThreadProcessId(handle, out pid);
                string processName = "?";
                try
                {
                    processName = Process.GetProcessById((int)pid).ProcessName;
                }
                catch
                {
                    // Process may already be gone.
                }

                return processName + " | " + className + " | " + title;
            }
            catch (Exception ex)
            {
                return "(probe failed: " + ex.Message + ")";
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int count);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
