using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>Glass hour/minute picker (DeskBox style, two columns).</summary>
    internal sealed class TimePickerForm : GlassForm
    {
        private readonly Label _title;
        private readonly Label _hint;
        private readonly GlassButton _ok;
        private readonly GlassButton _cancel;

        private int _hour;
        private int _minute;
        private int _hourHover = -1;
        private int _minuteHover = -1;

        private int _rowHeight;
        private int _listTop;
        private int _leftColumn;
        private int _rightColumn;
        private int _columnWidth;

        public int SelectedHour
        {
            get { return _hour; }
        }

        public int SelectedMinute
        {
            get { return _minute; }
        }

        public TimePickerForm(AppConfig config, int hour, int minute)
        {
            _hour = Math.Max(0, Math.Min(23, hour));
            _minute = Math.Max(0, Math.Min(59, (minute / 5) * 5));

            Ui.InitScale();
            CornerRadius = Ui.S(32);
            LiveBackdrop = config.LiveBackdrop;
            DragZone = Ui.S(44);

            Text = "选择时间";
            ClientSize = new Size(Ui.S(340), Ui.S(780));
            Font = Ui.Font(9f, FontStyle.Regular);

            _title = new Label
            {
                Text = "选择时间",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.Text,
                Font = Ui.Font(12f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _hint = new Label
            {
                Text = "小时 / 分钟",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.4f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _cancel = new GlassButton("取消", false);
            _cancel.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            _ok = new GlassButton("确定", true);
            _ok.Click += (sender, args) => { DialogResult = DialogResult.OK; Close(); };

            Controls.AddRange(new Control[] { _title, _hint, _cancel, _ok });

            _rowHeight = Ui.S(26);
            _listTop = Ui.S(64);
            _leftColumn = Ui.S(16);
            _columnWidth = Ui.S(70);
            _rightColumn = Ui.S(16) + _columnWidth + Ui.S(12);

            _title.SetBounds(Ui.S(16), Ui.S(12), Ui.S(240), Ui.S(28));
            _hint.SetBounds(Ui.S(16), Ui.S(40), Ui.S(240), Ui.S(16));
            _cancel.SetBounds(ClientSize.Width - Ui.S(186), ClientSize.Height - Ui.S(46), Ui.S(80), Ui.S(34));
            _ok.SetBounds(ClientSize.Width - Ui.S(98), ClientSize.Height - Ui.S(46), Ui.S(82), Ui.S(34));

            PlaceCenter();
            PrepareBackdrop();
        }

        private void PlaceCenter()
        {
            Rectangle working = Screen.FromPoint(Cursor.Position).WorkingArea;
            int x = Cursor.Position.X - Width / 2;
            int y = Cursor.Position.Y - Height / 2;
            x = Math.Max(working.Left + 8, Math.Min(x, working.Right - Width - 8));
            y = Math.Max(working.Top + 8, Math.Min(y, working.Bottom - Height - 8));
            Location = new Point(x, y);
        }

        private int ColumnAt(int x)
        {
            if (x >= _leftColumn && x <= _leftColumn + _columnWidth)
            {
                return 0;
            }

            if (x >= _rightColumn && x <= _rightColumn + _columnWidth)
            {
                return 1;
            }

            return -1;
        }

        private int RowAt(int y)
        {
            int maxRows = 60;
            if (y < _listTop)
            {
                return -1;
            }

            int row = (y - _listTop) / _rowHeight;
            return row >= 0 && row < maxRows ? row : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int column = ColumnAt(e.X);
            int row = RowAt(e.Y);
            int hourHover = column == 0 && row >= 0 && row < 24 ? row : -1;
            int minuteHover = column == 1 && row >= 0 && row < 12 ? row : -1;
            Cursor = hourHover >= 0 || minuteHover >= 0 ? Cursors.Hand : Cursors.Default;
            if (hourHover != _hourHover || minuteHover != _minuteHover)
            {
                _hourHover = hourHover;
                _minuteHover = minuteHover;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hourHover = -1;
            _minuteHover = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            int column = ColumnAt(e.X);
            int row = RowAt(e.Y);
            if (column == 0 && row >= 0 && row < 24)
            {
                _hour = row;
                Invalidate();
            }
            else if (column == 1 && row >= 0 && row < 12)
            {
                _minute = row * 5;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            TextRenderer.DrawText(
                g,
                _hour.ToString("00") + ":" + _minute.ToString("00"),
                Ui.Font(15f, FontStyle.Bold),
                new Rectangle(ClientSize.Width - Ui.S(140), Ui.S(10), Ui.S(124), Ui.S(32)),
                Ui.Accent,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            for (int hour = 0; hour < 24; hour++)
            {
                DrawCell(g, _leftColumn, hour, hour.ToString("00"), hour == _hour, hour == _hourHover);
            }

            for (int index = 0; index < 12; index++)
            {
                DrawCell(g, _rightColumn, index, (index * 5).ToString("00"), index * 5 == _minute, index == _minuteHover);
            }
        }

        private void DrawCell(Graphics g, int left, int row, string text, bool selected, bool hover)
        {
            var rect = new Rectangle(left, _listTop + row * _rowHeight, _columnWidth, _rowHeight - Ui.S(2));
            if (selected)
            {
                Ui.FillRounded(g, rect, Ui.S(7), Color.FromArgb(220, 86, 142, 247));
            }
            else if (hover)
            {
                Ui.FillRounded(g, rect, Ui.S(7), Color.FromArgb(34, 255, 255, 255));
            }

            TextRenderer.DrawText(
                g,
                text,
                Ui.Font(9f, selected ? FontStyle.Bold : FontStyle.Regular),
                rect,
                selected ? Color.White : Ui.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnPaletteChanged()
        {
            _title.ForeColor = Ui.Text;
            _hint.ForeColor = Ui.TextMuted;
            Invalidate(true);
        }
    }
}
