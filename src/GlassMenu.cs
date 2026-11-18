using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>
    /// Glass replacement for the native tray context menu: same frosted plate
    /// as the dialogs, rounded, with hover highlight and check marks.
    /// </summary>
    internal sealed class GlassMenu : GlassForm
    {
        private sealed class Entry
        {
            public string Text = "";
            public Action Action;
            public bool Checked;
            public bool Separator;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private int _hoverIndex = -1;
        private int _itemHeight;
        private int _separatorHeight;
        private int _padY;
        private System.Windows.Forms.Timer _watch;
        private DateTime _shownAt;

        public GlassMenu()
        {
            Ui.InitScale();
            CornerRadius = Ui.S(24);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Text = "menu";

            LiveBackdrop = false;   // periodic re-capture made the menu flicker
            EnableWindowDrag = false;
            _itemHeight = Ui.S(34);
            _separatorHeight = Ui.S(9);
            _padY = Ui.S(6);
        }

        public void AddItem(string text, Action action, bool isChecked = false)
        {
            _entries.Add(new Entry { Text = text, Action = action, Checked = isChecked });
        }

        public void AddSeparator()
        {
            _entries.Add(new Entry { Separator = true });
        }

        private int ComputeWidth()
        {
            int width = Ui.S(120);
            using (Graphics graphics = CreateGraphics())
            {
                foreach (Entry entry in _entries)
                {
                    if (entry.Separator)
                    {
                        continue;
                    }

                    Size size = TextRenderer.MeasureText(graphics, entry.Text, Ui.Font(9f, FontStyle.Regular));
                    width = Math.Max(width, size.Width + Ui.S(78));
                }
            }

            return Math.Min(Ui.S(320), width);
        }

        private int ComputeHeight()
        {
            int height = _padY * 2;
            foreach (Entry entry in _entries)
            {
                height += entry.Separator ? _separatorHeight : _itemHeight;
            }

            return height;
        }

        public void ShowAt(Point screenPoint)
        {
            ClientSize = new Size(ComputeWidth(), ComputeHeight());

            Rectangle working = Screen.FromPoint(screenPoint).WorkingArea;
            int x = screenPoint.X;
            int y = screenPoint.Y - Height;
            if (y < working.Top + Ui.S(4))
            {
                y = screenPoint.Y + Ui.S(4);
            }

            x = Math.Max(working.Left + Ui.S(4), Math.Min(x, working.Right - Width - Ui.S(4)));
            y = Math.Max(working.Top + Ui.S(4), Math.Min(y, working.Bottom - Height - Ui.S(4)));
            Location = new Point(x, y);

            PrepareBackdrop();
            Show();
            Activate();
            SetForegroundWindow(Handle);
            _shownAt = DateTime.UtcNow;
            Application.DoEvents();

            _watch = new System.Windows.Forms.Timer { Interval = 200 };
            _watch.Tick += (sender, args) =>
            {
                if (!Visible)
                {
                    return;
                }

                if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 200)
                {
                    return;
                }

                if (GetForegroundWindow() != Handle)
                {
                    _watch.Stop();
                    Close();
                }
            };
            _watch.Start();
        }

        private void StartClose()
        {
            if (_watch != null)
            {
                _watch.Stop();
            }

            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed)
                {
                    Close();
                }
            }));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitTest(e.Y);
            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverIndex = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            int index = HitTest(e.Y);
            if (index < 0 || index >= _entries.Count)
            {
                return;
            }

            Entry entry = _entries[index];
            if (entry.Separator || entry.Action == null)
            {
                return;
            }

            Action action = entry.Action;
            SynchronizationContext context = SynchronizationContext.Current;
            Close();
            if (context != null)
            {
                context.Post(state => action(), null);
            }
        }

        private int HitTest(int y)
        {
            int top = _padY;
            for (int index = 0; index < _entries.Count; index++)
            {
                Entry entry = _entries[index];
                int height = entry.Separator ? _separatorHeight : _itemHeight;
                if (y >= top && y < top + height)
                {
                    return entry.Separator ? -1 : index;
                }

                top += height;
            }

            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int top = _padY;
            for (int index = 0; index < _entries.Count; index++)
            {
                Entry entry = _entries[index];
                int height = entry.Separator ? _separatorHeight : _itemHeight;

                if (entry.Separator)
                {
                    using (var pen = new Pen(Color.FromArgb(30, 255, 255, 255), 1f))
                    {
                        int lineY = top + height / 2;
                        graphics.DrawLine(pen, Ui.S(14), lineY, Width - Ui.S(14), lineY);
                    }

                    top += height;
                    continue;
                }

                var row = new Rectangle(Ui.S(6), top + Ui.S(2), Width - Ui.S(12), height - Ui.S(4));
                if (index == _hoverIndex)
                {
                    Ui.FillRounded(graphics, row, Ui.S(8), Color.FromArgb(28, 255, 255, 255));
                }

                if (entry.Checked)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(255, 86, 142, 247)))
                    {
                        float size = Ui.S(5);
                        graphics.FillEllipse(brush, row.X + Ui.S(12), row.Y + row.Height / 2f - size / 2, size, size);
                    }
                }

                TextRenderer.DrawText(
                    graphics,
                    entry.Text,
                    Ui.Font(9f, FontStyle.Regular),
                    new Rectangle(row.X + Ui.S(24), row.Y, row.Width - Ui.S(32), row.Height),
                    Ui.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                top += height;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _watch != null)
            {
                _watch.Stop();
                _watch.Dispose();
                _watch = null;
            }

            base.Dispose(disposing);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
