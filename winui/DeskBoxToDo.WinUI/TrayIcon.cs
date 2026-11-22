using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Minimal Shell_NotifyIcon tray icon (WinUI has no NotifyIcon). Left click
    /// restores the window; right click opens a small native menu.
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        public const int CallbackMessage = 0x0400 + 1; // WM_USER + 1
        public const int CommandShow = 1;
        public const int CommandDesktopLayer = 2;
        public const int CommandSettings = 3;
        public const int CommandHelp = 4;
        public const int CommandExit = 5;

        private const uint NimAdd = 0;
        private const uint NimModify = 1;
        private const uint NimDelete = 2;
        private const uint NifMessage = 0x01;
        private const uint NifIcon = 0x02;
        private const uint NifTip = 0x04;
        private const uint MfString = 0x0000;
        private const uint MfSeparator = 0x0800;
        private const uint MfChecked = 0x0008;
        private const uint TpmRightButton = 0x0002;
        private const uint TpmReturnCmd = 0x0100;
        private const uint ImageIcon = 1;
        private const uint LrLoadFromFile = 0x0010;

        private IntPtr _hwnd;
        private IntPtr _icon;
        private bool _added;

        public void Create(IntPtr hwnd, string tooltip)
        {
            _hwnd = hwnd;
            _icon = LoadTrayIcon();

            var data = NewData();
            data.uFlags = NifMessage | NifIcon | NifTip;
            data.hIcon = _icon;
            data.szTip = tooltip;
            _added = Shell_NotifyIcon(NimAdd, ref data);
            Console.WriteLine("tray icon: " + _added);
        }

        /// <summary>Returns the chosen command id, or 0 when the menu was dismissed.</summary>
        public int ShowMenu(bool desktopLayer)
        {
            IntPtr menu = CreatePopupMenu();
            AppendMenu(menu, MfString, CommandShow, "打开待办");
            AppendMenu(menu, desktopLayer ? MfChecked : MfString, CommandDesktopLayer, "固定在桌面（置顶）");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, CommandSettings, "设置…");
            AppendMenu(menu, MfString, CommandHelp, "使用帮助");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, CommandExit, "退出程序");

            GetCursorPos(out POINT point);
            SetForegroundWindow(_hwnd);
            int command = TrackPopupMenu(
                menu,
                TpmRightButton | TpmReturnCmd,
                point.X,
                point.Y,
                0,
                _hwnd,
                IntPtr.Zero);
            DestroyMenu(menu);
            return command;
        }

        private NOTIFYICONDATA NewData()
        {
            return new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
                uCallbackMessage = CallbackMessage,
                szTip = "",
                szInfo = "",
                szInfoTitle = ""
            };
        }

        private static IntPtr LoadTrayIcon()
        {
            try
            {
                string exeDir = AppContext.BaseDirectory;
                string icoPath = Path.Combine(exeDir, "app.ico");
                if (File.Exists(icoPath))
                {
                    IntPtr loaded = LoadImage(IntPtr.Zero, icoPath, ImageIcon, 0, 0, LrLoadFromFile);
                    if (loaded != IntPtr.Zero)
                    {
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("tray icon load failed: " + ex.Message);
            }

            return LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION
        }

        public void Dispose()
        {
            if (!_added)
            {
                return;
            }

            var data = NewData();
            Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;

            public uint dwState;
            public uint dwStateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;

            public uint uVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;

            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int cx, int cy, uint load);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr menu, uint flags, int id, string text);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);
    }
}
