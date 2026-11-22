using System;
using System.Runtime.InteropServices;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// One subclass on the main HWND for the two messages WinUI does not surface:
    /// WM_HOTKEY (RegisterHotKey) and the Shell_NotifyIcon callback. Without this
    /// the global hotkey and the tray menu are registered but never answered.
    /// </summary>
    internal sealed class ShellMessageHook : IDisposable
    {
        private const uint WmHotkey = 0x0312;
        private const uint WmContextMenu = 0x007B;
        private const uint WmRButtonUp = 0x0205;
        private const uint WmLButtonDblClk = 0x0203;
        private const uint WmShowExisting = 0x0400 + 2;

        private readonly IntPtr _hwnd;
        private readonly uint _trayCallbackMessage;
        private readonly SubclassProc _proc;
        private bool _installed;

        public ShellMessageHook(IntPtr hwnd, uint trayCallbackMessage)
        {
            _hwnd = hwnd;
            _trayCallbackMessage = trayCallbackMessage;
            _proc = Dispatch;
            _installed = SetWindowSubclass(hwnd, _proc, new UIntPtr(0xD5B0), UIntPtr.Zero);
        }

        public event Action<int> HotkeyPressed;

        public event Action TrayLeftClick;

        public event Action TrayRightClick;

        public event Action ShowRequested;

        private IntPtr Dispatch(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
        {
            if (message == WmShowExisting)
            {
                ShowRequested?.Invoke();
                return IntPtr.Zero;
            }

            if (message == WmHotkey)
            {
                HotkeyPressed?.Invoke((int)wParam.ToUInt64());
                return IntPtr.Zero;
            }

            if (_trayCallbackMessage != 0 && message == _trayCallbackMessage)
            {
                uint mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouseMessage == WmRButtonUp || mouseMessage == WmContextMenu)
                {
                    TrayRightClick?.Invoke();
                    return IntPtr.Zero;
                }

                if (mouseMessage == WmLButtonDblClk)
                {
                    TrayLeftClick?.Invoke();
                    return IntPtr.Zero;
                }
            }

            return DefSubclassProc(hwnd, message, wParam, lParam);
        }

        public void Dispose()
        {
            if (!_installed)
            {
                return;
            }

            RemoveWindowSubclass(_hwnd, _proc, new UIntPtr(0xD5B0));
            _installed = false;
        }

        private delegate IntPtr SubclassProc(
            IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

        [DllImport("comctl32.dll")]
        private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, UIntPtr data);

        [DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    }
}
