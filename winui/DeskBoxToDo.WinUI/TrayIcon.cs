using System;
using System.Drawing;
using System.Windows.Forms;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Tray icon (WinForms NotifyIcon) — only the icon and its click events. The
    /// menu itself is our own glass window, so it matches every other surface.
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        public const int CommandShow = 1;
        public const int CommandDesktopLayer = 2;
        public const int CommandSettings = 3;
        public const int CommandHelp = 4;
        public const int CommandCheckUpdate = 6;
        public const int CommandExit = 5;
        public const int CommandTopMost = 7;
        public const int CommandAutostart = 8;
        public const int CommandUndo = 9;
        public const int CommandOpenLog = 10;

        private NotifyIcon _icon;

        /// <summary>Raised on right-click; the owner shows the glass menu.</summary>
        public event Action MenuRequested;

        public event Action<int> Command;

        public bool IsVisible => _icon?.Visible == true;

        public void Create(IntPtr hwnd, string tooltip, Func<WindowLayer> layerProvider)
        {
            _icon = new NotifyIcon
            {
                Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip,
                Icon = LoadIcon(),
                Visible = true
            };

            _icon.MouseUp += (sender, args) =>
            {
                if (args.Button == MouseButtons.Right)
                {
                    MenuRequested?.Invoke();
                }
            };
            _icon.DoubleClick += (sender, args) => Command?.Invoke(CommandShow);

            AppLog.Write("tray icon visible=" + _icon.Visible);
        }

        public void ShowBalloon(string title, string text)
        {
            try
            {
                _icon?.ShowBalloonTip(6000, title, text, ToolTipIcon.Warning);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] balloon failed: " + ex.Message);
            }
        }

        private static Icon LoadIcon()
        {
            try
            {
                string path = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
                if (System.IO.File.Exists(path))
                {
                    return new Icon(path);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] tray icon load failed: " + ex.Message);
            }

            return SystemIcons.Application;
        }

        public void Dispose()
        {
            if (_icon != null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
        }
    }
}
