using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>Row of DeskBox colour markers; the first swatch means "no colour".</summary>
    internal sealed class GlassColorPicker : Control
    {
        public static readonly string[] Markers =
        {
            "", "red", "orange", "yellow", "green", "blue", "purple", "teal", "pink"
        };

        private static readonly Color[] Swatches =
        {
            Color.Empty,
            Color.FromArgb(227, 77, 77),
            Color.FromArgb(240, 138, 60),
            Color.FromArgb(242, 201, 76),
            Color.FromArgb(76, 175, 109),
            Color.FromArgb(77, 143, 227),
            Color.FromArgb(155, 107, 232),
            Color.FromArgb(45, 183, 163),
            Color.FromArgb(230, 106, 162)
        };

        private int _selected;
        private int _hover = -1;

        public event EventHandler SelectedChanged;

        public GlassColorPicker(string marker)
        {
            SetStyle(
                ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint,
                true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            SelectedMarker = marker;
        }

        public string SelectedMarker
        {
            get { return Markers[_selected]; }
            set
            {
                int index = Array.FindIndex(Markers, m => string.Equals(m, value ?? "", StringComparison.OrdinalIgnoreCase));
                _selected = index < 0 ? 0 : index;
                Invalidate();
            }
        }

        private int DotSize
        {
            get { return Ui.S(20); }
        }

        private int Spacing
        {
            get { return Ui.S(30); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitTest(e.X);
            if (index != _hover)
            {
                _hover = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int index = HitTest(e.X);
            if (index < 0 || index == _selected)
            {
                return;
            }

            _selected = index;
            Invalidate();
            EventHandler handler = SelectedChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private int HitTest(int x)
        {
            int start = Ui.S(8);
            for (int index = 0; index < Markers.Length; index++)
            {
                int left = start + index * Spacing;
                if (x >= left - Ui.S(4) && x <= left + DotSize + Ui.S(4))
                {
                    return index;
                }
            }

            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int size = DotSize;
            int start = Ui.S(8);
            int top = (Height - size) / 2;

            for (int index = 0; index < Markers.Length; index++)
            {
                int left = start + index * Spacing;
                var rect = new Rectangle(left, top, size, size);
                bool isSelected = index == _selected;
                bool isHover = index == _hover;

                if (index == 0)
                {
                    // "No colour": hollow circle with a slash.
                    using (var pen = new Pen(Color.FromArgb(isSelected ? 240 : 150, 255, 255, 255), Math.Max(1.4f, 1.4f)))
                    {
                        graphics.DrawEllipse(pen, rect);
                        graphics.DrawLine(pen, rect.Left + 5, rect.Bottom - 5, rect.Right - 5, rect.Top + 5);
                    }
                }
                else
                {
                    using (var brush = new SolidBrush(Swatches[index]))
                    {
                        graphics.FillEllipse(brush, rect);
                    }

                    if (isHover)
                    {
                        using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 2f))
                        {
                            graphics.DrawEllipse(pen, rect);
                        }
                    }
                }

                if (isSelected)
                {
                    var ring = Rectangle.Inflate(rect, Ui.S(4), Ui.S(4));
                    using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(1.6f, 1.6f)))
                    {
                        graphics.DrawEllipse(pen, ring);
                    }
                }
            }
        }
    }
}
