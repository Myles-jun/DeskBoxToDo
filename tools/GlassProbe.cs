using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GlassProbe
{
    internal static class Native
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("user32.dll")]
        public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        [StructLayout(LayoutKind.Sequential)]
        public struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }
    }

    internal sealed class ProbeForm : Form
    {
        private readonly int _engine;

        public ProbeForm(int engine)
        {
            _engine = engine;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(700, 300, 420, 560);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
            ShowInTaskbar = false;
            TopMost = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            int corner = 2; // DWMWCP_ROUND
            Native.DwmSetWindowAttribute(Handle, 33, ref corner, sizeof(int));

            var margins = new Native.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            Console.WriteLine("extend=" + Native.DwmExtendFrameIntoClientArea(Handle, ref margins));

            if (_engine == 0)
            {
                // Win11 system backdrop: 3 = Acrylic
                int backdrop = 3;
                int hr = Native.DwmSetWindowAttribute(Handle, 38, ref backdrop, sizeof(int));
                Console.WriteLine("backdrop hr=" + hr);
            }
            else
            {
                var accent = new Native.AccentPolicy
                {
                    AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND
                    AccentFlags = 2,
                    GradientColor = unchecked((int)0x99101010),
                    AnimationId = 0
                };
                int size = Marshal.SizeOf(accent);
                IntPtr ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new Native.WindowCompositionAttributeData
                {
                    Attribute = 19, // WCA_ACCENT_POLICY
                    Data = ptr,
                    SizeOfData = size
                };
                int hr = Native.SetWindowCompositionAttribute(Handle, ref data);
                Marshal.FreeHGlobal(ptr);
                Console.WriteLine("accent hr=" + hr);
            }

            Console.WriteLine("engine=" + _engine + " handle=" + Handle);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Deliberately no fill: the DWM acrylic backdrop shows through.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Only a hairline border and a light wash; the blur comes from the system.
            using (var wash = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
            {
                g.FillRectangle(wash, new Rectangle(0, 0, Width, Height));
            }

            using (var pen = new Pen(Color.FromArgb(110, 255, 255, 255), 1f))
            {
                g.DrawRectangle(pen, 0.5f, 0.5f, Width - 1, Height - 1);
            }

            using (var font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
            {
                g.DrawString("引擎 " + (_engine == 0 ? "DWMWA_SYSTEMBACKDROP_TYPE=3 (Acrylic)" : "ACCENT_ENABLE_ACRYLICBLURBEHIND"), font, brush, 18, 18);
            }

            using (var font = new Font("Microsoft YaHei UI", 9.5f))
            using (var brush = new SolidBrush(Color.FromArgb(200, 240, 244, 255)))
            {
                g.DrawString("如果这块看起来就是桌面虚化，说明系统级毛玻璃可用；\r\n不需要截屏、不会有黑边、圆角由 DWM 提供。", font, brush, 18, 56);
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            int engine = args.Length > 0 && args[0] == "accent" ? 1 : 0;
            var form = new ProbeForm(engine);
            var timer = new Timer { Interval = 1200 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Console.WriteLine("rect=" + form.Bounds.X + "," + form.Bounds.Y + " " + form.Width + "x" + form.Height);
            };
            timer.Start();
            Application.Run(form);
        }
    }
}
