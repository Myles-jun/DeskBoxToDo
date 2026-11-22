using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DeskBoxToDo.WinUI
{
    /// <summary>Update prompt: release notes plus download-and-install.</summary>
    public sealed partial class UpdateWindow : Window
    {
        private readonly WindowChrome _chrome;
        private readonly UpdateChecker.Result _result;
        private bool _busy;

        internal UpdateWindow(UpdateChecker.Result result)
        {
            InitializeComponent();

            _result = result;
            VersionText.Text = "当前 " + result.CurrentVersion + "  →  最新 " + result.LatestTag;
            NotesText.Text = string.IsNullOrWhiteSpace(result.Notes)
                ? "（这个版本没有填写更新说明）"
                : result.Notes.Trim();

            if (string.IsNullOrWhiteSpace(result.AssetUrl))
            {
                InstallButton.IsEnabled = false;
                HintText.Text = "这个版本没有可下载的安装包，点「打开发布页」手动下载。";
            }

            _chrome = new WindowChrome(this, Shell, new ChromeOptions
            {
                Resizable = false,
                MinWidth = 420,
                MinHeight = 380,
                ToolWindow = true
            });
            _chrome.Attach();
            CenterOnWorkArea(470, 430);
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

        private async void OnInstallClick(object sender, RoutedEventArgs args)
        {
            if (_busy || string.IsNullOrWhiteSpace(_result.AssetUrl))
            {
                return;
            }

            _busy = true;
            InstallButton.IsEnabled = false;
            LaterButton.IsEnabled = false;
            StatusText.Text = "准备下载…";

            try
            {
                string path = await UpdateChecker.DownloadAsync(_result.AssetUrl, (received, total) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        StatusText.Text = total > 0
                            ? $"已下载 {received * 100 / total}%（{received / 1024 / 1024} MB / {total / 1024 / 1024} MB）"
                            : $"已下载 {received / 1024 / 1024} MB";
                    });
                });

                StatusText.Text = "正在启动安装程序…";
                UpdateChecker.RunInstaller(path);
                Application.Current.Exit();
            }
            catch (Exception ex)
            {
                StatusText.Text = "下载失败：" + ex.Message;
                InstallButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
                _busy = false;
            }
        }

        private void OnOpenPageClick(object sender, RoutedEventArgs args)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_result.PageUrl))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_result.PageUrl)
                    {
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "打不开浏览器：" + ex.Message;
            }
        }

        private void OnLaterClick(object sender, RoutedEventArgs args) => Close();
    }
}
