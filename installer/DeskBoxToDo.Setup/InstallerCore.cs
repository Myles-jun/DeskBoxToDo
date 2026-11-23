using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskBoxToDo.Setup
{
    internal static class InstallerCore
    {
        public const string ProductName = "DeskBoxToDo";
        public const string DisplayVersion = "2.0.5";
        public const string Publisher = "DeskBoxToDo";
        public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeskBoxToDo";
        public const string UninstallKeyPath32 = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\DeskBoxToDo";

        public static string DefaultDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            ProductName);

        public static string StartMenuShortcut => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            ProductName + ".lnk");

        public static string DesktopShortcut => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ProductName + ".lnk");

        /// <summary>Existing install directory, read from the uninstall entries (1.x or 2.x).</summary>
        public static string FindExistingDirectory()
        {
            foreach (string root in new[] { "HKCU", "HKLM", "HKLM32" })
            {
                try
                {
                    RegistryKey key = root switch
                    {
                        "HKCU" => Registry.CurrentUser.OpenSubKey(UninstallKeyPath),
                        "HKLM" => Registry.LocalMachine.OpenSubKey(UninstallKeyPath),
                        _ => Registry.LocalMachine.OpenSubKey(UninstallKeyPath32)
                    };

                    using (key)
                    {
                        string location = key?.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrWhiteSpace(location) &&
                            Directory.Exists(location.TrimEnd('\\')) &&
                            File.Exists(Path.Combine(location.TrimEnd('\\'), "DeskBoxToDo.exe")))
                        {
                            return location.TrimEnd('\\');
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        public static string FindExistingVersion()
        {
            foreach (string root in new[] { "HKCU", "HKLM" })
            {
                try
                {
                    RegistryKey key = root == "HKCU"
                        ? Registry.CurrentUser.OpenSubKey(UninstallKeyPath)
                        : Registry.LocalMachine.OpenSubKey(UninstallKeyPath);
                    using (key)
                    {
                        string version = key?.GetValue("DisplayVersion") as string;
                        if (!string.IsNullOrWhiteSpace(version))
                        {
                            return version;
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        /// <summary>Closes a running instance so its files can be replaced.</summary>
        public static int KillRunning()
        {
            int killed = 0;
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (!string.Equals(process.ProcessName, "DeskBoxToDo", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    process.Kill();
                    process.WaitForExit(4000);
                    killed++;
                }
                catch
                {
                }
            }

            return killed;
        }

        /// <summary>
        /// Probes the target before touching anything: a restricted caller (for
        /// example a process started from a chat client's sandbox) cannot write
        /// under %LOCALAPPDATA%, and a plain "Access denied" from deep inside the
        /// copy loop is useless to the user.
        /// </summary>
        public static bool CanWrite(string directory, out string error)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".dbt_write_probe");
                File.WriteAllText(probe, "probe");
                File.Delete(probe);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static void Install(string directory, bool desktopShortcut, bool keepExistingDesktopShortcut, Action<int, string> progress)
        {
            Directory.CreateDirectory(directory);

            progress?.Invoke(5, "关闭正在运行的 DeskBoxToDo…");
            KillRunning();

            // No staging directory at all: entries are streamed straight out of the
            // embedded archive into the install folder. %TEMP% (and even the parent
            // folder) can be write-protected for restricted callers, while the
            // install target itself must be writable anyway.
            progress?.Invoke(15, "解压安装文件…");
            using Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
                ?? throw new InvalidOperationException("安装包损坏：找不到内嵌文件");
            using var archive = new ZipArchive(resource, ZipArchiveMode.Read);

            int total = archive.Entries.Count;
            int done = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destination = Path.Combine(directory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destination);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                }

                done++;
                if (progress != null && total > 0 && (done % 8 == 0 || done == total))
                {
                    progress(15 + (int)(65.0 * done / total), "解压 " + entry.FullName);
                }
            }

            // 1.x shipped a screenshots folder that the new build does not use.
            TryDeleteDirectory(Path.Combine(directory, "screenshots"));

            progress?.Invoke(85, "注册卸载信息…");
            CopySelfAsUninstaller(directory);
            WriteUninstallEntry(directory);

            progress?.Invoke(92, "创建快捷方式…");
            string exe = Path.Combine(directory, "DeskBoxToDo.exe");
            CreateShortcut(StartMenuShortcut, exe, directory);
            if (desktopShortcut)
            {
                CreateShortcut(DesktopShortcut, exe, directory);
            }
            else if (!keepExistingDesktopShortcut)
            {
                TryDeleteFile(DesktopShortcut);
            }

            progress?.Invoke(100, "完成");
        }

        public static void Uninstall(string directory, Action<int, string> progress)
        {
            progress?.Invoke(10, "关闭 DeskBoxToDo…");
            KillRunning();

            progress?.Invoke(30, "删除快捷方式…");
            TryDeleteFile(StartMenuShortcut);
            TryDeleteFile(DesktopShortcut);

            progress?.Invoke(55, "移除卸载信息…");
            DeleteUninstallEntry();

            progress?.Invoke(70, "删除程序文件…");
            if (Directory.Exists(directory))
            {
                TryDeleteDirectory(directory);
            }

            progress?.Invoke(100, "卸载完成");
        }

        /// <summary>User data lives in %LOCALAPPDATA%\DeskBoxToDo and is intentionally kept.</summary>
        public static string DataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductName);

        private static void CopyTree(string source, string target, Action<int, string> progress, int from, int to)
        {
            string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            for (int index = 0; index < files.Length; index++)
            {
                string relative = Path.GetRelativePath(source, files[index]);
                string destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(files[index], destination, overwrite: true);

                if (progress != null && files.Length > 0)
                {
                    int percent = from + (int)((to - from) * (index + 1.0) / files.Length);
                    progress(Math.Min(to, percent), "复制 " + relative);
                }
            }
        }

        private static void CopySelfAsUninstaller(string directory)
        {
            string target = Path.Combine(directory, "Uninstall.exe");
            try
            {
                using Stream stub = Assembly.GetExecutingAssembly().GetManifestResourceStream("uninstaller.exe");
                if (stub == null)
                {
                    // Setup built without the stub: fall back to copying ourselves.
                    if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(Application.ExecutablePath), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(Application.ExecutablePath, target, overwrite: true);
                    }

                    return;
                }

                using FileStream file = File.Create(target);
                stub.CopyTo(file);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("copy uninstaller failed: " + ex.Message);
            }
        }

        private static void WriteUninstallEntry(string directory)
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath, writable: true);
                if (key == null)
                {
                    return;
                }

                string exe = Path.Combine(directory, "DeskBoxToDo.exe");
                key.SetValue("DisplayName", ProductName);
                key.SetValue("DisplayVersion", DisplayVersion);
                key.SetValue("Publisher", Publisher);
                key.SetValue("InstallLocation", directory);
                key.SetValue("DisplayIcon", exe + ",0");
                key.SetValue("UninstallString", "\"" + Path.Combine(directory, "Uninstall.exe") + "\" --uninstall");
                key.SetValue("QuietUninstallString", "\"" + Path.Combine(directory, "Uninstall.exe") + "\" --uninstall /S");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", EstimateSizeKb(directory), RegistryValueKind.DWord);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
            catch (Exception ex)
            {
                // A restricted caller may not be allowed to touch HKCU; the app is
                // still installed and can be removed by deleting its folder.
                Debug.WriteLine("uninstall entry failed: " + ex.Message);
            }
        }

        private static int EstimateSizeKb(string directory)
        {
            try
            {
                long bytes = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                    .Sum(path => new FileInfo(path).Length);
                return (int)Math.Max(1, bytes / 1024);
            }
            catch
            {
                return 1;
            }
        }

        private static void DeleteUninstallEntry()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
            }
            catch
            {
            }

            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
            }
            catch
            {
            }

            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath32, throwOnMissingSubKey: false);
            }
            catch
            {
            }
        }

        public static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic link = shell.CreateShortcut(shortcutPath);
                link.TargetPath = targetPath;
                link.WorkingDirectory = workingDirectory;
                link.IconLocation = targetPath + ",0";
                link.Description = "DeskBoxToDo — 选中一句话变成待办";
                link.Save();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("shortcut failed: " + ex.Message);
            }
        }

        public static bool TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public static bool TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public static void ScheduleSelfDelete(string path)
        {
            try
            {
                MoveFileEx(path, null, 0x4 /* MOVEFILE_DELAY_UNTIL_REBOOT */);
            }
            catch
            {
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existing, string replacement, int flags);
    }
}
