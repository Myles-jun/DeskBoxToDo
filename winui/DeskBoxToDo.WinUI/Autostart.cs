using System;
using Microsoft.Win32;

namespace DeskBoxToDo.WinUI
{
    /// <summary>HKCU Run entry, the same mechanism the 1.x build used.</summary>
    internal static class Autostart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DeskBoxToDo";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                    string value = key?.GetValue(ValueName) as string;
                    return !string.IsNullOrWhiteSpace(value);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void Set(bool enabled)
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (key == null)
                {
                    return;
                }

                if (enabled)
                {
                    string exe = Environment.ProcessPath ?? AppContext.BaseDirectory + "DeskBoxToDo.exe";
                    key.SetValue(ValueName, "\"" + exe + "\"", RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] autostart write failed: " + ex.Message);
            }
        }
    }
}
