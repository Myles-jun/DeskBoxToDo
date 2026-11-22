using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.UI;
using WinRT;

namespace DeskBoxToDo.WinUI
{
    /// <summary>Small settings card: default due time, data source and hotkey.</summary>
    public sealed partial class SettingsWindow : Window
    {
        private DesktopAcrylicController _acrylic;
        private SystemBackdropConfiguration _configuration;

        public event EventHandler<(int Hour, int Minute, string Mode, uint Modifiers, uint Key)> Saved;

        private static readonly (string Label, uint Modifiers, uint Key)[] Hotkeys =
        {
            ("Alt + Q", 0x0001, 0x51),
            ("Ctrl + Alt + Q", 0x0003, 0x51),
            ("Ctrl + Shift + Space", 0x0006, 0x20),
            ("Alt + Space", 0x0001, 0x20)
        };

        public SettingsWindow(GlassSettings settings, int currentHour, int currentMinute, string mode, uint modifiers, uint key)
        {
            InitializeComponent();
            ConfigureWindow();
            ApplyAcrylic();

            for (int hour = 0; hour < 24; hour++)
            {
                HourBox.Items.Add(hour.ToString("00"));
            }

            for (int minute = 0; minute < 60; minute += 5)
            {
                MinuteBox.Items.Add(minute.ToString("00"));
            }

            HourBox.SelectedIndex = Math.Clamp(currentHour, 0, 23);
            MinuteBox.SelectedIndex = Math.Clamp(currentMinute / 5, 0, 11);

            foreach (var entry in Hotkeys)
            {
                HotkeyBox.Items.Add(new ComboBoxItem { Content = entry.Label });
            }

            int hotkeyIndex = Array.FindIndex(Hotkeys, entry => entry.Modifiers == modifiers && entry.Key == key);
            HotkeyBox.SelectedIndex = hotkeyIndex < 0 ? 0 : hotkeyIndex;

            ModeBox.SelectedIndex = string.Equals(mode, "deskbox", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
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

            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            int borderColor = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));


            appWindow.Resize(new SizeInt32(Dpi.Px(hwnd, 440), Dpi.Px(hwnd, 420)));

            RECT work;
            SystemParametersInfo(0x0030, 0, out work, 0);
            appWindow.Move(new PointInt32(
                work.Left + (work.Right - work.Left - 440) / 2,
                work.Top + (work.Bottom - work.Top - 420) / 2));
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
                TintOpacity = 0.30f,
                LuminosityOpacity = 0.55f
            };
            _acrylic.SetSystemBackdropConfiguration(_configuration);
            _acrylic.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
        }

        private void OnSaveClick(object sender, RoutedEventArgs args)
        {
            int hour = HourBox.SelectedIndex < 0 ? 8 : HourBox.SelectedIndex;
            int minute = MinuteBox.SelectedIndex < 0 ? 0 : MinuteBox.SelectedIndex * 5;
            string mode = ModeBox.SelectedIndex == 1 ? "deskbox" : "standalone";
            int hotkeyIndex = HotkeyBox.SelectedIndex < 0 ? 0 : HotkeyBox.SelectedIndex;

            Saved?.Invoke(this, (hour, minute, mode, Hotkeys[hotkeyIndex].Modifiers, Hotkeys[hotkeyIndex].Key));
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs args)
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
