using System;
using System.IO;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Append-only diagnostic log next to the data (1.x wrote DeskBoxToDo.log too).
    /// Kept tiny and never throws: a failed log write must not break a hotkey press.
    /// </summary>
    internal static class AppLog
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 512 * 1024;

        public static string Path => System.IO.Path.Combine(TodoStore.DataRoot, "DeskBoxToDo.log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(TodoStore.DataRoot);
                    string file = Path;
                    var info = new FileInfo(file);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        File.Move(file, file + ".old", overwrite: true);
                    }

                    File.AppendAllText(file, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
                }
            }
            catch
            {
            }
        }
    }
}
