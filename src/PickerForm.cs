using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>
    /// Small glass single-choice sheet — DeskBox's due/recurrence/reminder
    /// pickers, but as a compact second-level dialog.
    /// </summary>
    internal sealed class PickerForm : GlassForm
    {
        private sealed class Option
        {
            public string Key = "";
            public string Label = "";
            public string Hint = "";
            public Color Swatch = Color.Transparent;
            public bool Checked;
        }

        private readonly List<Option> _options = new List<Option>();
        private readonly Label _title;
        private readonly Label _hint;
        private readonly GlassButton _ok;
        private readonly GlassButton _cancel;
        private int _hover = -1;
        private int _selected = -1;
        private int _rowHeight;
        private int _listTop;

        public string SelectedKey
        {
            get { return _selected >= 0 && _selected < _options.Count ? _options[_selected].Key : null; }
        }

        public PickerForm(AppConfig config, string title, string hint)
        {
            Ui.InitScale();
            CornerRadius = Ui.S(32);
            LiveBackdrop = config.LiveBackdrop;

            _rowHeight = Ui.S(34);
            Text = title;
            ClientSize = new Size(Ui.S(400), Ui.S(220));
            Font = Ui.Font(9f, FontStyle.Regular);

            _title = new Label
            {
                Text = title,
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.Text,
                Font = Ui.Font(11.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _hint = new Label
            {
                Text = hint ?? "",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.3f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _cancel = new GlassButton("取消", false);
            _cancel.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            _ok = new GlassButton("确定", true);
            _ok.Click += (sender, args) =>
            {
                if (_selected < 0)
                {
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.AddRange(new Control[] { _title, _hint, _cancel, _ok });
            PlaceCenter();
        }

        public void Add(string key, string label, string hint, bool isChecked, Color swatch = default(Color))
        {
            _options.Add(new Option
            {
                Key = key,
                Label = label,
                Hint = hint ?? "",
                Swatch = swatch,
                Checked = isChecked
            });

            if (isChecked && _selected < 0)
            {
                _selected = _options.Count - 1;
            }
        }

        /// <summary>Lays out the sheet and captures the backdrop (call after Add).</summary>
        public void Build()
        {
            int pad = Ui.S(22);
            int contentHeight = _options.Count * _rowHeight;
            int footer = Ui.S(56);
            ClientSize = new Size(Ui.S(400), Ui.S(88) + contentHeight + footer);

            _title.SetBounds(pad, Ui.S(14), ClientSize.Width - pad * 2, Ui.S(28));
            _hint.SetBounds(pad, Ui.S(42), ClientSize.Width - pad * 2, Ui.S(18));
            _listTop = Ui.S(66);

            _cancel.SetBounds(ClientSize.Width - pad - Ui.S(176), ClientSize.Height - Ui.S(44), Ui.S(80), Ui.S(34));
            _ok.SetBounds(ClientSize.Width - pad - Ui.S(88), ClientSize.Height - Ui.S(44), Ui.S(88), Ui.S(34));

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

        private int RowAt(int y)
        {
            if (y < _listTop)
            {
                return -1;
            }

            int index = (y - _listTop) / _rowHeight;
            return index >= 0 && index < _options.Count ? index : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row = RowAt(e.Y);
            if (row != _hover)
            {
                _hover = row;
                Cursor = row >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int row = RowAt(e.Y);
            if (row >= 0)
            {
                _selected = row;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int pad = Ui.S(22);
            for (int index = 0; index < _options.Count; index++)
            {
                Option option = _options[index];
                var row = new Rectangle(pad, _listTop + index * _rowHeight, ClientSize.Width - pad * 2, _rowHeight - Ui.S(4));

                if (index == _hover)
                {
                    Ui.FillRounded(g, row, Ui.S(8), Color.FromArgb(26, 255, 255, 255));
                }

                if (index == _selected)
                {
                    Ui.FillRounded(g, row, Ui.S(8), Color.FromArgb(52, 86, 142, 247));
                }

                int textLeft = row.X + Ui.S(14);
                if (option.Swatch != Color.Transparent)
                {
                    using (var brush = new SolidBrush(option.Swatch))
                    {
                        g.FillEllipse(brush, row.X + Ui.S(10), row.Y + row.Height / 2f - Ui.S(6), Ui.S(12), Ui.S(12));
                    }

                    textLeft = row.X + Ui.S(32);
                }

                TextRenderer.DrawText(
                    g,
                    option.Label,
                    Ui.Font(9.2f, FontStyle.Regular),
                    new Rectangle(textLeft, row.Y, row.Width - (textLeft - row.X) - Ui.S(90), row.Height),
                    Ui.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                if (option.Hint.Length > 0)
                {
                    TextRenderer.DrawText(
                        g,
                        option.Hint,
                        Ui.Font(8.2f, FontStyle.Regular),
                        new Rectangle(row.Right - Ui.S(96), row.Y, Ui.S(84), row.Height),
                        Ui.TextMuted,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
        }

        protected override void OnPaletteChanged()
        {
            _title.ForeColor = Ui.Text;
            _hint.ForeColor = Ui.TextMuted;
            Invalidate(true);
        }
    }
}
