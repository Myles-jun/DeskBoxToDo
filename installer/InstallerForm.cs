using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DeskBoxToDo.Setup
{
    /// <summary>Minimal glass installer: path, shortcuts, autostart, progress.</summary>
    internal sealed class InstallerForm : Form
    {
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Label _pathLabel;
        private readonly TextBox _pathBox;
        private readonly Button _browse;
        private readonly CheckBox _desktop;
        private readonly CheckBox _autoStart;
        private readonly CheckBox _launch;
        private readonly Button _install;
        private readonly Button _cancel;
        private readonly ProgressBar _progress;
        private readonly Label _status;
        private bool _working;
        private bool _finished;

        public InstallerForm()
        {
            string existingDir = InstallLogic.ExistingInstallDir();
            string existingVersion = InstallLogic.ExistingVersion();

            Text = "安装 DeskBoxToDo";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 380);
            BackColor = Color.FromArgb(32, 36, 46);
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);

            _title = new Label
            {
                Text = "DeskBoxToDo",
                Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold),
                ForeColor = Color.FromArgb(242, 244, 248),
                AutoSize = false
            };

            _subtitle = new Label
            {
                Text = existingVersion == null
                    ? "选中一句话，按快捷键就变成待办。安装到本机，不写系统目录。"
                    : "检测到已安装 " + existingVersion + "，将覆盖安装为 " + InstallerProgram.Version + "（待办数据保留）。",
                ForeColor = Color.FromArgb(196, 202, 214),
                AutoSize = false
            };

            _pathLabel = new Label
            {
                Text = "安装位置",
                ForeColor = Color.FromArgb(196, 202, 214),
                AutoSize = false
            };

            _pathBox = new TextBox
            {
                Text = existingDir ?? InstallerProgram.DefaultInstallDir,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(48, 54, 68),
                ForeColor = Color.FromArgb(238, 242, 250)
            };

            _browse = MakeButton("浏览…", false);
            _browse.Click += (sender, args) =>
            {
                using (var dialog = new FolderBrowserDialog { SelectedPath = _pathBox.Text })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        _pathBox.Text = Path.Combine(dialog.SelectedPath, InstallerProgram.ProductName);
                    }
                }
            };

            _desktop = new CheckBox { Text = "创建桌面快捷方式", Checked = true, ForeColor = Color.FromArgb(226, 230, 240) };
            _autoStart = new CheckBox { Text = "开机自动启动（随时可按快捷键）", Checked = true, ForeColor = Color.FromArgb(226, 230, 240) };
            _launch = new CheckBox { Text = "安装完成后立即启动", Checked = true, ForeColor = Color.FromArgb(226, 230, 240) };

            _install = MakeButton("安装", true);
            _install.Click += (sender, args) =>
            {
                if (_finished)
                {
                    Close();
                    return;
                }

                StartInstall();
            };

            _cancel = MakeButton("取消", false);
            _cancel.Click += (sender, args) => Close();

            _progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0 };
            _status = new Label { Text = "准备就绪", ForeColor = Color.FromArgb(196, 202, 214), AutoSize = false };

            Controls.AddRange(new Control[]
            {
                _title, _subtitle, _pathLabel, _pathBox, _browse,
                _desktop, _autoStart, _launch, _progress, _status, _install, _cancel
            });

            _title.SetBounds(24, 18, 460, 34);
            _subtitle.SetBounds(24, 54, 470, 22);
            _pathLabel.SetBounds(24, 92, 120, 20);
            _pathBox.SetBounds(24, 114, 380, 24);
            _browse.SetBounds(412, 113, 84, 26);
            _desktop.SetBounds(24, 152, 300, 22);
            _autoStart.SetBounds(24, 178, 360, 22);
            _launch.SetBounds(24, 204, 300, 22);
            _progress.SetBounds(24, 244, 472, 18);
            _status.SetBounds(24, 268, 472, 20);
            _cancel.SetBounds(300, 320, 90, 32);
            _install.SetBounds(400, 320, 96, 32);
        }

        private static Button MakeButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(86, 142, 247) : Color.FromArgb(58, 64, 82),
                ForeColor = Color.FromArgb(245, 247, 252),
                Font = new Font("Microsoft YaHei UI", 9f, primary ? FontStyle.Bold : FontStyle.Regular),
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = primary
                ? Color.FromArgb(110, 160, 255)
                : Color.FromArgb(90, 98, 120);
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(110, 160, 255)
                : Color.FromArgb(78, 86, 108);
            return button;
        }

        private void StartInstall()
        {
            if (_working)
            {
                return;
            }

            string directory = _pathBox.Text.Trim();
            if (directory.Length == 0)
            {
                MessageBox.Show(this, "请填写安装位置。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _working = true;
            SetEnabledState(false);
            System.Threading.ThreadPool.QueueUserWorkItem(state =>
            {
                try
                {
                    InstallLogic.Install(
                        directory,
                        _desktop.Checked,
                        _autoStart.Checked,
                        _launch.Checked,
                        (message, percent) => BeginInvoke(new Action(() =>
                        {
                            _status.Text = message;
                            _progress.Value = Math.Max(0, Math.Min(100, percent));
                        })));
                    BeginInvoke(new Action(() =>
                    {
                        _status.Text = "安装完成";
                        _finished = true;
                        _install.Text = "完成";
                        _working = false;
                        _install.Enabled = true;
                        _cancel.Enabled = true;
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() =>
                    {
                        _working = false;
                        SetEnabledState(true);
                        MessageBox.Show(this, "安装失败：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            });
        }

        private void SetEnabledState(bool enabled)
        {
            _pathBox.Enabled = enabled;
            _browse.Enabled = enabled;
            _desktop.Enabled = enabled;
            _autoStart.Enabled = enabled;
            _launch.Enabled = enabled;
            _install.Enabled = enabled;
            _cancel.Enabled = enabled;
        }
    }
}
