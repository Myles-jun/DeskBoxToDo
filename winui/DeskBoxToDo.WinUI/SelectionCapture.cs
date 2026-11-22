using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Grabs the text the user has selected in whatever window was in front:
    /// send Ctrl+C to it, then read the clipboard (1.x does the same thing).
    /// </summary>
    internal static class SelectionCapture
    {
        private const uint CfUnicodeText = 13;
        private const byte VkControl = 0x11;
        private const byte VkC = 0x43;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint format);

        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr handle);

        /// <summary>Copies the current selection of the foreground window and returns it.</summary>
        public static string CaptureFromForeground(IntPtr ourWindow)
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == ourWindow)
            {
                return "";
            }

            try
            {
                SendCopy(foreground);

                // The target app needs a moment to put the text on the clipboard;
                // retry a few times instead of reading once.
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    System.Threading.Thread.Sleep(90);
                    string text = ReadClipboard();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        Console.WriteLine("clipboard text: " + text.Length + " chars");
                        return text;
                    }
                }

                Console.WriteLine("clipboard still empty after retries");
                return "";
            }
            catch (Exception ex)
            {
                Console.WriteLine("selection capture failed: " + ex.Message);
                return "";
            }
        }

        private static void SendCopy(IntPtr target)
        {
            uint targetThread = GetWindowThreadProcessId(target, out _);
            uint myThread = GetCurrentThreadId();
            bool attached = targetThread != 0 && targetThread != myThread &&
                AttachThreadInput(myThread, targetThread, true);

            keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            keybd_event(VkC, 0, 0, UIntPtr.Zero);
            keybd_event(VkC, 0, 2, UIntPtr.Zero);
            keybd_event(VkControl, 0, 2, UIntPtr.Zero);

            if (attached)
            {
                AttachThreadInput(myThread, targetThread, false);
            }
        }

        private static string ReadClipboard()
        {
            if (!IsClipboardFormatAvailable(CfUnicodeText))
            {
                return "";
            }

            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    System.Threading.Thread.Sleep(60);
                    continue;
                }

                try
                {
                    IntPtr handle = GetClipboardData(CfUnicodeText);
                    if (handle == IntPtr.Zero)
                    {
                        return "";
                    }

                    IntPtr pointer = GlobalLock(handle);
                    if (pointer == IntPtr.Zero)
                    {
                        return "";
                    }

                    try
                    {
                        return Marshal.PtrToStringUni(pointer) ?? "";
                    }
                    finally
                    {
                        GlobalUnlock(handle);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }

            return "";
        }
    }
}
