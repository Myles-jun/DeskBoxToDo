using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace DeskBoxToDo.WinUI
{
    internal enum WindowLayer
    {
        Normal,
        Desktop,
        TopMost
    }

    internal sealed class ChromeOptions
    {
        public bool Resizable = true;
        public int MinWidth = 300;
        public int MinHeight = 380;
        public WindowLayer Layer = WindowLayer.Normal;
        public bool ToolWindow = false;
    }

    /// <summary>
    /// DeskBox's window-identity path, extracted into one place: strip the system
    /// frame, extend the client area, let DWM round the window while XAML draws the
    /// border, then own dragging/resizing with frame-coalesced SetWindowPos calls.
    /// </summary>
    internal sealed class WindowChrome
    {
        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const int GwlpHwndParent = -8;

        private const int WsCaption = 0x00C00000;
        private const int WsBorder = 0x00800000;
        private const int WsDlgFrame = 0x00400000;
        private const int WsThickFrame = 0x00040000;
        private const int WsSysMenu = 0x00080000;
        private const int WsMinimizeBox = 0x00020000;
        private const int WsMaximizeBox = 0x00010000;
        private const int WsExToolWindow = 0x00000080;

        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpNoSendChanging = 0x0400;
        private const uint SwpShowWindow = 0x0040;

        private static readonly IntPtr HwndTop = IntPtr.Zero;
        private static readonly IntPtr HwndBottom = new IntPtr(1);
        private static readonly IntPtr HwndTopMost = new IntPtr(-1);
        private static readonly IntPtr HwndNoTopMost = new IntPtr(-2);

        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaBorderColor = 34;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpRound = 2;

        private readonly Window _window;
        private readonly GlassSurface _surface;
        private readonly ChromeOptions _options;

        private IDisposable _dragClock;
        private IDisposable _resizeClock;
        private bool _dragging;
        private bool _resizeActive;
        private bool _dragDirty;
        private bool _resizeDirty;
        private bool _desktopAttached;
        private bool _raisedUntilDeactivated;
        private PointInt32 _dragStartWindow;
        private PointInt32 _dragPending;
        private POINT _dragStartCursor;
        private POINT _resizeStartCursor;
        private SizeInt32 _resizeStartSize;
        private PointInt32 _resizeStartPosition;
        private int _resizeEdge;
        private RectInt32 _resizePending;

        public WindowChrome(Window window, GlassSurface surface, ChromeOptions options)
        {
            _window = window;
            _surface = surface;
            _options = options ?? new ChromeOptions();
            Layer = _options.Layer;
        }

        public IntPtr Hwnd { get; private set; }

        public AppWindow AppWindow { get; private set; }

        public WindowLayer Layer { get; private set; }

        public string AppliedMaterial { get; private set; } = "";

        /// <summary>Raised after a drag or resize finishes, for bounds persistence.</summary>
        public event Action InteractionCompleted;

        public void Attach()
        {
            Hwnd = WindowNative.GetWindowHandle(_window);
            AppWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(Hwnd));

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }

            int style = GetWindowLong(Hwnd, GwlStyle);
            style &= ~(WsCaption | WsBorder | WsDlgFrame | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox);
            SetWindowLong(Hwnd, GwlStyle, style);

            if (_options.ToolWindow)
            {
                int exStyle = GetWindowLong(Hwnd, GwlExStyle);
                SetWindowLong(Hwnd, GwlExStyle, exStyle | WsExToolWindow);
            }

            SetWindowPos(Hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);

            // The client area is the whole window; XAML paints to the very edge.
            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(Hwnd, ref margins);

            ApplyDwmChrome();
            HookTitleDrag();
            HookResize();
            ApplyLayer(Layer);
            ApplyAppearance();
            Live.Add(new System.WeakReference<WindowChrome>(this));

            // Corner preference is a window attribute, re-apply when the radius changes.
            _window.SizeChanged += (sender, args) => ApplyCornerPreference();
        }

        /// <summary>Theme, rounded corners and the invisible DWM border, re-applied on theme changes.</summary>
        public void ApplyDwmChrome()
        {
            if (Hwnd == IntPtr.Zero)
            {
                return;
            }

            int dark = 1;
            DwmSetWindowAttribute(Hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            // 0xFFFFFFFE = DWMWA_COLOR_NONE: the visual border is XAML's hairline.
            int noBorder = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(Hwnd, DwmwaBorderColor, ref noBorder, sizeof(int));

            // Windows only anti-aliases its own corner radii, so the outer shape uses
            // DWM: 8 (round), 4 (small) or square. A GDI region can clip arbitrary
            // radii but renders a visible staircase, so it is not used.
            int radius = (int)Math.Round(Appearance.ResolveRadius());
            int preference = radius <= 0 ? 1 : (radius <= 4 ? 3 : 2);
            if (MaterialLadder.IsWindows11)
            {
                DwmSetWindowAttribute(Hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
            }
        }

        public string ApplyMaterial(MaterialLadder.Kind kind, int depth)
        {
            AppliedMaterial = MaterialLadder.Apply(_window, Hwnd, kind, depth, Color.FromArgb(255, 0x24, 0x1C, 0x2B));

            // A solid fallback needs the plate to carry the tint; real materials
            // should stay almost transparent so the blur shows through.
            _surface.PlateColor = AppliedMaterial == "solid"
                ? Color.FromArgb(0xE6, 0x1A, 0x16, 0x1E)
                : Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF);
            return AppliedMaterial;
        }

        /// <summary>
        /// Briefly lifts the window above topmost apps (so a hotkey-triggered card
        /// is actually visible), then restores the user's chosen layer.
        /// </summary>
        public void PulseTopMost(int milliseconds = 700)
        {
            if (Hwnd == IntPtr.Zero || Layer == WindowLayer.TopMost)
            {
                return;
            }

            SetWindowPos(Hwnd, HwndTopMost, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);

            Microsoft.UI.Dispatching.DispatcherQueueTimer timer = _window.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
            timer.IsRepeating = false;
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                ApplyLayer(Layer);
            };
            timer.Start();
        }

        /// <summary>
        /// Keeps the window above topmost apps until the user moves focus away, then
        /// restores the chosen layer. A plain 0.7s pulse is not enough when the app
        /// that triggered the hotkey is itself topmost.
        /// </summary>
        public void RaiseUntilDeactivated()
        {
            if (Hwnd == IntPtr.Zero || Layer == WindowLayer.TopMost)
            {
                return;
            }

            SetWindowPos(Hwnd, HwndTopMost, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);

            if (_raisedUntilDeactivated)
            {
                return;
            }

            _raisedUntilDeactivated = true;
            _window.Activated += OnRaiseActivated;
        }

        private void OnRaiseActivated(object sender, WindowActivatedEventArgs args)
        {
            // WinUI 3's Window has no Deactivated event; the state arrives here.
            if (!_raisedUntilDeactivated ||
                args.WindowActivationState != WindowActivationState.Deactivated)
            {
                return;
            }

            _raisedUntilDeactivated = false;
            _window.Activated -= OnRaiseActivated;
            ApplyLayer(Layer);
        }

        /// <summary>Flashes the taskbar button so a raised window is noticed.</summary>
        public void FlashTaskbar()
        {
            if (Hwnd == IntPtr.Zero)
            {
                return;
            }

            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = Hwnd,
                dwFlags = 0x00000002 /* FLASHW_TRAY */ | 0x00000004 /* FLASHW_TIMERNOFG */,
                uCount = 3,
                dwTimeout = 0
            };
            FlashWindowEx(ref info);
        }

        private static readonly System.Collections.Generic.List<System.WeakReference<WindowChrome>> Live =
            new System.Collections.Generic.List<System.WeakReference<WindowChrome>>();

        internal static void ApplyToAll()
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                if (!Live[i].TryGetTarget(out WindowChrome chrome))
                {
                    Live.RemoveAt(i);
                    continue;
                }

                chrome.ApplyAppearance();
            }
        }

        /// <summary>Applies the shared material + radius; every window uses this.</summary>
        public void ApplyAppearance()
        {
            if (Hwnd == IntPtr.Zero)
            {
                return;
            }

            _surface.CornerRadius = Appearance.ResolveRadius();
            ApplyMaterial(Appearance.ResolveMaterial(), Appearance.ResolveDepth());
            ApplyCornerPreference();
        }

        private void ApplyCornerPreference()
        {
            if (Hwnd == IntPtr.Zero || !MaterialLadder.IsWindows11)
            {
                return;
            }

            int radius = (int)Math.Round(Appearance.ResolveRadius());
            int preference = radius <= 0 ? 1 : (radius <= 4 ? 3 : 2);
            DwmSetWindowAttribute(Hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }

        public void ApplyLayer(WindowLayer layer)
        {
            Layer = layer;
            if (Hwnd == IntPtr.Zero)
            {
                return;
            }

            if (layer == WindowLayer.Desktop && TryAttachDesktop())
            {
                _desktopAttached = true;
                SetWindowPos(Hwnd, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
                return;
            }

            if (_desktopAttached)
            {
                SetWindowLongPtr(Hwnd, GwlpHwndParent, IntPtr.Zero);
                _desktopAttached = false;
            }

            SetWindowPos(
                Hwnd,
                layer == WindowLayer.TopMost ? HwndTopMost : HwndNoTopMost,
                0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        }

        /// <summary>
        /// Desktop-pinned widgets own to Explorer's icon view instead of being
        /// parented to it: Win+D stops hiding them, yet they stay in the desktop
        /// band instead of floating over applications.
        /// </summary>
        private bool TryAttachDesktop()
        {
            IntPtr defView = FindDesktopIconView();
            if (defView == IntPtr.Zero)
            {
                return false;
            }

            SetWindowLongPtr(Hwnd, GwlpHwndParent, defView);
            return GetWindowLongPtr(Hwnd, GwlpHwndParent) == defView;
        }

        private static IntPtr FindDesktopIconView()
        {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr view = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero)
            {
                return view;
            }

            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, _) =>
            {
                IntPtr child = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (child != IntPtr.Zero)
                {
                    found = child;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            return found;
        }

        // 鈹€鈹€ Title drag 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

        private void HookTitleDrag()
        {
            FrameworkElement handle = _surface.TitleBarElement;
            handle.PointerPressed += OnTitlePointerPressed;
            handle.PointerMoved += OnTitlePointerMoved;
            handle.PointerReleased += OnTitlePointerReleased;
            handle.PointerCaptureLost += OnTitlePointerCaptureLost;
        }

        private void OnTitlePointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (IsInteractiveSource(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (!GetCursorPos(out _dragStartCursor))
            {
                return;
            }

            _dragStartWindow = AppWindow.Position;
            _dragPending = _dragStartWindow;
            _dragDirty = false;
            _dragging = true;
            _surface.TitleBarElement.CapturePointer(e.Pointer);
            _dragClock ??= FrameClock.Register(ApplyDragFrame);
            e.Handled = true;
        }

        private void OnTitlePointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging || !GetCursorPos(out POINT now))
            {
                return;
            }

            _dragPending = new PointInt32(
                _dragStartWindow.X + (now.X - _dragStartCursor.X),
                _dragStartWindow.Y + (now.Y - _dragStartCursor.Y));
            _dragDirty = true;
            e.Handled = true;
        }

        private void OnTitlePointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_dragging)
            {
                return;
            }

            ApplyDragFrame();
            EndDrag();
            _surface.TitleBarElement.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
            InteractionCompleted?.Invoke();
        }

        private void OnTitlePointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_dragging)
            {
                EndDrag();
                InteractionCompleted?.Invoke();
            }
        }

        private void EndDrag()
        {
            _dragging = false;
            _dragDirty = false;
            _dragClock?.Dispose();
            _dragClock = null;
        }

        private void ApplyDragFrame()
        {
            if (!_dragDirty || Hwnd == IntPtr.Zero)
            {
                return;
            }

            _dragDirty = false;
            SetWindowPos(
                Hwnd,
                HwndTop,
                _dragPending.X,
                _dragPending.Y,
                0,
                0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpNoSendChanging);
        }

        // 鈹€鈹€ Edge resize 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

        private void HookResize()
        {
            if (!_options.Resizable)
            {
                foreach (ResizeEdge zone in _surface.Zones)
                {
                    zone.Visibility = Visibility.Collapsed;
                }

                return;
            }

            foreach (ResizeEdge zone in _surface.Zones)
            {
                zone.PointerPressed += OnZonePressed;
                zone.PointerMoved += OnZoneMoved;
                zone.PointerReleased += OnZoneReleased;
                zone.PointerCaptureLost += OnZoneCaptureLost;
            }
        }

        private void OnZonePressed(object sender, PointerRoutedEventArgs e)
        {
            if (!(sender is ResizeEdge zone) || !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (!GetCursorPos(out _resizeStartCursor))
            {
                return;
            }

            _resizeEdge = zone.EdgeBits;
            _resizeStartSize = AppWindow.Size;
            _resizeStartPosition = AppWindow.Position;
            _resizePending = new RectInt32(
                _resizeStartPosition.X, _resizeStartPosition.Y,
                _resizeStartSize.Width, _resizeStartSize.Height);
            _resizeActive = true;
            _resizeDirty = false;
            zone.CapturePointer(e.Pointer);
            _resizeClock ??= FrameClock.Register(ApplyResizeFrame);
            e.Handled = true;
        }

        private void OnZoneMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_resizeActive || !GetCursorPos(out POINT now))
            {
                return;
            }

            int dx = now.X - _resizeStartCursor.X;
            int dy = now.Y - _resizeStartCursor.Y;

            int x = _resizeStartPosition.X;
            int y = _resizeStartPosition.Y;
            int width = _resizeStartSize.Width;
            int height = _resizeStartSize.Height;

            if ((_resizeEdge & 1) != 0)
            {
                width -= dx;
                x += dx;
            }

            if ((_resizeEdge & 2) != 0)
            {
                width += dx;
            }

            if ((_resizeEdge & 4) != 0)
            {
                height -= dy;
                y += dy;
            }

            if ((_resizeEdge & 8) != 0)
            {
                height += dy;
            }

            double scale = Dpi.ScaleFor(Hwnd);
            int minWidth = (int)Math.Round(_options.MinWidth * scale);
            int minHeight = (int)Math.Round(_options.MinHeight * scale);

            if (width < minWidth)
            {
                if ((_resizeEdge & 1) != 0)
                {
                    x -= minWidth - width;
                }

                width = minWidth;
            }

            if (height < minHeight)
            {
                if ((_resizeEdge & 4) != 0)
                {
                    y -= minHeight - height;
                }

                height = minHeight;
            }

            _resizePending = new RectInt32(x, y, width, height);
            _resizeDirty = true;
            e.Handled = true;
        }

        private void OnZoneReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_resizeActive)
            {
                return;
            }

            ApplyResizeFrame();
            EndResize();
            (sender as ResizeEdge)?.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
            InteractionCompleted?.Invoke();
        }

        private void OnZoneCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_resizeActive)
            {
                EndResize();
                InteractionCompleted?.Invoke();
            }
        }

        private void EndResize()
        {
            _resizeActive = false;
            _resizeDirty = false;
            _resizeClock?.Dispose();
            _resizeClock = null;
        }

        private void ApplyResizeFrame()
        {
            if (!_resizeDirty || Hwnd == IntPtr.Zero)
            {
                return;
            }

            _resizeDirty = false;
            SetWindowPos(
                Hwnd,
                HwndTop,
                _resizePending.X,
                _resizePending.Y,
                _resizePending.Width,
                _resizePending.Height,
                SwpNoZOrder | SwpNoActivate | SwpNoSendChanging);
        }

        public void ResizeTo(int widthDip, int heightDip)
        {
            if (Hwnd == IntPtr.Zero)
            {
                return;
            }

            AppWindow.Resize(new SizeInt32(Dpi.Px(Hwnd, widthDip), Dpi.Px(Hwnd, heightDip)));
        }

        public RectInt32 GetBounds()
        {
            if (Hwnd != IntPtr.Zero && GetWindowRect(Hwnd, out RECT rect))
            {
                return new RectInt32(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }

            return new RectInt32(0, 0, 400, 560);
        }

        public void MoveTo(int x, int y)
        {
            if (Hwnd != IntPtr.Zero)
            {
                SetWindowPos(Hwnd, HwndTop, x, y, 0, 0,
                    SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpNoSendChanging);
            }
        }

        private static bool IsInteractiveSource(DependencyObject source)
        {
            DependencyObject current = source;
            int guard = 0;
            while (current != null && guard++ < 12)
            {
                if (current is ButtonBase || current is TextBox || current is ComboBox ||
                    current is ToggleSwitch || current is Slider || current is ScrollBar)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        // 鈹€鈹€ Win32 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO info);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    }
}

