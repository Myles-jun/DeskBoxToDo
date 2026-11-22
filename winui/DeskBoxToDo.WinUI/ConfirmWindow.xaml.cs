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
    /// Confirmation card shown before a quick-added item is written. The window
    /// keeps the native title bar (reliable dragging) with the caption painted in
    /// the same colour as the glass, and the client area is pure acrylic.
    /// </summary>
    public sealed partial class ConfirmWindow : Window
    {
        private static readonly (string Key, string Label)[] Repeats =
        {
            ("none", "不重复"), ("daily", "每天"), ("weekly", "每周"),
            ("monthly", "每月"), ("weekdays", "工作日")
        };

        private DesktopAcrylicController _acrylic;
        private string _selectedColor = "";

        public event EventHandler<(string Title, DateTimeOffset? Due, string Repeat, string Color, bool Important, string Steps)> Confirmed;
        public event EventHandler Cancelled;

        internal ConfirmWindow(TodoDraft draft, TodoItem source)
        {
            InitializeComponent();
            ConfigureWindow();
            ApplyAcrylic();
            BuildPickers();

            TitleBox.Text = string.IsNullOrWhiteSpace(draft.Title) ? (source?.Text ?? "") : draft.Title;

            DateTimeOffset due = draft.Due ?? DateTimeOffset.Now.Date.AddDays(1).AddHours(9);
            DateBox.Date = due.ToLocalTime();
            TimeBox.Time = due.ToLocalTime().TimeOfDay;

            RepeatBox.SelectedIndex = Math.Max(0, Array.FindIndex(Repeats, entry => entry.Key == (draft.RecurrenceMode ?? "none")));
            SelectColor(draft.ColorMarker ?? "");
            ImportantSwitch.IsOn = draft.Important;
            StepsBox.Text = string.Join("\r\n", draft.Steps ?? new List<string>());

            HookHeaderDrag();
            TitleBox.Focus(FocusState.Programmatic);
        }

        private void HookHeaderDrag()
        {
            RootGrid.PointerPressed += (sender, args) =>
            {
                var point = args.GetCurrentPoint(RootGrid);
                if (!point.Properties.IsLeftButtonPressed)
                {
                    return;
                }

                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (point.Position.Y > 34 * Dpi.ScaleFor(hwnd))
                {
                    return;
                }

                if (args.OriginalSource is Button || args.OriginalSource is TextBox)
                {
                    return;
                }

                ReleaseCapture();
                SendMessage(hwnd, 0x00A1, (IntPtr)2, IntPtr.Zero);
            };
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        private void ConfigureWindow()
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(id);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }

            // Dark caption in the same colour as the glass; no white strip.
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));
            int caption = unchecked((int)0x00261C18);
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
            int captionText = unchecked((int)0x00F2F4F8);
            DwmSetWindowAttribute(hwnd, 36, ref captionText, sizeof(int));

            var bar = appWindow.TitleBar;
            bar.BackgroundColor = Color.FromArgb(255, 24, 28, 38);
            bar.InactiveBackgroundColor = Color.FromArgb(255, 24, 28, 38);
            bar.ForegroundColor = Colors.White;
            bar.InactiveForegroundColor = Color.FromArgb(200, 226, 230, 240);
            bar.ButtonBackgroundColor = Color.FromArgb(255, 24, 28, 38);
            bar.ButtonInactiveBackgroundColor = Color.FromArgb(255, 24, 28, 38);
            bar.ButtonForegroundColor = Colors.White;
            bar.ButtonInactiveForegroundColor = Color.FromArgb(160, 226, 230, 240);
            bar.ButtonHoverBackgroundColor = Color.FromArgb(40, 255, 255, 255);
            bar.ButtonHoverForegroundColor = Colors.White;

            appWindow.Resize(new SizeInt32(Dpi.Px(hwnd, 420), Dpi.Px(hwnd, 470)));

            RECT work;
            SystemParametersInfo(0x0030, 0, out work, 0);
            appWindow.Move(new PointInt32(
                work.Left + (work.Right - work.Left - Dpi.Px(hwnd, 420)) / 2,
                work.Top + (work.Bottom - work.Top - Dpi.Px(hwnd, 470)) / 2));
        }

        private void ApplyAcrylic()
        {
            _acrylic = GlassHelper.Attach(this);
        }

        private void BuildPickers()
        {
            foreach (var repeat in Repeats)
            {
                RepeatBox.Items.Add(new ComboBoxItem { Content = repeat.Label, Tag = repeat.Key });
            }

            BuildColorSwatches();
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

                RepeatBox.SelectedIndex = Math.Max(0, Array.FindIndex(Repeats, entry => entry.Key == result.Recurrence));
                SelectColor(result.Color);
                ImportantSwitch.IsOn = result.Important;
                if (result.Steps.Length > 0)
                {
                    StepsBox.Text = string.Join("\r\n", result.Steps);
                }

                AiStatus.Text = "AI 已更新：标题 / 时间 / 重复 / 颜色";
            }
            catch (Exception ex)
            {
                AiStatus.Text = "AI 失败：" + ex.Message;
            }
            finally
            {
                AiButton.IsEnabled = true;
                AiButton.Content = "AI 重解析";
            }
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
            var steps = (StepsBox.Text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            Confirmed?.Invoke(this, (title, due, repeat, _selectedColor, ImportantSwitch.IsOn, string.Join("\r\n", steps)));
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

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}