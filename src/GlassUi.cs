using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    internal static class Ui
    {
        /// <summary>True when the glass is light enough to need dark text.</summary>
        public static bool DarkText { get; set; }

        /// <summary>User-chosen glass opacity; 0 = adaptive.</summary>
        public static float OpacityOverride = -1f;

        /// <summary>Blur sampling quality: 4 = standard, 3 = finer, 2 = finest (slower).</summary>
        public static int GlassDownscale = 4;

        public static int GlassBlurRadius = 7;
        public static int GlassBlurPasses = 3;

        public static Color Text
        {
            get { return Color.FromArgb(242, 244, 248); }
        }

        public static Color TextMuted
        {
            get { return Color.FromArgb(226, 230, 240); }
        }
        public static readonly Color Accent = Color.FromArgb(86, 142, 247);
        public static readonly Color AccentHover = Color.FromArgb(110, 160, 255);
        public static readonly Color FieldFill = Color.FromArgb(255, 34, 37, 45);
        public static readonly Color FieldBorder = Color.FromArgb(46, 255, 255, 255);

        public static int Scale { get; private set; } = 1;

        public static void SetScale(int scale)
        {
            Scale = Math.Max(1, scale);
        }

        public static void InitScale()
        {
            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi >= 96)
                {
                    Scale = Math.Max(1, (int)Math.Round(dpi / 96.0));
                    return;
                }
            }
            catch
            {
                // Fall through to the Graphics probe.
            }

            try
            {
                using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                {
                    Scale = Math.Max(1, (int)Math.Round(graphics.DpiX / 96.0));
                }
            }
            catch
            {
                Scale = 1;
            }
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        public static int S(int value)
        {
            return (int)Math.Round(value * Scale / 1.0);
        }

        public static Font Font(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }

        public static int Average(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                return 0;
            }

            long sum = 0;
            long count = 0;
            int stepX = Math.Max(1, bitmap.Width / 24);
            int stepY = Math.Max(1, bitmap.Height / 24);
            for (int y = 0; y < bitmap.Height; y += stepY)
            {
                for (int x = 0; x < bitmap.Width; x += stepX)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    sum += (pixel.R + pixel.G + pixel.B) / 3;
                    count++;
                }
            }

            return count == 0 ? 0 : (int)(sum / count);
        }

        public static Color Blend(Color baseColor, Color overlay, float amount)        {
            float t = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)(baseColor.R + (overlay.R - baseColor.R) * t),
                (int)(baseColor.G + (overlay.G - baseColor.G) * t),
                (int)(baseColor.B + (overlay.B - baseColor.B) * t));
        }

        private static Bitmap _grain;

        /// <summary>Subtle speckle so the surface reads as frosted glass.</summary>
        public static void DrawGrain(Graphics graphics, Rectangle rect, int alpha)
        {
            if (_grain == null)
            {
                _grain = CreateGrain(64);
            }

            using (var brush = new TextureBrush(_grain, WrapMode.Tile))
            {
                graphics.FillRectangle(brush, rect);
            }
        }

        private static Bitmap CreateGrain(int size)
        {
            var random = new Random(20261116);
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int value = random.Next(0, 3) == 0 ? 255 : 0;
                    int alpha = random.Next(2, 7);
                    bitmap.SetPixel(x, y, Color.FromArgb(alpha, value, value, value));
                }
            }

            return bitmap;
        }

        public static GraphicsPath RoundedRect(RectangleF rect, float radius)        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Clip-aware text drawing (GDI+ honours Graphics.SetClip).</summary>
        public static void TextClipped(Graphics g, string text, Font font, RectangleF rect, Color color, StringAlignment align, bool ellipsis)
        {
            using (var format = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.NoClip))
            {
                format.LineAlignment = StringAlignment.Center;
                format.Alignment = align;
                format.Trimming = ellipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None;
                using (var brush = new SolidBrush(color))
                {
                    g.DrawString(text, font, brush, rect, format);
                }
            }
        }

        public static void FillRounded(Graphics g, RectangleF rect, float radius, Color fill)
        {
            using (GraphicsPath path = RoundedRect(rect, radius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }
        }

        public static void StrokeRounded(Graphics g, RectangleF rect, float radius, Color color, float width)
        {
            RectangleF inset = RectangleF.Inflate(rect, -width / 2f, -width / 2f);
            using (GraphicsPath path = RoundedRect(inset, radius))
            using (var pen = new Pen(color, width))
            {
                g.DrawPath(pen, path);
            }
        }

        /// <summary>Screens the desktop behind <paramref name="bounds"/> and blurs it.</summary>
        public static Bitmap CaptureBlurred(Rectangle bounds, int radius)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return null;
            }

            var raw = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics graphics = Graphics.FromImage(raw))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            }

            int down = 4;
            int smallWidth = Math.Max(1, bounds.Width / down);
            int smallHeight = Math.Max(1, bounds.Height / down);

            var small = new Bitmap(smallWidth, smallHeight);
            using (Graphics graphics = Graphics.FromImage(small))
            {
                graphics.InterpolationMode = InterpolationMode.Bilinear;
                graphics.DrawImage(raw, new Rectangle(0, 0, smallWidth, smallHeight));
            }

            raw.Dispose();
            BoxBlur(small, radius, 3);

            var blurred = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics graphics = Graphics.FromImage(blurred))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(small, new Rectangle(0, 0, bounds.Width, bounds.Height));
            }

            small.Dispose();
            return blurred;
        }

        public static void BlurInPlace(Bitmap bitmap, int radius, int passes)
        {
            BoxBlur(bitmap, radius, passes);
        }

        private static void BoxBlur(Bitmap bitmap, int radius, int passes)
        {
            if (radius < 1)
            {
                return;
            }

            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            int width = bitmap.Width;
            int height = bitmap.Height;
            int count = width * height;
            var pixels = new int[count];
            Marshal.Copy(data.Scan0, pixels, 0, count);

            for (int pass = 0; pass < passes; pass++)
            {
                BlurHorizontal(pixels, width, height, radius);
                BlurVertical(pixels, width, height, radius);
            }

            Marshal.Copy(pixels, 0, data.Scan0, count);
            bitmap.UnlockBits(data);
        }

        private static void BlurHorizontal(int[] pixels, int width, int height, int radius)
        {
            var line = new int[width];
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * width;
                Array.Copy(pixels, rowStart, line, 0, width);

                for (int x = 0; x < width; x++)
                {
                    int a = 0, r = 0, g = 0, b = 0, n = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sample = Math.Min(width - 1, Math.Max(0, x + k));
                        int px = line[sample];
                        a += (px >> 24) & 0xFF;
                        r += (px >> 16) & 0xFF;
                        g += (px >> 8) & 0xFF;
                        b += px & 0xFF;
                        n++;
                    }

                    pixels[rowStart + x] = ((a / n) << 24) | ((r / n) << 16) | ((g / n) << 8) | (b / n);
                }
            }
        }

        private static void BlurVertical(int[] pixels, int width, int height, int radius)
        {
            var column = new int[height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    column[y] = pixels[y * width + x];
                }

                for (int y = 0; y < height; y++)
                {
                    int a = 0, r = 0, g = 0, b = 0, n = 0;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int sample = Math.Min(height - 1, Math.Max(0, y + k));
                        int px = column[sample];
                        a += (px >> 24) & 0xFF;
                        r += (px >> 16) & 0xFF;
                        g += (px >> 8) & 0xFF;
                        b += px & 0xFF;
                        n++;
                    }

                    pixels[y * width + x] = ((a / n) << 24) | ((r / n) << 16) | ((g / n) << 8) | (b / n);
                }
            }
        }
    }

    /// <summary>Borderless, rounded, blurred-backdrop window.</summary>
    internal class GlassForm : Form
    {
        private const int WmNclbuttondown = 0x00A1;
        private const int Htcaption = 0x0002;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaBorderColor = 34;

        /// <summary>Menus turn this off so their top items stay clickable.</summary>
        public bool EnableWindowDrag { get; set; } = true;

        /// <summary>Height of the caption drag zone; keep it below clickable tabs.</summary>
        public int DragZone { get; set; } = Ui.S(90);

        /// <summary>Borderless windows need manual hit testing to be resizable.</summary>
        public bool Resizable { get; set; }

        private Bitmap _backdrop;
        private Bitmap _desktop;
        private Point _desktopOrigin;

        private float _tintAlpha = 0.55f;

        /// <summary>Adaptive tint opacity so bright backdrops cannot wash out white text.</summary>
        public float TintAlpha
        {
            get { return _tintAlpha; }
        }

        public GlassForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(32, 36, 46);
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(20, 21, 26);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return parameters;
            }
        }

        /// <summary>
        /// Painted corner radius. Kept in sync with the DWM rounding radius
        /// (8 px at 96 dpi) so the 1px glass border hugs the real window edge
        /// instead of being clipped by a smaller system curve.
        /// </summary>
        public int CornerRadius = 8;

        /// <summary>
        /// Real acrylic feel: keep the window out of screen captures and grab
        /// the live background every frame. Off = snapshot mode, which keeps
        /// the dialog visible in user screenshots.
        /// </summary>
        public bool LiveBackdrop = true;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int roundResult = -1;
            try
            {
                int round = 2; // DWMWCP_ROUND
                roundResult = DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref round, sizeof(int));

                // 去掉系统绘制的边框颜色，避免四边深浅不一
                int noBorder = unchecked((int)0xFFFFFFFE);
                DwmSetWindowAttribute(Handle, DwmwaBorderColor, ref noBorder, sizeof(int));
                int dark = 1;
                DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            }
            catch (Exception ex)
            {
                Log.Write("dwm corner attribute failed: " + ex.Message);
            }

            // DWM rounds the window itself on Windows 11; clipping with a
            // Region on top of that produces jagged corners and a border that
            // does not follow the real edge, so only fall back when DWM said no.
            CornerRadius = Math.Max(4, (int)Math.Round(8.0 * Ui.Scale));
            if (roundResult != 0)
            {
                Log.Write("dwm rounding unavailable (" + roundResult + "), using region");
                ApplyRegion();
            }

            // WDA_EXCLUDEFROMCAPTURE: screen grabs (including our own
            // CopyFromScreen) then see straight through this window, which is
            // what makes a live backdrop possible while it is on screen.
            bool allowExclusion = LiveBackdrop &&
                Environment.GetEnvironmentVariable("DESKBOX_TODO_NO_CAPTURE_EXCLUSION") != "1";
            if (allowExclusion)
            {
                try
                {
                    _captureExcluded = SetWindowDisplayAffinity(Handle, 0x00000011);
                }
                catch (Exception ex)
                {
                    Log.Write("capture exclusion failed: " + ex.Message);
                    _captureExcluded = false;
                }

                Log.Write("capture exclusion = " + _captureExcluded);
            }
        }

        private readonly Timer _liveTimer = new Timer { Interval = 500 };
        private readonly Timer _recaptureTimer = new Timer { Interval = 140 };
        private bool _captureExcluded;
        private bool _dragging;
        private int _lastSyncAverage = -1;
        private int _lastLoggedAverage = -1;
        private int _liveFrames;

        private void StartLiveCapture()
        {
            if (!_captureExcluded)
            {
                return;
            }

            _liveTimer.Tick -= LiveTimerOnTick;
            _liveTimer.Tick += LiveTimerOnTick;
            _liveTimer.Start();
        }

        private void StopLiveCapture()
        {
            _liveTimer.Stop();
            _liveTimer.Tick -= LiveTimerOnTick;
        }

        private void LiveTimerOnTick(object sender, EventArgs e)
        {
            if (!Visible || IsDisposed)
            {
                return;
            }

            CaptureLive();
        }

        private void CaptureLive()
        {
            try
            {
                Bitmap fresh = Ui.CaptureBlurred(Bounds, Math.Max(4, Ui.GlassBlurRadius + 1));
                if (fresh == null)
                {
                    return;
                }

                Bitmap previous = _backdrop;
                _backdrop = fresh;
                if (previous != null)
                {
                    previous.Dispose();
                }

                int average = Ui.Average(fresh);
                _tintAlpha = Ui.OpacityOverride >= 0f
                    ? Ui.OpacityOverride
                    : Math.Min(0.34f, Math.Max(0.14f, 0.18f + (average - 70) / 620f));
                ApplyPalette(average);

                if (Math.Abs(average - _lastLoggedAverage) > 12 || _liveFrames % 40 == 0)
                {
                    _lastLoggedAverage = average;
                    Log.Write(string.Format(
                        "live pos={0},{1} avg={2} tint={3:0.00} drag={4}",
                        Left, Top, average, _tintAlpha, _dragging));
                }

                _liveFrames++;

                // Re-tinting the inner TextBoxes every frame would flicker;
                // only do it when the backdrop actually changed character.
                if (Math.Abs(average - _lastSyncAverage) > 8)
                {
                    _lastSyncAverage = average;
                    OnBackdropReady();
                }

                Invalidate();
            }
            catch (Exception ex)
            {
                Log.Write("live capture failed: " + ex.Message);
                StopLiveCapture();
            }
        }

        private const int WmNchittest = 0x0084;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WmNchittest && Resizable && WindowState == FormWindowState.Normal && (int)m.Result == 1)
            {
                int sx = (short)((long)m.LParam & 0xFFFF);
                int sy = (short)(((long)m.LParam >> 16) & 0xFFFF);
                Point client = PointToClient(new Point(sx, sy));
                int border = Ui.S(7);
                bool left = client.X <= border;
                bool right = client.X >= ClientSize.Width - border;
                bool top = client.Y <= border;
                bool bottom = client.Y >= ClientSize.Height - border;

                if (left && top) { m.Result = (IntPtr)13; }
                else if (right && top) { m.Result = (IntPtr)14; }
                else if (left && bottom) { m.Result = (IntPtr)16; }
                else if (right && bottom) { m.Result = (IntPtr)17; }
                else if (left) { m.Result = (IntPtr)10; }
                else if (right) { m.Result = (IntPtr)11; }
                else if (top) { m.Result = (IntPtr)12; }
                else if (bottom) { m.Result = (IntPtr)15; }
            }

            if (_captureExcluded)
            {
                if (m.Msg == 0x0231) // WM_ENTERSIZEMOVE
                {
                    _dragging = true;
                    _liveTimer.Interval = 33;
                }
                else if (m.Msg == 0x0232) // WM_EXITSIZEMOVE
                {
                    _dragging = false;
                    _liveTimer.Interval = 200;
                    CaptureLive();
                }
            }

            if (m.Msg == 0x0010) // WM_CLOSE
            {
                StopLiveCapture();
            }
        }

        private void ApplyRegion()
        {
            try
            {
                using (GraphicsPath path = Ui.RoundedRect(new RectangleF(0, 0, Width, Height), CornerRadius))
                {
                    Region = new Region(path);
                }
            }
            catch
            {
                // Ignore; square corners are still usable.
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_backdrop == null)
            {
                // Late fallback only; the normal path captures before showing.
                BackdropRefresh();
            }

            _recaptureTimer.Stop();
            _recaptureTimer.Tick += (sender, args) =>
            {
                _recaptureTimer.Stop();
                if (!IsHandleCreated || WindowState == FormWindowState.Minimized)
                {
                    return;
                }

                if (_captureExcluded)
                {
                    CaptureLive();
                }
                else
                {
                    RecropBackdrop();
                }

                OnBackdropReady();
                Invalidate();
            };

            StartLiveCapture();
            if (_captureExcluded)
            {
                CaptureLive();
            }
        }

        /// <summary>Average colour of the blurred backdrop under a client-area rectangle.</summary>
        /// <summary>Colour this form actually paints at the given bounds (backdrop + tint).</summary>
        public Color CompositeColor(Rectangle localRect)
        {
            Color raw = SampleBackdrop(localRect);
            return Ui.Blend(raw, Color.FromArgb(38, 42, 55), Math.Min(0.80f, TintAlpha + 0.06f));
        }

        public Color SampleBackdrop(Rectangle localRect)
        {
            if (_backdrop == null || localRect.Width <= 0 || localRect.Height <= 0)
            {
                return Color.FromArgb(26, 28, 34);
            }

            int x0 = Math.Max(0, localRect.X);
            int y0 = Math.Max(0, localRect.Y);
            int x1 = Math.Min(_backdrop.Width, localRect.Right);
            int y1 = Math.Min(_backdrop.Height, localRect.Bottom);
            if (x1 <= x0 || y1 <= y0)
            {
                return Color.FromArgb(26, 28, 34);
            }

            int stepX = Math.Max(1, (x1 - x0) / 12);
            int stepY = Math.Max(1, (y1 - y0) / 12);
            long r = 0, g = 0, b = 0, n = 0;
            for (int y = y0; y < y1; y += stepY)
            {
                for (int x = x0; x < x1; x += stepX)
                {
                    Color pixel = _backdrop.GetPixel(x, y);
                    r += pixel.R;
                    g += pixel.G;
                    b += pixel.B;
                    n++;
                }
            }

            if (n == 0)
            {
                return Color.FromArgb(26, 28, 34);
            }

            return Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));
        }

        protected virtual void OnBackdropReady()
        {
        }

        /// <summary>Flips the text palette when the glass under it turns light.</summary>
        private void ApplyPalette(int backdropAverage)
        {
            int glass = (int)(backdropAverage * (1f - _tintAlpha) + 38 * _tintAlpha) + 26;
            bool wantDarkText = glass > 95;
            if (wantDarkText == Ui.DarkText)
            {
                return;
            }

            Ui.DarkText = wantDarkText;
            Log.Write("palette darkText=" + wantDarkText + " glass=" + glass);
            OnPaletteChanged();
            Invalidate(true);
        }

        protected virtual void OnPaletteChanged()
        {
        }

        /// <summary>
        /// Captures the whole virtual screen (blurred at 1/4 scale) while the
        /// window is still hidden. Every later move just re-crops that plate,
        /// so the glass follows the desktop like real acrylic instead of
        /// dragging a frozen screenshot around.
        /// </summary>
        public void PrepareBackdrop()
        {
            try
            {
                Rectangle screen = SystemInformation.VirtualScreen;
                using (var full = new Bitmap(screen.Width, screen.Height))
                {
                    using (Graphics graphics = Graphics.FromImage(full))
                    {
                        graphics.CopyFromScreen(screen.Location, Point.Empty, screen.Size, CopyPixelOperation.SourceCopy);
                    }

                    int scale = Math.Max(2, Ui.GlassDownscale);
                    var small = new Bitmap(
                        Math.Max(1, screen.Width / scale),
                        Math.Max(1, screen.Height / scale));
                    using (Graphics graphics = Graphics.FromImage(small))
                    {
                        graphics.InterpolationMode = InterpolationMode.Bilinear;
                        graphics.DrawImage(full, new Rectangle(0, 0, small.Width, small.Height));
                    }

                    if (_desktop != null)
                    {
                        _desktop.Dispose();
                    }

                    _desktop = small;
                }

                Ui.BlurInPlace(_desktop, Math.Max(4, Ui.GlassBlurRadius), Ui.GlassBlurPasses);
                _desktopOrigin = screen.Location;
                RecropBackdrop();
            }
            catch (Exception ex)
            {
                Log.Write("desktop snapshot failed: " + ex.Message);
                _desktop = null;
                _backdrop = null;
            }

            OnBackdropReady();
            Invalidate();
        }

        /// <summary>Re-crops the blurred plate for the window's current position.</summary>
        protected void RecropBackdrop()
        {
            if (_desktop == null)
            {
                return;
            }

            int scale = Math.Max(2, Ui.GlassDownscale);
            int sourceWidth = Math.Max(1, (Width + scale - 1) / scale + 1);
            int sourceHeight = Math.Max(1, (Height + scale - 1) / scale + 1);
            int sourceX = (Left - _desktopOrigin.X) / scale;
            int sourceY = (Top - _desktopOrigin.Y) / scale;

            sourceX = Math.Max(0, Math.Min(sourceX, _desktop.Width - sourceWidth));
            sourceY = Math.Max(0, Math.Min(sourceY, _desktop.Height - sourceHeight));

            var cropped = new Bitmap(Width, Height);
            using (Graphics graphics = Graphics.FromImage(cropped))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(
                    _desktop,
                    new Rectangle(0, 0, Width, Height),
                    new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight),
                    GraphicsUnit.Pixel);
            }

            if (_backdrop != null)
            {
                _backdrop.Dispose();
            }

            _backdrop = cropped;
            int backdropAverage = Ui.Average(cropped);
            _tintAlpha = Ui.OpacityOverride >= 0f
                ? Ui.OpacityOverride
                : Math.Min(0.34f, Math.Max(0.14f, 0.18f + (backdropAverage - 70) / 620f));
            ApplyPalette(backdropAverage);
            Log.Write(string.Format(
                "recrop pos={0},{1} src={2},{3} avg={4} tint={5:0.00}",
                Left,
                Top,
                sourceX,
                sourceY,
                backdropAverage,
                _tintAlpha));
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);

            if (_captureExcluded)
            {
                // Live path: grab the real background at the new position.
                CaptureLive();
                return;
            }

            if (_desktop == null)
            {
                return;
            }

            RecropBackdrop();
            OnBackdropReady();
            Invalidate();
        }

        /// <summary>Draws a rounded glass tile cut from the blurred backdrop.</summary>
        /// <summary>
        /// Marks a tile on the glass: same background, only a hairline edge and
        /// an optional faint wash so rows read as separate without changing the glass.
        /// </summary>
        protected void DrawGlassCard(Graphics graphics, Rectangle rect, int radius, int lighten)
        {
            if (lighten > 0)
            {
                Ui.FillRounded(
                    graphics,
                    new RectangleF(rect.X, rect.Y, rect.Width, rect.Height),
                    radius,
                    Color.FromArgb(lighten, 255, 255, 255));
            }

            Ui.StrokeRounded(
                graphics,
                new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1, rect.Height - 1),
                radius,
                Color.FromArgb(lighten > 0 ? 40 : 22, 255, 255, 255),
                1f);
        }
        public void BackdropRefresh()
        {
            PrepareBackdrop();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Fully custom painted below.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var full = new Rectangle(0, 0, Width, Height);

            using (GraphicsPath clip = Ui.RoundedRect(new RectangleF(0, 0, Width, Height), CornerRadius))
            {
                graphics.SetClip(clip);

                if (_backdrop != null)
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(_backdrop, full);
                }
                else
                {
                    using (var brush = new SolidBrush(Color.FromArgb(24, 26, 32)))
                    {
                        graphics.FillRectangle(brush, full);
                    }
                }

                using (var tint = new LinearGradientBrush(
                    full,
                    Color.FromArgb((int)(255 * _tintAlpha), 34, 38, 50),
                    Color.FromArgb((int)(255 * Math.Min(0.80f, _tintAlpha + 0.12f)), 42, 47, 60),
                    LinearGradientMode.Vertical))
                {
                    graphics.FillRectangle(tint, full);
                }

                // Frost: a faint white wash plus grain is what makes the plate
                // read as glass rather than a black rectangle on a dark desktop.
                using (var frost = new SolidBrush(Color.FromArgb(46, 255, 255, 255)))
                {
                    graphics.FillRectangle(frost, full);
                }

                // No grain: noise read as dirty rather than frosted.

                graphics.ResetClip();
            }

            using (GraphicsPath border = Ui.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), CornerRadius))
            using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
            {
                graphics.DrawPath(pen, border);
            }
        }

        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);


        private void ApplyMonitorDpi()
        {
            try
            {
                uint dpi = GetDpiForWindow(Handle);
                if (dpi == 0)
                {
                    return;
                }

                int scale = Math.Max(1, (int)Math.Round(dpi / 96.0));
                if (scale == Ui.Scale)
                {
                    return;
                }

                int previous = Ui.Scale;
                Ui.SetScale(scale);
                OnScaleChanged(previous, scale);
                Invalidate(true);
            }
            catch
            {
            }
        }

        /// <summary>Called when the window lands on a monitor with another DPI.</summary>
        protected virtual void OnScaleChanged(int previousScale, int newScale)
        {
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control is Label)
            {
                e.Control.MouseDown += (sender, args) =>
                {
                    if (args.Button == MouseButtons.Left && EnableWindowDrag)
                    {
                        BeginCaptionDrag();
                    }
                };
            }
        }

        /// <summary>Starts a native window drag (used by the header labels too).</summary>
        public void BeginCaptionDrag()
        {
            ReleaseCapture();
            SendMessage(Handle, WmNclbuttondown, Htcaption, 0);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (EnableWindowDrag && e.Button == MouseButtons.Left && e.Y <= DragZone)
            {
                BeginCaptionDrag();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopLiveCapture();
                _liveTimer.Dispose();
            }

            if (disposing && _backdrop != null)
            {
                _backdrop.Dispose();
                _backdrop = null;
            }

            base.Dispose(disposing);
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    }

    /// <summary>Rounded input surface that hosts a borderless TextBox.</summary>
    /// <summary>TextBox that lets the glass behind it show through.</summary>
    internal sealed class GlassTextBox : TextBox
    {
        private const int WmCtlColorEdit = 0x0133;
        private const int WmCtlColorStatic = 0x0138;

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr SetBkMode(IntPtr hdc, int mode);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int index);

        private const int WmEraseBkgnd = 0x0014;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmEraseBkgnd)
            {
                // Let the parent-painted glass stay underneath our text.
                m.Result = (IntPtr)1;
                return;
            }

            if (m.Msg == WmCtlColorEdit || m.Msg == WmCtlColorStatic)
            {
                // 1 = TRANSPARENT, NULL_BRUSH (0) = let the parent paint through.
                SetBkMode(m.LParam, 1);
                m.Result = GetStockObject(0);
                return;
            }

            base.WndProc(ref m);
        }
    }

    internal sealed class GlassField : Panel
    {
        public TextBox Box { get; private set; }
        public string Placeholder
        {
            get { return _placeholderLabel == null ? "" : _placeholderLabel.Text; }
            set
            {
                SetCueBanner(value);
                if (_placeholderLabel != null)
                {
                    _placeholderLabel.Text = "";
                    UpdatePlaceholder();
                }
            }
        }

        private Label _placeholderLabel;
        private readonly bool _multiline;

        public GlassField(bool multiline, string text)
        {
            _multiline = multiline;
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            Box = new GlassTextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Ui.FieldFill,
                ForeColor = Ui.Text,
                Font = Ui.Font(9.5f, FontStyle.Regular),
                Multiline = multiline,
                Text = text ?? "",
                ScrollBars = ScrollBars.None,
                AcceptsReturn = multiline
            };

            _placeholderLabel = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Box.Font,
                ForeColor = Color.FromArgb(110, 255, 255, 255),
                BackColor = Color.Transparent,
                Visible = false,
                Cursor = Cursors.IBeam
            };


            Box.TextChanged += (sender, args) =>
            {
                UpdatePlaceholder();
                Invalidate();
            };
            Click += (sender, args) => Box.Focus();
            MouseDown += (sender, args) => Box.Focus();
            foreach (Control child in Controls)
            {
                child.Click += (sender, args) => Box.Focus();
            }
            Controls.Add(_placeholderLabel);
            Controls.Add(Box);
            _placeholderLabel.BringToFront();
        }

        private Color _lastGlass = Color.Empty;

        /// <summary>
        /// Pulls the glass colour from the owning GlassForm and tints the inner
        /// TextBox with it, so fields never fall back to the flat dark default.
        /// </summary>
        public void AutoSyncGlass()
        {
            var form = Parent as GlassForm;
            if (form == null)
            {
                return;
            }

            Color tinted = Ui.Blend(form.SampleBackdrop(Bounds), Color.FromArgb(30, 34, 44), form.TintAlpha);
            if (tinted.ToArgb() == _lastGlass.ToArgb())
            {
                return;
            }

            _lastGlass = tinted;
            SyncGlassColor(tinted);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private const int EmSetCueBanner = 0x1501;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!string.IsNullOrEmpty(Placeholder))
            {
                SetCueBanner(Placeholder);
            }
        }

        private void SetCueBanner(string text)
        {
            if (!Box.IsHandleCreated || _multiline)
            {
                return;
            }

            SendMessage(Box.Handle, EmSetCueBanner, new IntPtr(1), text ?? "");
        }

        private void UpdatePlaceholder()
        {
            if (_placeholderLabel == null)
            {
                return;
            }

            bool show = string.IsNullOrEmpty(Box.Text) && !string.IsNullOrEmpty(_placeholderLabel.Text);
            _placeholderLabel.Visible = show;
            if (show)
            {
                _placeholderLabel.Bounds = new Rectangle(
                    Box.Left + Ui.S(2),
                    Box.Top,
                    Math.Max(0, Box.Width - Ui.S(4)),
                    Box.Height);
                _placeholderLabel.BackColor = Color.Transparent;
                _placeholderLabel.BringToFront();
            }
        }

        /// <summary>Re-tints the inner TextBox so the field reads as glass over the backdrop.</summary>
        private Color _fillColor = Color.FromArgb(34, 255, 255, 255);
        private Color PlaceholderColor = Color.FromArgb(150, 156, 168);

        public void SyncGlassColor(Color glassColor)
        {
            // Fields are a slightly lighter chip of the same glass, never a
            // flat dark box; the text colour follows the chip brightness.
            Color blended = Ui.Blend(glassColor, Color.White, 0.22f);
            _fillColor = blended;
            Box.BackColor = blended;
            int luminance = (blended.R + blended.G + blended.B) / 3;
            bool darkText = luminance > 140;
            Box.ForeColor = darkText ? Color.FromArgb(24, 26, 32) : Color.FromArgb(242, 244, 248);
            PlaceholderColor = darkText ? Color.FromArgb(70, 76, 88) : Color.FromArgb(206, 212, 224);
            if (_placeholderLabel != null)
            {
                _placeholderLabel.BackColor = Color.Transparent;
                _placeholderLabel.ForeColor = darkText
                    ? Color.FromArgb(80, 86, 98)
                    : Color.FromArgb(190, 196, 206);
            }

            Invalidate();
            BackColor = Color.Transparent;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int padX = Ui.S(12);
            int padY = Ui.S(_multiline ? 10 : 10);
            Box.SetBounds(padX, padY, Math.Max(0, Width - padX * 2), Math.Max(0, Height - padY * 2));
            UpdatePlaceholder();
            AutoSyncGlass();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0, 0, Width, Height);

            // No fill: the blurred glass behind shows through the whole field.
            // Single-line fields get a hairline underline only.
            if (_multiline)
            {
                Ui.FillRounded(graphics, rect, Ui.S(10), Color.FromArgb(18, 255, 255, 255));
                Ui.StrokeRounded(graphics, rect, Ui.S(10), Enabled ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(16, 255, 255, 255), 1f);
            }
            else
            {
                using (var pen = new Pen(Enabled ? Color.FromArgb(54, 255, 255, 255) : Color.FromArgb(24, 255, 255, 255), 1f))
                {
                    graphics.DrawLine(pen, Ui.S(1), Height - Ui.S(2), Width - Ui.S(1), Height - Ui.S(2));
                }
            }

            AutoSyncGlass();

            if (!string.IsNullOrEmpty(Placeholder) && string.IsNullOrEmpty(Box.Text))
            {
                TextRenderer.DrawText(
                    graphics,
                    Placeholder,
                    Box.Font,
                    new Rectangle(Box.Left, Box.Top, Math.Max(0, Box.Width), Math.Max(0, Box.Height)),
                    PlaceholderColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            if (false)
            {
                TextRenderer.DrawText(
                    graphics,
                    Placeholder,
                    Box.Font,
                    new Rectangle(Ui.S(12), 0, Width - Ui.S(24), Height),
                    Color.FromArgb(92, 255, 255, 255),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            Ui.StrokeRounded(graphics, rect, Ui.S(10), Enabled ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(18, 255, 255, 255), 1f);
        }
    }

    internal sealed class GlassButton : Control
    {
        private bool _hover;

        public bool Primary { get; set; }
        public bool Ghost { get; set; }

        public GlassButton(string text, bool primary)
        {
            Text = text;
            Primary = primary;
            Font = Ui.Font(9.5f, primary ? FontStyle.Bold : FontStyle.Regular);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float radius = Ui.S(9);

            if (Primary)
            {
                Color fill = _hover ? Ui.AccentHover : Ui.Accent;
                Ui.FillRounded(graphics, rect, radius, fill);
                TextRenderer.DrawText(
                    graphics,
                    Text,
                    Font,
                    Rectangle.Round(rect),
                    Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else if (Ghost)
            {
                if (_hover)
                {
                    Ui.FillRounded(graphics, rect, radius, Color.FromArgb(54, 255, 255, 255));
                }

                TextRenderer.DrawText(
                    graphics,
                    Text,
                    Font,
                    Rectangle.Round(rect),
                    _hover ? Ui.Text : Ui.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else
            {
                Color fill = _hover ? Color.FromArgb(58, 255, 255, 255) : Color.FromArgb(18, 255, 255, 255);
                Ui.FillRounded(graphics, rect, radius, fill);
                Ui.StrokeRounded(graphics, rect, radius, _hover ? Color.FromArgb(96, 255, 255, 255) : Color.FromArgb(46, 255, 255, 255), 1f);
                TextRenderer.DrawText(
                    graphics,
                    Text,
                    Font,
                    Rectangle.Round(rect),
                    Ui.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>Minimal checkbox: small rounded square plus a label.</summary>
    internal sealed class GlassToggle : Control
    {
        private bool _checked;
        private bool _hover;

        public event EventHandler CheckedChanged;

        public GlassToggle(string text, bool initial)
        {
            Text = text;
            _checked = initial;
            Font = Ui.Font(8.5f, FontStyle.Regular);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value)
                {
                    return;
                }

                _checked = value;
                Invalidate();
                EventHandler handler = CheckedChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int size = Ui.S(16);
            int top = (Height - size) / 2;
            var box = new RectangleF(Ui.S(1), top, size, size);

            if (_checked)
            {
                Ui.FillRounded(graphics, box, Ui.S(5), Ui.Accent);
                using (var pen = new Pen(Color.White, Math.Max(2, Ui.S(2))))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    graphics.DrawLines(pen, new[]
                    {
                        new PointF(box.X + size * 0.26f, box.Y + size * 0.52f),
                        new PointF(box.X + size * 0.44f, box.Y + size * 0.70f),
                        new PointF(box.X + size * 0.74f, box.Y + size * 0.32f)
                    });
                }
            }
            else
            {
                Ui.FillRounded(graphics, box, Ui.S(5), _hover ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(16, 255, 255, 255));
                Ui.StrokeRounded(graphics, box, Ui.S(5), Color.FromArgb(70, 255, 255, 255), 1f);
            }

            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                new Rectangle(size + Ui.S(8), 0, Width - size - Ui.S(8), Height),
                _hover ? Ui.Text : Ui.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}