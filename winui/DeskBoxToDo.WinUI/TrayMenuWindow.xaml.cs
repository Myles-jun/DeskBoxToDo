using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Glass tray menu: the same GlassSurface + material as every other window, so
    /// the tray no longer opens an opaque WinForms menu.
    /// </summary>
    public sealed partial class TrayMenuWindow : Window
    {
        private readonly WindowChrome _chrome;

        public event Action<int> CommandInvoked;

        internal TrayMenuWindow(IReadOnlyList<(string Label, int Command, bool Checked, bool Enabled, bool Separator)> items)
        {
            InitializeComponent();

            _chrome = new WindowChrome(this, Shell, new ChromeOptions
            {
                Resizable = false,
                MinWidth = 180,
                MinHeight = 80,
                Layer = WindowLayer.TopMost,
                ToolWindow = true
            });
            _chrome.Attach();

            int height = 14;
            foreach (var item in items)
            {
                if (item.Separator)
                {
                    ItemsHost.Children.Add(new Border
                    {
                        Height = 1,
                        Margin = new Thickness(8, 4, 8, 4),
                        Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF))
                    });
                    height += 9;
                    continue;
                }

                var button = new Button
                {
                    Content = (item.Checked ? "✓  " : "    ") + item.Label,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 6, 8, 6),
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(5),
                    FontSize = 12.5,
                    IsEnabled = item.Enabled,
                    UseSystemFocusVisuals = false
                };
                int command = item.Command;
                button.Click += (sender, args) =>
                {
                    CommandInvoked?.Invoke(command);
                    Close();
                };
                ItemsHost.Children.Add(button);
                height += 33;
            }

            _chrome.ResizeTo(214, Math.Min(560, height));
            PlaceAtCursor();

            Activated += (sender, args) =>
            {
                if (args.WindowActivationState == WindowActivationState.Deactivated)
                {
                    Close();
                }
            };
        }

        private void PlaceAtCursor()
        {
            GetCursorPos(out POINT point);
            RectInt32 area = DisplayArea
                .GetFromWindowId(_chrome.AppWindow.Id, DisplayAreaFallback.Nearest)
                .WorkArea;
            SizeInt32 size = _chrome.AppWindow.Size;
            int x = Math.Clamp(point.X - 20, area.X + 8, area.X + area.Width - size.Width - 8);
            int y = Math.Clamp(point.Y - size.Height - 12, area.Y + 8, area.Y + area.Height - size.Height - 8);
            _chrome.MoveTo(x, y);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);    }
}
