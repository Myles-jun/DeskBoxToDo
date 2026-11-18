using System;
using System.Drawing;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>Minimal glass "about" card.</summary>
    internal sealed class AboutForm : GlassForm
    {
        public AboutForm(AppConfig config)
        {
            Ui.InitScale();
            CornerRadius = Ui.S(16);
            LiveBackdrop = config.LiveBackdrop;

            Text = "关于 DeskBoxToDo";
            ClientSize = new Size(Ui.S(420), Ui.S(300));
            Font = Ui.Font(9f, FontStyle.Regular);

            var title = new Label
            {
                Text = AppInfo.ProductName,
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.Text,
                Font = Ui.Font(15f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var version = new Label
            {
                Text = "版本 " + AppInfo.Version,
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.6f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var body = new Label
            {
                Text = AppInfo.Tagline + "\r\n\r\n" +
                       "选中任意文字按快捷键，解析成 DeskBox 待办。\r\n" +
                       "数据全部留在本机；只有你主动使用 AI 时才会联网。\r\n\r\n" +
                       "MIT License · 使用 .NET Framework 4.8",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.8f, FontStyle.Regular),
                TextAlign = ContentAlignment.TopLeft
            };

            var close = new GlassButton("关闭", true);
            close.Click += (sender, args) => Close();

            Controls.AddRange(new Control[] { title, version, body, close });

            int pad = Ui.S(28);
            title.SetBounds(pad, Ui.S(26), ClientSize.Width - pad * 2, Ui.S(36));
            version.SetBounds(pad, Ui.S(64), ClientSize.Width - pad * 2, Ui.S(20));
            body.SetBounds(pad, Ui.S(96), ClientSize.Width - pad * 2, Ui.S(120));
            close.SetBounds(ClientSize.Width - pad - Ui.S(96), Ui.S(228), Ui.S(96), Ui.S(40));

            PlaceCenter();
            PrepareBackdrop();
        }

        private void PlaceCenter()
        {
            Rectangle working = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                working.Left + (working.Width - Width) / 2,
                working.Top + (working.Height - Height) / 2);
        }

        protected override void OnPaletteChanged()
        {
            foreach (Control control in Controls)
            {
                var label = control as Label;
                if (label != null)
                {
                    label.ForeColor = label.Font != null && label.Font.Size > 12f ? Ui.Text : Ui.TextMuted;
                }

                control.Invalidate();
            }

            Invalidate(true);
        }
    }
}
