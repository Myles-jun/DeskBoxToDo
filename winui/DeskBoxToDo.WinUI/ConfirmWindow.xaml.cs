using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Confirmation card shown before a quick-added item is written. Same chrome
    /// as the main window (GlassSurface + WindowChrome), just a compact body.
    /// </summary>
    public sealed partial class ConfirmWindow : Window
    {
        private static readonly (string Key, string Label)[] Repeats =
        {
            ("none", "不重复"), ("daily", "每天"), ("weekly", "每周"),
            ("monthly", "每月"), ("weekdays", "工作日")
        };

        private static readonly (int Minutes, string Label)[] ReminderOptions =
        {
            (0, "准时提醒"), (5, "提前 5 分钟"), (10, "提前 10 分钟"), (15, "提前 15 分钟"),
            (30, "提前 30 分钟"), (60, "提前 1 小时"), (1440, "提前 1 天")
        };

        private readonly WindowChrome _chrome;
        private string _selectedColor = "";

        /// <summary>Lead time chosen in the card; the caller persists it after saving.</summary>
        public int ReminderMinutes { get; private set; } = 5;

        public event EventHandler<(string Title, DateTimeOffset? Due, string Repeat, string Color, bool Important, string Steps)> Confirmed;
        public event EventHandler Cancelled;

        internal ConfirmWindow(TodoDraft draft, TodoItem source)
        {
            InitializeComponent();

            _chrome = new WindowChrome(this, Shell, new ChromeOptions
            {
                Resizable = false,
                MinWidth = 380,
                MinHeight = 480,
                // The caller may be a topmost app (e.g. the chat client that raised
                // us); a normal-band card would open behind it.
                Layer = WindowLayer.TopMost,
                ToolWindow = true
            });
            _chrome.Attach();
            CenterOnWorkArea(400, 548);

            BuildPickers();

            var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
            escape.Invoked += (sender, args) =>
            {
                args.Handled = true;
                Cancel();
            };
            Shell.KeyboardAccelerators.Add(escape);

            TitleBox.Text = string.IsNullOrWhiteSpace(draft.Title) ? (source?.Text ?? "") : draft.Title;

            DateTimeOffset due = draft.Due ?? DateTimeOffset.Now.Date.AddDays(1).AddHours(9);
            DateBox.Date = due.ToLocalTime();
            HourBox.SelectedIndex = Math.Clamp(due.ToLocalTime().Hour, 0, 23);
            MinuteBox.SelectedIndex = Math.Clamp(due.ToLocalTime().Minute / 5, 0, 11);

            RepeatBox.SelectedIndex = Math.Max(0, Array.FindIndex(Repeats, entry => entry.Key == (draft.RecurrenceMode ?? "none")));
            SelectColor(draft.ColorMarker ?? "");
            ImportantSwitch.IsOn = draft.Important;
            StepsBox.Text = string.Join("\r\n", draft.Steps ?? new List<string>());

            TitleBox.Focus(FocusState.Programmatic);
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

        private void BuildPickers()
        {
            foreach (var repeat in Repeats)
            {
                RepeatBox.Items.Add(new ComboBoxItem { Content = repeat.Label, Tag = repeat.Key });
            }

            for (int hour = 0; hour < 24; hour++)
            {
                HourBox.Items.Add(hour.ToString("00"));
            }

            for (int minute = 0; minute < 60; minute += 5)
            {
                MinuteBox.Items.Add(minute.ToString("00"));
            }

            foreach (var option in ReminderOptions)
            {
                ReminderBox.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Minutes });
            }

            ReminderBox.SelectedIndex = 1;
            BuildColorSwatches();
        }

        private void BuildColorSwatches()
        {
            ColorSwatches.Children.Clear();
            foreach (var entry in TodoItem.Palette)
            {
                var pill = new Border
                {
                    Width = 16,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(entry.Value)
                };

                var host = new Button
                {
                    Width = 24,
                    Height = 22,
                    MinWidth = 24,
                    MinHeight = 22,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(4),
                    Content = pill,
                    Tag = entry.Key,
                    UseSystemFocusVisuals = false
                };

                ToolTipService.SetToolTip(host, TodoItem.LabelFor(entry.Key));
                host.Click += (sender, args) =>
                {
                    if (sender is FrameworkElement element && element.Tag is string key)
                    {
                        SelectColor(key);
                    }
                };
                ColorSwatches.Children.Add(host);
            }

            SelectColor(_selectedColor);
        }

        private void SelectColor(string marker)
        {
            _selectedColor = marker ?? "";
            foreach (object child in ColorSwatches.Children)
            {
                if (child is Button host && host.Tag is string key)
                {
                    bool selected = key == _selectedColor;
                    host.Background = new SolidColorBrush(selected
                        ? Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)
                        : Colors.Transparent);
                    host.BorderThickness = new Thickness(selected ? 1 : 0);
                    host.BorderBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
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
                    DateTimeOffset local = result.Due.Value.ToLocalTime();
                    DateBox.Date = local;
                    HourBox.SelectedIndex = Math.Clamp(local.Hour, 0, 23);
                    MinuteBox.SelectedIndex = Math.Clamp(local.Minute / 5, 0, 11);
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

        private void OnOkClick(object sender, RoutedEventArgs args) => Commit();

        private void OnCancelClick(object sender, RoutedEventArgs args) => Cancel();

        private void Cancel()
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
                int hour = Math.Clamp(HourBox.SelectedIndex, 0, 23);
                int minute = Math.Clamp(MinuteBox.SelectedIndex, 0, 11) * 5;
                due = new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0,
                    TimeZoneInfo.Local.GetUtcOffset(date));
            }

            string repeat = (RepeatBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
            string[] steps = (StepsBox.Text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            ReminderMinutes = (ReminderBox.SelectedItem as ComboBoxItem)?.Tag is int minutes ? minutes : 5;

            Confirmed?.Invoke(this, (title, due, repeat, _selectedColor, ImportantSwitch.IsOn, string.Join("\r\n", steps)));
            Close();
        }
    }
}
