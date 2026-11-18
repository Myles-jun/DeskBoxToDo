using System;
using System.Drawing;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>Tiny glass prompt used for inline edits.</summary>
    internal sealed class PromptForm : GlassForm
    {
        private readonly GlassField _field;
        private readonly GlassButton _ok;
        private string _value;

        private PromptForm(string title, string initial, bool liveBackdrop)
        {
            Ui.InitScale();
            CornerRadius = Ui.S(16);
            LiveBackdrop = liveBackdrop;

            Text = title;
            ClientSize = new Size(Ui.S(420), Ui.S(190));
            Font = Ui.Font(9f, FontStyle.Regular);

            var label = new Label
            {
                Text = title,
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.Text,
                Font = Ui.Font(11f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _field = new GlassField(false, initial);
            _field.Box.Font = Ui.Font(10f, FontStyle.Regular);
            _field.Box.KeyDown += (sender, args) =>
            {
                if (args.KeyCode == Keys.Enter)
                {
                    args.SuppressKeyPress = true;
                    Accept();
                }
            };

            var cancel = new GlassButton("取消", false);
            cancel.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            _ok = new GlassButton("确定", true);
            _ok.Click += (sender, args) => Accept();

            Controls.AddRange(new Control[] { label, _field, cancel, _ok });

            int pad = Ui.S(24);
            int width = ClientSize.Width;
            label.SetBounds(pad, Ui.S(18), width - pad * 2, Ui.S(30));
            _field.SetBounds(pad, Ui.S(56), width - pad * 2, Ui.S(42));
            cancel.SetBounds(width - pad - Ui.S(202), Ui.S(120), Ui.S(90), Ui.S(38));
            _ok.SetBounds(width - pad - Ui.S(100), Ui.S(120), Ui.S(100), Ui.S(38));

            ActiveControl = _field.Box;
            _field.Box.Focus();
            PlaceCenter();
            PrepareBackdrop();
        }

        private void Accept()
        {
            _value = _field.Box.Text;
            DialogResult = DialogResult.OK;
            Close();
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
                    label.ForeColor = label.Font != null && label.Font.Size > 10f ? Ui.Text : Ui.TextMuted;
                }

                control.Invalidate();
            }

            Invalidate(true);
        }

        public static string Ask(IWin32Window owner, string title, string initial)
        {
            using (var form = new PromptForm(title, initial, true))
            {
                return form.ShowDialog(owner) == DialogResult.OK ? form._value : null;
            }
        }
    }
}
