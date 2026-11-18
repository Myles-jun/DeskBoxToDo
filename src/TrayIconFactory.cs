using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DeskBoxToDo
{
    /// <summary>
    /// Minimal tray icon: one dark rounded tile, one white check, one hairline
    /// outline. No gradients, no decoration — legible on light and dark taskbars.
    /// </summary>
    internal static class TrayIconFactory
    {
        public static Icon Create(int size)
        {
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.Clear(Color.Transparent);

                    float unit = size / 32f;
                    float pad = 2.5f * unit;
                    var tile = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
                    float radius = 7f * unit;

                    using (GraphicsPath path = Ui.RoundedRect(tile, radius))
                    using (var brush = new SolidBrush(Color.FromArgb(255, 32, 35, 41)))
                    {
                        graphics.FillPath(brush, path);
                    }

                    using (GraphicsPath path = Ui.RoundedRect(tile, radius))
                    using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), Math.Max(1f, unit)))
                    {
                        graphics.DrawPath(pen, path);
                    }

                    using (var pen = new Pen(Color.FromArgb(255, 255, 255, 255), Math.Max(2f, 2.6f * unit)))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        pen.LineJoin = LineJoin.Round;
                        graphics.DrawLines(pen, new[]
                        {
                            new PointF(9.5f * unit, 16.5f * unit),
                            new PointF(14f * unit, 21f * unit),
                            new PointF(22.5f * unit, 12f * unit)
                        });
                    }
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (var temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
