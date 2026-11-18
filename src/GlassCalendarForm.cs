using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>Glass month calendar used by the date dialog.</summary>
    internal sealed class GlassCalendarForm : GlassForm
    {
        private readonly Label _title;
        private readonly GlassButton _prev;
        private readonly GlassButton _next;
        private readonly GlassButton _ok;
        private readonly GlassButton _cancel;

        private DateTime _month;
        private DateTime _selected;
        private int _hoverDay = -1;

        private int _gridTop;
        private int _cellWidth;
        private int _cellHeight;
        private int _gridLeft;

        public DateTime SelectedDate
        {
            get { return _selected.Date; }
        }

        public GlassCalendarForm(AppConfig config, DateTime initial)
        {
            _selected = initial.Date;
            _month = new DateTime(initial.Year, initial.Month, 1);

            Ui.InitScale();
            CornerRadius = Ui.S(28);
            LiveBackdrop = config.LiveBackdrop;
            DragZone = Ui.S(40);

            Text = "选择日期";
            ClientSize = new Size(Ui.S(322), Ui.S(360));
            Font = Ui.Font(9f, FontStyle.Regular);

            _title = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.Text,
                Font = Ui.Font(10.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            _prev = new GlassButton("‹", false) { Ghost = true, Font = Ui.Font(13f, FontStyle.Regular) };
            _prev.Click += (sender, args) => { _month = _month.AddMonths(-1); Invalidate(); };

            _next = new GlassButton("›", false) { Ghost = true, Font = Ui.Font(13f, FontStyle.Regular) };
            _next.Click += (sender, args) => { _month = _month.AddMonths(1); Invalidate(); };

            _ok = new GlassButton("确定", true);
            _ok.Click += (sender, args) => { DialogResult = DialogResult.OK; Close(); };

            _cancel = new GlassButton("取消", false);
            _cancel.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { _title, _prev, _next, _ok, _cancel });

            _prev.SetBounds(Ui.S(14), Ui.S(14), Ui.S(30), Ui.S(28));
            _next.SetBounds(ClientSize.Width - Ui.S(44), Ui.S(14), Ui.S(30), Ui.S(28));
            _title.SetBounds(Ui.S(50), Ui.S(14), ClientSize.Width - Ui.S(100), Ui.S(28));

            _gridLeft = Ui.S(14);
            _gridTop = Ui.S(70);
            _cellWidth = (ClientSize.Width - Ui.S(28)) / 7;
            _cellHeight = Ui.S(34);

            _cancel.SetBounds(ClientSize.Width - Ui.S(178), ClientSize.Height - Ui.S(42), Ui.S(76), Ui.S(30));
            _ok.SetBounds(ClientSize.Width - Ui.S(94), ClientSize.Height - Ui.S(42), Ui.S(80), Ui.S(30));

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

        private int DayAt(int x, int y)
        {
            int column = (x - _gridLeft) / _cellWidth;
            int row = (y - _gridTop - Ui.S(22)) / _cellHeight;
            if (row < 0)
            {
                return -1;
            }

            int firstOffset = ((int)_month.DayOfWeek + 6) % 7;
            int day = row * 7 + column - firstOffset + 1;
            int days = DateTime.DaysInMonth(_month.Year, _month.Month);
            return day >= 1 && day <= days ? day : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int day = DayAt(e.X, e.Y);
            Cursor = day > 0 ? Cursors.Hand : Cursors.Default;
            if (day != _hoverDay)
            {
                _hoverDay = day;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverDay = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            int day = DayAt(e.X, e.Y);
            if (day > 0)
            {
                _selected = new DateTime(_month.Year, _month.Month, day);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            _title.Text = _month.ToString("yyyy 年 M 月", CultureInfo.InvariantCulture);

            string[] weekday = { "一", "二", "三", "四", "五", "六", "日" };
            for (int index = 0; index < 7; index++)
            {
                TextRenderer.DrawText(
                    g,
                    weekday[index],
                    Ui.Font(8.4f, FontStyle.Regular),
                    new Rectangle(_gridLeft + index * _cellWidth, _gridTop, _cellWidth, Ui.S(20)),
                    Ui.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            int firstOffset = ((int)_month.DayOfWeek + 6) % 7;
            int days = DateTime.DaysInMonth(_month.Year, _month.Month);
            DateTime today = DateTime.Today;

            for (int day = 1; day <= days; day++)
            {
                int cell = day - 1 + firstOffset;
                int row = cell / 7;
                int column = cell % 7;
                var rect = new Rectangle(
                    _gridLeft + column * _cellWidth,
                    _gridTop + Ui.S(24) + row * _cellHeight,
                    _cellWidth - Ui.S(2),
                    _cellHeight - Ui.S(2));

                var date = new DateTime(_month.Year, _month.Month, day);
                bool selected = date == _selected;
                bool isToday = date == today;

                if (selected)
                {
                    Ui.FillRounded(g, rect, Ui.S(8), Color.FromArgb(230, 86, 142, 247));
                }
                else if (day == _hoverDay)
                {
                    Ui.FillRounded(g, rect, Ui.S(8), Color.FromArgb(34, 255, 255, 255));
                }
                else if (isToday)
                {
                    Ui.StrokeRounded(g, rect, Ui.S(8), Color.FromArgb(120, 255, 255, 255), 1f);
                }

                TextRenderer.DrawText(
                    g,
                    day.ToString(CultureInfo.InvariantCulture),
                    Ui.Font(9f, selected ? FontStyle.Bold : FontStyle.Regular),
                    rect,
                    selected ? Color.White : Ui.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnPaletteChanged()
        {
            _title.ForeColor = Ui.Text;
            Invalidate(true);
        }
    }
}
