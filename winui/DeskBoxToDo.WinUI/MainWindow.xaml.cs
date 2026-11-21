using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

        private List<TodoItem> _all = new List<TodoItem>();
        private string _filter = "all";
        private string _colorFilter;

        private bool _dragging;
        private PointInt32 _dragStartWindow;
        private Windows.Foundation.Point _dragStartCursor;

        public MainWindow()
        {
            InitializeComponent();
            ConfigureWindow();
            ApplyAcrylic();
            HookDrag();
            Reload();
        }

        private void ConfigureWindow()
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(id);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = true;
            }

            appWindow.Resize(new SizeInt32(420, 560));
            appWindow.Move(new PointInt32(980, 380));

            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            int border = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
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
                TintColor = Color.FromArgb(255, 24, 28, 38),
                TintOpacity = 0.30f,
                LuminosityOpacity = 0.55f,
                FallbackColor = Color.FromArgb(255, 22, 26, 34)
            };
            _acrylic.SetSystemBackdropConfiguration(_configuration);
            _acrylic.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
            Console.WriteLine("acrylic thin enabled");
        }

        private void HookDrag()
        {
            HeaderBar.PointerPressed += (sender, args) =>
            {
                var point = args.GetCurrentPoint(HeaderBar);
                if (!point.Properties.IsLeftButtonPressed)
                {
                    return;
                }

                // Only the empty part of the header drags; buttons keep working.
                var source = args.OriginalSource as FrameworkElement;
                if (source is Button || source is FontIcon)
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
                int dx = (int)(point.Position.X - _dragStartCursor.X);
                int dy = (int)(point.Position.Y - _dragStartCursor.Y);
                AppWindow.Move(new PointInt32(_dragStartWindow.X + dx, _dragStartWindow.Y + dy));
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

        public void Reload()
        {
            string path;
            _all = TodoStore.Load(out path);
            FooterText.Text = _all.Count + " 条 · " + (TodoStore.CurrentMode() == "deskbox" ? "DeskBox 数据" : "独立数据");
            Console.WriteLine("loaded " + _all.Count + " items from " + path);
            UpdateTabs();
            UpdateColorRow();
            ApplyFilter();
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

            var visible = rows
                .OrderBy(item => item.IsCompleted ? 1 : 0)
                .ThenBy(item => item.DueDate ?? DateTimeOffset.MaxValue)
                .ToList();

            TodoList.ItemsSource = visible;
            FooterText.Text = visible.Count + " 条 · " + (TodoStore.CurrentMode() == "deskbox" ? "DeskBox 数据" : "独立数据");
        }

        private void OnTabTapped(object sender, TappedRoutedEventArgs args)
        {
            var element = sender as FrameworkElement;
            if (element == null || element.Tag == null)
            {
                return;
            }

            _filter = element.Tag.ToString();
            UpdateTabs();
            ApplyFilter();
        }

        private void OnAddClick(object sender, RoutedEventArgs args)
        {
            Console.WriteLine("add clicked");
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
