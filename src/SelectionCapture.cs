using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace DeskBoxToDo
{
    /// <summary>
    /// Fetches the text the user has selected in whatever app has focus.
    /// UI Automation first (no clipboard side effects); falls back to a
    /// simulated Ctrl+C with clipboard save/restore.
    ///
    /// The hotkey arrives on key-down, so the modifiers are usually still
    /// held when we get here; every clipboard simulation waits for the user
    /// to let go first, otherwise Qt/Electron apps ignore the injected Ctrl+C.
    /// </summary>
    internal static class SelectionCapture
    {
        private const int VkControl = 0x11;
        private const int VkShift = 0x10;
        private const int VkMenu = 0x12;
        private const int VkLWin = 0x5B;
        private const int VkRWin = 0x5C;
        private const int VkC = 0x43;
        private const uint KeyeventfKeyup = 0x0002;

        public static string Capture(out string source)
        {
            WaitForModifiersReleased(1200);
            Thread.Sleep(60);

            source = "UIAutomation";
            string viaAutomation = TryAutomation();
            if (!string.IsNullOrWhiteSpace(viaAutomation))
            {
                Log.Write("capture via UIA ok");
                return viaAutomation.Trim();
            }

            Thread.Sleep(40);
            source = "Clipboard";
            string viaClipboard = TryClipboard();
            return viaClipboard == null ? "" : viaClipboard.Trim();
        }

        /// <summary>Blocks while any Ctrl/Alt/Shift/Win key is physically down.</summary>
        public static void WaitForModifiersReleased(int maxMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(maxMilliseconds);
            bool waited = false;
            while (DateTime.UtcNow < deadline)
            {
                if (!IsDown(VkControl) && !IsDown(VkMenu) && !IsDown(VkShift) &&
                    !IsDown(VkLWin) && !IsDown(VkRWin))
                {
                    if (waited)
                    {
                        Log.Write("modifiers released after wait");
                    }

                    return;
                }

                waited = true;
                Thread.Sleep(25);
            }

            Log.Write("modifier wait timed out; continuing");
        }

        private static bool IsDown(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        private static string TryAutomation()
        {
            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused == null)
                {
                    return null;
                }

                object pattern;
                if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
                {
                    return null;
                }

                var textPattern = (TextPattern)pattern;
                TextPatternRange[] ranges = textPattern.GetSelection();
                if (ranges == null || ranges.Length == 0)
                {
                    return null;
                }

                var builder = new StringBuilder();
                foreach (TextPatternRange range in ranges)
                {
                    string chunk = range.GetText(-1);
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        builder.Append(chunk);
                    }
                }

                string text = builder.ToString();
                return text.Length > 0 ? text : null;
            }
            catch (Exception ex)
            {
                Log.Write("UIA selection failed: " + ex.Message);
                return null;
            }
        }

        private static string TryClipboard()
        {
            string original;
            bool hadText = ClipboardHelper.TryGetText(out original);

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                ClipboardHelper.TryClear();
                Thread.Sleep(30);
                SendCtrlC();

                string captured = null;
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(800);
                while (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(50);
                    string text;
                    if (ClipboardHelper.TryGetText(out text) && !string.IsNullOrEmpty(text))
                    {
                        captured = text;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(captured))
                {
                    Log.Write("clipboard capture ok on attempt " + attempt);
                    if (hadText && original != captured)
                    {
                        ClipboardHelper.TrySetText(original);
                    }

                    return captured;
                }

                Log.Write("clipboard capture empty on attempt " + attempt);
                Thread.Sleep(120);
            }

            if (hadText && !string.IsNullOrEmpty(original))
            {
                ClipboardHelper.TrySetText(original);
            }

            return null;
        }

        private static void SendCtrlC()
        {
            keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            Thread.Sleep(15);
            keybd_event(VkC, 0, 0, UIntPtr.Zero);
            Thread.Sleep(25);
            keybd_event(VkC, 0, KeyeventfKeyup, UIntPtr.Zero);
            Thread.Sleep(15);
            keybd_event(VkControl, 0, KeyeventfKeyup, UIntPtr.Zero);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static class ClipboardHelper
        {
            public static bool TryGetText(out string text)
            {
                text = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        if (Clipboard.ContainsText())
                        {
                            text = Clipboard.GetText();
                            return text != null;
                        }

                        return false;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == 4)
                        {
                            Log.Write("clipboard get failed: " + ex.Message);
                        }

                        Thread.Sleep(40);
                    }
                }

                return false;
            }

            public static bool TrySetText(string text)
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        Clipboard.SetText(text);
                        return true;
                    }
                    catch
                    {
                        Thread.Sleep(40);
                    }
                }

                Log.Write("clipboard restore gave up");
                return false;
            }

            public static bool TryClear()
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        Clipboard.Clear();
                        return true;
                    }
                    catch
                    {
                        Thread.Sleep(30);
                    }
                }

                return false;
            }
        }
    }
}
