using System;
using System.Drawing;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>Glass settings page: AI switch, endpoint, model, key.</summary>
    internal sealed class SettingsForm : GlassForm
    {
        private readonly AppConfig _config;
        private GlassToggle _enabledToggle;
        private GlassToggle _autoToggle;
        private GlassToggle _revealToggle;
        private GlassField _urlField;
        private GlassField _modelField;
        private GlassField _keyField;
        private Label _statusLabel;

        public SettingsForm(AppConfig config)
        {
            _config = config;
            Ui.InitScale();
            CornerRadius = Ui.S(16);
            LiveBackdrop = config.LiveBackdrop;

            Text = "设置";
            ClientSize = new Size(Ui.S(560), Ui.S(520));
            Font = Ui.Font(9f, FontStyle.Regular);

            BuildUi();
            LoadConfig();
            PlaceCenter();
            PrepareBackdrop();
        }

        private void BuildUi()
        {
            var title = new Label
            {
                Text = "设置",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.Text,
                Font = Ui.Font(13f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var closeButton = new GlassButton("✕", false) { Font = Ui.Font(10.5f, FontStyle.Regular), Ghost = true };
            closeButton.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            var section = new Label
            {
                Text = "AI 解析（可选，默认关闭）",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.6f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _enabledToggle = new GlassToggle("启用 AI 解析", _config.LlmEnabled);
            _autoToggle = new GlassToggle("自动调用（不勾 = 手动：只在你点「AI 重解析」时才调）", _config.LlmAuto);

            var urlLabel = MakeLabel("API 地址（OpenAI 兼容，例如 https://api.deepseek.com/v1）");
            var modelLabel = MakeLabel("模型");
            var keyLabel = MakeLabel("API Key");

            _urlField = new GlassField(false, _config.LlmBaseUrl);
            _modelField = new GlassField(false, _config.LlmModel);
            _keyField = new GlassField(false, _config.LlmApiKey);
            _keyField.Box.UseSystemPasswordChar = true;

            _revealToggle = new GlassToggle("显示 Key", false);
            _revealToggle.CheckedChanged += (sender, args) =>
            {
                _keyField.Box.UseSystemPasswordChar = !_revealToggle.Checked;
            };

            var testButton = new GlassButton("测试连接", false);
            testButton.Click += (sender, args) => TestConnection();

            _statusLabel = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.3f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = string.IsNullOrEmpty(AiParser.LastUsage) ? "尚未调用过 API" : "上次用量：" + AiParser.LastUsage
            };

            var note = new Label
            {
                Text = "省 token：不发历史、不带示例，只发这条消息（≤800 字），max_tokens=320；同一条消息命中缓存不再计费。",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var cancelButton = new GlassButton("取消", false);
            cancelButton.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            var saveButton = new GlassButton("保存", true);
            saveButton.Click += (sender, args) => Save();

            Controls.AddRange(new Control[]
            {
                title, closeButton, section,
                _enabledToggle, _autoToggle,
                urlLabel, _urlField, modelLabel, _modelField, keyLabel, _keyField,
                _revealToggle, testButton, _statusLabel, note, cancelButton, saveButton
            });

            int pad = Ui.S(28);
            int width = ClientSize.Width;

            title.SetBounds(pad, Ui.S(20), width - pad * 2 - Ui.S(40), Ui.S(34));
            closeButton.SetBounds(width - pad - Ui.S(30), Ui.S(22), Ui.S(30), Ui.S(30));
            section.SetBounds(pad, Ui.S(56), width - pad * 2, Ui.S(20));

            _enabledToggle.SetBounds(pad, Ui.S(84), Ui.S(200), Ui.S(28));
            _autoToggle.SetBounds(pad, Ui.S(116), Ui.S(460), Ui.S(28));

            urlLabel.SetBounds(pad, Ui.S(156), width - pad * 2, Ui.S(18));
            _urlField.SetBounds(pad, Ui.S(176), width - pad * 2, Ui.S(42));

            modelLabel.SetBounds(pad, Ui.S(232), Ui.S(120), Ui.S(18));
            _modelField.SetBounds(pad, Ui.S(252), Ui.S(260), Ui.S(42));

            keyLabel.SetBounds(pad, Ui.S(308), width - pad * 2, Ui.S(18));
            _keyField.SetBounds(pad, Ui.S(328), width - pad * 2 - Ui.S(120), Ui.S(42));
            _revealToggle.SetBounds(width - pad - Ui.S(110), Ui.S(335), Ui.S(110), Ui.S(28));

            testButton.SetBounds(pad, Ui.S(386), Ui.S(110), Ui.S(38));
            _statusLabel.SetBounds(pad + Ui.S(122), Ui.S(390), width - pad * 2 - Ui.S(122), Ui.S(30));

            note.SetBounds(pad, Ui.S(430), width - pad * 2, Ui.S(32));

            cancelButton.SetBounds(width - pad - Ui.S(238), Ui.S(464), Ui.S(96), Ui.S(40));
            saveButton.SetBounds(width - pad - Ui.S(134), Ui.S(464), Ui.S(134), Ui.S(40));
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

        private void LoadConfig()
        {
            _urlField.Box.Text = _config.LlmBaseUrl;
            _modelField.Box.Text = _config.LlmModel;
            _keyField.Box.Text = _config.LlmApiKey;
            _enabledToggle.Checked = _config.LlmEnabled;
            _autoToggle.Checked = _config.LlmAuto;
        }

        private void PlaceCenter()
        {
            Rectangle working = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                working.Left + (working.Width - Width) / 2,
                working.Top + (working.Height - Height) / 2);
        }

        protected override void OnBackdropReady()
        {
            Color backdrop = SampleBackdrop(new Rectangle(0, 0, Width, Height));
            Color tinted = Ui.Blend(backdrop, Color.FromArgb(12, 14, 20), TintAlpha);
            _urlField.SyncGlassColor(tinted);
            _modelField.SyncGlassColor(tinted);
            _keyField.SyncGlassColor(tinted);
        }

        protected override void OnPaletteChanged()
        {
            foreach (Control control in Controls)
            {
                var label = control as Label;
                if (label != null)
                {
                    bool isHeading = label.Font != null && label.Font.Size >= 10f;
                    label.ForeColor = isHeading ? Ui.Text : Ui.TextMuted;
                }

                control.Invalidate();
            }

            OnBackdropReady();
        }

        private void TestConnection()
        {
            ApplyToConfig();
            _statusLabel.Text = "测试中…";
            _statusLabel.ForeColor = Ui.TextMuted;
            Cursor previous = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                string result = AiParser.Test(_config);
                _statusLabel.Text = result;
                _statusLabel.ForeColor = result.StartsWith("连接成功", StringComparison.Ordinal) ? Color.FromArgb(120, 220, 150) : Color.FromArgb(240, 150, 120);
            }
            finally
            {
                Cursor.Current = previous;
            }
        }

        private void ApplyToConfig()
        {
            _config.LlmEnabled = _enabledToggle.Checked;
            _config.LlmAuto = _autoToggle.Checked;
            _config.LlmAlways = false;
            _config.LlmBaseUrl = _urlField.Box.Text.Trim();
            _config.LlmModel = _modelField.Box.Text.Trim();
            _config.LlmApiKey = _keyField.Box.Text.Trim();
        }

        private void Save()
        {
            ApplyToConfig();
            _config.Save();
            Log.Write(string.Format(
                "settings saved; ai={0} auto={1} url={2} model={3} keyLen={4}",
                _config.LlmEnabled,
                _config.LlmAuto,
                _config.LlmBaseUrl,
                _config.LlmModel,
                _config.LlmApiKey.Length));
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
