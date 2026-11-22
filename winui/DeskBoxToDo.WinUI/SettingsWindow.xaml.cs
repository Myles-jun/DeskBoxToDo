using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.Graphics;

namespace DeskBoxToDo.WinUI
{
    /// <summary>Settings card: default due time, data source, hotkey recorder, appearance and AI.</summary>
    public sealed partial class SettingsWindow : Window
    {
        private readonly WindowChrome _chrome;
        private readonly GlassSettings _settings;

        public event EventHandler<(int Hour, int Minute, string Mode, uint Modifiers, uint Key)> Saved;

        private uint _modifiers;
        private uint _key;
        private bool _recording;

        public SettingsWindow(GlassSettings settings, int currentHour, int currentMinute, string mode, uint modifiers, uint key)
        {
            InitializeComponent();

            _settings = settings ?? GlassSettings.Load();
            _modifiers = modifiers;
            _key = key;

            _chrome = new WindowChrome(this, Shell, new ChromeOptions
            {
                Resizable = true,
                MinWidth = 400,
                MinHeight = 560,
                ToolWindow = true
            });
            _chrome.Attach();
            CenterOnWorkArea(460, 680);

            var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
            escape.Invoked += (sender, args) =>
            {
                args.Handled = true;
                Close();
            };
            Shell.KeyboardAccelerators.Add(escape);
            Shell.KeyDown += OnShellKeyDown;

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

            ModeBox.SelectedIndex = string.Equals(mode, "deskbox", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            AutostartSwitch.IsOn = Autostart.IsEnabled;

            for (int i = 0; i < RadiusBox.Items.Count; i++)
            {
                if (RadiusBox.Items[i] is ComboBoxItem item &&
                    int.TryParse(item.Tag?.ToString(), out int radius) &&
                    radius == _settings.CornerRadius)
                {
                    RadiusBox.SelectedIndex = i;
                    break;
                }
            }

            if (RadiusBox.SelectedIndex < 0)
            {
                RadiusBox.SelectedIndex = 0;
            }

            DensitySlider.Value = _settings.Brightness;
            HotkeyLabel.Text = DescribeHotkey(_modifiers, _key);

            var ai = ReadAiConfig();
            AiBaseBox.Text = ai.BaseUrl;
            AiModelBox.Text = ai.Model;
            AiKeyBox.Password = ai.ApiKey;
        }

        private void CenterOnWorkArea(int widthDip, int heightDip)
        {
            _chrome.ResizeTo(widthDip, heightDip);
            RectInt32 area = DisplayArea
                .GetFromWindowId(_chrome.AppWindow.Id, DisplayAreaFallback.Nearest)
                .WorkArea;
            int width = Dpi.Px(_chrome.Hwnd, widthDip);
            int height = Dpi.Px(_chrome.Hwnd, heightDip);
            _chrome.MoveTo(
                area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2);
        }

        // ── Hotkey recorder ────────────────────────────────────────

        private void OnRecordHotkeyClick(object sender, RoutedEventArgs args)
        {
            _recording = true;
            RecordButton.Content = "按下组合键…";
            HotkeyHint.Text = "正在录制：按下组合键（Esc 取消）。";
        }

        private void OnShellKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (!_recording)
            {
                return;
            }

            VirtualKey key = args.Key;
            if (key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or
                VirtualKey.LeftControl or VirtualKey.RightControl or
                VirtualKey.LeftShift or VirtualKey.RightShift or
                VirtualKey.LeftMenu or VirtualKey.RightMenu or
                VirtualKey.LeftWindows or VirtualKey.RightWindows)
            {
                args.Handled = true;
                return;
            }

            uint modifiers = 0;
            if (IsDown(VirtualKey.Control)) modifiers |= 0x0002; // MOD_CONTROL
            if (IsDown(VirtualKey.Shift)) modifiers |= 0x0004;    // MOD_SHIFT
            if (IsDown(VirtualKey.Menu)) modifiers |= 0x0001;     // MOD_ALT

            if (modifiers == 0)
            {
                HotkeyHint.Text = "组合键必须包含 Ctrl / Alt / Shift 之一，请重新按。";
                args.Handled = true;
                return;
            }

            _modifiers = modifiers;
            _key = (uint)key;
            _recording = false;
            RecordButton.Content = "录制";
            HotkeyLabel.Text = DescribeHotkey(_modifiers, _key);
            HotkeyHint.Text = "已记录，点「保存」生效。";
            args.Handled = true;
        }

        private static bool IsDown(VirtualKey key)
        {
            try
            {
                return InputKeyboardSource.GetKeyStateForCurrentThread(key)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            }
            catch
            {
                return false;
            }
        }

        private static string DescribeHotkey(uint modifiers, uint key)
        {
            var parts = new System.Collections.Generic.List<string>();
            if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
            if ((modifiers & 0x0001) != 0) parts.Add("Alt");
            if ((modifiers & 0x0004) != 0) parts.Add("Shift");
            if ((modifiers & 0x0008) != 0) parts.Add("Win");

            string keyName;
            try
            {
                keyName = ((VirtualKey)key).ToString();
            }
            catch
            {
                keyName = "?";
            }

            parts.Add(keyName);
            return string.Join(" + ", parts);
        }

        // ── Save ───────────────────────────────────────────────────

        private void OnSaveClick(object sender, RoutedEventArgs args)
        {
            int hour = HourBox.SelectedIndex < 0 ? 8 : HourBox.SelectedIndex;
            int minute = MinuteBox.SelectedIndex < 0 ? 0 : MinuteBox.SelectedIndex * 5;
            string mode = ModeBox.SelectedIndex == 1 ? "deskbox" : "standalone";

            WriteAiConfig(AiBaseBox.Text.Trim(), AiModelBox.Text.Trim(), AiKeyBox.Password);
            Autostart.Set(AutostartSwitch.IsOn);

            int radius = -1;
            if (RadiusBox.SelectedItem is ComboBoxItem radiusItem)
            {
                int.TryParse(radiusItem.Tag?.ToString(), out radius);
            }

            _settings.CornerRadius = radius;
            _settings.Brightness = (int)Math.Round(DensitySlider.Value);
            _settings.HotkeyModifiers = _modifiers;
            _settings.HotkeyKey = _key;
            _settings.Save();

            Appearance.Reload();
            Appearance.ApplyAll();

            Saved?.Invoke(this, (hour, minute, mode, _modifiers, _key));
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs args) => Close();

        // ── AI config ──────────────────────────────────────────────

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
                    using JsonDocument document = JsonDocument.Parse(System.IO.File.ReadAllText(path));
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
    }
}
