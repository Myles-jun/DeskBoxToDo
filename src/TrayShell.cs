using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskBoxToDo
{
    /// <summary>Hidden window that owns the global hotkey registration.</summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const int ModAlt = 0x0001;
        private const int ModControl = 0x0002;
        private const int ModShift = 0x0004;
        private const int ModWin = 0x0008;

        private readonly int _id = 0x4A21;
        private bool _registered;

        public event EventHandler Pressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        public bool Register(string modifiers, string keyName)
        {
            Unregister();
            int mods = ParseModifiers(modifiers);
            Keys key;
            if (!Enum.TryParse(keyName, true, out key) || key == Keys.None)
            {
                Log.Write("hotkey key unparsable: " + keyName);
                return false;
            }

            _registered = RegisterHotKey(Handle, _id, mods, (int)key);
            Log.Write(string.Format("hotkey register {0}+{1} => {2}", modifiers, keyName, _registered));
            return _registered;
        }

        public void Unregister()
        {
            if (_registered)
            {
                try
                {
                    UnregisterHotKey(Handle, _id);
                }
                catch
                {
                    // Ignore.
                }

                _registered = false;
            }
        }

        private static int ParseModifiers(string text)
        {
            int mods = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return mods;
            }

            foreach (string raw in text.Split(new[] { '+', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = raw.Trim().ToLowerInvariant();
                if (token == "ctrl" || token == "control")
                {
                    mods |= ModControl;
                }
                else if (token == "alt")
                {
                    mods |= ModAlt;
                }
                else if (token == "shift")
                {
                    mods |= ModShift;
                }
                else if (token == "win" || token == "windows")
                {
                    mods |= ModWin;
                }
            }

            return mods;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && m.WParam.ToInt32() == _id)
            {
                EventHandler handler = Pressed;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

    internal sealed class TrayShell : ApplicationContext
    {
        public const string ActivationEventName = @"Local\DeskBoxToDo_Activate";

        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "DeskBoxToDo";

        private readonly AppConfig _config;
        private readonly NotifyIcon _tray;
        private readonly HotkeyWindow _hotkey;
        private string _hotkeyText = "";
        private bool _busy;
        private DateTime _busySince;
        private TodoWindow _todoWindow;
        private readonly SynchronizationContext _uiContext;
        private EventWaitHandle _activation;
        private RegisteredWaitHandle _activationWait;
        private System.Windows.Forms.Timer _reminderTimer;
        private readonly System.Collections.Generic.HashSet<string> _notified = new System.Collections.Generic.HashSet<string>();

        public TrayShell()
        {
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            _config = AppConfig.Load();
            Log.Write("tray start; deskbox=" + _config.DeskBoxExePath);

            _hotkey = new HotkeyWindow();
            _hotkey.Pressed += (sender, args) =>
            {
                Log.Write("hotkey pressed busy=" + _busy);
                CaptureAndPreview();
            };
            _hotkeyText = RegisterHotkeyWithFallback();

            _tray = new NotifyIcon
            {
                Icon = TrayIconFactory.Create(32),
                Text = "DeskBoxToDo —— 选中文字，一句话变待办",
                Visible = true
            };
            _tray.MouseUp += TrayOnMouseUp;
            _tray.DoubleClick += (sender, args) => OpenTodoWindow();

            if (_config.AutoStart && !IsAutoStartEnabled())
            {
                SetAutoStart(true);
            }

            if (string.IsNullOrWhiteSpace(_config.UpdateFeedUrl))
            {
                _config.UpdateFeedUrl = AppConfig.DefaultUpdateFeed;
                _config.Save();
            }

            _config.Save();

            ApplyGlassQuality();
            ApplyGlassOpacity();
            StartActivationListener();
            var updateTimer = new System.Windows.Forms.Timer { Interval = 10000 };
            updateTimer.Tick += (sender, args) =>
            {
                updateTimer.Stop();
                updateTimer.Dispose();
                RunScheduledUpdateCheck();
            };
            updateTimer.Start();
            StartReminders();
            var firstScan = new System.Windows.Forms.Timer { Interval = 1500 };
            firstScan.Tick += (sender, args) =>
            {
                firstScan.Stop();
                firstScan.Dispose();
                CheckReminders();
            };
            firstScan.Start();

            if (_config.TodoWidgetOnDesktop)
            {
                var starterWindow = new System.Windows.Forms.Timer { Interval = 800 };
                starterWindow.Tick += (sender, args) =>
                {
                    starterWindow.Stop();
                    starterWindow.Dispose();
                    OpenTodoWindow();
                };
                starterWindow.Start();
            }

            if (_config.OnboardingVersion < AppInfo.OnboardingVersion)
            {
                var starter = new System.Windows.Forms.Timer { Interval = 500 };
                starter.Tick += (sender, args) =>
                {
                    starter.Stop();
                    starter.Dispose();
                    RunOnboarding();
                };
                starter.Start();
            }

            string hint = _hotkeyText == "(不可用)"
                ? "全局快捷键被占用，请用托盘图标的菜单「添加选中文字」。"
                : string.Format("选中一段文字，按 {0} 加入 DeskBox 待办。", _hotkeyText);
            _tray.ShowBalloonTip(2600, "DeskBoxToDo 已启动", hint, ToolTipIcon.Info);
        }

        /// <summary>Right-click opens the glass menu instead of a native one.</summary>
        private void TrayOnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var menu = new GlassMenu();
            menu.AddItem("固定在桌面（小组件）", () => ToggleTodoWidget(), _config.TodoWidgetOnDesktop);
            menu.AddItem("提醒音乐…", () => ChooseReminderSound());
            menu.AddItem("毛玻璃透明度…", () => ChooseGlassOpacity());
            menu.AddItem("毛玻璃质感…", () => ChooseGlassQuality());
            menu.AddSeparator();
            menu.AddItem("检查更新…", () => CheckForUpdatesManually());
            menu.AddItem("自动更新设置…", () => ChooseUpdateSettings());
            menu.AddItem("打开待办", () => OpenTodoWindow());
            menu.AddItem("添加选中文字  " + _hotkeyText, () => CaptureAndPreview());
            menu.AddSeparator();
            menu.AddItem("使用教程", () => RunOnboarding());
            menu.AddItem("设置…", () => OpenSettings());
            menu.AddItem("设置快捷键…", () => RecordHotkey());
            menu.AddSeparator();
            menu.AddItem("打开配置文件", () => OpenPath(AppConfig.ConfigPath));
            menu.AddItem("打开日志", () => OpenPath(Path.Combine(AppConfig.ConfigDirectory, "DeskBoxToDo.log")));
            menu.AddItem("开机自动启动", () => ToggleAutoStart(), IsAutoStartEnabled());
            menu.AddSeparator();
            menu.AddItem("关于", () => ShowAbout());
            menu.AddItem("退出", () => ExitThread());
            menu.ShowAt(Cursor.Position);
        }

        private void ToggleAutoStart()
        {
            bool enable = !IsAutoStartEnabled();
            SetAutoStart(enable);
            _tray.ShowBalloonTip(
                1500,
                "DeskBoxToDo",
                enable ? "已加入开机启动" : "已取消开机启动",
                ToolTipIcon.Info);
        }

        private void StartActivationListener()
        {
            try
            {
                _activation = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
                _activationWait = System.Threading.ThreadPool.RegisterWaitForSingleObject(
                    _activation,
                    (state, timedOut) =>
                    {
                        try
                        {
                            Log.Write("second launch detected, showing todo window");
                            _uiContext.Post(state => OpenTodoWindow(), null);
                        }
                        catch (Exception ex)
                        {
                            Log.Write("activation dispatch failed: " + ex.Message);
                        }
                    },
                    null,
                    Timeout.Infinite,
                    false);
            }
            catch (Exception ex)
            {
                Log.Write("activation listener failed: " + ex.Message);
            }
        }

        private void ToggleTodoWidget()
        {
            _config.TodoWidgetOnDesktop = !_config.TodoWidgetOnDesktop;
            _config.Save();
            if (!_config.TodoWidgetOnDesktop && _todoWindow != null && !_todoWindow.IsDisposed)
            {
                _todoWindow.Close();
            }

            if (_config.TodoWidgetOnDesktop)
            {
                OpenTodoWindow();
            }
        }

        private void OpenTodoWindow()
        {
            if (_todoWindow != null && !_todoWindow.IsDisposed)
            {
                _todoWindow.Activate();
                return;
            }

            _todoWindow = new TodoWindow(_config);
            _todoWindow.FormClosed += (sender, args) => _todoWindow = null;
            _todoWindow.Show();
        }

        private void StartReminders()
        {
            _reminderTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            _reminderTimer.Tick += (sender, args) => CheckReminders();
            _reminderTimer.Start();
        }

        private void PlayReminderSound()
        {
            if (!_config.ReminderSoundEnabled)
            {
                return;
            }

            try
            {
                string path = _config.ReminderSound;
                if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                {
                    using (var player = new System.Media.SoundPlayer(path))
                    {
                        player.Play();
                    }

                    return;
                }

                System.Media.SystemSounds.Exclamation.Play();
            }
            catch (Exception ex)
            {
                Log.Write("reminder sound failed: " + ex.Message);
            }
        }

        private void ChooseGlassOpacity()
        {
            using (var picker = new PickerForm(_config, "毛玻璃透明度", "越透越像玻璃，越实越清楚"))
            {
                picker.Add("auto", "跟随背景（自动）", "", _config.GlassOpacityPercent <= 0);
                foreach (int percent in new[] { 20, 30, 40, 50, 60 })
                {
                    int captured = percent;
                    picker.Add(
                        captured.ToString(),
                        captured <= 20 ? "很透 · " + captured + "%" : captured >= 60 ? "很实 · " + captured + "%" : "偏" + (captured <= 40 ? "透" : "实") + " · " + captured + "%",
                        "",
                        _config.GlassOpacityPercent == captured);
                }

                picker.Build();
                if (picker.ShowDialog() != DialogResult.OK || picker.SelectedKey == null)
                {
                    return;
                }

                int value = picker.SelectedKey == "auto" ? 0 : int.Parse(picker.SelectedKey);
                _config.GlassOpacityPercent = value;
                _config.Save();
                ApplyGlassOpacity();
            }
        }

        private void CheckForUpdatesManually()
        {
            if (string.IsNullOrWhiteSpace(_config.UpdateFeedUrl))
            {
                MessageBox.Show(
                    "还没有配置更新地址。\\r\\n\\r\\n在「自动更新设置…」里填 GitHub 仓库的 releases 接口，例如：\\r\\nhttps://api.github.com/repos/你的用户名/DeskBoxToDo/releases/latest",
                    "检查更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                ChooseUpdateSettings();
                return;
            }

            System.Windows.Forms.Cursor.Current = Cursors.WaitCursor;
            UpdateInfo info = null;
            string error = "";
            try
            {
                info = UpdateService.Check(_config.UpdateFeedUrl);
                error = UpdateService.LastError;
            }
            finally
            {
                System.Windows.Forms.Cursor.Current = Cursors.Default;
            }

            _config.LastUpdateCheckUtc = DateTimeOffset.UtcNow.ToString("o");
            _config.Save();

            if (info == null)
            {
                MessageBox.Show(
                    string.IsNullOrEmpty(error) ? "已经是最新版本 " + AppInfo.Version : "检查失败：" + error,
                    "检查更新",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            PromptInstallUpdate(info, true);
        }

        private void PromptInstallUpdate(UpdateInfo info, bool userInitiated)
        {
            string notes = string.IsNullOrWhiteSpace(info.Notes) ? "" : "\\r\\n\\r\\n更新内容：\\r\\n" + Truncate(info.Notes, 400);
            DialogResult answer = MessageBox.Show(
                "发现新版本 " + info.Version + "（当前 " + AppInfo.Version + "）" + notes + "\\r\\n\\r\\n现在下载并安装吗？程序会先关闭，安装完成后自动启动。",
                "发现新版本",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            string error;
            if (!UpdateService.DownloadAndRun(info, out error))
            {
                MessageBox.Show("更新失败：" + error, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            StopEverything();
            Application.Exit();
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        private void ChooseUpdateSettings()
        {
            using (var picker = new PickerForm(_config, "自动检查更新", string.IsNullOrWhiteSpace(_config.UpdateFeedUrl) ? "尚未配置更新地址" : _config.UpdateFeedUrl))
            {
                picker.Add("off", "关闭自动检查", "", _config.UpdateCheckMode == 0);
                picker.Add("startup", "每次启动检查", "", _config.UpdateCheckMode == 1);
                picker.Add("daily", "每天检查一次", "", _config.UpdateCheckMode == 2);
                picker.Add("weekly", "每周检查一次", "", _config.UpdateCheckMode == 3);
                picker.Build();
                if (picker.ShowDialog() == DialogResult.OK && picker.SelectedKey != null)
                {
                    _config.UpdateCheckMode = picker.SelectedKey == "off" ? 0
                        : picker.SelectedKey == "startup" ? 1
                        : picker.SelectedKey == "daily" ? 2 : 3;
                    _config.Save();
                }
            }

            string feed = PromptForm.Ask(null, "更新地址（GitHub releases/latest，留空则关闭）", _config.UpdateFeedUrl);
            if (feed != null)
            {
                _config.UpdateFeedUrl = feed.Trim();
                _config.Save();
            }
        }

        private void RunScheduledUpdateCheck()
        {
            if (_config.UpdateCheckMode <= 0 || string.IsNullOrWhiteSpace(_config.UpdateFeedUrl))
            {
                return;
            }

            if (_config.UpdateCheckMode != 1)
            {
                DateTimeOffset last;
                if (DateTimeOffset.TryParse(_config.LastUpdateCheckUtc, out last))
                {
                    TimeSpan interval = _config.UpdateCheckMode == 3 ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);
                    if (DateTimeOffset.UtcNow - last < interval)
                    {
                        return;
                    }
                }
            }

            var worker = new System.Threading.Thread(() =>
            {
                UpdateInfo info = UpdateService.Check(_config.UpdateFeedUrl);
                _config.LastUpdateCheckUtc = DateTimeOffset.UtcNow.ToString("o");
                _config.Save();
                if (info == null)
                {
                    return;
                }

                if (_config.UpdateAutoInstall)
                {
                    Log.Write("auto update: installing " + info.Version);
                    string error;
                    if (UpdateService.DownloadAndRun(info, out error))
                    {
                        _uiContext.Post(state =>
                        {
                            StopEverything();
                            Application.Exit();
                        }, null);
                    }
                    else
                    {
                        Log.Write("auto update failed: " + error);
                        _uiContext.Post(state => PromptInstallUpdate(info, false), null);
                    }

                    return;
                }

                _uiContext.Post(state => PromptInstallUpdate(info, false), null);
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void StopEverything()
        {
            try
            {
                _hotkey.Dispose();
                _tray.Visible = false;
                _tray.Dispose();
            }
            catch
            {
            }
        }

        private void ChooseGlassQuality()
        {
            using (var picker = new PickerForm(_config, "毛玻璃质感", "越细腻越像真磨砂，代价是稍慢"))
            {
                picker.Add("0", "流畅（省资源）", "", _config.GlassQuality == 0);
                picker.Add("1", "标准", "", _config.GlassQuality == 1);
                picker.Add("2", "细腻（推荐）", "", _config.GlassQuality == 2);
                picker.Add("3", "极致（最费资源）", "", _config.GlassQuality == 3);
                picker.Build();
                if (picker.ShowDialog() != DialogResult.OK || picker.SelectedKey == null)
                {
                    return;
                }

                _config.GlassQuality = int.Parse(picker.SelectedKey);
                _config.Save();
                ApplyGlassQuality();
            }
        }

        private void ApplyGlassQuality()
        {
            switch (_config.GlassQuality)
            {
                case 0:
                    Ui.GlassDownscale = 5;
                    Ui.GlassBlurRadius = 5;
                    Ui.GlassBlurPasses = 2;
                    break;
                case 2:
                    Ui.GlassDownscale = 2;
                    Ui.GlassBlurRadius = 10;
                    Ui.GlassBlurPasses = 4;
                    break;
                case 3:
                    Ui.GlassDownscale = 2;
                    Ui.GlassBlurRadius = 18;
                    Ui.GlassBlurPasses = 5;
                    break;
                default:
                    Ui.GlassDownscale = 4;
                    Ui.GlassBlurRadius = 7;
                    Ui.GlassBlurPasses = 3;
                    break;
            }

            foreach (Form form in Application.OpenForms)
            {
                var glass = form as GlassForm;
                if (glass != null)
                {
                    glass.BackdropRefresh();
                }
            }
        }

        private void ApplyGlassOpacity()
        {
            Ui.OpacityOverride = _config.GlassOpacityPercent <= 0
                ? -1f
                : _config.GlassOpacityPercent / 100f;

            foreach (Form form in Application.OpenForms)
            {
                var glass = form as GlassForm;
                if (glass != null)
                {
                    glass.BackdropRefresh();
                }
            }
        }

        private void ChooseReminderSound()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "选择提醒音乐",
                Filter = "音频文件 (*.wav)|*.wav|所有文件 (*.*)|*.*"
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                _config.ReminderSound = dialog.FileName;
                _config.Save();
                PlayReminderSound();
            }
        }

        private void ShowToast(string title, string body)
        {
            try
            {
                var toast = new ToastForm(_config, title, body, true);
                toast.Activated2 += (sender, args) => OpenTodoWindow();
                toast.Show();
            }
            catch (Exception ex)
            {
                Log.Write("toast failed: " + ex.Message);
            }
        }

        private void CheckReminders()
        {
            try
            {
                // Ported from DeskBox's TodoReminderService: per-item offset
                // (default 5 min), per-due-date dismissal, snooze, once per session.
                var service = new TodoService(_config);
                System.Collections.Generic.List<TodoItem> due = service.CollectDueReminders(DateTimeOffset.Now, _notified);
                if (due.Count > 0)
                {
                    Log.Write("reminder fired for " + due.Count + " item(s)");
                }

                foreach (TodoItem item in due)
                {
                    _notified.Add(item.Id);
                    string title = item.SnoozedUntil.HasValue ? "待办提醒" : "待办快到期了";
                    string body = item.Text + Environment.NewLine + item.DueText;
                    if (string.IsNullOrEmpty(item.DueText))
                    {
                        body = item.Text;
                    }

                    // Balloon may be suppressed by Windows notification settings,
                    // so we always show our own glass toast as well.
                    try
                    {
                        _tray.ShowBalloonTip(6000, title, body.Replace(Environment.NewLine, " · "), ToolTipIcon.Info);
                    }
                    catch
                    {
                    }

                    System.Threading.Tasks.Task.Run(() => PlayReminderSound());
                    ShowToast(title, body);
                }
            }
            catch (Exception ex)
            {
                Log.Write("reminder check failed: " + ex.Message);
            }
        }

        private void RunOnboarding()
        {
            using (var form = new OnboardingForm(_config, _hotkeyText, ApplyRecordedHotkey))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    OpenTodoWindow();
                }
            }

            SetAutoStart(_config.AutoStart);
            Log.Write("onboarding finished; version=" + _config.OnboardingVersion);
        }

        private void ApplyRecordedHotkey(int modifiers, Keys key)
        {
            string mods = HotkeyRecorderForm.ModifierConfigName(modifiers);
            _config.HotkeyModifiers = mods;
            _config.HotkeyKey = key.ToString();
            _config.Save();
            _hotkeyText = RegisterHotkeyWithFallback();
            Log.Write("hotkey from onboarding -> " + mods + "+" + key);
        }

        private void ShowAbout()
        {
            using (var form = new AboutForm(_config))
            {
                form.ShowDialog();
            }
        }

        private void OpenSettings()
        {
            using (var form = new SettingsForm(_config))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    _tray.ShowBalloonTip(2200, "设置已保存", "AI 解析相关设置已生效。", ToolTipIcon.Info);
                }
            }
        }

        private void RecordHotkey()
        {
            _hotkey.Unregister();
            try
            {
                using (var form = new HotkeyRecorderForm(_hotkeyText, _config.LiveBackdrop))
                {
                    if (form.ShowDialog() != DialogResult.OK)
                    {
                        RegisterHotkeyWithFallback();
                        return;
                    }

                    string modifiers = HotkeyRecorderForm.ModifierConfigName(form.Modifiers);
                    string key = form.KeyConfigName;
                    _config.HotkeyModifiers = modifiers;
                    _config.HotkeyKey = key;
                    _config.Save();

                    string registered = RegisterHotkeyWithFallback();
                    _hotkeyText = registered;


                    string body = registered == modifiers + "+" + key
                        ? "已设置：" + registered + "。现在按一下试试，看到托盘提示就说明没被别的软件抢。"
                        : "想要的组合注册失败，先用了 " + registered + "。";
                    Log.Write("hotkey recorded -> " + modifiers + "+" + key + " registered=" + registered);
                    _tray.ShowBalloonTip(3500, "DeskBoxToDo 快捷键", body, ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Log.Write("record hotkey failed: " + ex);
                RegisterHotkeyWithFallback();
            }
        }

        /// <summary>
        /// Ctrl+Alt+D is the default, but other apps claim some chords; walk a
        /// small list and persist whichever one Windows actually grants.
        /// </summary>
        private string RegisterHotkeyWithFallback()
        {
            string modifiers = _config.HotkeyModifiers;
            string configured = _config.HotkeyKey;
            if (_hotkey.Register(modifiers, configured))
            {
                return modifiers + "+" + configured;
            }

            string[] fallbacks = { "S", "J", "Q", "K", "Y", "N", "E" };
            foreach (string candidate in fallbacks)
            {
                if (string.Equals(candidate, configured, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (_hotkey.Register(modifiers, candidate))
                {
                    _config.HotkeyKey = candidate;
                    _config.Save();
                    Log.Write("hotkey fallback to " + modifiers + "+" + candidate);
                    return modifiers + "+" + candidate;
                }
            }

            Log.Write("no hotkey available");
            return "(不可用)";
        }

        private void CaptureAndPreview()
        {
            if (_busy)
            {
                // Watchdog: a stuck flag used to swallow every later press.
                if ((DateTime.UtcNow - _busySince).TotalSeconds > 30)
                {
                    Log.Write("busy watchdog reset after " + (DateTime.UtcNow - _busySince).TotalSeconds.ToString("0") + "s");
                    _busy = false;
                }
                else
                {
                    Log.Write("hotkey ignored: busy for " + (DateTime.UtcNow - _busySince).TotalSeconds.ToString("0.0") + "s");
                    return;
                }
            }

            _busy = true;
            _busySince = DateTime.UtcNow;
            Cursor previous = Cursor.Current;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                string source;
                string selected = SelectionCapture.Capture(out source);
                Cursor.Current = previous;
                Log.Write(string.Format(
                    "capture source={0} length={1}",
                    source,
                    selected == null ? 0 : selected.Length));
                ForegroundProbe.Sample("after-hotkey");

                if (string.IsNullOrWhiteSpace(selected))
                {
                    _tray.ShowBalloonTip(
                        2500,
                        "没有取到选中的文字",
                        "先选中一段文字（含作业通知的整条消息），再按快捷键。",
                        ToolTipIcon.Warning);
                    return;
                }

                TodoDraft draft = TodoParser.Parse(selected, _config, DateTimeOffset.Now);
                draft.Source = source;

                // Optional AI pass. It runs *after* the dialog is up, on the
                // thread pool: a slow model must never freeze the hotkey or the
                // window (that was the "pressed but nothing appears" cause).
                bool wantAi = AiParser.IsConfigured(_config) &&
                              _config.LlmAuto &&
                              (_config.LlmAlways || NeedsAi(draft, selected));
                bool keepExistingDue = draft.Due.HasValue && draft.TimeFound;
                using (var form = new PreviewForm(draft, _config))
                {
                    if (wantAi)
                    {
                        StartAiUpdate(form, selected, keepExistingDue);
                    }

                    DialogResult result = form.ShowDialog();
                    Log.Write("dialog result=" + result);
                    if (result != DialogResult.OK)
                    {
                        return;
                    }

                    bool deskBoxMode = TodoBackend.IsDeskBoxMode(_config);
                    try
                    {
                        TodoItem added = new TodoService(_config).Add(form.Result);
                        string addedId = added == null ? null : added.Id;
                        if (string.IsNullOrEmpty(addedId))
                        {
                            throw new InvalidOperationException("写入没有返回条目，详见日志");
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "写入失败：" + ex.Message,
                            "DeskBoxToDo",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }

                    string body = string.Format(
                        "{0}（{1} 项子任务）{2}{3}",
                        form.Result.Title,
                        form.Result.Steps.Count,
                        TodoParser.Describe(form.Result.Due),
                        deskBoxMode ? Environment.NewLine + "已写进 DeskBox，正在重启它" : "");
                    if (_todoWindow != null && !_todoWindow.IsDisposed)
                    {
                        _todoWindow.Reload();
                    }

                    _tray.ShowBalloonTip(
                        3000,
                        deskBoxMode ? "已加入 DeskBox" : "已加入待办",
                        body,
                        ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                Log.Write("capture failed: " + ex);
                MessageBox.Show("出错了：" + ex.Message, "DeskBoxToDo", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor.Current = previous;
                _busy = false;
            }
        }

        private void StartAiUpdate(PreviewForm form, string text, bool keepExistingDue)
        {
            form.SetAiBusy("AI 解析中…");
            Task<TodoDraft> task = AiParser.ParseAsync(text, _config);
            task.ContinueWith(completed =>
            {
                TodoDraft ai = null;
                try
                {
                    if (completed.Status == TaskStatus.RanToCompletion)
                    {
                        ai = completed.Result;
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("ai task failed: " + ex.Message);
                }

                try
                {
                    if (!form.IsDisposed && form.IsHandleCreated)
                    {
                        form.BeginInvoke(new Action(() => form.ApplyAiResult(ai, keepExistingDue)));
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("ai apply skipped: " + ex.Message);
                }
            });
        }

        /// <summary>Heuristic: call the model only when local rules look weak.</summary>
        private static bool NeedsAi(TodoDraft draft, string rawText)
        {
            if (!draft.Due.HasValue)
            {
                return true;
            }

            if (draft.Steps.Count == 0 && (rawText ?? "").Length > 24)
            {
                return true;
            }

            if (draft.Title.Length >= 26)
            {
                return true;
            }

            return false;
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    object value = key.GetValue(RunValueName);
                    return value != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        return;
                    }

                    if (enable)
                    {
                        key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    }
                    else
                    {
                        key.DeleteValue(RunValueName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("autostart update failed: " + ex.Message);
            }
        }

        private static void OpenPath(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show("文件还不存在：" + path, "DeskBoxToDo");
                }
            }
            catch (Exception ex)
            {
                Log.Write("open path failed: " + ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_activationWait != null) { _activationWait.Unregister(null); _activationWait = null; }
                if (_activation != null) { _activation.Dispose(); _activation = null; }
                _hotkey.Dispose();
                _tray.Visible = false;
                _tray.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
