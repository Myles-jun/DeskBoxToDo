using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>
    /// DeskBox-style "自定义日期" card: date field with a calendar button and a
    /// time pill with a clock button, then 保存 / 取消.
    /// </summary>
    internal sealed class DateTimeDialog : GlassForm
    {
        private readonly AppConfig _config;
        private readonly Label _title;
        private readonly GlassButton _close;
        private readonly GlassField _dateField;
        private readonly GlassButton _calendarButton;
        private readonly GlassButton _save;
        private readonly GlassButton _cancel;

        private DateTime _date;
        private int _hour;
        private int _minute;
        private Rectangle _timePill;
        private bool _timeHover;

        public DateTimeOffset? Result { get; private set; }

        public DateTimeDialog(AppConfig config, DateTimeOffset? current)
        {
            _config = config;
            Ui.InitScale();
            CornerRadius = Ui.S(28);
            LiveBackdrop = config.LiveBackdrop;
            DragZone = Ui.S(40);

            DateTimeOffset seed = current ?? DateTimeOffset.Now;
            _date = seed.ToLocalTime().Date;
            _hour = current.HasValue ? seed.ToLocalTime().Hour : 23;
            _minute = current.HasValue ? seed.ToLocalTime().Minute : 59;

            Text = "自定义日期";
            ClientSize = new Size(Ui.S(380), Ui.S(212));
            Font = Ui.Font(9f, FontStyle.Regular);

            _title = new Label
            {
                Text = "自定义日期",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.Text,
                Font = Ui.Font(10.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _close = new GlassButton("✕", false) { Ghost = true, Font = Ui.Font(10f, FontStyle.Regular) };
            _close.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            _dateField = new GlassField(false, _date.ToString("yyyy/M/d", CultureInfo.InvariantCulture));
            _dateField.Box.Font = Ui.Font(10f, FontStyle.Regular);

            _calendarButton = new GlassButton("▦", false) { Ghost = true, Font = Ui.Font(11f, FontStyle.Regular) };
            _calendarButton.Click += (sender, args) => PickDate();

            _save = new GlassButton("保存", true);
            _save.Click += (sender, args) => Save();

            _cancel = new GlassButton("取消", false);
            _cancel.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[]
            {
                _title, _close, _dateField, _calendarButton, _save, _cancel
            });

            int pad = Ui.S(18);
            _title.SetBounds(pad, Ui.S(14), Ui.S(200), Ui.S(26));
            _close.SetBounds(ClientSize.Width - pad - Ui.S(26), Ui.S(14), Ui.S(26), Ui.S(26));
            _dateField.SetBounds(pad, Ui.S(52), ClientSize.Width - pad * 2 - Ui.S(44), Ui.S(40));
            _calendarButton.SetBounds(ClientSize.Width - pad - Ui.S(38), Ui.S(52), Ui.S(38), Ui.S(40));
            _timePill = new Rectangle(pad, Ui.S(100), ClientSize.Width - pad * 2, Ui.S(40));
            _save.SetBounds(ClientSize.Width - pad - Ui.S(84), ClientSize.Height - Ui.S(44), Ui.S(84), Ui.S(32));
            _cancel.SetBounds(ClientSize.Width - pad - Ui.S(174), ClientSize.Height - Ui.S(44), Ui.S(84), Ui.S(32));

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

        private void PickDate()
        {
            using (var popup = new GlassCalendarForm(_config, ParseDateText()))
            {
                if (popup.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _date = popup.SelectedDate;
                _dateField.Box.Text = _date.ToString("yyyy/M/d", CultureInfo.InvariantCulture);
                Invalidate();
            }
        }

        private DateTime ParseDateText()
        {
            string text = _dateField.Box.Text.Trim();
            DateTime parsed;
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
            {
                return parsed.Date;
            }

            if (DateTime.TryParseExact(text, new[] { "yyyy/M/d", "yyyy-M-d", "yyyy/MM/dd", "yyyy-MM-dd" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed.Date;
            }

            TodoDraft draft = TodoParser.Parse(text, _config, DateTimeOffset.Now);
            return draft.Due.HasValue ? draft.Due.Value.ToLocalTime().Date : _date;
        }

        private void PickTime()
        {
            using (var picker = new TimePickerForm(_config, _hour, _minute))
            {
                if (picker.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _hour = picker.SelectedHour;
                _minute = picker.SelectedMinute;
                Invalidate();
            }
        }

        private void Save()
        {
            DateTime date = ParseDateText();
            var local = new DateTime(date.Year, date.Month, date.Day, _hour, _minute, 0, DateTimeKind.Local);
            Result = new DateTimeOffset(local);
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = _timePill.Contains(e.Location);
            Cursor = hover ? Cursors.Hand : Cursors.Default;
            if (hover != _timeHover)
            {
                _timeHover = hover;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _timeHover = false;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && _timePill.Contains(e.Location))
            {
                PickTime();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Calendar glyph inside the date field's right button.
            var calendar = new RectangleF(_calendarButton.Left + Ui.S(12), _calendarButton.Top + Ui.S(13), Ui.S(14), Ui.S(14));
            Ui.StrokeRounded(g, calendar, Ui.S(3), Color.FromArgb(150, 255, 255, 255), 1.1f);
            using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f))
            {
                g.DrawLine(pen, calendar.Left + 2, calendar.Top + Ui.S(5), calendar.Right - 2, calendar.Top + Ui.S(5));
            }

            // Time pill.
            var pill = new RectangleF(_timePill.X, _timePill.Y, _timePill.Width, _timePill.Height);
            Ui.FillRounded(g, pill, Ui.S(9), Color.FromArgb(_timeHover ? 46 : 26, 255, 255, 255));
            Ui.StrokeRounded(g, pill, Ui.S(9), Color.FromArgb(_timeHover ? 88 : 44, 255, 255, 255), 1f);

            TextRenderer.DrawText(
                g,
                _hour.ToString("00") + ":" + _minute.ToString("00"),
                Ui.Font(10f, FontStyle.Bold),
                Rectangle.Round(pill),
                Ui.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            // Clock glyph on the right of the pill.
            var clock = new RectangleF(pill.Right - Ui.S(34), pill.Top + Ui.S(12), Ui.S(15), Ui.S(15));
            Ui.StrokeRounded(g, clock, Ui.S(7), Color.FromArgb(150, 255, 255, 255), 1.1f);
            using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1.2f))
            {
                g.DrawLine(pen, clock.Left + clock.Width / 2f, clock.Top + clock.Height / 2f,
                    clock.Left + clock.Width / 2f, clock.Top + Ui.S(4));
                g.DrawLine(pen, clock.Left + clock.Width / 2f, clock.Top + clock.Height / 2f,
                    clock.Left + Ui.S(9), clock.Top + clock.Height / 2f);
            }
        }

        protected override void OnPaletteChanged()
        {
            _title.ForeColor = Ui.Text;
            Invalidate(true);
        }
    }
}
