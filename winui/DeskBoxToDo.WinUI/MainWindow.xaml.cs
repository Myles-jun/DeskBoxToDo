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
            foreach (var entry in MaterialBox.Items)
            {
                if (entry is ComboBoxItem item && (string)item.Tag == _settings.Material)
                {
                    MaterialBox.SelectedItem = item;
                    break;
                }
            }
            SourceLabel.Text = TodoStore.CurrentMode() == "deskbox"
                ? "DeskBox 小组件数据（与 DeskBox 同步）"
                : "独立数据（%LOCALAPPDATA%\\DeskBoxToDo\\todo.json）";

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
                presenter.IsMaximizable = false;
            }

            ApplyFrameless(hwnd);

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

            ApplyClassBackground(hwnd);

            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            // 让系统不要画边框色（0xFFFFFFFE = DWMWA_COLOR_NONE），避免四周黑边
            Activated += (sender, args) => InstallCursorHookOnce();

            int borderColor = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));
            int frameThickness = 0;
            DwmSetWindowAttribute(hwnd, 37, ref frameThickness, sizeof(int));
        }

        private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        private WndProcDelegate _wndProc;
        private IntPtr _oldWndProc;

        private const uint WmSetCursor = 0x0020;

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// WinUI resets the cursor for every WM_SETCURSOR; subclassing is the only
        /// reliable way to show the native resize arrows on a borderless window.
        /// </summary>
        private void InstallCursorHook(IntPtr hwnd)
        {
            _wndProc = (h, message, wParam, lParam) =>
            {
                if (message == 0x0231) // WM_ENTERSIZEMOVE
                {
                    SetTintOpaque(true);
                }
                else if (message == 0x0232) // WM_EXITSIZEMOVE
                {
                    SetTintOpaque(false);
                }
                else if (message == WmSetCursor)
                {
                    GetCursorPos(out POINT point);
                    ScreenToClient(hwnd, ref point);
                    int border = 7;
                    RECT client;
                    GetClientRect(h, out client);
                    bool left = point.X <= border;
                    bool right = point.X >= client.Right - border;
                    bool top = point.Y <= border;
                    bool bottom = point.Y >= client.Bottom - border;

                    int shape = 0;
                    if ((left || right) && (top || bottom))
                    {
                        shape = (left && top) || (right && bottom) ? IdcSizeNwse : IdcSizeNesw;
                    }
                    else if (left || right)
                    {
                        shape = IdcSizeWe;
                    }
                    else if (top || bottom)
                    {
                        shape = IdcSizeNs;
                    }

                    if (shape != 0)
                    {
                        SetCursor(LoadCursor(IntPtr.Zero, (IntPtr)shape));
                        Console.WriteLine("setcursor hook: " + point.X + "," + point.Y + " shape=" + shape);
                        return (IntPtr)1;
                    }
                }

                return CallWindowProc(_oldWndProc, h, message, wParam, lParam);
            };

            _oldWndProc = SetWindowLongPtr(hwnd, -4, System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_wndProc));
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
        private static extern IntPtr SetClassLongPtr(IntPtr hwnd, int index, IntPtr value);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(int color);

        /// <summary>
        /// Window class background: while the frame is being dragged/resized,
        /// Windows paints newly exposed pixels with this brush. Without it the
        /// trailing edge flashes pure black before XAML catches up.
        /// </summary>
        private void ApplyClassBackground(IntPtr hwnd)
        {
            try
            {
                // COLORREF is 0x00BBGGRR: this matches the glass tint (30, 34, 44).
                IntPtr brush = CreateSolidBrush(unchecked((int)0x002C221E));
                SetClassLongPtr(hwnd, -10, brush);
            }
            catch (Exception ex)
            {
                Console.WriteLine("class background failed: " + ex.Message);
            }
        }

        /// <summary>
        /// While Windows runs the move/resize loop the backdrop gets stretched and
        /// can flash black at the trailing edge. Painting the tint solid for those
        /// few hundred milliseconds hides it completely.
        /// </summary>
        private void SetTintOpaque(bool opaque)
        {
            try
            {
                if (TintLayer == null)
                {
                    return;
                }

                // The layer is always ~92% opaque: during the system move loop the
                // backdrop is suspended, and anything more transparent turns black.
                float t = Math.Clamp(_settings.Brightness / 100f, 0f, 1f);
                byte alpha = (byte)Math.Clamp(30 + t * 150, 0, 255);
                TintLayer.Fill = new SolidColorBrush(Color.FromArgb(alpha, 20, 24, 34));
            }
            catch
            {
            }
        }

        /// <summary>Borderless, no system frame: keeps the backdrop composed while moving.</summary>
        private void ApplyFrameless(IntPtr hwnd)
        {
            int style = GetWindowLong(hwnd, -16);
            style &= ~(0x00C00000 | 0x00040000 | 0x00080000 | 0x00020000 | 0x00010000);
            SetWindowLong(hwnd, -16, style);

            int ex = GetWindowLong(hwnd, -20);
            ex |= 0x00000080; // WS_EX_TOOLWINDOW: no drop shadow
            SetWindowLong(hwnd, -20, ex);

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0027);
            Console.WriteLine("frameless applied");
        }
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const int WsExToolWindow = 0x00000080;
        private const int WsCaption = 0x00C00000;
        private const int WsThickFrame = 0x00040000;
        private const int WsSysMenu = 0x00080000;
        private const int WsMinimizeBox = 0x00020000;
        private const int WsMaximizeBox = 0x00010000;
        private const int WsPopup = unchecked((int)0x80000000);

        /// <summary>Strips every system frame bit so Windows stops drawing its 1px border.</summary>
        private void ApplyPopupStyle(IntPtr hwnd)
        {
            int style = GetWindowLong(hwnd, GwlStyle);
            style &= ~(WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox);
            style |= WsPopup;
            SetWindowLong(hwnd, GwlStyle, style);

            // WS_EX_TOOLWINDOW：系统不再给窗口画投影（四周那圈黑边就是它）
            int exStyle = GetWindowLong(hwnd, GwlExStyle);
            exStyle |= Environment.GetEnvironmentVariable("DBT_NO_TOOLWINDOW") == "1" ? 0 : WsExToolWindow;
            SetWindowLong(hwnd, GwlExStyle, exStyle);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0027); // NOMOVE|NOSIZE|NOZORDER|FRAMECHANGED
        }

        private bool _resizing;
        private int _resizeEdge;
        private PointInt32 _resizeStartSize;
        private Windows.Foundation.Point _resizeStartPoint;

        private const int IdcSizeNs = 32645;
        private const int IdcSizeWe = 32644;
        private const int IdcSizeNwse = 32642;
        private const int IdcSizeNesw = 32643;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr cursor);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetCursor();

        private void ApplyEdgeCursor(PointerRoutedEventArgs args)
        {
            var point = args.GetCurrentPoint(RootGrid).Position;
            int border = 7;
            bool left = point.X <= border;
            bool right = point.X >= RootGrid.ActualWidth - border;
            bool top = point.Y <= border;
            bool bottom = point.Y >= RootGrid.ActualHeight - border;

            int shape = 0;
            if ((left || right) && (top || bottom))
            {
                shape = left && top || right && bottom ? IdcSizeNwse : IdcSizeNesw;
            }
            else if (left || right)
            {
                shape = IdcSizeWe;
            }
            else if (top || bottom)
            {
                shape = IdcSizeNs;
            }

            if (shape != 0)
            {
                SetCursor(LoadCursor(IntPtr.Zero, (IntPtr)shape));
            }
        }

        private void OnResizeExited(object sender, PointerRoutedEventArgs args)
        {
            SetCursor(LoadCursor(IntPtr.Zero, (IntPtr)32512)); // IDC_ARROW
        }

        /// <summary>Directly sets the native resize cursor for a hotzone (bit mask 1/2/4/8).</summary>
        private Microsoft.UI.Xaml.DispatcherTimer _cursorTimer;
        private int _cursorEdge;

        /// <summary>WinUI keeps resetting the cursor, so while the pointer sits on an
        /// edge strip we re-apply the native resize cursor a few dozen times a second.</summary>
        private void StartEdgeCursor(int edge)
        {
            _cursorEdge = edge;
            SetEdgeCursor(edge);
            if (_cursorTimer == null)
            {
                _cursorTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                _cursorTimer.Tick += (sender, args) => SetEdgeCursor(_cursorEdge);
            }

            _cursorTimer.Start();
        }

        private void StopEdgeCursor()
        {
            if (_cursorTimer != null)
            {
                _cursorTimer.Stop();
            }
        }
        private void SetEdgeCursor(int edge)
        {
            int shape;
            bool horizontal = (edge & 3) != 0;
            bool vertical = (edge & 12) != 0;
            if (horizontal && vertical)
            {
                shape = (edge & 1) != 0 && (edge & 4) != 0 || (edge & 2) != 0 && (edge & 8) != 0
                    ? IdcSizeNwse
                    : IdcSizeNesw;
            }
            else if (horizontal)
            {
                shape = IdcSizeWe;
            }
            else
            {
                shape = IdcSizeNs;
            }

            SetCursor(LoadCursor(IntPtr.Zero, (IntPtr)shape));
        }
        private void OnResizeEntered(object sender, PointerRoutedEventArgs args)
        {
            var border0 = sender as FrameworkElement;
            if (border0 != null && int.TryParse(border0.Tag?.ToString(), out int edge0))
            {
                SetEdgeCursor(edge0);
            }

            var border = sender as FrameworkElement;
            if (border == null)
            {
                return;
            }

        }

        private void OnResizePressed(object sender, PointerRoutedEventArgs args)
        {
            var border = sender as FrameworkElement;
            if (border == null)
            {
                return;
            }

            _resizing = true;
            _resizeEdge = int.TryParse((sender as FrameworkElement)?.Tag?.ToString(), out int edgeBits) ? edgeBits : 2;
            _resizeStartSize = new PointInt32(AppWindow.Size.Width, AppWindow.Size.Height);
            _resizeStartPoint = args.GetCurrentPoint(null).Position;
            border.CapturePointer(args.Pointer);
        }

        private void OnResizeMoved(object sender, PointerRoutedEventArgs args)
        {
            if (sender is Border zone2 && int.TryParse(zone2.Tag?.ToString(), out int zoneEdge))
            {
                StartEdgeCursor(zoneEdge);
            }

            if (!_resizing)
            {
                return;
            }

            var position = args.GetCurrentPoint(null).Position;
            int dx = (int)(position.X - _resizeStartPoint.X);
            int dy = (int)(position.Y - _resizeStartPoint.Y);
            int width = _resizeStartSize.X + ((_resizeEdge & 2) != 0 ? dx : 0) + ((_resizeEdge & 1) != 0 ? -dx : 0);
            int height = _resizeStartSize.Y + ((_resizeEdge & 8) != 0 ? dy : 0) + ((_resizeEdge & 4) != 0 ? -dy : 0);
            width = Math.Max(300, width);
            height = Math.Max(360, height);
            AppWindow.Resize(new SizeInt32(width, height));
        }

        private void OnResizeReleased(object sender, PointerRoutedEventArgs args)
        {
            if (_resizing)
            {
                _resizing = false;
                (sender as FrameworkElement)?.ReleasePointerCapture(args.Pointer);
            }
        }

        private bool _dragging;
        private PointInt32 _dragStartWindow;
        private POINT _dragStartScreen;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

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
                GetCursorPos(out _dragStartScreen);
                _dragStartWindow = AppWindow.Position;
                HeaderBar.CapturePointer(args.Pointer);
            };

            HeaderBar.PointerMoved += (sender, args) =>
            {
                if (!_dragging)
                {
                    return;
                }

                GetCursorPos(out POINT now);
                AppWindow.Move(new PointInt32(
                    _dragStartWindow.X + (now.X - _dragStartScreen.X),
                    _dragStartWindow.Y + (now.Y - _dragStartScreen.Y)));
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

        private MicaController _mica;

        /// <summary>Rebuilds the backdrop for the chosen material.</summary>
        public void ApplyMaterial()
        {
            try
            {
                if (_mica != null)
                {
                    _mica.RemoveAllSystemBackdropTargets();
                    _mica.Dispose();
                    _mica = null;
                }

                if (_acrylic != null)
                {
                    _acrylic.RemoveAllSystemBackdropTargets();
                    _acrylic.Dispose();
                    _acrylic = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("backdrop dispose: " + ex.Message);
            }

            ApplyAcrylic();
            Console.WriteLine("material = " + _settings.Material);
        }

        private void ApplyAcrylic()
        {
            _configuration = new SystemBackdropConfiguration
            {
                IsInputActive = true,
                Theme = SystemBackdropTheme.Dark
            };

            // Mica samples the wallpaper once instead of re-blurring on every
            // move, so dragging stays smooth and the backdrop does not flash.
            bool wantAcrylic = string.Equals(_settings.Material, "acrylic", StringComparison.OrdinalIgnoreCase);

            if (!wantAcrylic && MicaController.IsSupported())
            {
                _mica = new MicaController { Kind = MicaKind.Base };
                _mica.SetSystemBackdropConfiguration(_configuration);
                _mica.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                _mica.TintColor = Color.FromArgb(255, 24, 28, 38);
                _mica.FallbackColor = Color.FromArgb(255, 24, 28, 38);
                Console.WriteLine("backdrop: mica");
                ApplyBrightness();
                return;
            }

            if (DesktopAcrylicController.IsSupported())
            {
                _acrylic = new DesktopAcrylicController
                {
                    Kind = string.Equals(_settings.Material, "acrylic-thin", StringComparison.OrdinalIgnoreCase)
                        ? DesktopAcrylicKind.Thin
                        : DesktopAcrylicKind.Base,
                    TintColor = Color.FromArgb(255, 24, 28, 38),
                    // Fallback matches the tint, so a temporary blur loss shows
                    // the normal colour instead of flashing black.
                    FallbackColor = Color.FromArgb(255, 24, 28, 38)
                };
                _acrylic.SetSystemBackdropConfiguration(_configuration);
                _acrylic.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                Console.WriteLine("backdrop: acrylic");
            }

            ApplyBrightness();
        }

        /// <summary>Brightness 0 = clear/light glass, 100 = dark glass.</summary>
        private void ApplyBrightness()
        {
            float t = Math.Clamp(_settings.Brightness / 100f, 0f, 1f);
            if (TintLayer != null)
            {
                byte alpha = (byte)Math.Clamp(30 + t * 150, 0, 255);
                TintLayer.Fill = new SolidColorBrush(Color.FromArgb(alpha, 20, 24, 34));
            }

            if (_mica != null)
            {
                _mica.TintOpacity = 0.06f + t * 0.62f;
                _mica.LuminosityOpacity = 0.20f + t * 0.60f;
            }

            if (_acrylic != null)
            {
                _acrylic.TintOpacity = 0.06f + t * 0.62f;
                _acrylic.LuminosityOpacity = 0.20f + t * 0.60f;
            }

            BrightnessLabel.Text = "当前 " + _settings.Brightness + "（0 最清透 / 100 最厚重）";
        }

        private void OnMaterialChanged(object sender, SelectionChangedEventArgs args)
        {
            if (_loadingDetail || MaterialBox == null)
            {
                return;
            }

            if (MaterialBox.SelectedItem is ComboBoxItem item && item.Tag is string material)
            {
                _settings.Material = material;
                _settings.Save();
                ApplyMaterial();
            }
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

        private void OnQuickAddKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            args.Handled = true;
            string raw = (QuickAddBox.Text ?? "").Trim();
            if (raw.Length == 0)
            {
                return;
            }

            TodoDraft draft = TodoParser.Parse(raw, AppConfig.Load(), DateTimeOffset.Now);
            string title = string.IsNullOrWhiteSpace(draft.Title) ? raw : draft.Title;
            TodoRepo.Save(
                null,
                title,
                draft.Due,
                draft.RecurrenceMode,
                draft.ColorMarker,
                draft.Important,
                string.IsNullOrEmpty(draft.Notes) ? raw : draft.Notes,
                draft.Steps);

            QuickAddBox.Text = "";
            Reload();
            Console.WriteLine("quick add: " + title + " due=" + draft.Due + " rec=" + draft.RecurrenceMode);
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

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        private bool _hookInstalled;

        private void InstallCursorHookOnce()
        {
            if (_hookInstalled)
            {
                return;
            }

            _hookInstalled = true;
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            InstallCursorHook(hwnd);
            Console.WriteLine("wndproc hook installed");
        }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
