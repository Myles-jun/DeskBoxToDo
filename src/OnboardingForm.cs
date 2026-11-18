using System;
using System.Drawing;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>
    /// First-run tour: three minimal glass pages — what it does, pick a hotkey,
    /// optional AI. Nothing is written until 「开始使用」 is pressed.
    /// </summary>
    internal sealed class OnboardingForm : GlassForm
    {
        private readonly AppConfig _config;
        private readonly string _hotkeyText;
        private readonly Action<int, Keys> _hotkeyChanged;
        private int _currentModifiers;
        private Keys _currentKey;

        private readonly Label _stepLabel;
        private readonly Label _title;
        private readonly Label _body;
        private readonly Label _hint;
        private readonly GlassToggle _standaloneToggle;
        private readonly GlassToggle _deskboxToggle;
        private readonly GlassButton _download;
        private readonly Label _deskboxState;
        private readonly GlassToggle _autoStartToggle;
        private readonly GlassToggle _aiToggle;
        private readonly GlassButton _back;
        private readonly GlassButton _next;
        private readonly GlassButton _record;
        private readonly GlassButton _close;
        private int _page;
        private string _currentHotkey;

        public OnboardingForm(AppConfig config, string hotkeyText, Action<int, Keys> hotkeyChanged)
        {
            _config = config;
            _hotkeyText = hotkeyText;
            _currentHotkey = hotkeyText;
            _hotkeyChanged = hotkeyChanged;

            Ui.InitScale();
            CornerRadius = Ui.S(16);
            LiveBackdrop = config.LiveBackdrop;

            Text = "欢迎使用 DeskBoxToDo";
            ClientSize = new Size(Ui.S(560), Ui.S(580));
            Font = Ui.Font(9f, FontStyle.Regular);

            _stepLabel = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.4f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _title = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.Text,
                Font = Ui.Font(17f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _body = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(9.2f, FontStyle.Regular),
                TextAlign = ContentAlignment.TopLeft
            };

            _hint = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.4f, FontStyle.Regular),
                TextAlign = ContentAlignment.TopLeft
            };

            bool deskBoxFound = TodoBackend.DeskBoxAvailable(config);
            bool useDeskBox = TodoBackend.IsDeskBoxMode(config) && deskBoxFound;

            _standaloneToggle = new GlassToggle("自带待办（推荐，不依赖 DeskBox）", !useDeskBox);
            _deskboxToggle = new GlassToggle("接入已安装的 DeskBox", useDeskBox);
            _standaloneToggle.CheckedChanged += (sender, args) =>
            {
                if (_standaloneToggle.Checked)
                {
                    _deskboxToggle.Checked = false;
                }
            };
            _deskboxToggle.CheckedChanged += (sender, args) =>
            {
                if (_deskboxToggle.Checked)
                {
                    _standaloneToggle.Checked = false;
                }
            };

            _deskboxState = new Label
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.3f, FontStyle.Regular),
                TextAlign = ContentAlignment.TopLeft,
                Text = deskBoxFound
                    ? "已检测到 DeskBox：" + config.DeskBoxExePath
                    : "没检测到 DeskBox。接入模式需要它来显示格子；想用官方的桌面整理可以下载安装。"
            };

            _download = new GlassButton("下载 DeskBox", false);
            _download.Visible = !deskBoxFound;
            _download.Click += (sender, args) => OpenDeskBoxDownload();

            _autoStartToggle = new GlassToggle("开机自动启动（推荐）", true);
            _aiToggle = new GlassToggle("启用 AI 解析（可选，需要 API Key）", config.LlmEnabled);

            _close = new GlassButton("✕", false) { Font = Ui.Font(10.5f, FontStyle.Regular), Ghost = true };
            _close.Click += (sender, args) => { PersistPartial(); DialogResult = DialogResult.Cancel; Close(); };

            _record = new GlassButton("重新录制", false);
            _record.Click += (sender, args) => RecordHotkey();

            _back = new GlassButton("上一步", false);
            _back.Click += (sender, args) => { if (_page > 0) { _page--; Render(); } };

            _next = new GlassButton("下一步", true);
            _next.Click += (sender, args) => Next();

            Controls.AddRange(new Control[]
            {
                _stepLabel, _title, _body, _hint,
                _standaloneToggle, _deskboxToggle, _deskboxState, _download,
                _autoStartToggle, _aiToggle,
                _record, _back, _next, _close
            });

            foreach (Control control in Controls)
            {
                if (!(control is Label))
                {
                    control.BringToFront();
                }
            }

            LayoutOnce();
            PlaceCenter();
            Render();
            PrepareBackdrop();
        }

        private void LayoutOnce()
        {
            int pad = Ui.S(32);
            int width = ClientSize.Width;

            _stepLabel.SetBounds(pad, Ui.S(22), width - pad * 2 - Ui.S(40), Ui.S(20));
            _close.SetBounds(width - pad - Ui.S(30), Ui.S(20), Ui.S(30), Ui.S(30));
            _title.SetBounds(pad, Ui.S(58), width - pad * 2, Ui.S(46));
            _body.SetBounds(pad, Ui.S(116), width - pad * 2, Ui.S(190));
            _hint.SetBounds(pad, Ui.S(466), width - pad * 2, Ui.S(40));

            _standaloneToggle.SetBounds(pad, Ui.S(312), width - pad * 2, Ui.S(28));
            _deskboxToggle.SetBounds(pad, Ui.S(346), width - pad * 2, Ui.S(28));
            _deskboxState.SetBounds(pad, Ui.S(380), width - pad * 2, Ui.S(40));
            _download.SetBounds(pad, Ui.S(424), Ui.S(140), Ui.S(38));

            _autoStartToggle.SetBounds(pad, Ui.S(368), width - pad * 2, Ui.S(28));
            _aiToggle.SetBounds(pad, Ui.S(400), width - pad * 2, Ui.S(28));

            _record.SetBounds(pad, Ui.S(516), Ui.S(110), Ui.S(40));
            _back.SetBounds(width - pad - Ui.S(226), Ui.S(516), Ui.S(96), Ui.S(40));
            _next.SetBounds(width - pad - Ui.S(122), Ui.S(516), Ui.S(122), Ui.S(40));
        }

        private void PlaceCenter()
        {
            Rectangle working = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                working.Left + (working.Width - Width) / 2,
                working.Top + (working.Height - Height) / 2);
        }

        private void Render()
        {
            int total = 6;
            _stepLabel.Text = string.Format("{0} / {1}", _page + 1, total);

            switch (_page)
            {
                case 0:
                    _title.Text = "把一句话变成待办";
                    _body.Text =
                        "选中一段文字，按快捷键，确认窗里改两笔，它就成了 DeskBox 里的一条待办。\r\n\r\n" +
                        "· 适合群里的作业通知、会议安排、截止提醒\r\n" +
                        "· 自动认出「周五」「下周三 14:00」这类时间\r\n" +
                        "· 原文会存进备注，子项自动拆好\r\n\r\n" +
                        "数据都留在本机：不联网、不上传（除非你自己打开 AI）。";
                    _hint.Text = "点「下一步」选择快捷键。";
                    break;

                case 1:
                    _title.Text = "选一个快捷键";
                    _body.Text =
                        "当前：" + _currentHotkey + "\r\n\r\n" +
                        "用法：任意软件里选中文字 → 按这个快捷键 → 确认窗出现 → 回车加入。\r\n\r\n" +
                        "如果按了没反应，多半是被截图工具之类的软件用底层钩子抢了，换一个组合即可。";
                    _hint.Text = "点「重新录制」可以换一个组合键；也可以稍后在托盘菜单里改。";
                    break;

                case 2:
                    _title.Text = "待办存在哪里";
                    _body.Text =
                        "自带待办：直接存在本程序里（%LOCALAPPDATA%\\DeskBoxToDo\\todo.json），" +
                        "不装 DeskBox 也能用，加完立刻生效、不用重启。\r\n\r\n" +
                        "接入 DeskBox：写进 DeskBox 的待办格子，和桌面上的小部件共享同一份数据；" +
                        "代价是每次写入要让 DeskBox 重启一次（约 2 秒）。\r\n\r\n" +
                        "两种模式随时可以在设置里切换，数据格式一致。";
                    _hint.Text = "选一个就行，之后也能改。";
                    break;

                case 3:
                    _title.Text = "待办里能做什么";
                    _body.Text =
                        "· 页签：全部 / 今天 / 重要 / 完成，数量实时统计\r\n" +
                        "· 颜色：任务左侧的竖条；点上方色点只看某一种颜色\r\n" +
                        "· 重要：右侧金色 ★，在详情里点 ☆ 切换\r\n" +
                        "· 重复：每天 / 每周 / 每月 / 工作日，完成后自动生成下一次\r\n" +
                        "· 提醒：到点弹毛玻璃提示窗并播放提醒音乐（可换）\r\n\r\n" +
                        "拖动窗口边缘可缩放，右侧有滚动条可拖。";
                    _hint.Text = "这些都藏在右键菜单和详情页里。";
                    break;

                case 4:
                    _title.Text = "变成桌面小组件";
                    _body.Text =
                        "两种方式把待办放到桌面上：\r\n\r\n" +
                        "① 托盘图标右键 →「固定在桌面（小组件）」\r\n" +
                        "② 打开待办窗口，点右上角的「桌面」按钮（点一次=贴桌面，再点=置顶）\r\n\r\n" +
                        "取消：托盘菜单再点一次「固定在桌面（小组件）」，\r\n" +
                        "或把窗口右上角按钮点回普通浮窗即可。\r\n\r\n" +
                        "贴桌面时窗口在其它窗口下面，双击托盘图标可以把它叫到前面。";
                    _hint.Text = "小组件的位置会被记住，下次开机回到原处。";
                    break;

                default:
                    _title.Text = "两件小事";
                    _body.Text =
                        "开机自启：让它在后台待着，随时按快捷键都能用。\r\n\r\n" +
                        "AI 解析（可选）：默认手动——只有点「AI 重解析」才调用，需要先填 API Key。\r\n\r\n" +
                        "自动更新：默认每天检查一次，发现新版本会自动下载安装并重启；" +
                        "也可以在托盘「自动更新设置…」里改成只提示或关掉。";
                    _hint.Text = "准备好后点「开始使用」。";
                    break;
            }

            _record.Visible = _page == 1;
            _standaloneToggle.Visible = _page == 2;
            _deskboxToggle.Visible = _page == 2;
            _deskboxState.Visible = _page == 2;
            _download.Visible = _page == 2 && !TodoBackend.DeskBoxAvailable(_config);
            _autoStartToggle.Visible = _page == 5;
            _aiToggle.Visible = _page == 5;
            _back.Visible = _page > 0;
            _next.Text = _page == total - 1 ? "开始使用" : "下一步";
            Invalidate(true);
        }

        private void OpenDeskBoxDownload()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = AppInfo.DeskBoxDownloadUrl,
                    UseShellExecute = true
                });
                Log.Write("opened DeskBox download page");
            }
            catch (Exception ex)
            {
                Log.Write("open download failed: " + ex.Message);
            }
        }

        private void RecordHotkey()
        {
            using (var recorder = new HotkeyRecorderForm(_currentHotkey, _config.LiveBackdrop))
            {
                if (recorder.ShowDialog() == DialogResult.OK)
                {
                    _currentModifiers = recorder.Modifiers;
                    _currentKey = recorder.Key;
                    _currentHotkey = HotkeyRecorderForm.Describe(recorder.Modifiers, recorder.Key);
                    Render();
                }
            }
        }

        private void Next()
        {
            if (_page == 1 && _currentModifiers != 0)
            {
                _hotkeyChanged(_currentModifiers, _currentKey);
                _currentModifiers = 0;
            }

            if (_page < 5)
            {
                _page++;
                Render();
                return;
            }

            PersistPartial();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void PersistPartial()
        {
            _config.LlmEnabled = _aiToggle.Checked;
            _config.OnboardingVersion = AppInfo.OnboardingVersion;
            _config.AutoStart = _autoStartToggle.Checked;
            _config.TodoMode = _deskboxToggle.Checked ? "deskbox" : "standalone";
            _config.Save();
        }

        protected override void OnPaletteChanged()
        {
            _title.ForeColor = Ui.Text;
            _stepLabel.ForeColor = Ui.TextMuted;
            _body.ForeColor = Ui.TextMuted;
            _hint.ForeColor = Ui.TextMuted;
            Invalidate(true);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                PersistPartial();
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            if (keyData == Keys.Enter)
            {
                Next();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
