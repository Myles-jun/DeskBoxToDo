using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Makes a frameless window draggable by its top strip using WM_NCHITTEST →
    /// HTCAPTION. WinUI replaces the window procedure when it activates, so the
    /// hook is installed after activation and re-checked by a watchdog.
    /// Nothing is drawn by Windows (the caption style bits are removed), so the
    /// glass covers the whole window and there is no black title bar.
    /// </summary>
    internal sealed class NativeDragHook
    {
        private const int GwlStyle = -16;
        private const int GwlpWndProc = -4;
        private const int WmNchittest = 0x0084;
        private const int HtCaption = 2;

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        private readonly Window _window;
        private readonly IntPtr _hwnd;
        private readonly double _headerHeightDip;
        private readonly bool _resizeByStyle;
        private WndProc _proc;
        private IntPtr _previous;
        private DispatcherTimer _watchdog;

        private NativeDragHook(Window window, IntPtr hwnd, double headerHeightDip, bool resizable)
        {
            _window = window;
            _hwnd = hwnd;
            _headerHeightDip = headerHeightDip;
            _resizeByStyle = resizable;
        }

        internal static NativeDragHook Install(Window window, double headerHeightDip)
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var hook = new NativeDragHook(window, hwnd, headerHeightDip, false);
            hook.Attach();
            return hook;
        }

        private void Attach()
        {
            StripCaption();

            _proc = Callback;
            Apply();

            // WinUI re-creates its window procedure around activation; keep ours on top.
            _watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _watchdog.Tick += (sender, args) =>
            {
                if (GetWindowLongPtr(_hwnd, GwlpWndProc) != Marshal.GetFunctionPointerForDelegate(_proc))
                {
                    Apply();
                }
            };
            _watchdog.Start();

            if (_window != null)
            {
                _window.Activated += (sender, args) => Apply();
            }
        }

        private void Apply()
        {
            try
            {
                IntPtr mine = Marshal.GetFunctionPointerForDelegate(_proc);
                IntPtr current = GetWindowLongPtr(_hwnd, GwlpWndProc);
                if (current == mine)
                {
                    return; // already ours, chaining again would recurse forever
                }

                _previous = current;
                SetWindowLongPtr(_hwnd, GwlpWndProc, mine);
                Console.WriteLine("drag hook applied hwnd=" + _hwnd);
            }
            catch (Exception ex)
            {
                Console.WriteLine("drag hook failed: " + ex.Message);
            }
        }

        private void StripCaption()
        {
            try
            {
                int style = GetWindowLong(_hwnd, GwlStyle);
                style &= ~(0x00C00000 | 0x00080000 | 0x00020000 | 0x00010000); // CAPTION|SYSMENU|MINBOX|MAXBOX
                SetWindowLong(_hwnd, GwlStyle, style);
                SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0027); // NOMOVE|NOSIZE|NOZORDER|FRAMECHANGED
                Console.WriteLine("caption stripped style=0x" + style.ToString("X8"));
            }
            catch (Exception ex)
            {
                Console.WriteLine("strip caption failed: " + ex.Message);
            }
        }

        private IntPtr Callback(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == WmNchittest)
            {
                if (GetCursorPos(out POINT screen) && ScreenToClient(hwnd, ref screen))
                {
                    double scale = Dpi.ScaleFor(hwnd);
                    int header = (int)Math.Round(_headerHeightDip * scale);
                    if (screen.Y >= 0 && screen.Y <= header)
                    {
                        Console.WriteLine("nchit caption y=" + screen.Y);
                        return (IntPtr)HtCaption;
                    }
                }
            }

            return CallWindowProc(_previous, hwnd, message, wParam, lParam);
        }
    }
}
