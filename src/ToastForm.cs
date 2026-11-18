using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>
    /// Small glass toast shown bottom-right. Used because Windows balloon tips
    /// are silently dropped when notifications are off.
    /// </summary>
    internal sealed class ToastForm : GlassForm
    {
        private readonly Timer _life;
        private readonly string _title;
        private readonly string _body;
        private readonly bool _hasAction;

        public event EventHandler Activated2;

        public ToastForm(AppConfig config, string title, string body, bool hasAction)
        {
            _title = title ?? "";
            _body = body ?? "";
            _hasAction = hasAction;

            Ui.InitScale();
            CornerRadius = Ui.S(12);
            LiveBackdrop = config.LiveBackdrop;
            EnableWindowDrag = true;
            DragZone = 0;
            TopMost = true;
            ShowInTaskbar = false;

            Text = "待办提醒";
            ClientSize = new Size(Ui.S(340), Ui.S(112));

            Rectangle working = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                working.Right - Width - Ui.S(18),
                working.Bottom - Height - Ui.S(18));

            PrepareBackdrop();

            _life = new Timer { Interval = 9000 };
            _life.Tick += (sender, args) =>
            {
                _life.Stop();
                Close();
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _life.Start();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _life.Stop();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _life.Start();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_hasAction && Activated2 != null)
            {
                Activated2(this, EventArgs.Empty);
            }

            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var accent = new RectangleF(Ui.S(14), Ui.S(20), Ui.S(4), Ui.S(24));
            Ui.FillRounded(g, accent, Ui.S(2), Ui.Accent);

            TextRenderer.DrawText(
                g,
                _title,
                Ui.Font(9.6f, FontStyle.Bold),
                new Rectangle(Ui.S(28), Ui.S(14), ClientSize.Width - Ui.S(44), Ui.S(22)),
                Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(
                g,
                _body,
                Ui.Font(8.8f, FontStyle.Regular),
                new Rectangle(Ui.S(28), Ui.S(38), ClientSize.Width - Ui.S(44), Ui.S(56)),
                Ui.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (_hasAction)
            {
                TextRenderer.DrawText(
                    g,
                    "点击打开待办",
                    Ui.Font(8.2f, FontStyle.Regular),
                    new Rectangle(Ui.S(28), ClientSize.Height - Ui.S(26), ClientSize.Width - Ui.S(44), Ui.S(18)),
                    Ui.Accent,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _life?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
