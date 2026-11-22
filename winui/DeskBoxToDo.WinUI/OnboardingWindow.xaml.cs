using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DeskBoxToDo.WinUI
{
    /// <summary>First-run welcome card: the three things a new user needs to know.</summary>
    public sealed partial class OnboardingWindow : Window
    {
        private readonly WindowChrome _chrome;

        public OnboardingWindow(string hotkeyLabel)
        {
            InitializeComponent();

            HotkeyText.Text = string.IsNullOrWhiteSpace(hotkeyLabel) ? "Alt + Q" : hotkeyLabel;
            SourceText.Text = TodoStore.CurrentMode() == "deskbox" ? "当前数据：DeskBox 小组件" : "当前数据：独立待办";

            _chrome = new WindowChrome(this, Shell, new ChromeOptions
            {
                Resizable = false,
                MinWidth = 380,
                MinHeight = 380,
                ToolWindow = true
            });
            _chrome.Attach();
            CenterOnWorkArea(430, 352);

            Closed += (sender, args) => AppConfig.MarkOnboardingDone();
        }

        private void CenterOnWorkArea(int widthDip, int heightDip)
        {
            _chrome.ResizeTo(widthDip, heightDip);
            RectInt32 area = DisplayArea
                .GetFromWindowId(_chrome.AppWindow.Id, DisplayAreaFallback.Nearest)
                .WorkArea;
            int width = Dpi.Px(_chrome.Hwnd, widthDip);
            int height = Dpi.Px(_chrome.Hwnd, heightDip);
            _chrome.MoveTo(
                area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2);
        }

        private void OnStartClick(object sender, RoutedEventArgs args) => Close();
    }
}
