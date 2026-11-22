using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            HookResizeBorders();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(HeaderBar);
            Console.WriteLine("titlebar host set");
            _dragHook = NativeDragHook.Install(this, 46);
            Console.WriteLine("main drag hook ready");
            StartReminderTimer();

            if (Environment.GetEnvironmentVariable("DBT_AI_TEST") == "1")
            {
                var aiProbe = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                aiProbe.Tick += async (sender, args) =>
                {
                    aiProbe.Stop();
                    try
                    {
                        AiReparse.Result r = await AiReparse.ParseAsync("明天下午三点 交周报 重要");
                        Console.WriteLine("AI OK title=" + r.Title + " due=" + r.Due + " rec=" + r.Recurrence + " color=" + r.Color + " steps=" + r.Steps.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("AI FAIL: " + ex.Message);
                    }
                };
                aiProbe.Start();
            }

            if (Environment.GetEnvironmentVariable("DBT_OPEN_CONFIRM") == "1")
            {
                var confirmProbe = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                confirmProbe.Tick += (sender, args) =>
                {
                    confirmProbe.Stop();
                    OpenConfirmWindow("明天下午三点 交周报 重要");
                };
                confirmProbe.Start();
            }

            if (Environment.GetEnvironmentVariable("DBT_OPEN_SETTINGS") == "1")
            {
                var settingsProbe = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                settingsProbe.Tick += (sender, args) =>
                {
                    settingsProbe.Stop();
                    OpenSettingsWindow();
                };
                settingsProbe.Start();
            }
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

        private Microsoft.UI.Input.InputNonClientPointerSource _ncSource;

        /// <summary>
        /// Registers the window edges as real non-client resize borders, so Windows
        /// gives native resize, snapping and resize cursors without WS_THICKFRAME.
        /// </summary>
        private void SetupNonClientRegions()
        {
            try
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
                _ncSource = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(id);
                UpdateNonClientRegions();

                AppWindow.Changed += (sender, args) =>
                {
                    if (args.DidSizeChange)
                    {
                        UpdateNonClientRegions();
                    }
                };

                Console.WriteLine("non-client regions ready");
            }
            catch (Exception ex)
            {
                Console.WriteLine("non-client regions failed: " + ex.Message);
            }
        }

        private void UpdateNonClientRegions()
        {
            if (_ncSource == null)
            {
                return;
            }

            int w = AppWindow.Size.Width;
            int h = AppWindow.Size.Height;
            int edge = 8;
            int corner = 12;

            _ncSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.LeftBorder,
                new[] { new Windows.Graphics.RectInt32(0, 0, edge, h) });
            _ncSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.RightBorder,
                new[] { new Windows.Graphics.RectInt32(w - edge, 0, edge, h) });
            _ncSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.TopBorder,
                new[] { new Windows.Graphics.RectInt32(0, 0, w, edge) });
            _ncSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.BottomBorder,
                new[] { new Windows.Graphics.RectInt32(0, h - edge, w, edge) });

            // 该枚举没有"角"区域，Windows 会用相邻边的组合处理转角
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

            appWindow.Resize(new SizeInt32(Dpi.Px(hwnd, _settings.Width), Dpi.Px(hwnd, _settings.Height)));
            appWindow.Move(new PointInt32(980, 380));

            ApplyClassBackground(hwnd);

            // Explicitly NOT topmost: the window behaves like a normal app window.
            SetWindowPos(hwnd, (IntPtr)0xFFFFFFFE /* HWND_NOTOPMOST */, 0, 0, 0, 0, 0x0003);

            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            // 让系统不要画边框色（0xFFFFFFFE = DWMWA_COLOR_NONE），避免四周黑边
            var boot = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            boot.Tick += (sender, args) =>
            {
                boot.Stop();
                InstallTrayAndHotkeyOnce();
            };
            boot.Start();

            var hookWatchdog = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            hookWatchdog.Tick += (sender, args) => VerifyWndProcHook();
            hookWatchdog.Start();
            // 缩放改为 DeskBox 同款手动实现（MainWindow.Resize.cs）

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
        /// <summary>
        /// During a drag/resize gesture the backdrop is dropped to a plain tint:
        /// it keeps the gesture at full frame rate and hides any compositor gap.
        /// </summary>
        private void SimplifyBackdropForInteraction(bool simplify)
        {
            try
            {
                if (simplify)
                {
                    SetTintOpaque(true);
                }
                else
                {
                    SetTintOpaque(false);
                    ApplyBrightness();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("simplify backdrop failed: " + ex.Message);
            }
        }

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
        private POINT _resizeStartScreen;
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

            GetCursorPos(out POINT now);
            int dx = now.X - _resizeStartScreen.X;
            int dy = now.Y - _resizeStartScreen.Y;
            int width = _resizeStartSize.X + ((_resizeEdge & 2) != 0 ? dx : 0) + ((_resizeEdge & 1) != 0 ? -dx : 0);
            int height = _resizeStartSize.Y + ((_resizeEdge & 8) != 0 ? dy : 0) + ((_resizeEdge & 4) != 0 ? -dy : 0);
            width = Math.Max(300, width);
            height = Math.Max(360, height);
            AppWindow.Resize(new SizeInt32(width, height));
            ApplyFrameless(WinRT.Interop.WindowNative.GetWindowHandle(this));
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

        private void HookEdgeResize()
        {
            Console.WriteLine("drag handlers attached");
            RootGrid.PointerPressed += (sender, args) =>
            {
                Console.WriteLine("root pressed");
                var point = args.GetCurrentPoint(RootGrid);
                if (!point.Properties.IsLeftButtonPressed)
                {
                    return;
                }

                double x = point.Position.X;
                double y = point.Position.Y;
                double w = RootGrid.ActualWidth;
                double h = RootGrid.ActualHeight;
                int border = 7;

                int edge = 0;
                if (x <= border) { edge |= 1; }
                if (x >= w - border) { edge |= 2; }
                if (y <= border) { edge |= 4; }
                if (y >= h - border) { edge |= 8; }

                if (edge == 0)
                {
                    return;
                }

                _resizing = true;
                _resizeEdge = edge;
                _resizeStartSize = new PointInt32(AppWindow.Size.Width, AppWindow.Size.Height);
                GetCursorPos(out _resizeStartScreen);
                RootGrid.CapturePointer(args.Pointer);
                Console.WriteLine("resize start edge=" + edge + " size=" + _resizeStartSize.X);
            };

            RootGrid.PointerMoved += (sender, args) => OnResizeMoved(sender, args);
            RootGrid.PointerReleased += (sender, args) => OnResizeReleased(sender, args);
        }
        /// <summary>True when the press landed on (or inside) a real control.</summary>
        private static bool IsInteractiveSource(DependencyObject source)
        {
            DependencyObject current = source;
            int guard = 0;
            while (current != null && guard++ < 12)
            {
                if (current is Button || current is CheckBox || current is TextBox ||
                    current is ComboBox || current is ToggleSwitch || current is TimePicker ||
                    current is CalendarDatePicker || current is ScrollBar || current is ListViewItem ||
                    current is ListView || current is ScrollViewer)
                {
                    return true;
                }

                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }

            return false;
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
                ToolTipService.SetToolTip(dot, TodoItem.LabelFor(entry.Key));
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

        private Microsoft.UI.Xaml.DispatcherTimer _reminderTimer;
        private ReminderWindow _reminderWindow;

        private void StartReminderTimer()
        {
            _reminderTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _reminderTimer.Tick += (sender, args) => CheckDueReminders();
            _reminderTimer.Start();
            CheckDueReminders();
        }

        /// <summary>Fires the toast for items whose due time arrived (2h catch-up window).</summary>
        private void CheckDueReminders()
        {
            try
            {
                DateTimeOffset now = DateTimeOffset.Now;
                foreach (TodoItem item in TodoStore.Load(out _))
                {
                    if (item.IsCompleted || item.DueDate == null)
                    {
                        continue;
                    }

                    DateTimeOffset due = item.DueDate.Value.ToLocalTime();
                    if (due > now || (now - due) > TimeSpan.FromHours(2))
                    {
                        continue;
                    }

                    string dueIso = item.DueDate.Value.ToString("o");
                    if (item.NotifiedForDue == dueIso)
                    {
                        continue;
                    }

                    TodoRepo.MarkNotified(item.Id, dueIso);
                    ShowReminderToast(item);
                    break; // one toast at a time
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("reminder check failed: " + ex.Message);
            }
        }

        private void ShowReminderToast(TodoItem item)
        {
            try
            {
                string detail = item.Subtitle;
                _reminderWindow?.Close();
                _reminderWindow = new ReminderWindow(item.Text, detail);
                _reminderWindow.OpenRequested += (sender, args) =>
                {
                    _reminderWindow?.Close();
                    _reminderWindow = null;
                    ShowFromTray();
                };
                _reminderWindow.Activate();
                RaiseAsOwnedDialog(_reminderWindow);
                Console.WriteLine("reminder toast: " + item.Text);

                var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    _reminderWindow?.Close();
                    _reminderWindow = null;
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("reminder toast failed: " + ex.Message);
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

            QuickAddBox.Text = "";
            OpenConfirmWindow(raw);
        }

        private void OnSettingsClick(object sender, RoutedEventArgs args)
        {
            OpenSettingsWindow();
        }

        private void OnCloseWindowClick(object sender, RoutedEventArgs args)
        {
            Close();
        }

        private void OnExitAppClick(object sender, RoutedEventArgs args)
        {
            Application.Current.Exit();
        }

        /// <summary>
        /// Makes a dialog owned by the main window and pushes it to the top of the
        /// z-order, so the main window can never cover it.
        /// </summary>
        private void RaiseAsOwnedDialog(Window dialog)
        {
            try
            {
                IntPtr owner = WinRT.Interop.WindowNative.GetWindowHandle(this);
                IntPtr child = WinRT.Interop.WindowNative.GetWindowHandle(dialog);
                SetWindowLongPtr(child, -8 /* GWLP_HWNDPARENT */, owner);
                SetWindowPos(child, IntPtr.Zero, 0, 0, 0, 0, 0x0013); // NOSIZE|NOMOVE|NOACTIVATE? keep flags below
                SetWindowPos(child, (IntPtr)0 /* HWND_TOP */, 0, 0, 0, 0, 0x0003); // NOMOVE|NOSIZE
            }
            catch (Exception ex)
            {
                Console.WriteLine("raise dialog failed: " + ex.Message);
            }
        }

        private void OpenConfirmWindow(string raw)
        {
            TodoDraft draft = TodoParser.Parse(raw, AppConfig.Load(), DateTimeOffset.Now);
            ConfirmWindow window;
            try
            {
                window = new ConfirmWindow(draft, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine("confirm create failed: " + ex);
                return;
            }
            window.Confirmed += (sender, result) =>
            {
                TodoRepo.Save(
                    null,
                    result.Title,
                    result.Due,
                    result.Repeat,
                    result.Color,
                    result.Important,
                    raw,
                    result.Steps.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries));
                Reload();
                Console.WriteLine("confirm add: " + result.Title);
            };
            window.Activate();
            RaiseAsOwnedDialog(window);
        }

        private void OpenSettingsWindow()
        {
            AppConfig shared = AppConfig.Load();
            var window = new SettingsWindow(_settings, shared.DefaultDueHour, shared.DefaultDueMinute,
                TodoStore.CurrentMode(), _hotkeyModifiers, _hotkeyKey);
            window.Saved += (sender, result) =>
            {
                shared.DefaultDueHour = result.Hour;
                shared.DefaultDueMinute = result.Minute;
                shared.Save();

                WriteSharedValue("todoMode", result.Mode);
                _hotkeyModifiers = result.Modifiers;
                _hotkeyKey = result.Key;
                _settings.HotkeyModifiers = result.Modifiers;
                _settings.HotkeyKey = result.Key;
                _settings.Save();

                ReRegisterHotkey();
                Reload();
                Console.WriteLine("settings saved: mode=" + result.Mode + " hotkey=" + result.Modifiers + "/" + result.Key);
            };
            window.Activate();
            RaiseAsOwnedDialog(window);
        }

        private void ReRegisterHotkey()
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            UnregisterHotKey(hwnd, HotkeyId);
            RegisterHotKey(hwnd, HotkeyId, _hotkeyModifiers, _hotkeyKey);
        }

        /// <summary>Writes one scalar into the 1.x config.json so both builds agree.</summary>
        private static void WriteSharedValue(string key, string value)
        {
            try
            {
                string path = System.IO.Path.Combine(TodoStore.DataRoot, "config.json");
                JsonObject root = File.Exists(path)
                    ? (JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject())
                    : new JsonObject();
                root[key] = value;
                File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            }
            catch (Exception ex)
            {
                Console.WriteLine("shared config write failed: " + ex.Message);
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
                ColorBox.Items.Add(new ComboBoxItem { Content = TodoItem.LabelFor(color.Key), Tag = color.Key });
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

            string color = item?.ColorMarker ?? "";
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
            string color = (ColorBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
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

        private const int HotkeyId = 0xB1D0;
        private bool _hotkeyRegistered;

        private uint _hotkeyModifiers = 0x0001;
        private uint _hotkeyKey = 0x51;
        private const uint WmHotkey = 0x0312;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        /// <summary>Bringing a window forward can be refused; attach to the foreground thread first.</summary>
        private void ForceForeground(IntPtr hwnd)
        {
            try
            {
                IntPtr foreground = GetForegroundWindow();
                uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
                uint myThread = GetCurrentThreadId();
                bool attached = foregroundThread != 0 &&
                    foregroundThread != myThread &&
                    AttachThreadInput(foregroundThread, myThread, true);

                ShowWindow(hwnd, 9); // SW_RESTORE
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                SetFocus(hwnd);

                if (attached)
                {
                    AttachThreadInput(foregroundThread, myThread, false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("foreground failed: " + ex.Message);
            }
        }

        private bool _alwaysOnTop = true;

        private void HandleTrayCommand(int command)
        {
            switch (command)
            {
                case TrayIcon.CommandShow:
                    ShowFromTray();
                    break;
                case TrayIcon.CommandDesktopLayer:
                    ToggleAlwaysOnTop();
                    break;
                case TrayIcon.CommandSettings:
                    ShowFromTray();
                    OpenSettingsWindow();
                    break;
                case TrayIcon.CommandHelp:
                    ShowFromTray();
                    ShowHotkeyNotice("用法：在任何软件里选中文字 → 按热键 → 在确认卡片里改两笔 → 加入待办。");
                    break;
                case TrayIcon.CommandExit:
                    Application.Current.Exit();
                    break;
            }
        }

        private void ToggleAlwaysOnTop()
        {
            _alwaysOnTop = !_alwaysOnTop;
            try
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
                if (AppWindow.GetFromWindowId(id).Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsAlwaysOnTop = _alwaysOnTop;
                }

                Console.WriteLine("always on top: " + _alwaysOnTop);
            }
            catch (Exception ex)
            {
                Console.WriteLine("toggle topmost failed: " + ex.Message);
            }
        }
        private void ShowFromTray()
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                ForceForeground(hwnd);
                Activate();
            }
            catch (Exception ex)
            {
                Console.WriteLine("show from tray failed: " + ex.Message);
            }
        }

        private void ShowHotkeyNotice(string message)
        {
            try
            {
                var notice = new ReminderWindow("热键提示", message);
                notice.Activate();
                RaiseAsOwnedDialog(notice);
                var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    notice.Close();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("hotkey notice failed: " + ex.Message);
            }
        }
        private void FlashQuickAddBorder()
        {
            try
            {
                QuickAddBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 127, 178, 255));
                var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    QuickAddBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0, 127, 178, 255));
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("flash failed: " + ex.Message);
            }
        }
        /// <summary>
        /// 1.x behaviour: copy whatever is selected in the front window, then show
        /// the confirm card prefilled with it; fall back to focusing the input box.
        /// </summary>
        /// <summary>Drag by the top strip (XAML path; the native HTCAPTION hook is a backup).</summary>
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        private Microsoft.UI.Xaml.DispatcherTimer _dragTimer;

        private void DragTick(object sender, object args)
        {
            if ((GetAsyncKeyState(0x01) & 0x8000) == 0)
            {
                _dragTimer?.Stop();
                return;
            }

            GetCursorPos(out POINT now);
            AppWindow.Move(new PointInt32(
                _dragStartWindow.X + (now.X - _dragStartScreen.X),
                _dragStartWindow.Y + (now.Y - _dragStartScreen.Y)));
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
        private void HookTitleDrag()
        {
            RootGrid.PointerPressed += (sender, args) =>
            {
                var point = args.GetCurrentPoint(RootGrid);
                Console.WriteLine("main press y=" + point.Position.Y + " left=" + point.Properties.IsLeftButtonPressed + " src=" + (args.OriginalSource == null ? "null" : args.OriginalSource.GetType().Name));
                if (!point.Properties.IsLeftButtonPressed)
                {
                    return;
                }

                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                double limit = 46 * Dpi.ScaleFor(hwnd);
                if (point.Position.Y > limit)
                {
                    return;
                }

                if (args.OriginalSource is Button || args.OriginalSource is CheckBox)
                {
                    return;
                }

                _dragging = true;
                GetCursorPos(out _dragStartScreen);
                _dragStartWindow = AppWindow.Position;
                Console.WriteLine("title drag start");
                _dragStartScreen = new POINT();
                GetCursorPos(out _dragStartScreen);
                _dragStartWindow = AppWindow.Position;

                // Poll the cursor instead of relying on PointerMoved: on this window
                // WinUI drops the move events once the pointer is captured.
                _dragTimer ??= new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _dragTimer.Tick += DragTick;
                _dragTimer.Start();
            };

            RootGrid.PointerMoved += (sender, args) =>
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

            RootGrid.PointerReleased += (sender, args) =>
            {
                if (_dragging)
                {
                    _dragging = false;
                    RootGrid.ReleasePointerCapture(args.Pointer);
                    Console.WriteLine("title drag end");
                }
            };
        }
        private void HandleHotkey()
        {
            try
            {
                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                Console.WriteLine("hotkey: fg=" + GetForegroundWindow() + " ours=" + hwnd);
                string selected = SelectionCapture.CaptureFromForeground(hwnd);
                ForceForeground(hwnd);

                if (!string.IsNullOrWhiteSpace(selected))
                {
                    string trimmed = selected.Trim();
                    if (trimmed.Length > 400)
                    {
                        trimmed = trimmed.Substring(0, 400);
                    }

                    Console.WriteLine("selection captured: " + trimmed.Length + " chars");
                    OpenConfirmWindow(trimmed);
                    return;
                }

                FocusQuickAdd();
            }
            catch (Exception ex)
            {
                Console.WriteLine("hotkey handling failed: " + ex.Message);
                FocusQuickAdd();
            }
        }
        private void FocusQuickAdd()
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                ForceForeground(hwnd);
                QuickAddBox.Focus(FocusState.Programmatic);
                FlashQuickAddBorder();
            }
            catch (Exception ex)
            {
                Console.WriteLine("focus quick add failed: " + ex.Message);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hwnd);
        private TrayIcon _tray;

        private NativeDragHook _dragHook;

        private bool _hookInstalled;

        private void VerifyWndProcHook()
        {
            // The old WndProc hook is gone; dragging now goes through SetTitleBar.
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        private void InstallTrayAndHotkeyOnce()
        {
            if (_hookInstalled)
            {
                return;
            }

            _hookInstalled = true;
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ApplyFrameless(hwnd);
            _hotkeyRegistered = RegisterHotKey(hwnd, HotkeyId, _hotkeyModifiers, _hotkeyKey);
            Console.WriteLine("hotkey registered: " + _hotkeyRegistered);

            if (!_hotkeyRegistered)
            {
                // Alt+Q may be taken (older DeskBoxToDo 1.x uses it too). Fall back once.
                _hotkeyModifiers = 0x0003; // Ctrl + Alt
                _hotkeyKey = 0x51;         // Q
                _hotkeyRegistered = RegisterHotKey(hwnd, HotkeyId, _hotkeyModifiers, _hotkeyKey);
                Console.WriteLine("hotkey fallback Ctrl+Alt+Q: " + _hotkeyRegistered);
                ShowHotkeyNotice(_hotkeyRegistered
                    ? "Alt+Q 已被其它程序占用（可能是 DeskBoxToDo 1.x），已临时改用 Ctrl+Alt+Q。可在「设置」里换回来。"
                    : "全局热键被其它程序占用，请在「设置」里换一个组合。");
            }

            if (_tray == null)
            {
                _tray = new TrayIcon();
                _tray.Create(hwnd, "DeskBoxToDo —— 选中一句话变成待办");
            }

            // WinUI re-applies the frame shortly after activation; strip it again.
            int attempts = 0;
            var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (sender, args) =>
            {
                ApplyFrameless(hwnd);
                if (++attempts >= 6)
                {
                    timer.Stop();
                }
            };
            timer.Start();
            Console.WriteLine("wndproc hook installed");
        }
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
