using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Manual, coalesced window resizing on a borderless window — the same model
    /// DeskBox uses: transparent XAML strips on every edge/corner keep track of the
    /// pointer, we compute the new bounds ourselves and apply one SetWindowPos per
    /// frame. The backdrop is simplified for the duration of the gesture.
    /// </summary>
    public sealed partial class MainWindow
    {
        private const int ResizeBorderThickness = 7;

        private readonly Dictionary<string, FrameworkElement> _resizeBorders =
            new Dictionary<string, FrameworkElement>();

        private bool _isResizing;
        private string _resizeDirection = "";
        private PointInt32 _resizeInitialPointer;
        private RectInt32 _resizeInitialBounds;
        private SizeInt32 _resizeMinimumSize = new SizeInt32(320, 380);

        private PointInt32 _pendingResizePointer;
        private bool _resizeFrameQueued;
        private DispatcherQueue _dispatcher;

        private Rectangle _snapGuideVertical;
        private Rectangle _snapGuideHorizontal;

        private void HookResizeBorders()
        {
            _dispatcher = DispatcherQueue.GetForCurrentThread();

            foreach (var entry in new (string Direction, FrameworkElement Element)[]
            {
                ("Left", ResizeLeft),
                ("Right", ResizeRight),
                ("Top", ResizeTop),
                ("Bottom", ResizeBottom),
                ("Left Top", ResizeTopLeft),
                ("Right Top", ResizeTopRight),
                ("Left Bottom", ResizeBottomLeft),
                ("Right Bottom", ResizeBottomRight)
            })
            {
                if (entry.Element == null)
                {
                    continue;
                }

                _resizeBorders[entry.Direction] = entry.Element;
                entry.Element.PointerPressed += ResizeBorderPointerPressed;
                entry.Element.PointerMoved += ResizeBorderPointerMoved;
                entry.Element.PointerReleased += ResizeBorderPointerReleased;
                entry.Element.PointerCaptureLost += ResizeBorderCaptureLost;
                entry.Element.PointerEntered += ResizeBorderPointerEntered;
                entry.Element.PointerExited += (sender, args) =>
                {
                    if (!_isResizing && sender is UIElement element)
                    {
                        UiElementCursorHelper.SetCursor(element, InputSystemCursorShape.Arrow);
                    }
                };
            }

            _snapGuideVertical = SnapGuideVertical;
            _snapGuideHorizontal = SnapGuideHorizontal;

            Console.WriteLine("resize borders hooked: " + _resizeBorders.Count);
        }

        private void ResizeBorderPointerEntered(object sender, PointerRoutedEventArgs args)
        {
            if (_isResizing)
            {
                return;
            }

            if (sender is FrameworkElement element && element.Tag is string direction)
            {
                UiElementCursorHelper.SetCursor(element, UiElementCursorHelper.ShapeForDirection(direction));
            }
        }

        private void ResizeBorderPointerPressed(object sender, PointerRoutedEventArgs args)
        {
            var point = args.GetCurrentPoint(this.Content as UIElement);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (!(sender is FrameworkElement element) || !(element.Tag is string direction))
            {
                return;
            }

            _isResizing = true;
            _resizeDirection = direction;
            POINT initial;
            GetCursorPos(out initial);
            _resizeInitialPointer = new PointInt32(initial.X, initial.Y);
            _resizeInitialBounds = GetWindowBounds();
            _resizeMinimumSize = GetMinimumWindowSize();
            SimplifyBackdropForInteraction(true);

            element.CapturePointer(args.Pointer);
            args.Handled = true;
            Console.WriteLine("resize start " + direction + " bounds=" + _resizeInitialBounds.Width + "x" + _resizeInitialBounds.Height);
        }

        private void ResizeBorderPointerMoved(object sender, PointerRoutedEventArgs args)
        {
            if (!_isResizing)
            {
                return;
            }

            if (GetCursorPos(out POINT current))
            {
                QueueInteractiveResize(new PointInt32(current.X, current.Y));
            }

            args.Handled = true;
        }

        private void ResizeBorderPointerReleased(object sender, PointerRoutedEventArgs args)
        {
            if (!_isResizing)
            {
                return;
            }

            (sender as UIElement)?.ReleasePointerCapture(args.Pointer);
            CompleteInteractiveResize();
            args.Handled = true;
        }

        private void ResizeBorderCaptureLost(object sender, PointerRoutedEventArgs args)
        {
            if (_isResizing)
            {
                CompleteInteractiveResize();
            }
        }

        /// <summary>Coalesces pointer moves into one SetWindowPos per dispatcher turn.</summary>
        private void QueueInteractiveResize(PointInt32 pointer)
        {
            _pendingResizePointer = pointer;
            if (_resizeFrameQueued)
            {
                return;
            }

            _resizeFrameQueued = true;
            _dispatcher.TryEnqueue(DispatcherQueuePriority.High, () =>
            {
                _resizeFrameQueued = false;
                if (_isResizing)
                {
                    ApplyInteractiveResize(_pendingResizePointer);
                }
            });
        }

        private void ApplyInteractiveResize(PointInt32 pointer)
        {
            int deltaX = pointer.X - _resizeInitialPointer.X;
            int deltaY = pointer.Y - _resizeInitialPointer.Y;

            int newWidth = _resizeInitialBounds.Width;
            int newHeight = _resizeInitialBounds.Height;
            int newX = _resizeInitialBounds.X;
            int newY = _resizeInitialBounds.Y;

            if (_resizeDirection.Contains("Right"))
            {
                newWidth = Math.Max(_resizeMinimumSize.Width, _resizeInitialBounds.Width + deltaX);
            }
            else if (_resizeDirection.Contains("Left"))
            {
                int rightEdge = _resizeInitialBounds.X + _resizeInitialBounds.Width;
                newWidth = Math.Max(_resizeMinimumSize.Width, _resizeInitialBounds.Width - deltaX);
                newX = rightEdge - newWidth;
            }

            if (_resizeDirection.Contains("Bottom"))
            {
                newHeight = Math.Max(_resizeMinimumSize.Height, _resizeInitialBounds.Height + deltaY);
            }
            else if (_resizeDirection.Contains("Top"))
            {
                int bottomEdge = _resizeInitialBounds.Y + _resizeInitialBounds.Height;
                newHeight = Math.Max(_resizeMinimumSize.Height, _resizeInitialBounds.Height - deltaY);
                newY = bottomEdge - newHeight;
            }

            var proposed = new RectInt32(newX, newY, newWidth, newHeight);
            var snapped = SnapToScreenEdges(proposed);
            SetWindowBounds(snapped);
        }

        /// <summary>Light-weight version of DeskBox's ResizeGuideOverlay: snap to monitor edges.</summary>
        private RectInt32 SnapToScreenEdges(RectInt32 proposed)
        {
            const int SnapDistance = 12;
            WorkArea working = GetWorkingArea();
            int x = proposed.X;
            int y = proposed.Y;
            int width = proposed.Width;
            int height = proposed.Height;

            bool snapLeft = Math.Abs(x - working.X) <= SnapDistance;
            bool snapRight = Math.Abs((x + width) - (working.X + working.Width)) <= SnapDistance;
            bool snapTop = Math.Abs(y - working.Y) <= SnapDistance;
            bool snapBottom = Math.Abs((y + height) - (working.Y + working.Height)) <= SnapDistance;

            if (_resizeDirection.Contains("Left") && snapLeft)
            {
                width = (x + width) - working.X;
                x = working.X;
            }
            else if (_resizeDirection.Contains("Right") && snapRight)
            {
                width = (working.X + working.Width) - x;
            }

            if (_resizeDirection.Contains("Top") && snapTop)
            {
                height = (y + height) - working.Y;
                y = working.Y;
            }
            else if (_resizeDirection.Contains("Bottom") && snapBottom)
            {
                height = (working.Y + working.Height) - y;
            }

            ShowSnapGuides(_resizeDirection.Contains("Left") || _resizeDirection.Contains("Right"),
                (_resizeDirection.Contains("Left") && snapLeft) || (_resizeDirection.Contains("Right") && snapRight),
                _resizeDirection.Contains("Top") || _resizeDirection.Contains("Bottom"),
                (_resizeDirection.Contains("Top") && snapTop) || (_resizeDirection.Contains("Bottom") && snapBottom));

            return new RectInt32(x, y, Math.Max(1, width), Math.Max(1, height));
        }

        private void ShowSnapGuides(bool horizontalResize, bool horizontalSnap, bool verticalResize, bool verticalSnap)
        {
            if (_snapGuideVertical != null)
            {
                _snapGuideVertical.Visibility = horizontalResize && horizontalSnap
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (_snapGuideHorizontal != null)
            {
                _snapGuideHorizontal.Visibility = verticalResize && verticalSnap
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void CompleteInteractiveResize()
        {
            _isResizing = false;
            _resizeDirection = "";
            SimplifyBackdropForInteraction(false);
            ShowSnapGuides(false, false, false, false);

            RectInt32 final = GetWindowBounds();
            GlassSettings settings = GlassSettings.Load();
            settings.Width = final.Width;
            settings.Height = final.Height;
            settings.Save();
            Console.WriteLine("resize end " + final.Width + "x" + final.Height);
        }

        private RectInt32 GetWindowBounds()
        {
            if (GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(this), out RECT rect))
            {
                return new RectInt32(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }

            return new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        }

        private void SetWindowBounds(RectInt32 bounds)
        {
            SetWindowPos(
                WinRT.Interop.WindowNative.GetWindowHandle(this),
                IntPtr.Zero,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                SwpNoZOrder | SwpNoActivate);
        }

        private SizeInt32 GetMinimumWindowSize()
        {
            return new SizeInt32(320, 380);
        }

        private WorkArea GetWorkingArea()
        {
            if (SystemParametersInfo(0x0030 /* SPI_GETWORKAREA */, 0, out RECT work, 0))
            {
                return new WorkArea(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
            }

            return new WorkArea(0, 0, 1920, 1080);
        }

        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint action, uint param, out RECT data, uint winIni);

        private readonly struct WorkArea
        {
            public WorkArea(int x, int y, int width, int height)
            {
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }

            public int X { get; }
            public int Y { get; }
            public int Width { get; }
            public int Height { get; }
        }
    }
}
