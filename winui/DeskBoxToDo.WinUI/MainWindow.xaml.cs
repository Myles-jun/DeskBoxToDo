using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.UI;
using WinRT;

namespace DeskBoxToDo.WinUI
{
    public sealed partial class MainWindow : Window
    {
        private DesktopAcrylicController _acrylic;
        private SystemBackdropConfiguration _configuration;
        private GlassSettings _settings = GlassSettings.Load();

        private readonly ObservableCollection<TodoItem> _visible = new ObservableCollection<TodoItem>();
        private List<TodoItem> _all = new List<TodoItem>();
        private string _filter = "all";
        private string _colorFilter;

        private TodoItem _editing;
        private bool _editingImportant;
        private bool _loadingDetail;

        private static readonly (string Key, string Label)[] Repeats =
        {
            ("none", "不重复"), ("daily", "每天"), ("weekly", "每周"),
            ("monthly", "每月"), ("weekdays", "工作日")
        };

        public MainWindow()
        {
            InitializeComponent();
            ConfigureWindow();
            ApplyAcrylic();
            HookDrag();
            BuildDetailPickers();

            TodoList.ItemsSource = _visible;
            BrightnessSlider.Value = _settings.Brightness;
            SourceLabel.Text = TodoStore.CurrentMode() == "deskbox"
                ? "DeskBox 小组件数据（与 DeskBox 同步）"
                : "独立数据（%LOCALAPPDATA%\\DeskBoxToDo\\todo.json）";

            Reload();
            TopEdgeMask.Visibility = Visibility.Visible;
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
            }

            appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            appWindow.TitleBar.BackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ForegroundColor = Colors.Transparent;
            appWindow.TitleBar.InactiveBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonForegroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveForegroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 255, 255, 255);

            appWindow.Resize(new SizeInt32(420, 560));
            appWindow.Move(new PointInt32(980, 380));

            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            // 1px 系统边框染成和玻璃同色，避免顶部/四周出现白线
            int borderColor = unchecked((int)0x00281E1A);
            DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));
        }

        private bool _dragging;
        private PointInt32 _dragStartWindow;
        private Windows.Foundation.Point _dragStartCursor;

        private void HookDrag()
        {
            HeaderBar.PointerPressed += (sender, args) =>
            {
                var point = args.GetCurrentPoint(HeaderBar);
                if (!point.Properties.IsLeftButtonPressed)
                {
                    return;
                }

                if (args.OriginalSource is Button || args.OriginalSource is FontIcon)
                {
                    return;
                }

                _dragging = true;
                _dragStartCursor = point.Position;
                _dragStartWindow = AppWindow.Position;
                HeaderBar.CapturePointer(args.Pointer);
            };

            HeaderBar.PointerMoved += (sender, args) =>
            {
                if (!_dragging)
                {
                    return;
                }

                var point = args.GetCurrentPoint(HeaderBar);
                AppWindow.Move(new PointInt32(
                    _dragStartWindow.X + (int)(point.Position.X - _dragStartCursor.X),
                    _dragStartWindow.Y + (int)(point.Position.Y - _dragStartCursor.Y)));
            };

            HeaderBar.PointerReleased += (sender, args) =>
            {
                if (_dragging)
                {
                    _dragging = false;
                    HeaderBar.ReleasePointerCapture(args.Pointer);
                }
            };
        }

        private void ApplyAcrylic()
        {
            if (!DesktopAcrylicController.IsSupported())
            {
                Console.WriteLine("acrylic unsupported");
                return;
            }

            _configuration = new SystemBackdropConfiguration
            {
                IsInputActive = true,
                Theme = SystemBackdropTheme.Dark
            };

            _acrylic = new DesktopAcrylicController
            {
                Kind = DesktopAcrylicKind.Thin,
                FallbackColor = Color.FromArgb(255, 22, 26, 34)
            };
            _acrylic.SetSystemBackdropConfiguration(_configuration);
            _acrylic.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
            ApplyBrightness();
        }

        /// <summary>Brightness 0 = clear/light glass, 100 = dark glass.</summary>
        private void ApplyBrightness()
        {
            if (_acrylic == null)
            {
                return;
            }

            float t = Math.Clamp(_settings.Brightness / 100f, 0f, 1f);
            _acrylic.TintColor = Color.FromArgb(255, 26, 30, 40);
            _acrylic.TintOpacity = 0.06f + t * 0.62f;
            _acrylic.LuminosityOpacity = 0.20f + t * 0.60f;
            BrightnessLabel.Text = "当前 " + _settings.Brightness + "（0 最清透 / 100 最厚重）";
        }

        private void OnBrightnessChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            if (BrightnessLabel == null)
            {
                return;
            }

            _settings.Brightness = (int)Math.Round(args.NewValue);
            _settings.Save();
            ApplyBrightness();
        }

        public void Reload()
        {
            string path;
            _all = TodoStore.Load(out path);
            UpdateTabs();
            UpdateColorRow();
            ApplyFilter();
            Console.WriteLine("loaded " + _all.Count + " items from " + path);
        }

        private int CountFor(string filter)
        {
            switch (filter)
            {
                case "today":
                    return _all.Count(item => item.IsToday && !item.IsCompleted);
                case "important":
                    return _all.Count(item => item.IsImportant && !item.IsCompleted);
                case "done":
                    return _all.Count(item => item.IsCompleted);
                default:
                    return _all.Count(item => !item.IsCompleted);
            }
        }

        private void UpdateTabs()
        {
            TabAllText.Text = "全部 " + CountFor("all");
            TabTodayText.Text = "今天 " + CountFor("today");
            TabImportantText.Text = "重要 " + CountFor("important");
            TabDoneText.Text = "完成 " + CountFor("done");

            Highlight(TabAllText, TabAllBar, _filter == "all");
            Highlight(TabTodayText, TabTodayBar, _filter == "today");
            Highlight(TabImportantText, TabImportantBar, _filter == "important");
            Highlight(TabDoneText, TabDoneBar, _filter == "done");
        }

        private static void Highlight(TextBlock label, Rectangle bar, bool active)
        {
            label.Foreground = new SolidColorBrush(active ? Color.FromArgb(255, 142, 184, 255) : Color.FromArgb(255, 201, 207, 219));
            bar.Fill = new SolidColorBrush(active ? Color.FromArgb(255, 127, 178, 255) : Colors.Transparent);
            label.FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }

        private void UpdateColorRow()
        {
            ColorRow.Children.Clear();
            foreach (var entry in TodoItem.Palette)
            {
                var dot = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(7),
                    Background = new SolidColorBrush(entry.Value),
                    BorderThickness = new Thickness(_colorFilter == entry.Key ? 2 : 0),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255))
                };

                string key = entry.Key;
                dot.Tapped += (sender, args) =>
                {
                    _colorFilter = _colorFilter == key ? null : key;
                    UpdateColorRow();
                    ApplyFilter();
                };
                ToolTipService.SetToolTip(dot, entry.Key);
                ColorRow.Children.Add(dot);
            }
        }

        private void ApplyFilter()
        {
            IEnumerable<TodoItem> rows = _all;
            switch (_filter)
            {
                case "today":
                    rows = rows.Where(item => item.IsToday && !item.IsCompleted);
                    break;
                case "important":
                    rows = rows.Where(item => item.IsImportant && !item.IsCompleted);
                    break;
                case "done":
                    rows = rows.Where(item => item.IsCompleted);
                    break;
                default:
                    rows = rows.Where(item => !item.IsCompleted);
                    break;
            }

            if (!string.IsNullOrEmpty(_colorFilter))
            {
                rows = rows.Where(item => item.ColorMarker == _colorFilter);
            }

            var ordered = rows
                .OrderBy(item => item.IsCompleted ? 1 : 0)
                .ThenBy(item => item.DueDate ?? DateTimeOffset.MaxValue)
                .ToList();

            _visible.Clear();
            foreach (TodoItem item in ordered)
            {
                _visible.Add(item);
            }

            FooterText.Text = _visible.Count + " 条 · " +
                (TodoStore.CurrentMode() == "deskbox" ? "DeskBox 数据" : "独立数据");
        }

        private void OnTabTapped(object sender, TappedRoutedEventArgs args)
        {
            if (sender is FrameworkElement element && element.Tag != null)
            {
                _filter = element.Tag.ToString();
                UpdateTabs();
                ApplyFilter();
            }
        }

        private void OnCheckBoxClick(object sender, RoutedEventArgs args)
        {
            if (sender is CheckBox box && box.Tag is string id)
            {
                bool done = box.IsChecked == true;
                TodoRepo.SetCompleted(id, done);
                Reload();
            }
        }

        private void OnClearDoneClick(object sender, RoutedEventArgs args)
        {
            foreach (TodoItem item in _all.Where(row => row.IsCompleted).ToList())
            {
                TodoRepo.Delete(item.Id);
            }

            Reload();
        }

        private void OnItemClick(object sender, ItemClickEventArgs args)
        {
            if (args.ClickedItem is TodoItem item)
            {
                OpenDetail(item);
            }
        }

        private void OnAddClick(object sender, RoutedEventArgs args)
        {
            OpenDetail(null);
        }

        private void BuildDetailPickers()
        {
            foreach (var repeat in Repeats)
            {
                RepeatBox.Items.Add(new ComboBoxItem { Content = repeat.Label, Tag = repeat.Key });
            }

            foreach (var color in TodoItem.Palette)
            {
                ColorBox.Items.Add(new ComboBoxItem { Content = color.Key, Tag = color.Key });
            }

            RepeatBox.SelectedIndex = 0;
            ColorBox.SelectedIndex = 0;
        }

        private void OpenDetail(TodoItem item)
        {
            _loadingDetail = true;
            _editing = item;
            _editingImportant = item?.IsImportant ?? false;

            TitleBox.Text = item?.Text ?? "";
            NotesBox.Text = item?.Notes ?? "";
            DateBox.Date = item?.DueDate?.ToLocalTime() ?? DateTimeOffset.Now.Date.AddDays(1);
            TimeBox.Time = (item?.DueDate?.ToLocalTime() ?? DateTimeOffset.Now.Date.AddDays(1).AddHours(9)).TimeOfDay;

            string mode = item?.RecurrenceMode ?? "none";
            RepeatBox.SelectedIndex = Math.Max(0, Array.FindIndex(Repeats, entry => entry.Key == mode));

            string color = item?.ColorMarker ?? "白";
            ColorBox.SelectedIndex = Math.Max(0, TodoItem.Palette.FindIndex(entry => entry.Key == color));

            var root = TodoRepo.ReadRoot();
            var node = item == null ? null : TodoRepo.Find(root, item.Id);
            StepsBox.Text = string.Join("\r\n", TodoRepo.StepTexts(node));

            UpdateStarIcon();
            DetailHint.Text = item == null ? "新待办" : "编辑 · " + item.Id.Substring(0, 6);
            DetailPage.Visibility = Visibility.Visible;
            ListPage.Visibility = Visibility.Collapsed;
            _loadingDetail = false;
            TitleBox.Focus(FocusState.Programmatic);
        }

        private void UpdateStarIcon()
        {
            StarIcon.Glyph = _editingImportant ? "\uE735" : "\uE734";
            StarIcon.Foreground = new SolidColorBrush(_editingImportant
                ? Color.FromArgb(255, 233, 196, 106)
                : Color.FromArgb(255, 230, 234, 242));
        }

        private void OnToggleDetailImportant(object sender, RoutedEventArgs args)
        {
            _editingImportant = !_editingImportant;
            UpdateStarIcon();
        }

        private void OnCloseDetail(object sender, RoutedEventArgs args)
        {
            DetailPage.Visibility = Visibility.Collapsed;
            ListPage.Visibility = Visibility.Visible;
            _editing = null;
        }

        private void OnDeleteDetail(object sender, RoutedEventArgs args)
        {
            if (_editing != null)
            {
                TodoRepo.Delete(_editing.Id);
            }

            OnCloseDetail(sender, args);
            Reload();
        }

        private void OnSaveDetail(object sender, RoutedEventArgs args)
        {
            string title = (TitleBox.Text ?? "").Trim();
            if (title.Length == 0)
            {
                DetailHint.Text = "标题不能为空";
                return;
            }

            DateTimeOffset? due = null;
            if (DateBox.Date != null)
            {
                DateTime date = DateBox.Date.Value.Date;
                TimeSpan time = TimeBox.Time;
                due = new DateTimeOffset(date.Year, date.Month, date.Day, time.Hours, time.Minutes, 0, TimeZoneInfo.Local.GetUtcOffset(date));
            }

            string repeat = (RepeatBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
            string color = (ColorBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "白";
            var steps = (StepsBox.Text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            TodoRepo.Save(
                _editing?.Id,
                title,
                due,
                repeat,
                color,
                _editingImportant,
                NotesBox.Text ?? "",
                steps);

            OnCloseDetail(sender, args);
            Reload();
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
