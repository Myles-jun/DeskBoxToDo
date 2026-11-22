using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT;

namespace DeskBoxToDo.WinUI
{
    /// <summary>Small glass toast that shows one due todo; click opens the list.</summary>
    public sealed partial class ReminderWindow : Window
    {
        private DesktopAcrylicController _acrylic;
        private SystemBackdropConfiguration _configuration;

        public event EventHandler OpenRequested;

        public ReminderWindow(string title, string detail)
        {
            InitializeComponent();
            TitleText.Text = title;
            DetailText.Text = detail;

            ConfigureWindow();
            ApplyAcrylic();

            RootGrid.Tapped += (sender, args) => OpenRequested?.Invoke(this, EventArgs.Empty);
            Activated += (sender, args) => Console.WriteLine("reminder window active");
        }

        private void HideSystemCaptionButtons(IntPtr hwnd)
        {
            // Our own header already has a close button; drop the system ones so
            // no light caption strip is drawn on the glass.
            int style = GetWindowLong(hwnd, -16);
            style &= ~0x00080000; // WS_SYSMENU
            SetWindowLong(hwnd, -16, style);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0027);
        }
        private void ConfigureWindow()
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(id);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsAlwaysOnTop = true;
            }

            // Dark caption: the default system title bar would be white on glass.
            // Win11: paint the caption itself (the button strip ignores AppWindow colours
            // unless the caption colour is set through DWM).
            int captionColor = unchecked((int)0x00261C18); // COLORREF 0x00BBGGRR = rgb(24,28,38)
            DwmSetWindowAttribute(hwnd, 35, ref captionColor, sizeof(int));
            int captionTextColor = unchecked((int)0x00F2F4F8); // near-white text
            DwmSetWindowAttribute(hwnd, 36, ref captionTextColor, sizeof(int));

            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));

            var titleBar = appWindow.TitleBar;
            titleBar.BackgroundColor = Color.FromArgb(255, 24, 28, 38);
            titleBar.InactiveBackgroundColor = Color.FromArgb(255, 24, 28, 38);
            titleBar.ForegroundColor = Colors.White;
            titleBar.InactiveForegroundColor = Color.FromArgb(200, 226, 230, 240);
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = Colors.White;
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(160, 226, 230, 240);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 255, 255, 255);
            titleBar.ButtonHoverForegroundColor = Colors.White;
            HideSystemCaptionButtons(WinRT.Interop.WindowNative.GetWindowHandle(this));

            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            int borderColor = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));


            appWindow.Resize(new SizeInt32(Dpi.Px(hwnd, 360), Dpi.Px(hwnd, 124)));

            RECT work;
            SystemParametersInfo(0x0030, 0, out work, 0);
            appWindow.Move(new PointInt32(work.Right - 360 - 24, work.Bottom - 124 - 24));
        }

        private void ApplyAcrylic()
        {
            if (!DesktopAcrylicController.IsSupported())
            {
                return;
            }

            _configuration = new SystemBackdropConfiguration
            {
                IsInputActive = true,
                Theme = SystemBackdropTheme.Dark
            };

            _acrylic = new DesktopAcrylicController
            {
                Kind = DesktopAcrylicKind.Base,
                TintColor = Color.FromArgb(255, 24, 28, 38),
                FallbackColor = Color.FromArgb(255, 24, 28, 38),
                TintOpacity = 0.35f,
                LuminosityOpacity = 0.55f
            };
            _acrylic.SetSystemBackdropConfiguration(_configuration);
            _acrylic.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
        }

        private void OnCloseClick(object sender, RoutedEventArgs args)
        {
            Close();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint action, uint param, out RECT data, uint winIni);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
