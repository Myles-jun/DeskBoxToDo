using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace DeskBoxToDo.Setup
{
    internal static class InstallerProgram
    {
        public const string ProductName = "DeskBoxToDo";
        public const string Version = "1.5.3";
        public const string Publisher = "DeskBoxToDo";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeskBoxToDo";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string PayloadResource = "payload.zip";

        public static string DefaultInstallDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    ProductName);
            }
        }

        private static void Trace(string message)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DeskBoxToDo_setup.log"),
                    DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Trace("start args=" + string.Join(" ", args));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = false;
            bool uninstall = false;
            string target = null;
            string uninstallDir = null;

            foreach (string arg in args)
            {
                string value = arg ?? "";
                if (value.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("--silent", StringComparison.OrdinalIgnoreCase))
                {
                    silent = true;
                }
                else if (value.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                         value.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    uninstall = true;
                }
                else if (value.StartsWith("--uninstall-dir=", StringComparison.OrdinalIgnoreCase))
                {
                    uninstallDir = value.Substring("--uninstall-dir=".Length).Trim('"');
                }
                else if (value.StartsWith("/D=", StringComparison.OrdinalIgnoreCase))
                {
                    target = value.Substring(3).Trim('"');
                }
            }

            try
            {
                if (!string.IsNullOrEmpty(uninstallDir))
                {
                    return InstallLogic.CleanupDirectory(uninstallDir);
                }

                if (uninstall)
                {
                    return silent ? InstallLogic.UninstallSilent() : RunUninstallUi();
                }

                if (silent)
                {
                    Trace("silent install -> " + (target ?? DefaultInstallDir));
                    int code = InstallLogic.Install(target ?? DefaultInstallDir, true, true, false, null);
                    Trace("silent install done code=" + code);
                    return code;
                }

                using (var form = new InstallerForm())
                {
                    Application.Run(form);
                }

                return 0;
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show("安装程序出错：" + ex.Message, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return 1;
            }
        }

        private static int RunUninstallUi()
        {
            DialogResult answer = MessageBox.Show(
                "要卸载 DeskBoxToDo 吗？\r\n\r\n会删除程序文件和快捷方式，保留你的配置与待办数据。",
                ProductName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                return 0;
            }

            return InstallLogic.UninstallSilent();
        }
    }

    internal static class InstallLogic
    {
        public static string InstalledExePath(string dir)
        {
            return Path.Combine(dir, "DeskBoxToDo.exe");
        }

        public static int Install(string directory, bool desktopShortcut, bool autoStart, bool launch, Action<string, int> report)
        {
            Report(report, "准备安装目录…", 5);
            Directory.CreateDirectory(directory);

            Report(report, "结束正在运行的程序…", 12);
            StopRunningApp(directory);

            Report(report, "清理旧版本文件…", 20);
            ClearOldFiles(directory);

            Report(report, "解压程序文件…", 30);
            ExtractPayload(directory);

            Report(report, "布置卸载程序…", 62);
            string uninstaller = Path.Combine(directory, "Uninstall.exe");
            try
            {
                File.Copy(Application.ExecutablePath, uninstaller, true);
            }
            catch
            {
            }

            Report(report, "创建快捷方式…", 70);
            string exe = InstalledExePath(directory);
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), InstallerProgram.ProductName + ".lnk"),
                exe,
                directory);

            if (desktopShortcut)
            {
                CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), InstallerProgram.ProductName + ".lnk"),
                    exe,
                    directory);
            }

            Report(report, "写入注册信息…", 82);
            WriteUninstallEntry(directory, exe);
            SetAutoStart(autoStart, exe);

            Report(report, "完成", 100);
            if (launch && File.Exists(exe))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
                }
                catch
                {
                }
            }

            return 0;
        }

        private static void Report(Action<string, int> report, string message, int percent)
        {
            if (report != null)
            {
                report(message, percent);
            }
        }

        private static void StopRunningApp(string directory)
        {
            foreach (Process process in Process.GetProcessesByName("DeskBoxToDo"))
            {
                try
                {
                    string path = process.MainModule == null ? "" : process.MainModule.FileName;
                    if (string.IsNullOrEmpty(path) ||
                        path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                }
                catch
                {
                }
            }
        }

        /// <summary>Removes files left by a previous version so nothing stale survives.</summary>
        private static void ClearOldFiles(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return;
                }

                foreach (string file in Directory.GetFiles(directory))
                {
                    try { File.Delete(file); } catch { }
                }

                foreach (string sub in Directory.GetDirectories(directory))
                {
                    try { Directory.Delete(sub, true); } catch { }
                }
            }
            catch
            {
            }
        }

        /// <summary>Path of an already installed copy, if any.</summary>
        public static string ExistingInstallDir()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(InstallerProgram.UninstallKey))
                {
                    if (key != null)
                    {
                        string location = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrWhiteSpace(location) && Directory.Exists(location))
                        {
                            return location;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        public static string ExistingVersion()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(InstallerProgram.UninstallKey))
                {
                    return key == null ? null : key.GetValue("DisplayVersion") as string;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void ExtractPayload(string directory)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string resource = null;
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith(InstallerProgram.PayloadResource, StringComparison.OrdinalIgnoreCase))
                {
                    resource = name;
                    break;
                }
            }

            if (resource == null)
            {
                throw new InvalidOperationException("安装包缺少程序文件");
            }

            string temp = Path.Combine(Path.GetTempPath(), "DeskBoxToDo_payload_" + Guid.NewGuid().ToString("N") + ".zip");
            using (Stream source = assembly.GetManifestResourceStream(resource))
            using (FileStream target = File.Create(temp))
            {
                source.CopyTo(target);
            }

            try
            {
                ZipFile.ExtractToDirectory(temp, directory);
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }

        private static void CreateShortcut(string linkPath, string exePath, string workingDirectory)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return;
                }

                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember(
                    "CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { exePath + ",0" });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "选中一句话，直接变成待办" });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch
            {
            }
        }

        private static void WriteUninstallEntry(string directory, string exePath)
        {
            using (Microsoft.Win32.RegistryKey key =
                Microsoft.Win32.Registry.CurrentUser.CreateSubKey(InstallerProgram.UninstallKey))
            {
                if (key == null)
                {
                    return;
                }

                key.SetValue("DisplayName", InstallerProgram.ProductName);
                key.SetValue("DisplayVersion", InstallerProgram.Version);
                key.SetValue("Publisher", InstallerProgram.Publisher);
                key.SetValue("InstallLocation", directory);
                key.SetValue("DisplayIcon", exePath + ",0");
                key.SetValue("UninstallString", "\"" + Path.Combine(directory, "Uninstall.exe") + "\" --uninstall");
                key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", EstimateSize(directory), Microsoft.Win32.RegistryValueKind.DWord);
            }

            using (Microsoft.Win32.RegistryKey key =
                Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\DeskBoxToDo"))
            {
                if (key != null)
                {
                    key.SetValue("InstallDir", directory);
                    key.SetValue("Installer", Application.ExecutablePath);
                }
            }
        }

        private static int EstimateSize(string directory)
        {
            try
            {
                long total = 0;
                foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    total += new FileInfo(file).Length;
                }

                return (int)Math.Min(int.MaxValue, total / 1024);
            }
            catch
            {
                return 0;
            }
        }

        public static void SetAutoStart(bool enabled, string exePath)
        {
            using (Microsoft.Win32.RegistryKey key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(InstallerProgram.RunKey, true))
            {
                if (key == null)
                {
                    return;
                }

                if (enabled)
                {
                    key.SetValue(InstallerProgram.ProductName, "\"" + exePath + "\"");
                }
                else
                {
                    key.DeleteValue(InstallerProgram.ProductName, false);
                }
            }
        }

        public static int UninstallSilent()
        {
            string directory = null;
            using (Microsoft.Win32.RegistryKey key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\DeskBoxToDo"))
            {
                if (key != null)
                {
                    directory = key.GetValue("InstallDir") as string;
                }
            }

            if (string.IsNullOrEmpty(directory))
            {
                directory = InstallerProgram.DefaultInstallDir;
            }

            StopRunningApp(directory);
            SetAutoStart(false, InstalledExePath(directory));

            TryDelete(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                InstallerProgram.ProductName + ".lnk"));
            TryDelete(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                InstallerProgram.ProductName + ".lnk"));

            string selfPath = Application.ExecutablePath;
            bool selfInside = selfPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(directory))
                {
                    foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                    {
                        if (string.Equals(file, selfPath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        try { File.Delete(file); } catch { }
                    }

                    if (!selfInside)
                    {
                        Directory.Delete(directory, true);
                    }
                }
            }
            catch
            {
            }

            if (selfInside)
            {
                try
                {
                    string temp = Path.Combine(Path.GetTempPath(), "DeskBoxToDo_uninstall_" + Guid.NewGuid().ToString("N") + ".exe");
                    File.Copy(selfPath, temp, true);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = temp,
                        Arguments = "--uninstall-dir=\"" + directory + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                catch
                {
                }
            }

            if (false)
            {
                // Let cmd clean up the folder after this process exits.
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + directory + "\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch
                {
                }
            }

            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(InstallerProgram.UninstallKey, false);
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\DeskBoxToDo", false);

            return 0;
        }

        public static int CleanupDirectory(string directory)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, true);
                    }

                    return 0;
                }
                catch
                {
                    System.Threading.Thread.Sleep(300);
                }
            }

            return 0;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
