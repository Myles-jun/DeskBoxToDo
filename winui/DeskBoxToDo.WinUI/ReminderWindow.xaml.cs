using System;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DeskBoxToDo.WinUI
{
    /// <summary>Small glass toast that shows one due todo; click opens the list.</summary>
    public sealed partial class ReminderWindow : Window
    {
        private readonly WindowChrome _chrome;

        public event EventHandler OpenRequested;

        public event EventHandler Snoozed;

        public ReminderWindow(string title, string detail)
        {
            InitializeComponent();

            TitleText.Text = title;
            DetailText.Text = detail;
            Shell.IconBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xE9, 0xC4, 0x6A));

            _chrome = new WindowChrome(this, Shell, new ChromeOptions
            {
                Resizable = false,
                MinWidth = 300,
                MinHeight = 120,
                Layer = WindowLayer.TopMost,
                ToolWindow = true
            });
            _chrome.Attach();
            PlaceBottomRight(360, 140);

            RootGridTapped();
        }

        private void RootGridTapped()
        {
            if (Content is FrameworkElement root)
            {
                root.Tapped += (sender, args) =>
                {
                    if (IsInsideButton(args.OriginalSource as DependencyObject))
                    {
                        return;
                    }

                    OpenRequested?.Invoke(this, EventArgs.Empty);
                };
            }
        }

        private static bool IsInsideButton(DependencyObject source)
        {
            DependencyObject current = source;
            int guard = 0;
            while (current != null && guard++ < 10)
            {
                if (current is Button)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void PlaceBottomRight(int widthDip, int heightDip)
        {
            _chrome.ResizeTo(widthDip, heightDip);
            RectInt32 area = DisplayArea
                .GetFromWindowId(_chrome.AppWindow.Id, DisplayAreaFallback.Nearest)
                .WorkArea;
            int width = Dpi.Px(_chrome.Hwnd, widthDip);
            int height = Dpi.Px(_chrome.Hwnd, heightDip);
            _chrome.MoveTo(area.X + area.Width - width - 24, area.Y + area.Height - height - 24);
        }

        private void OnCloseClick(object sender, RoutedEventArgs args) => Close();

        private void OnSnoozeClick(object sender, RoutedEventArgs args)
        {
            Snoozed?.Invoke(this, EventArgs.Empty);
        }
    }
}

