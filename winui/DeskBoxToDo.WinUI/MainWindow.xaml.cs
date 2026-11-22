using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.UI;

namespace DeskBoxToDo.WinUI
{
    public sealed partial class MainWindow : Window
    {
        private readonly WindowChrome _chrome;
        private GlassSettings _settings = GlassSettings.Load();

        private readonly ObservableCollection<TodoItem> _visible = new ObservableCollection<TodoItem>();
        private List<TodoItem> _all = new List<TodoItem>();
        private string _filter = "all";
        private string _colorFilter;

        private TodoItem _editing;
        private bool _editingImportant;
        private string _detailColor = "";
        private bool _hiddenToTray;
        private bool _loadingDetail;

        private TrayIcon _tray;
        private ShellMessageHook _messages;
        private ReminderWindow _reminderWindow;
        private Microsoft.UI.Xaml.DispatcherTimer _reminderTimer;
        private Microsoft.UI.Xaml.DispatcherTimer _autoUpdateTimer;

        private const int HotkeyId = 0xB1D0;
        private const int HotkeyIdFallback = 0xB1D1;
        private bool _hotkeyRegistered;
        private bool _fallbackHotkeyRegistered;
        private uint _hotkeyModifiers = 0x0001;
        private uint _hotkeyKey = 0x51;
        private const uint FallbackModifiers = 0x0003; // Ctrl + Alt
        private const uint FallbackKey = 0x51;         // Q

        private static readonly (string Key, string Label)[] Repeats =
        {
            ("none", "不重复"), ("daily", "每天"), ("weekly", "每周"),
            ("monthly", "每月"), ("weekdays", "工作日")
        };

        private static readonly (int Minutes, string Label)[] ReminderOptions =
        {
            (0, "准时提醒"), (5, "提前 5 分钟"), (10, "提前 10 分钟"), (15, "提前 15 分钟"),
            (30, "提前 30 分钟"), (60, "提前 1 小时"), (1440, "提前 1 天")
        };

        public MainWindow()
        {
            InitializeComponent();

            // Keep accelerator hints (the floating "Ctrl+Z" chip) off the surface.
            Shell.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

            // DeskBox's todo widget uses a tinted glyph rather than plain white.
            Shell.IconBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x8F, 0xB8, 0xFF));

            _chrome = new WindowChrome(this, Shell, new ChromeOptions
            {
                MinWidth = 320,
                MinHeight = 420,
                Layer = ParseLayer(_settings.Layer)
            });
            _chrome.Attach();
            _chrome.InteractionCompleted += PersistBounds;

            RestoreWindowPlacement();
            Closed += OnWindowClosed;

            var undo = new KeyboardAccelerator
            {
                Key = Windows.System.VirtualKey.Z,
                Modifiers = Windows.System.VirtualKeyModifiers.Control
            };
            undo.Invoked += (sender, args) =>
            {
                args.Handled = true;
                UndoLast();
            };
            Shell.KeyboardAccelerators.Add(undo);

            TodoList.ItemsSource = _visible;
            BuildDetailPickers();
            SyncMaterialUi();
            UpdateLayerUi();
            SourceLabel.Text = TodoStore.CurrentMode() == "deskbox"
                ? "DeskBox 小组件数据（与 DeskBox 同步）"
                : "独立数据（%LOCALAPPDATA%\\DeskBoxToDo\\todo.json）";
            if (!string.IsNullOrEmpty(TodoStore.DataRootNotice))
            {
                SourceLabel.Text += "\n⚠ " + TodoStore.DataRootNotice;
            }

            Reload();

            // Tray + hotkey need a live HWND that has already presented once.
            Shell.Loaded += OnShellLoaded;
        }

        private void OnShellLoaded(object sender, RoutedEventArgs e)
        {
            Shell.Loaded -= OnShellLoaded;
            InstallShellIntegration();
            StartReminderTimer();
            StartAutoUpdateCheck();

            // Make sure a freshly started instance is actually seen: it may launch
            // behind an always-on-top app (chat client, overlay, ...).
            _chrome.RaiseUntilDeactivated();
            _chrome.FlashTaskbar();

            if (!AppConfig.IsOnboardingDone)
            {
                ShowOnboarding();
            }

            // Dev probes: open a dialog on launch so screenshots can be automated.
            if (Environment.GetEnvironmentVariable("DBT_FAKE_UPDATE") == "1")
            {
                var fake = new UpdateChecker.Result
                {
                    HasUpdate = true,
                    CurrentVersion = UpdateChecker.CurrentVersion,
                    LatestTag = "v2.1.0",
                    PageUrl = "https://github.com/Myles-jun/DeskBoxToDo/releases",
                    AssetUrl = "https://github.com/Myles-jun/DeskBoxToDo/releases/download/v2.1.0/DeskBoxToDo_Setup_2.1.0.exe",
                    Notes = "• 新增：待办支持自定义提醒提前量\n• 修复：窄窗口下页签被裁切\n• 优化：拖动时背景不再闪"
                };
                ShowUpdateWindow(fake);
            }

            if (Environment.GetEnvironmentVariable("DBT_HIDE_MAIN") == "1")
            {
                HideToTray();
            }

            // Dev probe: parse one sentence and log the result.
            string parseSample = Environment.GetEnvironmentVariable("DBT_PARSE_TEST");
            if (!string.IsNullOrEmpty(parseSample))
            {
                var draft = TodoParser.Parse(parseSample, AppConfig.Load(), DateTimeOffset.Now);
                AppLog.Write("[parse] " + parseSample + " => title=[" + draft.Title + "] due=" +
                    (draft.Due?.ToString("yyyy-MM-dd HH:mm") ?? "none") + " steps=" +
                    string.Join(" | ", draft.Steps));
            }

            if (Environment.GetEnvironmentVariable("DBT_OPEN_CONFIRM") == "1")
            {
                OpenConfirmWindow("明天下午三点 交周报 重要");
            }

            if (Environment.GetEnvironmentVariable("DBT_OPEN_TRAY_MENU") == "1")
            {
                ShowTrayMenu();
            }

            if (Environment.GetEnvironmentVariable("DBT_OPEN_SETTINGS") == "1")
            {
                var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                timer.Tick += (probeSender, probeArgs) =>
                {
                    timer.Stop();
                    OpenSettingsWindow();
                };
                timer.Start();
            }
        }

        // ── Window placement ───────────────────────────────────────

        private void RestoreWindowPlacement()
        {
            int width = Math.Max(320, _settings.Width);
            int height = Math.Max(420, _settings.Height);
            _chrome.ResizeTo(width, height);

            if (_settings.X != 0 || _settings.Y != 0)
            {
                _chrome.MoveTo(_settings.X, _settings.Y);
            }
            else
            {
                RectInt32 bounds = _chrome.GetBounds();
                int x = Math.Max(40, bounds.Width + 120);
                _chrome.MoveTo(x, 140);
            }
        }

        private void PersistBounds()
        {
            RectInt32 bounds = _chrome.GetBounds();
            _settings.X = bounds.X;
            _settings.Y = bounds.Y;
            _settings.Width = Math.Max(320, Dpi.Dip(_chrome.Hwnd, bounds.Width));
            _settings.Height = Math.Max(420, Dpi.Dip(_chrome.Hwnd, bounds.Height));
            _settings.Layer = _chrome.Layer.ToString().ToLowerInvariant();
            _settings.Save();
        }

        private static WindowLayer ParseLayer(string value)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case "desktop": return WindowLayer.Desktop;
                case "topmost": return WindowLayer.TopMost;
                default: return WindowLayer.Normal;
            }
        }

        // ── Material / layer ───────────────────────────────────────

        private void SyncMaterialUi()
        {
            BrightnessSlider.Value = _settings.Brightness;
            foreach (object entry in MaterialBox.Items)
            {
                if (entry is ComboBoxItem item && (string)item.Tag == _settings.Material)
                {
                    MaterialBox.SelectedItem = item;
                    break;
                }
            }

            BrightnessLabel.Text = "当前 " + _settings.Brightness + "（0 最清透 / 100 最厚重）";
        }

        private void OnMaterialChanged(object sender, SelectionChangedEventArgs args)
        {
            if (MaterialBox.SelectedItem is ComboBoxItem item && item.Tag is string material)
            {
                _settings.Material = material;
                _settings.Save();
                Appearance.Reload();
                Appearance.ApplyAll();
            }
        }

        private void OnBrightnessChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            if (BrightnessLabel == null)
            {
                return;
            }

            _settings.Brightness = (int)Math.Round(args.NewValue);
            _settings.Save();
            Appearance.Reload();
            Appearance.ApplyAll();
            BrightnessLabel.Text = "当前 " + _settings.Brightness + "（0 最清透 / 100 最厚重）";
        }

        private void OnLayerNormalClick(object sender, RoutedEventArgs args) => SetLayer(WindowLayer.Normal);

        private void OnLayerDesktopClick(object sender, RoutedEventArgs args) => SetLayer(WindowLayer.Desktop);

        private void OnLayerTopClick(object sender, RoutedEventArgs args) => SetLayer(WindowLayer.TopMost);

        private void OnCycleLayerClick(object sender, RoutedEventArgs args)
        {
            SetLayer(_chrome.Layer switch
            {
                WindowLayer.Normal => WindowLayer.Desktop,
                WindowLayer.Desktop => WindowLayer.TopMost,
                _ => WindowLayer.Normal
            });
        }

        private void SetLayer(WindowLayer layer)
        {
            _chrome.ApplyLayer(layer);
            _settings.Layer = layer.ToString().ToLowerInvariant();
            _settings.Save();
            UpdateLayerUi();
        }

        private void UpdateLayerUi()
        {
            string label = _chrome.Layer switch
            {
                WindowLayer.Desktop => "桌面",
                WindowLayer.TopMost => "置顶",
                _ => "浮窗"
            };

            LayerButton.Content = label;

            var active = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
            var idle = new SolidColorBrush(Colors.Transparent);
            LayerNormalButton.Background = _chrome.Layer == WindowLayer.Normal ? active : idle;
            LayerDesktopButton.Background = _chrome.Layer == WindowLayer.Desktop ? active : idle;
            LayerTopButton.Background = _chrome.Layer == WindowLayer.TopMost ? active : idle;
        }

        // ── Data ───────────────────────────────────────────────────

        public void Reload()
        {
            _all = TodoStore.Load(out string path);
            UpdateTabs();
            UpdateColorRow();
            ApplyFilter();
            Console.WriteLine("[Todo] loaded " + _all.Count + " items from " + path);
        }

        private int CountFor(string filter)
        {
            switch (filter)
            {
                case "today":
                    return _all.Count(item => item.IsToday && !item.IsCompleted);
                case "important":
                    return _all.Count(item => item.IsImportant && !item.IsCompleted);
                case "done":
                    return _all.Count(item => item.IsCompleted);
                default:
                    return _all.Count(item => !item.IsCompleted);
            }
        }

        private void UpdateTabs()
        {
            TabAllText.Text = "全部 " + CountFor("all");
            TabTodayText.Text = "今天 " + CountFor("today");
            TabImportantText.Text = "重要 " + CountFor("important");
            TabDoneText.Text = "已完成 " + CountFor("done");

            Highlight(TabAll, TabAllText, TabAllBar, _filter == "all");
            Highlight(TabToday, TabTodayText, TabTodayBar, _filter == "today");
            Highlight(TabImportant, TabImportantText, TabImportantBar, _filter == "important");
            Highlight(TabDone, TabDoneText, TabDoneBar, _filter == "done");
        }

        private static void Highlight(Button tab, TextBlock label, Rectangle bar, bool active)
        {
            tab.Background = new SolidColorBrush(active
                ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)
                : Colors.Transparent);
            label.Foreground = new SolidColorBrush(active
                ? Color.FromArgb(0xFF, 0xF2, 0xF5, 0xFF)
                : Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF));
            label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            bar.Fill = new SolidColorBrush(active
                ? Color.FromArgb(0xFF, 0xA8, 0x8B, 0xFF)
                : Colors.Transparent);
        }

        private void UpdateColorRow()
        {
            ColorRow.Children.Clear();
            foreach (var entry in TodoItem.Palette)
            {
                var pill = new Border
                {
                    Width = 14,
                    Height = 6,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(entry.Value)
                };

                var host = new Button
                {
                    Width = 26,
                    Height = 24,
                    MinWidth = 26,
                    MinHeight = 24,
                    Padding = new Thickness(0),
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(_colorFilter == entry.Key ? 1 : 0),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
                    CornerRadius = new CornerRadius(4),
                    Content = pill,
                    UseSystemFocusVisuals = false
                };

                string key = entry.Key;
                host.Click += (sender, args) =>
                {
                    _colorFilter = _colorFilter == key ? null : key;
                    UpdateColorRow();
                    ApplyFilter();
                };
                ToolTipService.SetToolTip(host, TodoItem.LabelFor(entry.Key));
                ColorRow.Children.Add(host);
            }
        }

        private void ApplyFilter()
        {
            IEnumerable<TodoItem> rows = _all;
            switch (_filter)
            {
                case "today":
                    rows = rows.Where(item => item.IsToday && !item.IsCompleted);
                    break;
                case "important":
                    rows = rows.Where(item => item.IsImportant && !item.IsCompleted);
                    break;
                case "done":
                    rows = rows.Where(item => item.IsCompleted);
                    break;
                default:
                    rows = rows.Where(item => !item.IsCompleted);
                    break;
            }

            if (!string.IsNullOrEmpty(_colorFilter))
            {
                rows = rows.Where(item => item.ColorMarker == _colorFilter);
            }

            List<TodoItem> ordered = rows
                .OrderBy(item => item.IsCompleted ? 1 : 0)
                .ThenBy(item => item.DueDate ?? DateTimeOffset.MaxValue)
                .ToList();

            _visible.Clear();
            foreach (TodoItem item in ordered)
            {
                _visible.Add(item);
            }

            FooterText.Text = _visible.Count + " 条 · " +
                (TodoStore.CurrentMode() == "deskbox" ? "DeskBox 数据" : "独立数据");
            EmptyState.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnTabClick(object sender, RoutedEventArgs args)
        {
            if (sender is FrameworkElement element && element.Tag != null)
            {
                _filter = element.Tag.ToString();
                UpdateTabs();
                ApplyFilter();
            }
        }

        private void ItemRoot_PointerEntered(object sender, PointerRoutedEventArgs args)
        {
            if (sender is Grid grid)
            {
                grid.Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            }
        }

        private void ItemRoot_PointerExited(object sender, PointerRoutedEventArgs args)
        {
            if (sender is Grid grid)
            {
                grid.Background = new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));
            }
        }

        private void OnCheckBoxClick(object sender, RoutedEventArgs args)
        {
            if (sender is CheckBox box && box.Tag is string id)
            {
                TodoRepo.SetCompleted(id, box.IsChecked == true);
                Reload();
            }
        }

        private void OnClearDoneClick(object sender, RoutedEventArgs args)
        {
            foreach (TodoItem item in _all.Where(row => row.IsCompleted).ToList())
            {
                TodoRepo.Delete(item.Id);
            }

            Reload();
        }

        private void OnItemClick(object sender, ItemClickEventArgs args)
        {
            if (args.ClickedItem is TodoItem item)
            {
                OpenDetail(item);
            }
        }

        /// <summary>
        /// Row tap opens the detail page. The checkbox handles its own click, so a
        /// completion tap never navigates away by accident.
        /// </summary>
        private void ItemRoot_Tapped(object sender, TappedRoutedEventArgs args)
        {
            if (IsInsideControl(args.OriginalSource as DependencyObject))
            {
                return;
            }

            if (sender is FrameworkElement element && element.DataContext is TodoItem item)
            {
                OpenDetail(item);
            }
        }

        private static bool IsInsideControl(DependencyObject source)
        {
            DependencyObject current = source;
            int guard = 0;
            while (current != null && guard++ < 12)
            {
                if (current is CheckBox || current is ButtonBase)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        // ── Reminders ──────────────────────────────────────────────

        private void StartReminderTimer()
        {
            _reminderTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _reminderTimer.Tick += (sender, args) => CheckDueReminders();
            _reminderTimer.Start();
            CheckDueReminders();
        }

        /// <summary>
        /// Fires the toast for items whose reminder point arrived. The reminder
        /// point is due-minus-lead (default 5 minutes); a "稍后 10 分钟" snooze
        /// re-arms the same item once and survives restarts.
        /// </summary>
        private void CheckDueReminders()
        {
            try
            {
                DateTimeOffset now = DateTimeOffset.Now;
                foreach (TodoItem item in TodoStore.Load(out _))
                {
                    if (item.IsCompleted || item.DueDate == null)
                    {
                        continue;
                    }

                    DateTimeOffset due = item.DueDate.Value.ToLocalTime();

                    if (item.SnoozedUntil is { } snoozed)
                    {
                        DateTimeOffset snoozedLocal = snoozed.ToLocalTime();
                        string snoozeKey = snoozed.ToUniversalTime().ToString("o");
                        if (now < snoozedLocal || item.SnoozeNotifiedFor == snoozeKey)
                        {
                            continue;
                        }

                        if ((now - snoozedLocal) > TimeSpan.FromHours(2))
                        {
                            continue;
                        }

                        TodoRepo.MarkSnoozeNotified(item.Id, snoozed);
                        ShowReminderToast(item);
                        break;
                    }

                    int leadMinutes = item.ReminderOffsetMinutes ?? 5;
                    DateTimeOffset notifyAt = due.AddMinutes(-leadMinutes);
                    if (now < notifyAt || (now - notifyAt) > TimeSpan.FromHours(2))
                    {
                        continue;
                    }

                    string dueIso = item.DueDate.Value.ToString("o");
                    if (item.NotifiedForDue == dueIso)
                    {
                        continue;
                    }

                    TodoRepo.MarkNotified(item.Id, dueIso);
                    ShowReminderToast(item);
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] reminder check failed: " + ex.Message);
            }
        }

        private void ShowReminderToast(TodoItem item)
        {
            try
            {
                _reminderWindow?.Close();
                _reminderWindow = new ReminderWindow(item.Text, item.Subtitle);
                _reminderWindow.OpenRequested += (sender, args) =>
                {
                    _reminderWindow?.Close();
                    _reminderWindow = null;
                    ShowFromTray();
                };
                _reminderWindow.Snoozed += (sender, args) =>
                {
                    TodoRepo.Snooze(item.Id, DateTimeOffset.Now.AddMinutes(10));
                    _reminderWindow?.Close();
                    _reminderWindow = null;
                };
                _reminderWindow.Activate();
                RaiseAsOwnedDialog(_reminderWindow);

                var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    _reminderWindow?.Close();
                    _reminderWindow = null;
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] reminder toast failed: " + ex.Message);
            }
        }

        // ── Quick add / dialogs ────────────────────────────────────

        private void OnQuickAddKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            args.Handled = true;
            string raw = (QuickAddBox.Text ?? "").Trim();
            if (raw.Length == 0)
            {
                return;
            }

            QuickAddBox.Text = "";
            OpenConfirmWindow(raw);
        }

        private void OnSettingsClick(object sender, RoutedEventArgs args) => OpenSettingsWindow();

        private void OnOpenLogClick(object sender, RoutedEventArgs args)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppLog.Path)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppLog.Write("open log failed: " + ex.Message);
            }
        }

        private void OnCloseWindowClick(object sender, RoutedEventArgs args) => HideToTray();

        private void OnExitAppClick(object sender, RoutedEventArgs args) => Application.Current.Exit();

        private void HideToTray()
        {
            // Never hide into a tray that is not there: without a visible icon the
            // user would have no way back.
            if (_tray?.IsVisible == true)
            {
                _chrome.AppWindow.Hide();
                _hiddenToTray = true;
                AppLog.Write("hidden to tray");
                return;
            }

            MinimizeToTaskbar();
            AppLog.Write("tray icon unavailable, minimized to taskbar instead");
        }

        private void MinimizeToTaskbar()
        {
            try
            {
                ShowWindow(_chrome.Hwnd, 6 /* SW_MINIMIZE */);
            }
            catch (Exception ex)
            {
                AppLog.Write("minimize failed: " + ex.Message);
            }
        }

        /// <summary>Owns a dialog to the main window so it can never be covered by it.</summary>
        private void RaiseAsOwnedDialog(Window dialog)
        {
            try
            {
                IntPtr owner = _chrome.Hwnd;
                IntPtr child = WinRT.Interop.WindowNative.GetWindowHandle(dialog);
                SetWindowLongPtr(child, -8, owner);
                SetWindowPos(child, IntPtr.Zero, 0, 0, 0, 0, 0x0013);
                SetWindowPos(child, IntPtr.Zero, 0, 0, 0, 0, 0x0003);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] raise dialog failed: " + ex.Message);
            }
        }

        private void OpenConfirmWindow(string raw)
        {
            TodoDraft draft = TodoParser.Parse(raw, AppConfig.Load(), DateTimeOffset.Now);
            var window = new ConfirmWindow(draft, null);
            window.Confirmed += (sender, result) =>
            {
                string savedId = TodoRepo.Save(
                    null,
                    result.Title,
                    result.Due,
                    result.Repeat,
                    result.Color,
                    result.Important,
                    raw,
                    result.Steps.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries));

                int reminderMinutes = sender is ConfirmWindow confirm ? confirm.ReminderMinutes : 5;
                TodoRepo.SetReminder(savedId, reminderMinutes);
                Reload();
            };
            window.Activate();
            if (!_hiddenToTray)
            {
                RaiseAsOwnedDialog(window);
            }
        }

        private void OpenSettingsWindow()
        {
            AppConfig shared = AppConfig.Load();
            var window = new SettingsWindow(
                _settings,
                shared.DefaultDueHour,
                shared.DefaultDueMinute,
                TodoStore.CurrentMode(),
                _hotkeyModifiers,
                _hotkeyKey);

            window.Saved += (sender, result) =>
            {
                shared.DefaultDueHour = result.Hour;
                shared.DefaultDueMinute = result.Minute;
                shared.Save();

                WriteSharedValue("todoMode", result.Mode);
                _hotkeyModifiers = result.Modifiers;
                _hotkeyKey = result.Key;
                _settings.HotkeyModifiers = result.Modifiers;
                _settings.HotkeyKey = result.Key;
                _settings.Save();

                ReRegisterHotkey();
                SourceLabel.Text = TodoStore.CurrentMode() == "deskbox"
                    ? "DeskBox 小组件数据（与 DeskBox 同步）"
                    : "独立数据（%LOCALAPPDATA%\\DeskBoxToDo\\todo.json）";
                Reload();
            };

            window.Activate();
            RaiseAsOwnedDialog(window);
        }

        private void ReRegisterHotkey()
        {
            UnregisterHotKey(_chrome.Hwnd, HotkeyId);
            _hotkeyRegistered = RegisterHotKey(_chrome.Hwnd, HotkeyId, _hotkeyModifiers, _hotkeyKey);
            AppLog.Write("hotkey re-registered=" + _hotkeyRegistered +
                " mods=0x" + _hotkeyModifiers.ToString("X") + " vk=0x" + _hotkeyKey.ToString("X"));
        }

        /// <summary>Writes one scalar into the 1.x config.json so both builds agree.</summary>
        private static void WriteSharedValue(string key, string value)
        {
            try
            {
                string path = System.IO.Path.Combine(TodoStore.DataRoot, "config.json");
                JsonObject root = File.Exists(path)
                    ? (JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject())
                    : new JsonObject();
                root[key] = value;
                File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] shared config write failed: " + ex.Message);
            }
        }

        // ── Detail page ────────────────────────────────────────────

        private void OnAddClick(object sender, RoutedEventArgs args) => OpenDetail(null);

        private void BuildDetailColorSwatches()
        {
            DetailColorHost.Children.Clear();
            var grid = new Grid { ColumnSpacing = 4, RowSpacing = 3 };
            for (int column = 0; column < 7; column++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            int index = 0;
            foreach (var entry in TodoItem.Palette)
            {
                int row = index / 7;
                int column = index % 7;
                index++;

                var dot = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(entry.Value),
                    BorderThickness = new Thickness(0)
                };

                var host = new Button
                {
                    Content = dot,
                    Tag = entry.Key,
                    Padding = new Thickness(0),
                    Width = 24,
                    Height = 24,
                    MinWidth = 0,
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(6),
                    UseSystemFocusVisuals = false
                };
                ToolTipService.SetToolTip(host, TodoItem.LabelFor(entry.Key));
                host.Click += (sender, args) =>
                {
                    _detailColor = (sender as Button)?.Tag as string ?? "";
                    RefreshDetailColorSwatches();
                };

                Grid.SetRow(host, row);
                Grid.SetColumn(host, column);
                grid.Children.Add(host);
            }

            DetailColorHost.Children.Add(grid);
        }

        private void RefreshDetailColorSwatches()
        {
            if (DetailColorHost.Children.Count == 0 ||
                DetailColorHost.Children[0] is not Grid grid)
            {
                return;
            }

            foreach (object child in grid.Children)
            {
                if (child is Button host && host.Tag is string key)
                {
                    bool selected = string.Equals(key, _detailColor, StringComparison.OrdinalIgnoreCase);
                    if (host.Content is Border dot)
                    {
                        dot.BorderThickness = new Thickness(selected ? 2 : 0);
                        dot.BorderBrush = new SolidColorBrush(Colors.White);
                    }
                }
            }
        }

        private void BuildDetailPickers()
        {
            foreach (var repeat in Repeats)
            {
                RepeatBox.Items.Add(new ComboBoxItem { Content = repeat.Label, Tag = repeat.Key });
            }

            BuildDetailColorSwatches();

            for (int hour = 0; hour < 24; hour++)
            {
                HourBox.Items.Add(hour.ToString("00"));
            }

            for (int minute = 0; minute < 60; minute += 5)
            {
                MinuteBox.Items.Add(minute.ToString("00"));
            }

            foreach (var option in ReminderOptions)
            {
                ReminderBox.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Minutes });
            }

            RepeatBox.SelectedIndex = 0;
            HourBox.SelectedIndex = 9;
            MinuteBox.SelectedIndex = 0;
            ReminderBox.SelectedIndex = 1;
        }

        private void OpenDetail(TodoItem item)
        {
            _loadingDetail = true;
            _editing = item;
            _editingImportant = item?.IsImportant ?? false;

            TitleBox.Text = item?.Text ?? "";
            NotesBox.Text = item?.Notes ?? "";
            DateTimeOffset due = item?.DueDate?.ToLocalTime() ?? DateTimeOffset.Now.Date.AddDays(1).AddHours(9);
            DateBox.Date = due;
            HourBox.SelectedIndex = Math.Clamp(due.Hour, 0, 23);
            MinuteBox.SelectedIndex = Math.Clamp(due.Minute / 5, 0, 11);

            string mode = item?.RecurrenceMode ?? "none";
            RepeatBox.SelectedIndex = Math.Max(0, Array.FindIndex(Repeats, entry => entry.Key == mode));

            string color = item?.ColorMarker ?? "";
            _detailColor = color;
            RefreshDetailColorSwatches();

            JsonObject root = TodoRepo.ReadRoot();
            JsonObject node = item == null ? null : TodoRepo.Find(root, item.Id);
            StepsBox.Text = string.Join("\r\n", TodoRepo.StepTexts(node));
            BuildStepsChecklist(node);

            int reminderMinutes = item?.ReminderOffsetMinutes ?? 5;
            int reminderIndex = Array.FindIndex(ReminderOptions, entry => entry.Minutes == reminderMinutes);
            ReminderBox.SelectedIndex = reminderIndex < 0 ? 1 : reminderIndex;

            UpdateStarIcon();
            DetailTitle.Text = item == null ? "新待办" : "编辑 · " + item.Id.Substring(0, 6);
            DetailHint.Text = item == null ? "填写后点右上角 ✓ 保存" : "修改后点右上角 ✓ 保存";
            DetailPage.Visibility = Visibility.Visible;
            ListPage.Visibility = Visibility.Collapsed;
            _loadingDetail = false;
            TitleBox.Focus(FocusState.Programmatic);
        }

        /// <summary>
        /// Per-step checkboxes (1.x parity): ticking one writes immediately, and
        /// completing the last step completes the parent item.
        /// </summary>
        private void BuildStepsChecklist(JsonObject node)
        {
            StepsChecklist.Children.Clear();
            if (node?["steps"] is not JsonArray steps || steps.Count == 0)
            {
                StepsChecklist.Visibility = Visibility.Collapsed;
                return;
            }

            int index = 0;
            foreach (JsonNode entry in steps)
            {
                if (entry is not JsonObject step)
                {
                    continue;
                }

                int stepIndex = index++;
                var box = new CheckBox
                {
                    Content = (string)step["text"] ?? "",
                    IsChecked = (bool?)step["isCompleted"] ?? false,
                    FontSize = 12.5,
                    MinWidth = 0,
                    Tag = stepIndex,
                    UseSystemFocusVisuals = false
                };
                box.Click += OnStepCheckBoxClick;
                StepsChecklist.Children.Add(box);
            }

            StepsChecklist.Visibility = Visibility.Visible;
        }

        private void OnStepCheckBoxClick(object sender, RoutedEventArgs args)
        {
            if (_editing == null || sender is not CheckBox box || box.Tag is not int stepIndex)
            {
                return;
            }

            bool allDone = TodoRepo.SetStepCompleted(_editing.Id, stepIndex, box.IsChecked == true);
            if (allDone)
            {
                TodoRepo.SetCompleted(_editing.Id, true);
                OnCloseDetail(sender, args);
                Reload();
                return;
            }

            RefreshStepsChecklist();
        }

        private void RefreshStepsChecklist()
        {
            if (_editing == null)
            {
                return;
            }

            BuildStepsChecklist(TodoRepo.Find(TodoRepo.ReadRoot(), _editing.Id));
        }

        private void UpdateStarIcon()
        {
            StarIcon.Glyph = _editingImportant ? "\uE735" : "\uE734";
            StarIcon.Foreground = new SolidColorBrush(_editingImportant
                ? Color.FromArgb(0xFF, 0xE9, 0xC4, 0x6A)
                : Color.FromArgb(0xFF, 0xE6, 0xEA, 0xF2));
        }

        private void OnToggleDetailImportant(object sender, RoutedEventArgs args)
        {
            _editingImportant = !_editingImportant;
            UpdateStarIcon();
        }

        private void OnCloseDetail(object sender, RoutedEventArgs args)
        {
            DetailPage.Visibility = Visibility.Collapsed;
            ListPage.Visibility = Visibility.Visible;
            _editing = null;
        }

        private void OnDeleteDetail(object sender, RoutedEventArgs args)
        {
            if (_editing != null)
            {
                TodoRepo.Delete(_editing.Id);
            }

            OnCloseDetail(sender, args);
            Reload();
        }

        private void OnSaveDetail(object sender, RoutedEventArgs args)
        {
            string title = (TitleBox.Text ?? "").Trim();
            if (title.Length == 0)
            {
                DetailHint.Text = "标题不能为空";
                return;
            }

            DateTimeOffset? due = null;
            if (DateBox.Date != null)
            {
                DateTime date = DateBox.Date.Value.Date;
                int hour = Math.Clamp(HourBox.SelectedIndex, 0, 23);
                int minute = Math.Clamp(MinuteBox.SelectedIndex, 0, 11) * 5;
                due = new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, TimeZoneInfo.Local.GetUtcOffset(date));
            }

            string repeat = (RepeatBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
            string color = _detailColor;
            string[] steps = (StepsBox.Text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            string savedId = TodoRepo.Save(_editing?.Id, title, due, repeat, color, _editingImportant, NotesBox.Text ?? "", steps);
            int reminderMinutes = (ReminderBox.SelectedItem as ComboBoxItem)?.Tag is int minutes ? minutes : 5;
            TodoRepo.SetReminder(savedId, reminderMinutes);

            OnCloseDetail(sender, args);
            Reload();
        }

        // ── Tray / hotkey / updates ────────────────────────────────

        private void InstallShellIntegration()
        {
            IntPtr hwnd = _chrome.Hwnd;

            _messages = new ShellMessageHook(hwnd, 0);
            _messages.HotkeyPressed += id =>
            {
                if (id == HotkeyId || id == HotkeyIdFallback)
                {
                    HandleHotkey();
                }
            };
            _messages.ShowRequested += ShowFromTray;

            _hotkeyRegistered = RegisterHotKey(hwnd, HotkeyId, _hotkeyModifiers, _hotkeyKey);
            // Always also claim Ctrl+Alt+Q: if Alt+Q is owned by another app (very
            // common: the installed 1.x build) the feature still has a working key.
            _fallbackHotkeyRegistered = RegisterHotKey(hwnd, HotkeyIdFallback, FallbackModifiers, FallbackKey);
            AppLog.Write("hotkey Alt+Q registered=" + _hotkeyRegistered +
                " Ctrl+Alt+Q registered=" + _fallbackHotkeyRegistered);

            _tray = new TrayIcon();
            _tray.Command += HandleTrayCommand;
            _tray.MenuRequested += ShowTrayMenu;
            _tray.Create(hwnd, "DeskBoxToDo —— 选中一句话变成待办", () => _chrome.Layer);
            _tray.ShowBalloon("DeskBoxToDo 正在运行",
                "托盘图标可能收在 ^ 里。选中文字后按 Alt+Q（被占用时用 Ctrl+Alt+Q）。");
            AppLog.Write("main hwnd=0x" + hwnd.ToInt64().ToString("X"));

            if (!_hotkeyRegistered)
            {
                string warning = _fallbackHotkeyRegistered
                    ? "Alt+Q 被其它程序占用（常见于还在运行的 DeskBoxToDo 1.x），请改用 Ctrl+Alt+Q，或在「设置」里换一个组合。"
                    : "全局热键注册失败：Alt+Q 和 Ctrl+Alt+Q 都被占用。请关闭旧版 DeskBoxToDo 或在「设置」里换组合。";
                ShowHotkeyNotice(warning);
                _tray.ShowBalloon("DeskBoxToDo 热键", warning);
            }

            AppLog.Write("startup complete; data=" + TodoStore.DataRoot);
        }

        private TrayMenuWindow _trayMenu;

        private void ShowTrayMenu()
        {
            try
            {
                _trayMenu?.Close();
                var items = new System.Collections.Generic.List<(string, int, bool, bool, bool)>
                {
                    ("打开待办", TrayIcon.CommandShow, false, true, false),
                    ("撤销上一步", TrayIcon.CommandUndo, false, TodoRepo.CanUndo, false),
                    ("", 0, false, true, true),
                    ("固定在桌面（小组件）", TrayIcon.CommandDesktopLayer, _chrome.Layer == WindowLayer.Desktop, true, false),
                    ("窗口置顶", TrayIcon.CommandTopMost, _chrome.Layer == WindowLayer.TopMost, true, false),
                    ("开机自动启动", TrayIcon.CommandAutostart, Autostart.IsEnabled, true, false),
                    ("", 0, false, true, true),
                    ("设置…", TrayIcon.CommandSettings, false, true, false),
                    ("使用教程", TrayIcon.CommandHelp, false, true, false),
                    ("检查更新…", TrayIcon.CommandCheckUpdate, false, true, false),
                    ("打开日志", TrayIcon.CommandOpenLog, false, true, false),
                    ("", 0, false, true, true),
                    ("退出程序", TrayIcon.CommandExit, false, true, false)
                };

                _trayMenu = new TrayMenuWindow(items);
                _trayMenu.CommandInvoked += HandleTrayCommand;
                _trayMenu.Activate();
            }
            catch (Exception ex)
            {
                AppLog.Write("tray menu failed: " + ex.Message);
            }
        }

        private void HandleTrayCommand(int command)
        {
            switch (command)
            {
                case TrayIcon.CommandShow:
                    ShowFromTray();
                    break;
                case TrayIcon.CommandDesktopLayer:
                    SetLayer(_chrome.Layer == WindowLayer.Desktop ? WindowLayer.Normal : WindowLayer.Desktop);
                    break;
                case TrayIcon.CommandTopMost:
                    SetLayer(_chrome.Layer == WindowLayer.TopMost ? WindowLayer.Normal : WindowLayer.TopMost);
                    break;
                case TrayIcon.CommandAutostart:
                    Autostart.Set(!Autostart.IsEnabled);
                    break;
                case TrayIcon.CommandUndo:
                    UndoLast();
                    break;
                case TrayIcon.CommandSettings:
                    ShowFromTray();
                    OpenSettingsWindow();
                    break;
                case TrayIcon.CommandCheckUpdate:
                    CheckForUpdatesAsync(manual: true);
                    break;
                case TrayIcon.CommandOpenLog:
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppLog.Path)
                        {
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        AppLog.Write("open log failed: " + ex.Message);
                    }
                    break;
                case TrayIcon.CommandHelp:
                    ShowFromTray();
                    ShowOnboarding();
                    break;
                case TrayIcon.CommandExit:
                    Application.Current.Exit();
                    break;
            }
        }

        private void ShowFromTray()
        {
            try
            {
                _chrome.AppWindow.Show();
                _hiddenToTray = false;
                ForceForeground(_chrome.Hwnd);
                Activate();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] show from tray failed: " + ex.Message);
            }
        }

        private void ShowUpdateWindow(UpdateChecker.Result result)
        {
            try
            {
                var window = new UpdateWindow(result);
                window.Activate();
                RaiseAsOwnedDialog(window);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] update window failed: " + ex.Message);
            }
        }

        private void UndoLast()
        {
            if (TodoRepo.Undo())
            {
                Reload();
                ShowHotkeyNotice("已撤销上一步");
            }
            else
            {
                ShowHotkeyNotice("没有可撤销的操作");
            }
        }

        /// <summary>First-run welcome card; also reachable from the tray "使用教程".</summary>
        private void ShowOnboarding()
        {
            try
            {
                string label = (_hotkeyModifiers, _hotkeyKey) switch
                {
                    (0x0001, 0x51) => "Alt + Q",
                    (0x0003, 0x51) => "Ctrl + Alt + Q",
                    (0x0006, 0x20) => "Ctrl + Shift + Space",
                    (0x0001, 0x20) => "Alt + Space",
                    _ => "Alt + Q"
                };

                var window = new OnboardingWindow(label);
                window.Activate();
                RaiseAsOwnedDialog(window);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] onboarding failed: " + ex.Message);
            }
        }

        private void ShowHotkeyNotice(string message)
        {
            try
            {
                var notice = new ReminderWindow("提示", message);
                notice.Activate();
                RaiseAsOwnedDialog(notice);
                var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    notice.Close();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] notice failed: " + ex.Message);
            }
        }

        private void FlashQuickAddBorder()
        {
            QuickAddBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x7F, 0xB2, 0xFF));
            QuickAddBorder.BorderThickness = new Thickness(1);
            var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                QuickAddBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x1F, 0xFF, 0xFF));
            };
            timer.Start();
        }

        private void HandleHotkey()
        {
            try
            {
                AppLog.Write("hotkey fired, foreground=0x" + GetForegroundWindow().ToInt64().ToString("X") +
                    " ours=0x" + _chrome.Hwnd.ToInt64().ToString("X"));

                // Capture BEFORE showing: AppWindow.Show() activates this window,
                // which makes GetForegroundWindow() return our own HWND and the
                // selection is lost.
                string selected = Environment.GetEnvironmentVariable("DBT_FAKE_SELECTION");
                if (string.IsNullOrEmpty(selected))
                {
                    selected = CaptureWithTimeout(_chrome.Hwnd, 2600);
                }

                AppLog.Write("capture result: " + selected.Length + " chars");

                if (!string.IsNullOrWhiteSpace(selected))
                {
                    string trimmed = selected.Trim();
                    if (trimmed.Length > 400)
                    {
                        trimmed = trimmed.Substring(0, 400);
                    }

                    if (!_hiddenToTray)
                    {
                        // Main window is on screen already: keep it behind the card.
                        _chrome.AppWindow.Show();
                        ForceForeground(_chrome.Hwnd);
                        _chrome.RaiseUntilDeactivated();
                    }

                    // When hidden to tray only the card appears — the list stays out
                    // of the way until the user asks for it.
                    OpenConfirmWindow(trimmed);
                    return;
                }

                // Nothing captured: surface the window but DO NOT steal keyboard
                // focus. Otherwise the next Alt+Q would run against our own window
                // and could never capture from the app the user is working in.
                _hiddenToTray = false;
                ShowWindow(_chrome.Hwnd, 4 /* SW_SHOWNOACTIVATE */);
                _chrome.RaiseUntilDeactivated();
                _chrome.FlashTaskbar();
                ShowCaptureHint();
            }
            catch (Exception ex)
            {
                AppLog.Write("hotkey handling failed: " + ex);
                _chrome.AppWindow.Show();
                ForceForeground(_chrome.Hwnd);
                FocusQuickAdd();
            }
        }

        /// <summary>
        /// Runs the capture on a worker thread with a hard deadline: UI Automation
        /// or a busy browser can block for seconds, and a hotkey must never hang the
        /// UI thread.
        /// </summary>
        private static string CaptureWithTimeout(IntPtr ourWindow, int timeoutMs)
        {
            string result = null;
            var worker = new System.Threading.Thread(() =>
            {
                try
                {
                    result = SelectionCapture.CaptureFromForeground(ourWindow);
                }
                catch (Exception ex)
                {
                    AppLog.Write("capture threw: " + ex.Message);
                }
            })
            {
                IsBackground = true,
                Name = "DeskBoxToDo.Capture"
            };
            worker.Start();
            if (!worker.Join(timeoutMs))
            {
                AppLog.Write("capture timed out after " + timeoutMs + " ms");
                return "";
            }

            return result ?? "";
        }

        private void FocusQuickAdd()
        {
            QuickAddBox.Focus(FocusState.Programmatic);
            FlashQuickAddBorder();
            ShowCaptureHint();
        }

        /// <summary>Shows the "nothing captured" hint without taking focus.</summary>
        private void ShowCaptureHint()
        {
            QuickAddBox.PlaceholderText = "没有取到选中的文字，直接输入要点…";
            var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                QuickAddBox.PlaceholderText = "记一条…（回车保存）";
            };
            timer.Start();
        }

        private void StartAutoUpdateCheck()
        {
            _autoUpdateTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            _autoUpdateTimer.Tick += (sender, args) => CheckForUpdatesAsync(manual: false);
            _autoUpdateTimer.Start();

            var first = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            first.Tick += (sender, args) =>
            {
                first.Stop();
                CheckForUpdatesAsync(manual: false);
            };
            first.Start();
        }

        private async void CheckForUpdatesAsync(bool manual)
        {
            try
            {
                UpdateChecker.Result result = await UpdateChecker.CheckAsync();
                if (result.HasUpdate)
                {
                    ShowUpdateWindow(result);
                }
                else if (manual)
                {
                    ShowHotkeyNotice("已是最新版本（" + result.CurrentVersion + "）");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] update check failed: " + ex.Message);
                if (manual)
                {
                    ShowHotkeyNotice("检查更新失败：" + ex.Message);
                }
            }
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            try
            {
                _messages?.Dispose();
                _tray?.Dispose();
                if (_hotkeyRegistered)
                {
                    UnregisterHotKey(_chrome.Hwnd, HotkeyId);
                }

                if (_fallbackHotkeyRegistered)
                {
                    UnregisterHotKey(_chrome.Hwnd, HotkeyIdFallback);
                }

                _reminderTimer?.Stop();
                _autoUpdateTimer?.Stop();
                _reminderWindow?.Close();
                MaterialLadder.Dispose(this);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] cleanup failed: " + ex.Message);
            }
        }

        // ── Win32 ──────────────────────────────────────────────────

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hwnd);

        /// <summary>Bringing a window forward can be refused; attach to the foreground thread first.</summary>
        private static void ForceForeground(IntPtr hwnd)
        {
            try
            {
                IntPtr foreground = GetForegroundWindow();
                uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
                uint myThread = GetCurrentThreadId();
                bool attached = foregroundThread != 0 &&
                    foregroundThread != myThread &&
                    AttachThreadInput(foregroundThread, myThread, true);

                ShowWindow(hwnd, 9);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                SetFocus(hwnd);

                if (attached)
                {
                    AttachThreadInput(foregroundThread, myThread, false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Todo] foreground failed: " + ex.Message);
            }
        }
    }
}
