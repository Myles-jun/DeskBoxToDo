using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>
    /// Glass hotkey recorder: captures the next physical chord with a low-level
    /// keyboard hook, so the user can pick a combination no other app
    /// (screenshot tools love Alt+letter) is already eating.
    /// </summary>
    internal sealed class HotkeyRecorderForm : GlassForm
    {
        private const int WhKeyboardLl = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;

        private readonly LowLevelKeyboardProc _hookProc;
        private IntPtr _hook;
        private readonly Label _chordLabel;
        private readonly Label _hintLabel;
        private readonly List<Label> _labels = new List<Label>();

        public int Modifiers { get; private set; }
        public Keys Key { get; private set; }

        /// <summary>Enum name suitable for config.json (e.g. "Oemcomma").</summary>
        public string KeyConfigName
        {
            get { return Key.ToString(); }
        }

        public static string ModifierConfigName(int mods)
        {
            var parts = new List<string>();
            if ((mods & 2) != 0) parts.Add("Ctrl");
            if ((mods & 1) != 0) parts.Add("Alt");
            if ((mods & 4) != 0) parts.Add("Shift");
            if ((mods & 8) != 0) parts.Add("Win");
            return string.Join("+", parts.ToArray());
        }

        public HotkeyRecorderForm(string current, bool liveBackdrop)
        {
            _hookProc = HookCallback;

            Ui.InitScale();
            CornerRadius = Ui.S(16);
            LiveBackdrop = liveBackdrop;

            Text = "设置快捷键";
            ClientSize = new Size(Ui.S(440), Ui.S(250));
            Font = Ui.Font(9f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;

            var title = new Label
            {
                Text = "设置快捷键",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.Text,
                Font = Ui.Font(13f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var closeButton = new GlassButton("✕", false) { Font = Ui.Font(10.5f, FontStyle.Regular), Ghost = true };
            closeButton.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            var hint = new Label
            {
                Text = "按下想用的组合键（必须含 Ctrl / Alt / Shift / Win 之一），按 Esc 取消。",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.6f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _labels.Add(hint);

            _chordLabel = new Label
            {
                Text = "当前：" + current,
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.Text,
                Font = Ui.Font(15f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            _hintLabel = new Label
            {
                Text = "按下后立即生效",
                AutoSize = false,
                BackColor = Color.Transparent,
                ForeColor = Ui.TextMuted,
                Font = Ui.Font(8.4f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _labels.Add(_hintLabel);

            var cancel = new GlassButton("取消", false);
            cancel.Click += (sender, args) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { title, closeButton, hint, _chordLabel, _hintLabel, cancel });

            int pad = Ui.S(24);
            int width = ClientSize.Width;
            title.SetBounds(pad, Ui.S(18), width - pad * 2 - Ui.S(40), Ui.S(34));
            closeButton.SetBounds(width - pad - Ui.S(30), Ui.S(20), Ui.S(30), Ui.S(30));
            hint.SetBounds(pad, Ui.S(62), width - pad * 2, Ui.S(40));
            _chordLabel.SetBounds(pad, Ui.S(108), width - pad * 2, Ui.S(46));
            _hintLabel.SetBounds(pad, Ui.S(156), width - pad * 2, Ui.S(24));
            cancel.SetBounds(width - pad - Ui.S(96), Ui.S(196), Ui.S(96), Ui.S(38));

            PlaceCenter();
            PrepareBackdrop();

            Load += (sender, args) =>
            {
                _hook = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
            };
            FormClosed += (sender, args) => UninstallHook();
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
            foreach (Label label in _labels)
            {
                label.ForeColor = Ui.TextMuted;
            }

            _chordLabel.ForeColor = Ui.Text;
            Invalidate(true);
        }

        private void UninstallHook()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && (wParam.ToInt32() == WmKeyDown || wParam.ToInt32() == WmSysKeyDown))
            {
                int vk = Marshal.ReadInt32(lParam);
                var key = (Keys)vk;

                if (key == Keys.Escape)
                {
                    BeginInvoke(new Action(() => { DialogResult = DialogResult.Cancel; Close(); }));
                    return (IntPtr)1;
                }

                bool isModifier = key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                                  key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu ||
                                  key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                                  key == Keys.LWin || key == Keys.RWin;

                int mods = 0;
                if ((GetKeyState(0x11) & 0x8000) != 0) mods |= 2;   // VK_CONTROL
                if ((GetKeyState(0x12) & 0x8000) != 0) mods |= 1;   // VK_MENU
                if ((GetKeyState(0x10) & 0x8000) != 0) mods |= 4;   // VK_SHIFT
                if ((GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0) mods |= 8;

                if (!isModifier && mods != 0)
                {
                    string text = Describe(mods, key);
                    BeginInvoke(new Action(() =>
                    {
                        Modifiers = mods;
                        Key = key;
                        _chordLabel.Text = text;
                        _hintLabel.Text = "已记录，正在保存…";
                        DialogResult = DialogResult.OK;
                        Close();
                    }));
                    return (IntPtr)1;
                }

                if (!isModifier && mods == 0)
                {
                    BeginInvoke(new Action(() => { _hintLabel.Text = "需要一个修饰键：Ctrl / Alt / Shift / Win"; }));
                }
            }

            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        public static string Describe(int mods, Keys key)
        {
            var builder = new StringBuilder();
            if ((mods & 2) != 0) builder.Append("Ctrl + ");
            if ((mods & 1) != 0) builder.Append("Alt + ");
            if ((mods & 4) != 0) builder.Append("Shift + ");
            if ((mods & 8) != 0) builder.Append("Win + ");
            builder.Append(KeyName(key));
            return builder.ToString();
        }

        private static string KeyName(Keys key)
        {
            switch (key)
            {
                case Keys.Space: return "Space";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.Oemtilde: return "`";
                case Keys.OemQuestion: return "/";
                default: return key.ToString();
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
