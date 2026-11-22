using System;
using System.Runtime.InteropServices;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// WinUI lays out in DIPs but AppWindow sizes in physical pixels, so every
    /// window size must be multiplied by the monitor scale or the content gets
    /// clipped (this is what cut the time picker off at 125%/150%).
    /// </summary>
    internal static class Dpi
    {
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        internal static double ScaleFor(IntPtr hwnd)
        {
            try
            {
                uint dpi = GetDpiForWindow(hwnd);
                return dpi == 0 ? 1.0 : dpi / 96.0;
            }
            catch
            {
                return 1.0;
            }
        }

        internal static int Px(IntPtr hwnd, int dip)
        {
            return (int)Math.Round(dip * ScaleFor(hwnd));
        }

        internal static int Dip(IntPtr hwnd, int px)
        {
            double scale = ScaleFor(hwnd);
            return scale <= 0 ? px : (int)Math.Round(px / scale);
        }
    }
}
