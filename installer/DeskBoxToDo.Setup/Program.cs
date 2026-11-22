using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DeskBoxToDo.Setup
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = args.Any(a => a.Equals("/s", StringComparison.OrdinalIgnoreCase));
            bool uninstall = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                                           a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
            bool uninstallRun = args.Any(a => a.Equals("/uninstall-run", StringComparison.OrdinalIgnoreCase));

            if (uninstallRun)
            {
                RunUninstall(args);
                return;
            }

            if (uninstall)
            {
                if (!silent && MessageBox.Show(
                        "确定要卸载 DeskBoxToDo 吗？\n\n你的待办数据会保留在 %LOCALAPPDATA%\\DeskBoxToDo。",
                        "卸载 DeskBoxToDo",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }

                StartDetachedUninstall();
                return;
            }

            string directory = GetArgument(args, "/dir=") ?? InstallerCore.FindExistingDirectory() ?? InstallerCore.DefaultDirectory;
            bool desktop = args.Any(a => a.Equals("/desktop", StringComparison.OrdinalIgnoreCase));
            bool launch = args.Any(a => a.Equals("/launch", StringComparison.OrdinalIgnoreCase));

            if (silent)
            {
                try
                {
                    if (!InstallerCore.CanWrite(directory, out string error))
                    {
                        WriteErrorLog(new UnauthorizedAccessException(directory + ": " + error));
                        Environment.ExitCode = 2;
                        return;
                    }

                    InstallerCore.Install(directory, desktop, null);
                    if (launch)
                    {
                        Process.Start(new ProcessStartInfo(Path.Combine(directory, "DeskBoxToDo.exe")) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    WriteErrorLog(ex);
                    Environment.ExitCode = 1;
                }

                return;
            }

            Application.Run(new SetupForm(directory, desktop, launch));
        }

        /// <summary>Writes next to the exe: %TEMP% can be write-protected for the caller.</summary>
        private static void WriteErrorLog(Exception ex)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(AppContext.BaseDirectory, "setup_error.log"),
                    DateTime.Now + Environment.NewLine + ex);
            }
            catch
            {
            }
        }

        private static string GetArgument(string[] args, string prefix)
        {
            foreach (string arg in args)
            {
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(prefix.Length).Trim('"');
                }
            }

            return null;
        }

        /// <summary>
        /// Copies the uninstaller out of the install folder first: a running exe
        /// cannot delete the directory that contains it.
        /// </summary>
        private static void StartDetachedUninstall()
        {
            string directory = Path.GetDirectoryName(Application.ExecutablePath) ?? InstallerCore.DefaultDirectory;
            string temp = Path.Combine(
                Path.GetDirectoryName(directory.TrimEnd('\\')) ?? Path.GetTempPath(),
                ".dbt_uninstall_" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                File.Copy(Application.ExecutablePath, temp, overwrite: true);
                Process.Start(new ProcessStartInfo(temp, "/uninstall-run \"" + directory + "\"")
                {
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                // Last resort: remove everything we can and let a reboot drop the rest.
                try
                {
                    InstallerCore.Uninstall(directory, null);
                    InstallerCore.ScheduleSelfDelete(Application.ExecutablePath);
                }
                catch
                {
                }

                if (!Environment.GetCommandLineArgs().Any(a => a.Equals("/s", StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("卸载已执行（部分文件可能需要在重启后删除）：" + ex.Message,
                        "DeskBoxToDo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private static void RunUninstall(string[] args)
        {
            string directory = args.FirstOrDefault(a => !a.StartsWith("/", StringComparison.Ordinal) &&
                                                        !a.StartsWith("--", StringComparison.Ordinal))
                ?? Path.GetDirectoryName(Application.ExecutablePath);

            try
            {
                InstallerCore.Uninstall(directory, null);
            }
            catch (Exception ex)
            {
                WriteErrorLog(ex);
            }

            // The temp copy cannot delete itself while it runs.
            InstallerCore.ScheduleSelfDelete(Application.ExecutablePath);
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly TextBox _pathBox = new TextBox();
        private readonly Label _status = new Label();
        private readonly Label _existing = new Label();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly CheckBox _desktop = new CheckBox();
        private readonly CheckBox _launch = new CheckBox();
        private readonly Button _install = new Button();
        private readonly Button _cancel = new Button();
        private bool _busy;

        private static readonly Color Background = Color.FromArgb(0x1E, 0x1B, 0x24);
        private static readonly Color Surface = Color.FromArgb(0x2A, 0x26, 0x33);
        private static readonly Color TextPrimary = Color.FromArgb(0xF2, 0xEE, 0xF8);
        private static readonly Color TextSecondary = Color.FromArgb(0xB9, 0xB2, 0xC7);
        private static readonly Color Accent = Color.FromArgb(0xA8, 0x8B, 0xFF);

        private readonly bool _launchAfter;
        private readonly bool _desktopShortcut;

        public SetupForm(string directory, bool desktopShortcut, bool launchAfter)
        {
            _desktopShortcut = desktopShortcut;
            _launchAfter = launchAfter;

            Text = "DeskBoxToDo " + InstallerCore.DisplayVersion + " 安装程序";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 360);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Microsoft YaHei UI", 9f);
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            var title = new Label
            {
                Text = "DeskBoxToDo " + InstallerCore.DisplayVersion,
                Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold),
                ForeColor = TextPrimary,
                AutoSize = true,
                Location = new Point(24, 22)
            };
            Controls.Add(title);

            var subtitle = new Label
            {
                Text = "选中一句话，Alt+Q 变成待办",
                Font = new Font("Microsoft YaHei UI", 9.5f),
                ForeColor = TextSecondary,
                AutoSize = true,
                Location = new Point(26, 62)
            };
            Controls.Add(subtitle);

            string existingVersion = InstallerCore.FindExistingVersion();
            string existingDir = InstallerCore.FindExistingDirectory();
            _existing.Text = existingVersion != null
                ? "检测到已安装 " + existingVersion + "，将覆盖安装到原目录（待办数据保留）"
                : "将安装到当前用户目录，无需管理员权限";
            _existing.ForeColor = existingVersion != null ? Accent : TextSecondary;
            _existing.Font = new Font("Microsoft YaHei UI", 9f);
            _existing.AutoSize = false;
            _existing.Size = new Size(470, 36);
            _existing.Location = new Point(26, 90);
            Controls.Add(_existing);

            var pathLabel = new Label
            {
                Text = "安装位置",
                ForeColor = TextSecondary,
                AutoSize = true,
                Location = new Point(26, 140)
            };
            Controls.Add(pathLabel);

            _pathBox.Text = directory;
            _pathBox.Location = new Point(26, 162);
            _pathBox.Size = new Size(380, 26);
            _pathBox.BackColor = Surface;
            _pathBox.ForeColor = TextPrimary;
            _pathBox.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(_pathBox);

            var browse = new Button
            {
                Text = "浏览…",
                Location = new Point(414, 161),
                Size = new Size(80, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Surface,
                ForeColor = TextPrimary
            };
            browse.FlatAppearance.BorderColor = Color.FromArgb(0x44, 0x3E, 0x50);
            browse.Click += (sender, args) => Browse();
            Controls.Add(browse);

            _desktop.Text = "创建桌面快捷方式";
            _desktop.ForeColor = TextSecondary;
            _desktop.AutoSize = true;
            _desktop.Checked = desktopShortcut;
            _desktop.Location = new Point(26, 202);
            Controls.Add(_desktop);

            _launch.Text = "安装完成后启动 DeskBoxToDo";
            _launch.ForeColor = TextSecondary;
            _launch.AutoSize = true;
            _launch.Checked = launchAfter;
            _launch.Location = new Point(220, 202);
            Controls.Add(_launch);

            _progress.Location = new Point(26, 240);
            _progress.Size = new Size(468, 8);
            _progress.Style = ProgressBarStyle.Continuous;
            Controls.Add(_progress);

            _status.Text = "准备就绪";
            _status.ForeColor = TextSecondary;
            _status.AutoSize = false;
            _status.Size = new Size(468, 22);
            _status.Location = new Point(26, 252);
            Controls.Add(_status);

            _install.Text = "安装";
            _install.Location = new Point(316, 296);
            _install.Size = new Size(84, 34);
            _install.FlatStyle = FlatStyle.Flat;
            _install.BackColor = Accent;
            _install.ForeColor = Color.FromArgb(0x1A, 0x16, 0x22);
            _install.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            _install.FlatAppearance.BorderSize = 0;
            _install.Click += async (sender, args) => await InstallAsync();
            Controls.Add(_install);

            _cancel.Text = "取消";
            _cancel.Location = new Point(410, 296);
            _cancel.Size = new Size(84, 34);
            _cancel.FlatStyle = FlatStyle.Flat;
            _cancel.BackColor = Surface;
            _cancel.ForeColor = TextPrimary;
            _cancel.FlatAppearance.BorderColor = Color.FromArgb(0x44, 0x3E, 0x50);
            _cancel.Click += (sender, args) => Close();
            Controls.Add(_cancel);
        }

        private void Browse()
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = _pathBox.Text, ShowNewFolderButton = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _pathBox.Text = dialog.SelectedPath;
            }
        }

        private async Task InstallAsync()
        {
            if (_busy)
            {
                return;
            }

            string directory = _pathBox.Text.Trim();
            if (directory.Length == 0)
            {
                MessageBox.Show(this, "请选择安装位置", "DeskBoxToDo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _busy = true;
            _install.Enabled = false;
            _cancel.Enabled = false;
            _pathBox.Enabled = false;
            _desktop.Enabled = false;
            _launch.Enabled = false;

            if (!InstallerCore.CanWrite(directory, out string probeError))
            {
                _busy = false;
                _install.Enabled = true;
                _cancel.Enabled = true;
                _pathBox.Enabled = true;
                _desktop.Enabled = true;
                _launch.Enabled = true;
                _status.Text = "目标目录不可写";
                MessageBox.Show(
                    this,
                    "没有写入这个目录的权限：\n" + directory + "\n\n" + probeError +
                    "\n\n最常见的原因：安装程序是在聊天窗口 / 沙箱里点开的，这类进程不允许写 %LOCALAPPDATA%。\n" +
                    "请关掉本窗口，到「文件资源管理器」里双击安装程序再运行；或者点「浏览」换一个可写目录。",
                    "DeskBoxToDo " + InstallerCore.DisplayVersion + " 安装程序",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Action<int, string> report = (percent, message) =>
            {
                if (IsDisposed)
                {
                    return;
                }

                BeginInvoke(new Action(() =>
                {
                    _progress.Value = Math.Clamp(percent, 0, 100);
                    _status.Text = message;
                }));
            };

            try
            {
                await Task.Run(() => InstallerCore.Install(directory, _desktop.Checked, report));

                _progress.Value = 100;
                _status.Text = "安装完成";

                if (_launch.Checked)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(Path.Combine(directory, "DeskBoxToDo.exe")) { UseShellExecute = true });
                    }
                    catch
                    {
                    }
                }

                Close();
            }
            catch (Exception ex)
            {
                _busy = false;
                _install.Enabled = true;
                _cancel.Enabled = true;
                _pathBox.Enabled = true;
                _desktop.Enabled = true;
                _launch.Enabled = true;
                _status.Text = "安装失败";
                MessageBox.Show(this, "安装失败：" + ex.Message, "DeskBoxToDo", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
