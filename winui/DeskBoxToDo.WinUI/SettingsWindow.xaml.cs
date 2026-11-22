using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT;

namespace DeskBoxToDo.WinUI
{
    /// <summary>Small settings card: default due time, data source and hotkey.</summary>
    public sealed partial class SettingsWindow : Window
    {
        private DesktopAcrylicController _acrylic;
        private SystemBackdropConfiguration _configuration;

        public event EventHandler<(int Hour, int Minute, string Mode, uint Modifiers, uint Key)> Saved;

        private static readonly (string Label, uint Modifiers, uint Key)[] Hotkeys =
        {
            ("Alt + Q", 0x0001, 0x51),
            ("Ctrl + Alt + Q", 0x0003, 0x51),
            ("Ctrl + Shift + Space", 0x0006, 0x20),
            ("Alt + Space", 0x0001, 0x20)
        };

        public SettingsWindow(GlassSettings settings, int currentHour, int currentMinute, string mode, uint modifiers, uint key)
        {
            InitializeComponent();
            ConfigureWindow();
            ApplyAcrylic();

            for (int hour = 0; hour < 24; hour++)
            {
                HourBox.Items.Add(hour.ToString("00"));
            }

            for (int minute = 0; minute < 60; minute += 5)
            {
                MinuteBox.Items.Add(minute.ToString("00"));
            }

            HourBox.SelectedIndex = Math.Clamp(currentHour, 0, 23);
            MinuteBox.SelectedIndex = Math.Clamp(currentMinute / 5, 0, 11);

            foreach (var entry in Hotkeys)
            {
                HotkeyBox.Items.Add(new ComboBoxItem { Content = entry.Label });
            }

            int hotkeyIndex = Array.FindIndex(Hotkeys, entry => entry.Modifiers == modifiers && entry.Key == key);
            HotkeyBox.SelectedIndex = hotkeyIndex < 0 ? 0 : hotkeyIndex;

            ModeBox.SelectedIndex = string.Equals(mode, "deskbox", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            var ai = ReadAiConfig();
            AiBaseBox.Text = ai.BaseUrl;
            AiModelBox.Text = ai.Model;
            AiKeyBox.Password = ai.ApiKey;
        }

        private void HideSystemCaptionButtons(IntPtr hwnd)
        {
            // Our own header already has a close button; drop the system ones so
            // no light caption strip is drawn on the glass.
            int style = GetWindowLong(hwnd, -16);
            style &= ~0x00080000; // WS_SYSMENU
            SetWindowLong(hwnd, -16, style);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0027);
        }
        private void ConfigureWindow()
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(id);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsAlwaysOnTop = true;
            }

            // Dark caption: the default system title bar would be white on glass.
            // Win11: paint the caption itself (the button strip ignores AppWindow colours
            // unless the caption colour is set through DWM).
            int captionColor = unchecked((int)0x00261C18); // COLORREF 0x00BBGGRR = rgb(24,28,38)
            DwmSetWindowAttribute(hwnd, 35, ref captionColor, sizeof(int));
            int captionTextColor = unchecked((int)0x00F2F4F8); // near-white text
            DwmSetWindowAttribute(hwnd, 36, ref captionTextColor, sizeof(int));

            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int));

            var titleBar = appWindow.TitleBar;
            titleBar.BackgroundColor = Color.FromArgb(255, 24, 28, 38);
            titleBar.InactiveBackgroundColor = Color.FromArgb(255, 24, 28, 38);
            titleBar.ForegroundColor = Colors.White;
            titleBar.InactiveForegroundColor = Color.FromArgb(200, 226, 230, 240);
            // Match the caption: a transparent button background shows up as a
            // white box on the glass.
            titleBar.ButtonBackgroundColor = Color.FromArgb(255, 24, 28, 38);
            titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(255, 24, 28, 38);
            titleBar.ButtonForegroundColor = Colors.White;
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(160, 226, 230, 240);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(40, 255, 255, 255);
            titleBar.ButtonHoverForegroundColor = Colors.White;
            HideSystemCaptionButtons(WinRT.Interop.WindowNative.GetWindowHandle(this));

            int round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            int borderColor = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));


            appWindow.Resize(new SizeInt32(Dpi.Px(hwnd, 440), Dpi.Px(hwnd, 420)));

            RECT work;
            SystemParametersInfo(0x0030, 0, out work, 0);
            appWindow.Move(new PointInt32(
                work.Left + (work.Right - work.Left - 440) / 2,
                work.Top + (work.Bottom - work.Top - 420) / 2));
        }

        private void ApplyAcrylic()
        {
            _acrylic = GlassHelper.Attach(this);
        }
        private static (string BaseUrl, string Model, string ApiKey) ReadAiConfig()
        {
            string baseUrl = "https://api.deepseek.com/v1";
            string model = "deepseek-chat";
            string key = "";
            try
            {
                string path = System.IO.Path.Combine(TodoStore.DataRoot, "config.json");
                if (System.IO.File.Exists(path))
                {
                    using (JsonDocument document = JsonDocument.Parse(System.IO.File.ReadAllText(path)))
                    {
                        JsonElement root = document.RootElement;
                        if (root.TryGetProperty("llmBaseUrl", out JsonElement b) && b.ValueKind == JsonValueKind.String)
                        {
                            baseUrl = b.GetString() ?? baseUrl;
                        }

                        if (root.TryGetProperty("llmModel", out JsonElement m) && m.ValueKind == JsonValueKind.String)
                        {
                            model = m.GetString() ?? model;
                        }

                        if (root.TryGetProperty("llmApiKey", out JsonElement k) && k.ValueKind == JsonValueKind.String)
                        {
                            key = k.GetString() ?? "";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("read ai config failed: " + ex.Message);
            }

            return (baseUrl, model, key);
        }

        private static void WriteAiConfig(string baseUrl, string model, string apiKey)
        {
            try
            {
                string path = System.IO.Path.Combine(TodoStore.DataRoot, "config.json");
                JsonObject root = System.IO.File.Exists(path)
                    ? (JsonNode.Parse(System.IO.File.ReadAllText(path)) as JsonObject ?? new JsonObject())
                    : new JsonObject();

                root["llmBaseUrl"] = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.deepseek.com/v1" : baseUrl;
                root["llmModel"] = string.IsNullOrWhiteSpace(model) ? "deepseek-chat" : model;
                root["llmApiKey"] = apiKey ?? "";
                root["llmEnabled"] = !string.IsNullOrWhiteSpace(apiKey);

                System.IO.Directory.CreateDirectory(TodoStore.DataRoot);
                System.IO.File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            }
            catch (Exception ex)
            {
                Console.WriteLine("write ai config failed: " + ex.Message);
            }
        }
        private void OnSaveClick(object sender, RoutedEventArgs args)
        {
            int hour = HourBox.SelectedIndex < 0 ? 8 : HourBox.SelectedIndex;
            int minute = MinuteBox.SelectedIndex < 0 ? 0 : MinuteBox.SelectedIndex * 5;
            string mode = ModeBox.SelectedIndex == 1 ? "deskbox" : "standalone";
            int hotkeyIndex = HotkeyBox.SelectedIndex < 0 ? 0 : HotkeyBox.SelectedIndex;

            WriteAiConfig(AiBaseBox.Text.Trim(), AiModelBox.Text.Trim(), AiKeyBox.Password);
            Saved?.Invoke(this, (hour, minute, mode, Hotkeys[hotkeyIndex].Modifiers, Hotkeys[hotkeyIndex].Key));
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs args)
        {
            Close();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint action, uint param, out RECT data, uint winIni);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
