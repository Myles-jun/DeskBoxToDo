using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>
    /// Full-field editor, mirroring DeskBox's detail pane: title, due, colour,
    /// importance, recurrence, reminder, steps, notes and attachments.
    /// </summary>
    internal sealed class TodoEditForm : GlassForm
    {
        private readonly AppConfig _config;
        private readonly TodoItem _item;

        private GlassField _titleField;
        private GlassField _dueField;
        private GlassField _stepsField;
        private GlassField _notesField;
        private GlassColorPicker _colorPicker;
        private GlassToggle _importantToggle;
        private readonly List<GlassToggle> _recurrenceToggles = new List<GlassToggle>();
        private readonly List<GlassToggle> _reminderToggles = new List<GlassToggle>();
        private Label _attachmentLabel;

        private readonly List<TodoAttachment> _attachments = new List<TodoAttachment>();
        private string _recurrenceMode;
        private int? _reminderOffset;

        public TodoEditForm(AppConfig config, TodoItem item)
        {
            _config = config;
            _item = item;
            _recurrenceMode = item.Recurrence == null ? TodoRecurrenceMode.None : item.Recurrence.Mode;
            _reminderOffset = item.ReminderOffsetMinutes;
            _attachments.AddRange(item.Attachments);

            Ui.InitScale();
            CornerRadius = Ui.S(16);
            LiveBackdrop = config.LiveBackdrop;

            Text = "编辑待办";
            ClientSize = new Size(Ui.S(600), Ui.S(720));
            Font = Ui.Font(9f, FontStyle.Regular);

            BuildUi();
            LoadItem();
            PlaceCenter();
            PrepareBackdrop();
        }

        private Label MakeLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.3f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private void BuildUi()
        {
            var title = new Label
            {
                Text = "编辑待办",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.Text,
                Font = Ui.Font(13f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var close = new GlassButton("✕", false) { Font = Ui.Font(10.5f, FontStyle.Regular), Ghost = true };
            close.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            _titleField = new GlassField(false, _item.Text);
            _titleField.Placeholder = "标题";
            _dueField = new GlassField(false, "");
            _dueField.Placeholder = "2026-11-20 08:00 / 周五 / 明天";
            _stepsField = new GlassField(true, string.Join(Environment.NewLine, _item.Steps.Select(step => step.Text)));
            _stepsField.Placeholder = "每行一个子项";
            _notesField = new GlassField(true, _item.Notes ?? "");
            _notesField.Placeholder = "备注（支持 Markdown 文本）";
            _colorPicker = new GlassColorPicker(_item.ColorMarker);
            _importantToggle = new GlassToggle("标记为重要", _item.IsImportant);

            _attachmentLabel = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.2f, FontStyle.Regular),
                TextAlign = ContentAlignment.TopLeft
            };

            var addAttachment = new GlassButton("添加文件…", false) { Font = Ui.Font(8.2f, FontStyle.Regular) };
            addAttachment.Click += (sender, args) => AddAttachment();

            var clearAttachments = new GlassButton("清空附件", false) { Font = Ui.Font(8.2f, FontStyle.Regular) };
            clearAttachments.Click += (sender, args) =>
            {
                _attachments.Clear();
                UpdateAttachmentLabel();
            };

            var cancel = new GlassButton("取消", false);
            cancel.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            var save = new GlassButton("保存", true);
            save.Click += (sender, args) => SaveItem();

            Controls.AddRange(new Control[]
            {
                title, close,
                MakeLabel("标题"), _titleField,
                MakeLabel("截止时间"), _dueField,
                MakeLabel("颜色"), _colorPicker,
                _importantToggle,
                MakeLabel("重复"), _stepsField,
                MakeLabel("子项 · 每行一个"), _notesField,
                MakeLabel("备注"), _attachmentLabel,
                addAttachment, clearAttachments,
                cancel, save
            });

            int pad = Ui.S(26);
            int width = ClientSize.Width;
            title.SetBounds(pad, Ui.S(16), width - pad * 2 - Ui.S(40), Ui.S(32));
            close.SetBounds(width - pad - Ui.S(30), Ui.S(18), Ui.S(30), Ui.S(30));

            MakeLabelAt(Controls, "标题", pad, Ui.S(56));
            _titleField.SetBounds(pad, Ui.S(74), width - pad * 2, Ui.S(40));

            MakeLabelAt(Controls, "截止时间", pad, Ui.S(122));
            _dueField.SetBounds(pad, Ui.S(140), width - pad * 2, Ui.S(40));

            MakeLabelAt(Controls, "颜色", pad, Ui.S(188));
            _colorPicker.SetBounds(pad, Ui.S(206), width - pad * 2, Ui.S(30));
            _importantToggle.SetBounds(pad, Ui.S(242), Ui.S(200), Ui.S(28));

            MakeLabelAt(Controls, "重复", pad, Ui.S(278));
            BuildRecurrenceRow(pad, Ui.S(296), width);

            MakeLabelAt(Controls, "提醒", pad, Ui.S(334));
            BuildReminderRow(pad, Ui.S(352), width);

            MakeLabelAt(Controls, "子项 · 每行一个", pad, Ui.S(390));
            _stepsField.SetBounds(pad, Ui.S(408), width - pad * 2, Ui.S(90));

            MakeLabelAt(Controls, "备注", pad, Ui.S(506));
            _notesField.SetBounds(pad, Ui.S(524), width - pad * 2, Ui.S(86));

            MakeLabelAt(Controls, "附件", pad, Ui.S(618));
            _attachmentLabel.SetBounds(pad, Ui.S(636), width - pad * 2 - Ui.S(210), Ui.S(34));
            addAttachment.SetBounds(width - pad - Ui.S(196), Ui.S(630), Ui.S(92), Ui.S(32));
            clearAttachments.SetBounds(width - pad - Ui.S(98), Ui.S(630), Ui.S(92), Ui.S(32));

            cancel.SetBounds(width - pad - Ui.S(198), Ui.S(672), Ui.S(90), Ui.S(38));
            save.SetBounds(width - pad - Ui.S(100), Ui.S(672), Ui.S(100), Ui.S(38));

            foreach (Control control in Controls)
            {
                if (!(control is Label))
                {
                    control.BringToFront();
                }
            }
        }

        private static void MakeLabelAt(Control.ControlCollection controls, string text, int left, int top)
        {
            foreach (Control control in controls)
            {
                var label = control as Label;
                if (label != null && label.Text == text)
                {
                    label.SetBounds(left, top, Ui.S(240), Ui.S(18));
                    return;
                }
            }
        }

        private void BuildRecurrenceRow(int left, int top, int width)
        {
            int x = left;
            foreach (string mode in TodoRecurrenceMode.Supported)
            {
                string captured = mode;
                var toggle = new GlassToggle(TodoRecurrenceMode.Display(mode), _recurrenceMode == mode)
                {
                    Font = Ui.Font(8.2f, FontStyle.Regular)
                };
                toggle.CheckedChanged += (sender, args) =>
                {
                    if (!toggle.Checked)
                    {
                        return;
                    }

                    _recurrenceMode = captured;
                    foreach (GlassToggle other in _recurrenceToggles)
                    {
                        if (!ReferenceEquals(other, toggle))
                        {
                            other.Checked = false;
                        }
                    }
                };
                toggle.SetBounds(x, top, Ui.S(76), Ui.S(26));
                _recurrenceToggles.Add(toggle);
                Controls.Add(toggle);
                x += Ui.S(80);
            }
        }

        private void BuildReminderRow(int left, int top, int width)
        {
            var offsets = new List<int?> { TodoReminderOptions.ReminderOff };
            foreach (int offset in TodoReminderOptions.Offsets)
            {
                offsets.Add(offset);
            }

            int x = left;
            foreach (int? offset in offsets)
            {
                int? captured = offset;
                string label = ReminderChipLabel(offset);
                var toggle = new GlassToggle(label, Nullable.Equals(_reminderOffset, offset))
                {
                    Font = Ui.Font(8.2f, FontStyle.Regular)
                };
                toggle.CheckedChanged += (sender, args) =>
                {
                    if (!toggle.Checked)
                    {
                        return;
                    }

                    _reminderOffset = captured;
                    foreach (GlassToggle other in _reminderToggles)
                    {
                        if (!ReferenceEquals(other, toggle))
                        {
                            other.Checked = false;
                        }
                    }
                };
                toggle.SetBounds(x, top, Ui.S(62), Ui.S(26));
                _reminderToggles.Add(toggle);
                Controls.Add(toggle);
                x += Ui.S(66);
            }
        }

        private static string ReminderChipLabel(int? offset)
        {
            if (!offset.HasValue || offset.Value == TodoReminderOptions.ReminderOff)
            {
                return "关闭";
            }

            if (offset.Value == 0)
            {
                return "准时";
            }

            if (offset.Value >= 1440)
            {
                return "1 天";
            }

            if (offset.Value >= 60)
            {
                return "1 时";
            }

            return offset.Value + " 分";
        }

        private void LoadItem()
        {
            _titleField.Box.Text = _item.Text;
            _dueField.Box.Text = _item.DueDate.HasValue
                ? _item.DueDate.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : "";
            UpdateAttachmentLabel();
        }

        private void UpdateAttachmentLabel()
        {
            if (_attachments.Count == 0)
            {
                _attachmentLabel.Text = "没有附件";
                return;
            }

            _attachmentLabel.Text = string.Join("  ·  ", _attachments.Take(3).Select(attachment => attachment.DisplayName)) +
                                   (_attachments.Count > 3 ? "  等 " + _attachments.Count + " 个" : "");
        }

        private void AddAttachment()
        {
            using (var dialog = new OpenFileDialog { Multiselect = true, Title = "选择附件" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                foreach (string path in dialog.FileNames)
                {
                    _attachments.Add(new TodoAttachment
                    {
                        FilePath = path,
                        DisplayName = System.IO.Path.GetFileName(path)
                    });
                }

                UpdateAttachmentLabel();
            }
        }

        private void SaveItem()
        {
            string title = _titleField.Box.Text.Trim();
            if (title.Length == 0)
            {
                MessageBox.Show(this, "标题不能为空。", "DeskBoxToDo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _item.Text = title;

            string dueText = _dueField.Box.Text.Trim();
            if (dueText.Length == 0)
            {
                _item.DueDate = null;
            }
            else
            {
                TodoDraft parsed = TodoParser.Parse(dueText, _config, DateTimeOffset.Now);
                DateTimeOffset due;
                if (DateTimeOffset.TryParse(dueText, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out due))
                {
                    _item.DueDate = due;
                }
                else if (parsed.Due.HasValue)
                {
                    _item.DueDate = parsed.Due;
                }
            }

            _item.ColorMarker = _colorPicker.SelectedMarker;
            _item.IsImportant = _importantToggle.Checked;
            _item.ReminderOffsetMinutes = _reminderOffset;

            if (_recurrenceMode == TodoRecurrenceMode.None || !_item.DueDate.HasValue)
            {
                _item.Recurrence = null;
                _item.RecurrenceSeriesId = null;
            }
            else
            {
                _item.Recurrence = TodoRecurrenceService.Create(_recurrenceMode, _item.DueDate);
                if (string.IsNullOrWhiteSpace(_item.RecurrenceSeriesId))
                {
                    _item.RecurrenceSeriesId = Guid.NewGuid().ToString("N");
                }
            }

            var steps = _stepsField.Box.Text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
            var existing = _item.Steps.ToDictionary(step => step.Text, StringComparer.Ordinal);
            _item.Steps = steps.Select((text, index) => new TodoStep
            {
                Id = existing.ContainsKey(text) ? existing[text].Id : Guid.NewGuid().ToString("N"),
                Text = text,
                IsCompleted = existing.ContainsKey(text) && existing[text].IsCompleted,
                SortOrder = index
            }).ToList();

            _item.Notes = string.IsNullOrWhiteSpace(_notesField.Box.Text) ? null : _notesField.Box.Text;
            _item.Attachments = _attachments.ToList();
            _item.UpdatedAt = DateTimeOffset.UtcNow;

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
                    label.ForeColor = label.Font != null && label.Font.Size > 11f ? Ui.Text : Ui.TextMuted;
                }

                control.Invalidate();
            }

            Invalidate(true);
        }
    }
}
