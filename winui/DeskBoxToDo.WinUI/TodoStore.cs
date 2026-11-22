using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DeskBoxToDo.WinUI
{
    /// <summary>One todo row, read from the same JSON store the WinForms build writes.</summary>
    public sealed class TodoItem
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public bool IsCompleted { get; set; }
        public bool IsImportant { get; set; }
        public string ColorMarker { get; set; }
        public DateTimeOffset? DueDate { get; set; }
        public string RecurrenceMode { get; set; } = "none";
        public int StepCount { get; set; }

        public Microsoft.UI.Xaml.Media.Brush AccentColor =>
            new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorFor(ColorMarker));

        public Microsoft.UI.Xaml.Media.Brush StarBrush =>
            new Microsoft.UI.Xaml.Media.SolidColorBrush(
                IsImportant ? Windows.UI.Color.FromArgb(255, 233, 196, 106) : Windows.UI.Color.FromArgb(0, 255, 255, 255));

        public static readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Windows.UI.Color>> Palette =
            new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Windows.UI.Color>>
            {
                // 与 1.x 完全一致的色标（英文 key，存进 JSON 的也是这些）
                new System.Collections.Generic.KeyValuePair<string, Windows.UI.Color>("red", Windows.UI.Color.FromArgb(255, 227, 77, 77)),
                new System.Collections.Generic.KeyValuePair<string, Windows.UI.Color>("orange", Windows.UI.Color.FromArgb(255, 240, 138, 60)),
                new System.Collections.Generic.KeyValuePair<string, Windows.UI.Color>("yellow", Windows.UI.Color.FromArgb(255, 242, 201, 76)),
                new System.Collections.Generic.KeyValuePair<string, Windows.UI.Color>("green", Windows.UI.Color.FromArgb(255, 76, 175, 109)),
                new System.Collections.Generic.KeyValuePair<string, Windows.UI.Color>("blue", Windows.UI.Color.FromArgb(255, 77, 143, 227)),
                new System.Collections.Generic.KeyValuePair<string, Windows.UI.Color>("purple", Windows.UI.Color.FromArgb(255, 155, 107, 232)),
                new System.Collections.Generic.KeyValuePair<string, Windows.UI.Color>("teal", Windows.UI.Color.FromArgb(255, 45, 183, 163)),
                new System.Collections.Generic.KeyValuePair<string, Windows.UI.Color>("pink", Windows.UI.Color.FromArgb(255, 230, 106, 162))
            };

        /// <summary>Chinese labels for UI, keyed by the stored English marker.</summary>
        public static string LabelFor(string marker)
        {
            switch ((marker ?? "").Trim().ToLowerInvariant())
            {
                case "red": return "红";
                case "orange": return "橙";
                case "yellow": return "黄";
                case "green": return "绿";
                case "blue": return "蓝";
                case "purple": return "紫";
                case "teal": return "青";
                case "pink": return "粉";
                default: return "无";
            }
        }

        public static string MarkerForLabel(string label)
        {
            switch (label)
            {
                case "红": return "red";
                case "橙": return "orange";
                case "黄": return "yellow";
                case "绿": return "green";
                case "蓝": return "blue";
                case "紫": return "purple";
                case "青": return "teal";
                case "粉": return "pink";
                default: return "";
            }
        }

        public static Windows.UI.Color ColorFor(string marker)
        {
            string key = (marker ?? "").Trim();
            if (key.Length > 0)
            {
                string lower = key.ToLowerInvariant();
                foreach (var entry in Palette)
                {
                    if (entry.Key == lower)
                    {
                        return entry.Value;
                    }
                }

                // 兼容中文标签与 #RRGGBB
                string mapped = MarkerForLabel(key);
                if (mapped.Length > 0)
                {
                    foreach (var entry in Palette)
                    {
                        if (entry.Key == mapped)
                        {
                            return entry.Value;
                        }
                    }
                }

                try
                {
                    if (key.StartsWith("#") && key.Length >= 7)
                    {
                        byte r = Convert.ToByte(key.Substring(1, 2), 16);
                        byte g = Convert.ToByte(key.Substring(3, 2), 16);
                        byte b = Convert.ToByte(key.Substring(5, 2), 16);
                        return Windows.UI.Color.FromArgb(255, r, g, b);
                    }
                }
                catch
                {
                }
            }

            return Windows.UI.Color.FromArgb(255, 148, 154, 166);
        }
        public string Notes { get; set; } = "";

        /// <summary>Due date string the reminder was already shown for.</summary>
        public string NotifiedForDue { get; set; } = "";

        public bool IsToday
        {
            get
            {
                if (DueDate == null)
                {
                    return false;
                }

                return DueDate.Value.ToLocalTime().Date == DateTimeOffset.Now.Date;
            }
        }

        /// <summary>Second line on the card: due text plus recurrence hint.</summary>
        public string Subtitle
        {
            get
            {
                string text;
                if (DueDate == null)
                {
                    text = IsCompleted ? "已完成" : "无截止时间";
                }
                else
                {
                    DateTimeOffset local = DueDate.Value.ToLocalTime();
                    DateTime today = DateTime.Now.Date;
                    if (local.Date == today)
                    {
                        text = "今天 " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
                    }
                    else if (local.Date == today.AddDays(1))
                    {
                        text = "明天 " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
                    }
                    else if (local.Date == today.AddDays(-1))
                    {
                        text = "昨天 " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        text = "截止 " + local.ToString("yyyy/M/d HH:mm", CultureInfo.InvariantCulture);
                    }

                    if (local < DateTimeOffset.Now && !IsCompleted)
                    {
                        text = "已逾期 · " + text;
                    }
                }

                if (RecurrenceMode != "none")
                {
                    text += " · 重复" + TodoStore.RecurrenceLabel(RecurrenceMode);
                }

                return text;
            }
        }
    }

    /// <summary>Reads the DeskBoxToDo store (standalone file, or DeskBox's widget file).</summary>
    public static class TodoStore
    {
        public static string DataRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskBoxToDo");

        public static string ConfigPath => Path.Combine(DataRoot, "config.json");

        public static string StandalonePath => Path.Combine(DataRoot, "todo.json");

        public static string DeskBoxWidgetId = "d3dba7d5-648b-4a58-86a6-c83eff39e131";

        public static string DeskBoxPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskBox", "data", "widgets", DeskBoxWidgetId, "todo.json");

        /// <summary>Which file the app should use ("deskbox" or "standalone"), from config.json.</summary>
        public static string CurrentMode()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(ConfigPath)))
                    {
                        if (document.RootElement.TryGetProperty("todoMode", out JsonElement mode))
                        {
                            return (mode.GetString() ?? "standalone").ToLowerInvariant();
                        }
                    }
                }
            }
            catch
            {
            }

            return "standalone";
        }

        public static string CurrentPath()
        {
            string mode = CurrentMode();
            if (mode == "deskbox" && File.Exists(DeskBoxPath))
            {
                return DeskBoxPath;
            }

            return StandalonePath;
        }

        public static List<TodoItem> Load(out string path)
        {
            path = CurrentPath();
            var items = new List<TodoItem>();
            if (!File.Exists(path))
            {
                return items;
            }

            try
            {
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
                {
                    if (!document.RootElement.TryGetProperty("items", out JsonElement array) ||
                        array.ValueKind != JsonValueKind.Array)
                    {
                        return items;
                    }

                    foreach (JsonElement row in array.EnumerateArray())
                    {
                        var item = new TodoItem
                        {
                            Id = Text(row, "id"),
                            Text = Text(row, "text"),
                            IsCompleted = Flag(row, "isCompleted"),
                            IsImportant = Flag(row, "isImportant"),
                            ColorMarker = OptionalText(row, "colorMarker"),
                            Notes = Text(row, "notes"),
                            NotifiedForDue = Text(row, "reminderDismissedForDueDate")
                        };

                        string due = OptionalText(row, "dueDate");
                        if (!string.IsNullOrEmpty(due) &&
                            DateTimeOffset.TryParse(due, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed))
                        {
                            item.DueDate = parsed;
                        }

                        if (row.TryGetProperty("recurrence", out JsonElement recurrence) &&
                            recurrence.ValueKind == JsonValueKind.Object)
                        {
                            string mode = OptionalText(recurrence, "mode");
                            if (!string.IsNullOrEmpty(mode))
                            {
                                item.RecurrenceMode = mode;
                            }
                        }

                        if (row.TryGetProperty("steps", out JsonElement steps) && steps.ValueKind == JsonValueKind.Array)
                        {
                            item.StepCount = steps.GetArrayLength();
                        }

                        items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("store read failed: " + ex.Message);
            }

            return items;
        }

        public static string RecurrenceLabel(string mode)
        {
            switch (mode)
            {
                case "daily":
                    return "每天";
                case "weekly":
                    return "每周";
                case "monthly":
                    return "每月";
                case "weekdays":
                    return "工作日";
                default:
                    return "";
            }
        }

        private static string Text(JsonElement row, string name)
        {
            return OptionalText(row, name) ?? "";
        }

        private static string OptionalText(JsonElement row, string name)
        {
            if (row.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return null;
        }

        private static bool Flag(JsonElement row, string name)
        {
            if (row.TryGetProperty(name, out JsonElement value) &&
                (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                return value.GetBoolean();
            }

            return false;
        }
    }
}
