using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>
    /// Detail pane modelled on DeskBox: back/save, big title, 2x2 property rows,
    /// steps, notes, attachments and the "created at" footer.
    /// </summary>
    internal sealed class TodoDetailForm : GlassForm
    {
        private readonly AppConfig _config;
        private readonly TodoItem _item;
        private readonly GlassField _titleField;
        private readonly GlassField _notesField;
        private readonly GlassButton _back;
        private readonly GlassButton _save;
        private readonly GlassButton _star;
        private readonly GlassButton _trash;

        private int _propsTop;
        private int _stepsTop;
        private int _notesTop;
        private int _filesTop;
        private int _hoverRegion;

        public bool Deleted { get; private set; }

        public TodoDetailForm(AppConfig config, TodoItem item, bool isNew)
        {
            _config = config;
            _item = item;
            Ui.InitScale();
            CornerRadius = Ui.S(32);
            LiveBackdrop = config.LiveBackdrop;

            Text = "待办详情";
            ClientSize = new Size(Ui.S(400), Ui.S(860));
            Font = Ui.Font(9f, FontStyle.Regular);
            DragZone = Ui.S(48);
            Resizable = true;
            MinimumSize = new Size(Ui.S(320), Ui.S(420));

            int pad = Ui.S(16);

            _back = new GlassButton("←", false) { Ghost = true, Font = Ui.Font(12f, FontStyle.Regular) };
            _back.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            _save = new GlassButton("✓", false) { Ghost = true, Font = Ui.Font(12f, FontStyle.Bold) };
            _save.Click += (sender, args) => Save();

            _star = new GlassButton("☆", false) { Ghost = true, Font = Ui.Font(12f, FontStyle.Regular) };
            _star.Click += (sender, args) =>
            {
                _item.IsImportant = !_item.IsImportant;
                _item.UpdatedAt = DateTimeOffset.UtcNow;
                _star.Text = _item.IsImportant ? "★" : "☆";
                _star.ForeColor = _item.IsImportant ? Color.FromArgb(242, 201, 76) : Ui.TextMuted;
                _star.Invalidate();
                Invalidate();
            };

            _trash = new GlassButton("🗑", false) { Ghost = true, Font = Ui.Font(11f, FontStyle.Regular) };
            _trash.Click += (sender, args) =>
            {
                if (isNew)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return;
                }

                Deleted = true;
                DialogResult = DialogResult.OK;
                Close();
            };

            _titleField = new GlassField(true, item.Text) { Placeholder = "待办内容" };
            _titleField.Box.Font = Ui.Font(11f, FontStyle.Regular);

            _notesField = new GlassField(true, item.Notes ?? "") { Placeholder = "备注" };
            _notesField.Box.Font = Ui.Font(9f, FontStyle.Regular);

            _star.Text = item.IsImportant ? "★" : "☆";
            _star.ForeColor = item.IsImportant ? Color.FromArgb(242, 201, 76) : Ui.TextMuted;

            Controls.AddRange(new Control[]
            {
                _back, _save, _star, _trash, _titleField, _notesField
            });

            _back.SetBounds(pad, Ui.S(14), Ui.S(34), Ui.S(32));
            _save.SetBounds(pad + Ui.S(40), Ui.S(14), Ui.S(34), Ui.S(32));
            _trash.SetBounds(ClientSize.Width - pad - Ui.S(34), Ui.S(14), Ui.S(34), Ui.S(32));
            _star.SetBounds(ClientSize.Width - pad - Ui.S(72), Ui.S(14), Ui.S(34), Ui.S(32));

            _titleField.SetBounds(pad, Ui.S(60), ClientSize.Width - pad * 2, Ui.S(66));

            _propsTop = Ui.S(140);
            _stepsTop = _propsTop + Ui.S(104);
            _notesTop = _stepsTop + Ui.S(96);
            _filesTop = _notesTop + Ui.S(150);

            _notesField.SetBounds(pad, _notesTop + Ui.S(26), ClientSize.Width - pad * 2, Ui.S(104));

            LayoutSections();
            PrepareBackdrop();
            PlaceCenter();
        }

        private void LayoutSections()
        {
            int pad = Ui.S(16);
            _stepsTop = _propsTop + Ui.S(104);
            _notesTop = _stepsTop + Ui.S(74) + _item.Steps.Count * Ui.S(26);
            _filesTop = _notesTop + Ui.S(154);
            _notesField.SetBounds(pad, _notesTop + Ui.S(26), ClientSize.Width - pad * 2, Ui.S(104));
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

        private void Save()
        {
            string text = _titleField.Box.Text.Trim();
            if (text.Length == 0)
            {
                return;
            }

            _item.Text = text;
            _item.Notes = string.IsNullOrWhiteSpace(_notesField.Box.Text) ? null : _notesField.Box.Text;
            _item.UpdatedAt = DateTimeOffset.UtcNow;
            DialogResult = DialogResult.OK;
            Close();
        }

        private sealed class Region
        {
            public Rectangle Bounds;
            public string Kind = "";
            public int Index;
        }

        private List<Region> BuildRegions()
        {
            int pad = Ui.S(16);
            int width = ClientSize.Width - pad * 2;
            int half = width / 2;
            var regions = new List<Region>
            {
                new Region { Bounds = new Rectangle(pad, _propsTop, half, Ui.S(52)), Kind = "due" },
                new Region { Bounds = new Rectangle(pad + half, _propsTop, half, Ui.S(52)), Kind = "reminder" },
                new Region { Bounds = new Rectangle(pad, _propsTop + Ui.S(56), half, Ui.S(52)), Kind = "recurrence" },
                new Region { Bounds = new Rectangle(pad + half, _propsTop + Ui.S(56), half, Ui.S(52)), Kind = "color" },
                new Region { Bounds = new Rectangle(pad, _stepsTop + Ui.S(26), width, Ui.S(34)), Kind = "addStep" },
                new Region { Bounds = new Rectangle(pad, _filesTop, width, Ui.S(34)), Kind = "addFile" }
            };

            for (int index = 0; index < _item.Steps.Count; index++)
            {
                regions.Add(new Region
                {
                    Bounds = new Rectangle(pad, _stepsTop + Ui.S(62) + index * Ui.S(26), width, Ui.S(26)),
                    Kind = "step",
                    Index = index
                });
            }

            return regions;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            Region region = BuildRegions().FirstOrDefault(entry => entry.Bounds.Contains(e.Location));
            if (region == null)
            {
                return;
            }

            switch (region.Kind)
            {
                case "due":
                    PickDue();
                    break;
                case "reminder":
                    PickReminder();
                    break;
                case "recurrence":
                    PickRecurrence();
                    break;
                case "color":
                    PickColor();
                    break;
                case "addStep":
                    AddStep();
                    break;
                case "step":
                    ToggleStep(_item.Steps[region.Index]);
                    break;
                case "addFile":
                    AddFile();
                    break;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = BuildRegions().FindIndex(entry => entry.Bounds.Contains(e.Location));
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            if (index != _hoverRegion)
            {
                _hoverRegion = index;
                Invalidate();
            }
        }

        private void PickDue()
        {
            using (var dialog = new DateTimeDialog(_config, _item.DueDate))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _item.DueDate = dialog.Result;
                if (!_item.DueDate.HasValue)
                {
                    _item.Recurrence = null;
                }
                else if (_item.Recurrence != null)
                {
                    _item.Recurrence.AnchorDueDate = _item.DueDate;
                }

                Invalidate();
            }
        }
        private void PickReminder()
        {
            using (var picker = new PickerForm(_config, "提醒", "默认提前 5 分钟"))
            {
                picker.Add("off", "关闭提醒", "", TodoReminderOptions.IsOff(_item.ReminderOffsetMinutes));
                foreach (int offset in TodoReminderOptions.Offsets)
                {
                    int captured = offset;
                    picker.Add(captured.ToString(), offset == 0 ? "准时" : TodoReminderOptions.Display(offset), "", _item.ReminderOffsetMinutes == offset);
                }

                picker.Build();
                if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedKey == null)
                {
                    return;
                }

                _item.ReminderOffsetMinutes = picker.SelectedKey == "off"
                    ? TodoReminderOptions.ReminderOff
                    : int.Parse(picker.SelectedKey);
                Invalidate();
            }
        }

        private void PickRecurrence()
        {
            string current = _item.Recurrence == null ? TodoRecurrenceMode.None : _item.Recurrence.Mode;
            using (var picker = new PickerForm(_config, "重复", "需要先有日期"))
            {
                foreach (string mode in TodoRecurrenceMode.Supported)
                {
                    string captured = mode;
                    picker.Add(captured, TodoRecurrenceMode.Display(mode), "", current == mode);
                }

                picker.Build();
                if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedKey == null)
                {
                    return;
                }

                if (picker.SelectedKey == TodoRecurrenceMode.None)
                {
                    _item.Recurrence = null;
                    _item.RecurrenceSeriesId = null;
                }
                else
                {
                    // Recurrence needs an anchor date: default to today 23:59.
                    if (!_item.DueDate.HasValue)
                    {
                        _item.DueDate = TodoService.DuePreset("today");
                    }

                    _item.Recurrence = TodoRecurrenceService.Create(picker.SelectedKey, _item.DueDate);
                    if (string.IsNullOrWhiteSpace(_item.RecurrenceSeriesId))
                    {
                        _item.RecurrenceSeriesId = Guid.NewGuid().ToString("N");
                    }
                }

                Invalidate();
            }
        }

        private void PickColor()
        {
            using (var picker = new PickerForm(_config, "颜色", "对应 DeskBox 的待办颜色"))
            {
                foreach (string marker in TodoItem.Markers)
                {
                    string captured = marker;
                    picker.Add(
                        string.IsNullOrEmpty(marker) ? "none" : marker,
                        string.IsNullOrEmpty(marker) ? "无颜色" : MarkerName(marker),
                        "",
                        string.Equals(_item.ColorMarker ?? "", marker, StringComparison.Ordinal),
                        TodoItem.MarkerColor(marker));
                }

                picker.Build();
                if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedKey == null)
                {
                    return;
                }

                _item.ColorMarker = picker.SelectedKey == "none" ? "" : picker.SelectedKey;
                Invalidate();
            }
        }

        private void AddStep()
        {
            string text = PromptForm.Ask(this, "添加步骤", "");
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            _item.Steps.Add(new TodoStep { Text = text.Trim(), SortOrder = _item.Steps.Count });
            LayoutSections();
            Invalidate();
        }

        private void ToggleStep(TodoStep step)
        {
            step.IsCompleted = !step.IsCompleted;
            Invalidate();
        }

        private void AddFile()
        {
            using (var dialog = new OpenFileDialog { Multiselect = true, Title = "添加文件" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                foreach (string path in dialog.FileNames)
                {
                    _item.Attachments.Add(new TodoAttachment
                    {
                        FilePath = path,
                        DisplayName = System.IO.Path.GetFileName(path)
                    });
                }

                Invalidate();
            }
        }

        private static string MarkerName(string marker)
        {
            switch (marker)
            {
                case "red": return "红色";
                case "orange": return "橙色";
                case "yellow": return "黄色";
                case "green": return "绿色";
                case "blue": return "蓝色";
                case "purple": return "紫色";
                case "teal": return "青色";
                case "pink": return "粉色";
                default: return "无颜色";
            }
        }

        private string SecondaryText()
        {
            if (!_item.DueDate.HasValue)
            {
                return "无日期";
            }

            DateTimeOffset due = _item.DueDate.Value.ToLocalTime();
            DateTimeOffset now = DateTimeOffset.Now;
            if (due.Date == now.Date)
            {
                return "今天 " + due.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            if (due.Date == now.Date.AddDays(1))
            {
                return "明天 " + due.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            return due.ToString("yyyy/M/d HH:mm", CultureInfo.InvariantCulture);
        }

        protected override void OnScaleChanged(int previousScale, int newScale)
        {
            base.OnScaleChanged(previousScale, newScale);
            if (previousScale <= 0 || newScale <= 0 || !IsHandleCreated)
            {
                return;
            }

            double ratio = (double)newScale / previousScale;
            ClientSize = new Size(
                Math.Max(Ui.S(320), (int)Math.Round(ClientSize.Width * ratio)),
                Math.Max(Ui.S(420), (int)Math.Round(ClientSize.Height * ratio)));
            LayoutSections();
            Invalidate(true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (IsHandleCreated)
            {
                LayoutSections();
                Invalidate(true);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int pad = Ui.S(16);
            int width = ClientSize.Width - pad * 2;

            // Accent underline under the title, like the DeskBox detail pane.
            using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1f))
            {
                g.DrawLine(pen, pad, Ui.S(128), pad + width, Ui.S(128));
            }

            DrawProperty(g, new Rectangle(pad, _propsTop, width / 2, Ui.S(52)), "date", "日期", SecondaryText());
            DrawProperty(g, new Rectangle(pad + width / 2, _propsTop, width / 2, Ui.S(52)), "bell", "提醒", ReminderText());
            DrawProperty(g, new Rectangle(pad, _propsTop + Ui.S(56), width / 2, Ui.S(52)), "repeat", "重复", _item.Recurrence == null ? "不重复" : TodoRecurrenceMode.Display(_item.Recurrence.Mode));
            DrawProperty(g, new Rectangle(pad + width / 2, _propsTop + Ui.S(56), width / 2, Ui.S(52)), "dot", "颜色", string.IsNullOrEmpty(_item.ColorMarker) ? "无颜色" : MarkerName(_item.ColorMarker));

            TextRenderer.DrawText(g, "步骤", Ui.Font(9f, FontStyle.Bold), new Rectangle(pad, _stepsTop, width, Ui.S(20)), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(g, "添加步骤", Ui.Font(9f, FontStyle.Regular), new Rectangle(pad + Ui.S(2), _stepsTop + Ui.S(28), width - Ui.S(28), Ui.S(30)), Ui.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "＋", Ui.Font(11f, FontStyle.Regular), new Rectangle(pad + width - Ui.S(24), _stepsTop + Ui.S(28), Ui.S(22), Ui.S(30)), Ui.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            for (int index = 0; index < _item.Steps.Count; index++)
            {
                TodoStep step = _item.Steps[index];
                int top = _stepsTop + Ui.S(62) + index * Ui.S(26);
                var box = new RectangleF(pad + Ui.S(4), top + Ui.S(5), Ui.S(14), Ui.S(14));
                if (step.IsCompleted)
                {
                    Ui.FillRounded(g, box, Ui.S(4), Ui.Accent);
                }
                else
                {
                    Ui.StrokeRounded(g, box, Ui.S(4), Color.FromArgb(110, 255, 255, 255), 1f);
                }

                TextRenderer.DrawText(
                    g,
                    step.Text,
                    Ui.Font(9f, FontStyle.Regular),
                    new Rectangle(pad + Ui.S(26), top, width - Ui.S(30), Ui.S(26)),
                    step.IsCompleted ? Ui.TextMuted : Ui.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            TextRenderer.DrawText(g, "备注", Ui.Font(9f, FontStyle.Bold), new Rectangle(pad, _notesTop, width, Ui.S(20)), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "✎", Ui.Font(11f, FontStyle.Regular), new Rectangle(pad + width - Ui.S(22), _notesTop, Ui.S(22), Ui.S(20)), Ui.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            Ui.StrokeRounded(g, new Rectangle(pad, _filesTop + Ui.S(9), Ui.S(10), Ui.S(12)), Ui.S(2), Color.FromArgb(130, 255, 255, 255), 1.1f);
            TextRenderer.DrawText(g, "添加文件", Ui.Font(9f, FontStyle.Regular), new Rectangle(pad + Ui.S(20), _filesTop, width - Ui.S(22), Ui.S(30)), Ui.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            if (_item.Attachments.Count > 0)
            {
                TextRenderer.DrawText(
                    g,
                    string.Join("   ", _item.Attachments.Take(3).Select(attachment => attachment.DisplayName)),
                    Ui.Font(8.4f, FontStyle.Regular),
                    new Rectangle(pad, _filesTop + Ui.S(32), width, Ui.S(22)),
                    Ui.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            TextRenderer.DrawText(
                g,
                "创建于 " + _item.CreatedAt.ToLocalTime().ToString("yyyy/M/d HH:mm", CultureInfo.InvariantCulture),
                Ui.Font(8.2f, FontStyle.Regular),
                new Rectangle(pad, ClientSize.Height - Ui.S(34), width, Ui.S(20)),
                Ui.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private string ReminderText()
        {
            return TodoReminderOptions.IsOff(_item.ReminderOffsetMinutes)
                ? "已关闭"
                : _item.ReminderOffsetMinutes.HasValue
                    ? TodoReminderOptions.Display(_item.ReminderOffsetMinutes)
                    : "提醒";
        }

        private void DrawProperty(Graphics g, Rectangle bounds, string icon, string label, string value)
        {
            var iconRect = new Rectangle(bounds.X, bounds.Y + Ui.S(14), Ui.S(16), Ui.S(16));
            Ui.StrokeRounded(g, iconRect, Ui.S(3), Color.FromArgb(120, 255, 255, 255), 1.2f);
            if (icon == "dot" && !string.IsNullOrEmpty(_item.ColorMarker))
            {
                using (var brush = new SolidBrush(TodoItem.MarkerColor(_item.ColorMarker)))
                {
                    g.FillEllipse(brush, iconRect.X + Ui.S(4), iconRect.Y + Ui.S(4), Ui.S(8), Ui.S(8));
                }
            }
            else if (icon == "date")
            {
                using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f))
                {
                    g.DrawLine(pen, iconRect.Left + 2, iconRect.Top + Ui.S(5), iconRect.Right - 2, iconRect.Top + Ui.S(5));
                }
            }
            else if (icon == "bell")
            {
                g.FillEllipse(Brushes.White, iconRect.X + Ui.S(6), iconRect.Y + Ui.S(6), Ui.S(3), Ui.S(3));
            }
            else if (icon == "repeat")
            {
                using (var pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1f))
                {
                    g.DrawArc(pen, iconRect.X + 3, iconRect.Y + 3, Ui.S(10), Ui.S(10), 40, 280);
                }
            }

            TextRenderer.DrawText(g, label, Ui.Font(8.6f, FontStyle.Regular), new Rectangle(bounds.X + Ui.S(24), bounds.Y, bounds.Width - Ui.S(26), Ui.S(18)), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(g, value, Ui.Font(8.6f, FontStyle.Regular), new Rectangle(bounds.X + Ui.S(24), bounds.Y + Ui.S(18), bounds.Width - Ui.S(26), Ui.S(18)), Ui.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnPaletteChanged()
        {
            Invalidate(true);
        }
    }
}
