using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using WinRT;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Confirmation card shown before a quick-added item is written: the parsed
    /// title/time/repeat can still be corrected here (same flow as 1.x).
    /// </summary>
    public sealed partial class ConfirmWindow : Window
    {
        private static readonly (string Key, string Label)[] Repeats =
        {
            ("none", "不重复"), ("daily", "每天"), ("weekly", "每周"),
            ("monthly", "每月"), ("weekdays", "工作日")
        };

        private string _selectedColor = "";

        private DesktopAcrylicController _acrylic;
        private SystemBackdropConfiguration _configuration;

        public event EventHandler<(string Title, DateTimeOffset? Due, string Repeat, string Color, bool Important, string Steps)> Confirmed;
        public event EventHandler Cancelled;

        internal ConfirmWindow(TodoDraft draft, TodoItem source)
        {
            InitializeComponent();
            ConfigureWindow();
            ApplyAcrylic();

            BuildPickers();

            TitleBox.Text = string.IsNullOrWhiteSpace(draft.Title)
                ? (source?.Text ?? "")
                : draft.Title;

            DateTimeOffset due = draft.Due ?? DateTimeOffset.Now.Date.AddDays(1).AddHours(9);
            DateBox.Date = due.ToLocalTime();
            TimeBox.Time = due.ToLocalTime().TimeOfDay;

            RepeatBox.SelectedIndex = Math.Max(0, Array.FindIndex(Repeats, entry => entry.Key == (draft.RecurrenceMode ?? "none")));
            SelectColor(draft.ColorMarker ?? "");
            ImportantSwitch.IsOn = draft.Important;
            StepsBox.Text = string.Join("\r\n", draft.Steps ?? new List<string>());

            TitleBox.Focus(FocusState.Programmatic);
        }

        private void ConfigureWindow()
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(id);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsAlwaysOnTop = true;
            }

            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            int borderColor = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));

            int style = GetWindowLong(hwnd, -16);
            style &= ~(0x00C00000 | 0x00040000 | 0x00080000 | 0x00020000 | 0x00010000);
            SetWindowLong(hwnd, -16, style);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0027);

            appWindow.Resize(new SizeInt32(Dpi.Px(hwnd, 560), Dpi.Px(hwnd, 620)));

            RECT work;
            SystemParametersInfo(0x0030, 0, out work, 0);
            appWindow.Move(new PointInt32(
                work.Left + (work.Right - work.Left - 560) / 2,
                work.Top + (work.Bottom - work.Top - 620) / 2));
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

        private void BuildPickers()
        {
            foreach (var repeat in Repeats)
            {
                RepeatBox.Items.Add(new ComboBoxItem { Content = repeat.Label, Tag = repeat.Key });
            }

            BuildColorSwatches();
        }

        private void OnTitleKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key == VirtualKey.Enter)
            {
                args.Handled = true;
                Commit();
            }
        }

        private void OnOkClick(object sender, RoutedEventArgs args)
        {
            Commit();
        }

        private void OnCancelClick(object sender, RoutedEventArgs args)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            Close();
        }

        private void BuildColorSwatches()
        {
            ColorSwatches.Children.Clear();
            foreach (var entry in TodoItem.Palette)
            {
                var dot = new Border
                {
                    Width = 15,
                    Height = 15,
                    CornerRadius = new CornerRadius(7.5),
                    Background = new SolidColorBrush(entry.Value),
                    BorderThickness = new Thickness(0),
                    Tag = entry.Key
                };

                ToolTipService.SetToolTip(dot, TodoItem.LabelFor(entry.Key));
                dot.Tapped += (sender, args) =>
                {
                    if (sender is FrameworkElement element && element.Tag is string key)
                    {
                        SelectColor(key);
                    }
                };
                ColorSwatches.Children.Add(dot);
            }

            SelectColor(_selectedColor);
        }

        private void SelectColor(string marker)
        {
            _selectedColor = marker ?? "";
            foreach (var child in ColorSwatches.Children)
            {
                if (child is Border dot && dot.Tag is string key)
                {
                    dot.BorderThickness = new Thickness(key == _selectedColor ? 2 : 0);
                    dot.BorderBrush = new SolidColorBrush(Colors.White);
                }
            }
        }

        private async void OnAiClick(object sender, RoutedEventArgs args)
        {
            AiButton.IsEnabled = false;
            AiButton.Content = "解析中…";
            AiStatus.Text = "正在请求 AI…";
            try
            {
                string text = (TitleBox.Text ?? "").Trim();
                if (text.Length == 0)
                {
                    return;
                }

                AiReparse.Result result = await AiReparse.ParseAsync(text);
                if (!string.IsNullOrWhiteSpace(result.Title))
                {
                    TitleBox.Text = result.Title;
                }

                if (result.Due != null)
                {
                    DateBox.Date = result.Due.Value.ToLocalTime();
                    TimeBox.Time = result.Due.Value.ToLocalTime().TimeOfDay;
                }

                RepeatBox.SelectedIndex = Math.Max(0,
                    Array.FindIndex(Repeats, entry => entry.Key == result.Recurrence));
                SelectColor(result.Color);
                ImportantSwitch.IsOn = result.Important;
                if (result.Steps.Length > 0)
                {
                    StepsBox.Text = string.Join("\r\n", result.Steps);
                }

                AiStatus.Text = "AI 已更新：标题 / 时间 / 重复 / 颜色";
                Console.WriteLine("ai reparse ok: " + result.Title);
            }
            catch (Exception ex)
            {
                Console.WriteLine("ai reparse failed: " + ex.Message);
                AiStatus.Text = "AI 失败：" + ex.Message;
            }
            finally
            {
                AiButton.IsEnabled = true;
                AiButton.Content = "AI 重解析";
            }
        }

        private void Commit()
        {
            string title = (TitleBox.Text ?? "").Trim();
            if (title.Length == 0)
            {
                return;
            }

            DateTimeOffset? due = null;
            if (DateBox.Date != null)
            {
                DateTime date = DateBox.Date.Value.Date;
                TimeSpan time = TimeBox.Time;
                due = new DateTimeOffset(date.Year, date.Month, date.Day, time.Hours, time.Minutes, 0,
                    TimeZoneInfo.Local.GetUtcOffset(date));
            }

            string repeat = (RepeatBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
            string color = _selectedColor;
            var steps = (StepsBox.Text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            Confirmed?.Invoke(this, (title, due, repeat, color, ImportantSwitch.IsOn, string.Join("\r\n", steps)));
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
