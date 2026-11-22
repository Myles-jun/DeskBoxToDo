using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Grabs the text the user has selected in whatever window was in front:
    /// send Ctrl+C to it, then read the clipboard (1.x does the same thing).
    /// The clipboard sequence number is the guard: only text that appeared after
    /// our Ctrl+C is accepted, so a stale clipboard can never become a todo.
    /// </summary>
    internal static class SelectionCapture
    {
        private const uint CfUnicodeText = 13;
        private const byte VkControl = 0x11;
        private const byte VkMenu = 0x12;
        private const byte VkShift = 0x10;
        private const byte VkC = 0x43;
        private const uint KeyeventfKeyUp = 0x0002;
        private const uint MapvkVkToVsc = 0;

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint format);

        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // INPUT is a tagged union sized by its largest member (MOUSEINPUT); a
        // KEYBDINPUT-only struct makes SendInput fail with ERROR_INVALID_PARAMETER.
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint Type;
            public INPUTUNION Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)]
            public MOUSEINPUT Mouse;

            [FieldOffset(0)]
            public KEYBDINPUT Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int Dx;
            public int Dy;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

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
                uint sequenceBefore = GetClipboardSequenceNumber();

                for (int round = 0; round < 2; round++)
                {
                    // Inject from a plain worker thread: the UI thread owns windows
                    // and some shells ignore synthetic input sent from it.
                    var injector = new Thread(SendCopy) { IsBackground = true };
                    injector.Start();
                    injector.Join(600);
                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        Thread.Sleep(80);
                        if (GetClipboardSequenceNumber() != sequenceBefore)
                        {
                            break;
                        }
                    }

                    if (GetClipboardSequenceNumber() != sequenceBefore)
                    {
                        break;
                    }

                    Thread.Sleep(120);
                }

                if (GetClipboardSequenceNumber() == sequenceBefore)
                {
                    AppLog.Write("inject rounds done, clipboard still unchanged; trying WM_COPY");
                    SendWmCopy();
                    for (int attempt = 0; attempt < 6; attempt++)
                    {
                        Thread.Sleep(70);
                        if (GetClipboardSequenceNumber() != sequenceBefore)
                        {
                            break;
                        }
                    }
                }

                if (GetClipboardSequenceNumber() == sequenceBefore)
                {
                    // Last resort: the UIA text provider reads the selection without
                    // touching the clipboard. Bounded hard — a slow provider (a
                    // WebView2 window can take seconds) must not freeze the hotkey.
                    AppLog.Write("trying UIA");
                    string uiaText = TryReadViaUiaBounded(900);
                    if (!string.IsNullOrWhiteSpace(uiaText))
                    {
                        AppLog.Write("uia selection: " + uiaText.Length + " chars");
                        return uiaText;
                    }
                }

                if (GetClipboardSequenceNumber() == sequenceBefore)
                {
                    AppLog.Write("clipboard unchanged after Ctrl+C (seq " + sequenceBefore + "), treating as empty selection");
                    return "";
                }

                string text = ReadClipboard();
                Console.WriteLine("clipboard text: " + text.Length + " chars (seq " + sequenceBefore + "→" + GetClipboardSequenceNumber() + ")");
                return text;
            }
            catch (Exception ex)
            {
                Console.WriteLine("selection capture failed: " + ex.Message);
                return "";
            }
        }

        /// <summary>Reads the focused element's text selection through UI Automation.</summary>
        private static string TryReadViaUiaBounded(int timeoutMs)
        {
            string result = null;
            var worker = new Thread(() =>
            {
                try
                {
                    result = TryReadViaUia();
                }
                catch (Exception ex)
                {
                    AppLog.Write("uia threw: " + ex.Message);
                }
            })
            {
                IsBackground = true,
                Name = "DeskBoxToDo.Uia"
            };
            worker.Start();
            if (!worker.Join(timeoutMs))
            {
                AppLog.Write("uia timed out after " + timeoutMs + " ms");
                return "";
            }

            return result ?? "";
        }

        private static string TryReadViaUia()
        {
            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused == null)
                {
                    return "";
                }

                if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out object pattern) ||
                    pattern is not TextPattern textPattern)
                {
                    return "";
                }

                TextPatternRange[] ranges = textPattern.GetSelection();
                if (ranges == null || ranges.Length == 0)
                {
                    return "";
                }

                var builder = new StringBuilder();
                foreach (TextPatternRange range in ranges)
                {
                    string piece = range?.GetText(4096);
                    if (string.IsNullOrEmpty(piece))
                    {
                        continue;
                    }

                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.Append(piece);
                }

                return builder.ToString().Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine("uia read failed: " + ex.Message);
                return "";
            }
        }

        /// <summary>Diagnostic: injects a plain 'X' to see whether synthetic input lands at all.</summary>
        internal static void SendTestKey()
        {
            Thread.Sleep(150);
            keybd_event(VkMenu, 0, KeyeventfKeyUp, UIntPtr.Zero);
            keybd_event(VkControl, 0, KeyeventfKeyUp, UIntPtr.Zero);
            var inputs = new[] { MakeKey(0x58, false), MakeKey(0x58, true) };
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            Console.WriteLine("[Diag] test key sent=" + sent + " err=" + Marshal.GetLastWin32Error());
        }

        /// <summary>WM_COPY to the focused control — works for standard edit controls without input injection.</summary>
        private static void SendWmCopy()
        {
            try
            {
                IntPtr foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                {
                    return;
                }

                uint threadId = GetWindowThreadProcessId(foreground, out _);
                if (threadId == 0)
                {
                    return;
                }

                var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
                IntPtr focus = GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero
                    ? info.hwndFocus
                    : foreground;
                // SendMessageTimeout, not SendMessage: a busy browser would block the
                // hotkey for its full message timeout.
                SendMessageTimeout(focus, 0x0301 /* WM_COPY */, IntPtr.Zero, IntPtr.Zero, 0x0002, 400, out _);
            }
            catch (Exception ex)
            {
                Console.WriteLine("wm_copy failed: " + ex.Message);
            }
        }

        private static void SendCopy()
        {
            // Let the hotkey's Alt-up reach the foreground app first: injecting
            // Ctrl+C while Alt is still down arrives as Ctrl+Alt+C.
            Thread.Sleep(120);

            // Clear modifiers the hotkey may have left logically down.
            keybd_event(VkMenu, 0, KeyeventfKeyUp, UIntPtr.Zero);
            keybd_event(VkShift, 0, KeyeventfKeyUp, UIntPtr.Zero);
            keybd_event(VkControl, 0, KeyeventfKeyUp, UIntPtr.Zero);

            var inputs = new INPUT[4];
            inputs[0] = MakeKey(VkControl, false);
            inputs[1] = MakeKey(VkC, false);
            inputs[2] = MakeKey(VkC, true);
            inputs[3] = MakeKey(VkControl, true);

            // NOTE: do not AttachThreadInput here. Joining the foreground thread's
            // input queue while that app is busy (browsers especially) deadlocks the
            // hotkey handler, which is worse than a missed copy.
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (sent != inputs.Length)
            {
                Console.WriteLine("SendInput sent=" + sent + " err=" + Marshal.GetLastWin32Error() + ", falling back to keybd_event");
                keybd_event(VkControl, 0, 0, UIntPtr.Zero);
                keybd_event(VkC, 0, 0, UIntPtr.Zero);
                keybd_event(VkC, 0, KeyeventfKeyUp, UIntPtr.Zero);
                keybd_event(VkControl, 0, KeyeventfKeyUp, UIntPtr.Zero);
            }
        }

        private static INPUT MakeKey(byte vk, bool keyUp)
        {
            return new INPUT
            {
                Type = 1, // INPUT_KEYBOARD
                Data = new INPUTUNION
                {
                    Keyboard = new KEYBDINPUT
                    {
                        VirtualKey = vk,
                        ScanCode = (ushort)MapVirtualKey(vk, MapvkVkToVsc),
                        Flags = keyUp ? KeyeventfKeyUp : 0,
                        Time = 0,
                        ExtraInfo = IntPtr.Zero
                    }
                }
            };
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
                    Thread.Sleep(60);
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
