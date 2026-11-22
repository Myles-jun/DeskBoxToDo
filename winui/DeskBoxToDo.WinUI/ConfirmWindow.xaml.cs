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
using Windows.UI.ViewManagement;
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

            HookMinimumSize(WinRT.Interop.WindowNative.GetWindowHandle(this));
            TitleBox.Focus(FocusState.Programmatic);
        }

        private bool _dragging;
        private PointInt32 _dragStartWindow;
        private POINT _dragStartCursor;

        private const int WmNchittest = 0x0084;
        private const int HtCaption = 2;

        private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        private SubclassProc _subclass;
        private IntPtr _oldProc;

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

        /// <summary>
        /// Let Windows drag the dialog by its header row (HTCAPTION). XAML pointer
        /// capture on this window proved unreliable, and the native path also gives
        /// proper snap behaviour for free.
        /// </summary>
        private void HookNativeDrag(IntPtr hwnd)
        {
            _subclass = (h, message, wParam, lParam) =>
            {
                if (message == WmNchittest)
                {
                    GetCursorPos(out POINT screen);
                    POINT client = screen;
                    if (ScreenToClient(hwnd, ref client))
                    {
                        int headerHeight = (int)Math.Round(56 * Dpi.ScaleFor(hwnd));
                        if (client.Y >= 0 && client.Y <= headerHeight)
                        {
                            return (IntPtr)HtCaption;
                        }
                    }
                }

                return CallWindowProc(_oldProc, h, message, wParam, lParam);
            };

            _oldProc = SetWindowLongPtr(hwnd, -4, Marshal.GetFunctionPointerForDelegate(_subclass));
        }
        private static bool IsControl(DependencyObject source)
        {
            DependencyObject current = source;
            int guard = 0;
            while (current != null && guard++ < 12)
            {
                if (current is Button || current is TextBox || current is ComboBox ||
                    current is TimePicker || current is CalendarDatePicker || current is ToggleSwitch)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
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
        private const int WmGetMinMaxInfo = 0x0024;

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT Reserved;
            public POINT MaxSize;
            public POINT MaxPosition;
            public POINT MinTrackSize;
            public POINT MaxTrackSize;
        }

        private delegate IntPtr MinMaxProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        private MinMaxProc _minMaxProc;
        private IntPtr _minMaxOldProc;



        /// <summary>Keeps the dialog from being resized below a usable size.</summary>
        private void HookMinimumSize(IntPtr hwnd)
        {
            _minMaxProc = (h, message, wParam, lParam) =>
            {
                IntPtr result = CallWindowProc(_minMaxOldProc, h, message, wParam, lParam);
                if (message == WmGetMinMaxInfo)
                {
                    var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    info.MinTrackSize.X = Dpi.Px(hwnd, 360);
                    info.MinTrackSize.Y = Dpi.Px(hwnd, 400);
                    Marshal.StructureToPtr(info, lParam, false);
                }

                return result;
            };

            _minMaxOldProc = SetWindowLongPtr(hwnd, -4, Marshal.GetFunctionPointerForDelegate(_minMaxProc));
        }
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
            // Match the caption: a transparent button background shows up as a
            // white box on the glass.
            titleBar.ButtonBackgroundColor = Color.FromArgb(255, 24, 28, 38);
            titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(255, 24, 28, 38);
            titleBar.ButtonForegroundColor = Colors.White;
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(160, 226, 230, 240);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 255, 255, 255);
            titleBar.ButtonHoverForegroundColor = Colors.White;
            HideSystemCaptionButtons(WinRT.Interop.WindowNative.GetWindowHandle(this));

            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            int borderColor = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));


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
        private static extern bool GetCursorPos(out POINT point);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
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
