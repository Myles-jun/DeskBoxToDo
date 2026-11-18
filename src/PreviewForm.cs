using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    internal sealed class PreviewForm : GlassForm
    {
        private readonly TodoDraft _draft;
        private readonly AppConfig _config;
        private TextBox _titleBox;
        private TextBox _dueBox;
        private TextBox _stepsBox;
        private GlassField _titleField;
        private GlassField _dueField;
        private GlassField _stepsField;
        private readonly List<Label> _labels = new List<Label>();
        private Label _titleLabel;
        private GlassColorPicker _colorPicker;
        private GlassButton _aiButton;
        private bool _aiBusy;
        private GlassToggle _noDueToggle;
        private GlassToggle _importantToggle;
        private readonly List<GlassToggle> _recurrenceToggles = new List<GlassToggle>();
        private string _recurrenceMode = "";

        public TodoDraft Result
        {
            get { return _draft; }
        }

        public PreviewForm(TodoDraft draft, AppConfig config)
        {
            _draft = draft;
            _config = config;

            Ui.InitScale();
            CornerRadius = Ui.S(16);
            LiveBackdrop = config.LiveBackdrop;

            Text = "加入 DeskBox 待办";
            ClientSize = new Size(Ui.S(520), Ui.S(580));
            Font = Ui.Font(9f, FontStyle.Regular);
            KeyPreview = true;

            BuildUi();
            LoadDraft();
            PlaceNearCursor();
            PrepareBackdrop();
        }

        private void BuildUi()
        {
            _titleLabel = new Label
            {
                Text = "加入 DeskBox 待办",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.Text,
                Font = Ui.Font(13f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var title = _titleLabel;

            var closeButton = new GlassButton("✕", false)
            {
                Font = Ui.Font(10.5f, FontStyle.Regular),
                Ghost = true
            };
            closeButton.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            var source = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.3f, FontStyle.Regular),
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            string raw = (_draft.Notes ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            source.Text = raw.Length > 0 ? "来源：" + (raw.Length > 72 ? raw.Substring(0, 72) + "…" : raw) : "";

            var titleLabel = MakeLabel("标题");
            var dueLabel = MakeLabel("截止时间");
            var colorLabel = MakeLabel("颜色");
            _colorPicker = new GlassColorPicker(
                string.IsNullOrWhiteSpace(_draft.ColorMarker) ? _config.ColorMarker : _draft.ColorMarker);

            var recurrenceLabel = MakeLabel("重复");
            _recurrenceMode = TodoRecurrenceMode.Normalize(_draft.RecurrenceMode);

            var stepsLabel = MakeLabel("子项 · 每行一条");

            var titleField = new GlassField(false, _draft.Title);
            _titleField = titleField;
            _titleBox = titleField.Box;

            var dueField = new GlassField(false, "");
            _dueField = dueField;
            _dueBox = dueField.Box;

            var stepsField = new GlassField(true, string.Join(Environment.NewLine, _draft.Steps));
            _stepsField = stepsField;
            _stepsBox = stepsField.Box;

            _noDueToggle = new GlassToggle("不设截止时间", !_draft.Due.HasValue);
            _noDueToggle.CheckedChanged += (sender, args) =>
            {
                _dueBox.Enabled = !_noDueToggle.Checked;
                _dueBox.ForeColor = _noDueToggle.Checked ? Ui.TextMuted : Ui.Text;
                dueField.Invalidate();
            };

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
                _recurrenceToggles.Add(toggle);
            }

            _importantToggle = new GlassToggle("标记为重要", _draft.Important);

            _aiButton = new GlassButton("AI 重解析", false) { Font = Ui.Font(8.4f, FontStyle.Regular) };
            _aiButton.Click += (sender, args) => RunAiReparse();

            var cancelButton = new GlassButton("取消", false);
            cancelButton.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            var okButton = new GlassButton("加入 DeskBox", true);
            okButton.Click += (sender, args) => OkButtonOnClick();

            Controls.AddRange(new Control[]
            {
                title, closeButton, source,
                titleLabel, titleField,
                dueLabel, dueField, _noDueToggle,
                colorLabel, _colorPicker,
                recurrenceLabel,
                stepsLabel, stepsField,
                _importantToggle, _aiButton, cancelButton, okButton
            });

            foreach (GlassToggle toggle in _recurrenceToggles)
            {
                Controls.Add(toggle);
            }

            void Layout(object sender, EventArgs args)
            {
                int pad = Ui.S(28);
                int width = ClientSize.Width;

                title.SetBounds(pad, Ui.S(20), width - pad * 2 - Ui.S(40), Ui.S(34));
                closeButton.SetBounds(width - pad - Ui.S(30), Ui.S(22), Ui.S(30), Ui.S(30));
                source.SetBounds(pad, Ui.S(56), width - pad * 2, Ui.S(20));

                titleLabel.SetBounds(pad, Ui.S(88), Ui.S(120), Ui.S(18));
                titleField.SetBounds(pad, Ui.S(108), width - pad * 2, Ui.S(42));

                dueLabel.SetBounds(pad, Ui.S(164), Ui.S(120), Ui.S(18));
                dueField.SetBounds(pad, Ui.S(184), Ui.S(286), Ui.S(42));
                _noDueToggle.SetBounds(pad + Ui.S(300), Ui.S(191), Ui.S(160), Ui.S(28));

                colorLabel.SetBounds(pad, Ui.S(238), Ui.S(120), Ui.S(18));
                _colorPicker.SetBounds(pad, Ui.S(258), width - pad * 2, Ui.S(30));

                recurrenceLabel.SetBounds(pad, Ui.S(298), Ui.S(120), Ui.S(18));
                for (int index = 0; index < _recurrenceToggles.Count; index++)
                {
                    _recurrenceToggles[index].SetBounds(
                        pad + index * Ui.S(78),
                        Ui.S(318),
                        Ui.S(74),
                        Ui.S(26));
                }

                stepsLabel.SetBounds(pad, Ui.S(356), Ui.S(220), Ui.S(18));
                stepsField.SetBounds(pad, Ui.S(376), width - pad * 2, Ui.S(110));

                // 右侧按钮从右往左排，左侧控件留出安全间距，避免互相压住。
                int okWidth = Ui.S(134);
                int cancelWidth = Ui.S(96);
                int gap = Ui.S(8);
                int okLeft = width - pad - okWidth;
                int cancelLeft = okLeft - gap - cancelWidth;

                okButton.SetBounds(okLeft, Ui.S(508), okWidth, Ui.S(40));
                cancelButton.SetBounds(cancelLeft, Ui.S(508), cancelWidth, Ui.S(40));

                int aiWidth = Ui.S(96);
                int toggleWidth = Math.Max(Ui.S(96), cancelLeft - Ui.S(12) - pad - aiWidth - Ui.S(8));
                _importantToggle.SetBounds(pad, Ui.S(514), toggleWidth, Ui.S(28));
                _aiButton.SetBounds(pad + toggleWidth + gap, Ui.S(510), aiWidth, Ui.S(36));
            }

            Layout(null, EventArgs.Empty);
        }

        protected override void OnBackdropReady()
        {
            // 让输入框内部颜色跟随它背后的玻璃色，整体才像一块半透明玻璃。
            SyncField(_titleField);
            SyncField(_dueField);
            SyncField(_stepsField);
        }

        private void SyncField(GlassField field)
        {
            if (field == null)
            {
                return;
            }

            Color backdrop = SampleBackdrop(field.Bounds);
            Color tinted = Ui.Blend(backdrop, Color.FromArgb(12, 14, 20), TintAlpha);
            field.SyncGlassColor(tinted);
        }

        private Label MakeLabel(string text)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.3f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _labels.Add(label);
            return label;
        }

        protected override void OnPaletteChanged()
        {
            if (_titleLabel != null)
            {
                _titleLabel.ForeColor = Ui.Text;
            }

            foreach (Label label in _labels)
            {
                label.ForeColor = Ui.TextMuted;
            }

            SyncField(_titleField);
            SyncField(_dueField);
            SyncField(_stepsField);

            foreach (Control control in Controls)
            {
                control.Invalidate();
            }
        }

        private void LoadDraft()
        {
            _titleBox.Text = _draft.Title ?? "";
            _importantToggle.Checked = _draft.Important;

            DateTimeOffset due;
            if (!_draft.Due.HasValue)
            {
                due = DefaultDue();
            }
            else
            {
                due = _draft.Due.Value;
            }

            _dueBox.Text = FormatDue(due);
            _dueBox.Enabled = !_noDueToggle.Checked;
            _dueBox.ForeColor = _noDueToggle.Checked ? Ui.TextMuted : Ui.Text;
        }

        private static string FormatDue(DateTimeOffset due)
        {
            string[] names = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd} {1} {0:HH:mm}",
                due,
                names[(int)due.DayOfWeek]);
        }

        private DateTimeOffset DefaultDue()
        {
            DateTime today = DateTime.Today;
            int hour = Math.Max(0, Math.Min(23, _config.DefaultDueHour));
            int minute = Math.Max(0, Math.Min(59, _config.DefaultDueMinute));
            DateTime candidate = today.AddHours(hour).AddMinutes(minute);
            if (candidate <= DateTime.Now)
            {
                candidate = candidate.AddDays(1);
            }

            return new DateTimeOffset(candidate, TimeZoneInfo.Local.GetUtcOffset(candidate));
        }

        private static DateTimeOffset? ParseDue(string text)
        {
            string cleaned = Regex.Replace(text ?? "", @"\s*周[一二三四五六日天]\s*", " ").Trim();
            if (cleaned.Length == 0)
            {
                return null;
            }

            string[] formats =
            {
                "yyyy-MM-dd HH:mm", "yyyy-M-d H:mm", "yyyy/MM/dd HH:mm", "yyyy-M-d H:mm",
                "MM-dd HH:mm", "M-d H:mm", "yyyy-MM-dd", "yyyy/M/d"
            };

            DateTime parsed;
            if (DateTime.TryParseExact(cleaned, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed) ||
                DateTime.TryParse(cleaned, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
            {
                if (parsed.Year < 2000)
                {
                    parsed = new DateTime(DateTime.Now.Year, parsed.Month, parsed.Day, parsed.Hour, parsed.Minute, 0);
                    if (parsed < DateTime.Now.AddDays(-1))
                    {
                        parsed = parsed.AddYears(1);
                    }
                }

                var local = new DateTime(
                    parsed.Year, parsed.Month, parsed.Day, parsed.Hour, parsed.Minute, 0, DateTimeKind.Unspecified);
                return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
            }

            TodoDraft fallback = TodoParser.Parse(cleaned, new AppConfig(), DateTimeOffset.Now);
            return fallback.Due;
        }

        private void PlaceNearCursor()
        {
            Point cursor = Cursor.Position;
            Rectangle working = Screen.FromPoint(cursor).WorkingArea;
            int x = cursor.X - Width / 2;
            int y = cursor.Y + Ui.S(20);
            x = Math.Max(working.Left + Ui.S(8), Math.Min(x, working.Right - Width - Ui.S(8)));
            if (y + Height > working.Bottom - Ui.S(8))
            {
                y = cursor.Y - Height - Ui.S(20);
            }

            y = Math.Max(working.Top + Ui.S(8), y);
            Location = new Point(x, y);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            if (keyData == Keys.Enter && !(_stepsBox != null && _stepsBox.Focused))
            {
                OkButtonOnClick();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// Copies the controls back into the draft. Split out of the OK click
        /// handler so the mapping can be exercised head-lessly in tests.
        /// </summary>
        internal bool ApplyToDraft()
        {
            string title = _titleBox.Text.Trim();
            if (title.Length == 0)
            {
                return false;
            }

            _draft.Title = title;
            _draft.Important = _importantToggle.Checked;
            _draft.RecurrenceMode = _recurrenceMode == TodoRecurrenceMode.None ? "" : _recurrenceMode;
            _draft.ColorMarker = _colorPicker.SelectedMarker;

            _draft.Steps = new List<string>();
            foreach (string line in _stepsBox.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    _draft.Steps.Add(trimmed);
                }
            }

            if (_noDueToggle.Checked)
            {
                _draft.Due = null;
            }
            else
            {
                DateTimeOffset? parsed = ParseDue(_dueBox.Text);
                _draft.Due = parsed;
            }

            return true;
        }

        /// <summary>Shows that a background model call is running.</summary>
        public void SetAiBusy(string text)
        {
            if (IsDisposed)
            {
                return;
            }

            _aiBusy = true;
            _aiButton.Enabled = false;
            _aiButton.Text = text;
        }

        /// <summary>Applies a result that arrived after the dialog opened.</summary>
        public void ApplyAiResult(TodoDraft ai, bool keepExistingDue)
        {
            if (IsDisposed)
            {
                return;
            }

            _aiBusy = false;
            _aiButton.Enabled = true;

            if (ai == null)
            {
                _aiButton.Text = "AI 失败";
                Log.Write("ai result empty: " + AiParser.LastError);
                return;
            }

            if (!string.IsNullOrWhiteSpace(ai.Title))
            {
                _titleBox.Text = ai.Title;
            }

            _importantToggle.Checked = ai.Important;

            if (ai.Steps.Count > 0)
            {
                _stepsBox.Text = string.Join(Environment.NewLine, ai.Steps);
            }

            if (!string.IsNullOrEmpty(ai.RecurrenceMode) && ai.RecurrenceMode != TodoRecurrenceMode.None)
            {
                _recurrenceMode = TodoRecurrenceMode.Normalize(ai.RecurrenceMode);
                foreach (GlassToggle toggle in _recurrenceToggles)
                {
                    toggle.Checked = toggle.Text == TodoRecurrenceMode.Display(_recurrenceMode);
                }
            }

            if (ai.Due.HasValue && !keepExistingDue)
            {
                _noDueToggle.Checked = false;
                _dueBox.Text = FormatDue(ai.Due.Value);
                _dueBox.Enabled = true;
                _dueBox.ForeColor = Ui.Text;
            }

            _aiButton.Text = "已用 AI";
            Log.Write("ai applied to dialog; " + AiParser.LastUsage);
        }

        private void RunAiReparse()
        {
            if (_aiBusy)
            {
                return;
            }

            if (!AiParser.IsConfigured(_config))
            {
                MessageBox.Show(
                    this,
                    "先在托盘菜单「设置…」里填写 API 地址和 Key，并打开「启用 AI 解析」。",
                    "DeskBoxToDo",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string source = string.IsNullOrWhiteSpace(_draft.Notes) ? _titleBox.Text : _draft.Notes;
            _aiBusy = true;
            _aiButton.Enabled = false;
            _aiButton.Text = "解析中…";

            Task<TodoDraft> task = AiParser.ParseAsync(source, _config);
            task.ContinueWith(result =>
            {
                if (IsDisposed)
                {
                    return;
                }

                BeginInvoke(new Action(() =>
                {
                    TodoDraft ai = null;
                    try
                    {
                        ai = result.Status == TaskStatus.RanToCompletion ? result.Result : null;
                    }
                    catch (Exception ex)
                    {
                        Log.Write("ai reparse failed: " + ex.Message);
                    }

                    ApplyAiResult(ai, false);
                }));
            });
        }

        private void OkButtonOnClick()
        {
            if (!ApplyToDraft())
            {
                MessageBox.Show(this, "标题不能为空。", "DeskBoxToDo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
