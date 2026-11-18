using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DeskBoxToDo
{
    /// <summary>
    /// Persistence for the todo feature, schema-identical to DeskBox's todo.json
    /// (version 3, same field names), in either our own file or DeskBox's widget
    /// store. Normalisation mirrors DeskBox's TodoWidgetStore.Normalize.
    /// </summary>
    internal static class TodoBackend
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static bool IsDeskBoxMode(AppConfig config)
        {
            return string.Equals(config.TodoMode, "deskbox", StringComparison.OrdinalIgnoreCase);
        }

        public static string StandalonePath
        {
            get { return Path.Combine(AppConfig.ConfigDirectory, "todo.json"); }
        }

        public static bool DeskBoxAvailable(AppConfig config)
        {
            try
            {
                return File.Exists(config.DeskBoxExePath);
            }
            catch
            {
                return false;
            }
        }

        public static List<TodoItem> Load(AppConfig config)
        {
            return IsDeskBoxMode(config) ? LoadFromDeskBox(config) : LoadStandalone();
        }

        private static List<TodoItem> LoadStandalone()
        {
            string path = StandalonePath;
            if (!File.Exists(path))
            {
                return new List<TodoItem>();
            }

            try
            {
                return Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Log.Write("todo load failed: " + ex.Message);
                return new List<TodoItem>();
            }
        }

        private static List<TodoItem> LoadFromDeskBox(AppConfig config)
        {
            try
            {
                string root = DeskBoxStore.ResolveDataRoot(config);
                string widgetId = DeskBoxStore.FindTodoWidgetId(root);
                if (string.IsNullOrEmpty(widgetId))
                {
                    return new List<TodoItem>();
                }

                string path = Path.Combine(root, "data", "widgets", widgetId, "todo.json");
                if (!File.Exists(path))
                {
                    return new List<TodoItem>();
                }

                return Parse(File.ReadAllText(path, Encoding.UTF8)).Where(item => !item.IsDeleted).ToList();
            }
            catch (Exception ex)
            {
                Log.Write("deskbox todo load failed: " + ex.Message);
                return new List<TodoItem>();
            }
        }

        public static void Save(AppConfig config, List<TodoItem> items)
        {
            if (IsDeskBoxMode(config))
            {
                SaveToDeskBox(config, items);
            }
            else
            {
                SaveStandalone(items);
            }
        }

        private static void SaveStandalone(List<TodoItem> items)
        {
            try
            {
                Directory.CreateDirectory(AppConfig.ConfigDirectory);
                string path = StandalonePath;
                string temp = path + ".tmp";
                File.WriteAllText(temp, Serialize(items), new UTF8Encoding(false));
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
            catch (Exception ex)
            {
                Log.Write("todo save failed: " + ex.Message);
            }
        }

        private static void SaveToDeskBox(AppConfig config, List<TodoItem> items)
        {
            try
            {
                string root = DeskBoxStore.ResolveDataRoot(config);
                string widgetId = DeskBoxStore.FindTodoWidgetId(root);
                if (string.IsNullOrEmpty(widgetId))
                {
                    return;
                }

                string directory = Path.Combine(root, "data", "widgets", widgetId);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "todo.json");

                int stopped;
                string error;
                bool wasRunning = DeskBoxApp.FindRunning(config.DeskBoxExePath).Count > 0;
                if (wasRunning && !DeskBoxApp.Stop(config.DeskBoxExePath, TimeSpan.FromSeconds(8), out stopped, out error))
                {
                    Log.Write("deskbox stop failed before save: " + error);
                    return;
                }

                if (File.Exists(path))
                {
                    File.Copy(path, path + ".bak", true);
                }

                File.WriteAllText(path, Serialize(items), new UTF8Encoding(false));
                if (wasRunning)
                {
                    DeskBoxApp.Start(config.DeskBoxExePath, out error);
                }
            }
            catch (Exception ex)
            {
                Log.Write("deskbox todo save failed: " + ex.Message);
            }
        }

        // ---------- normalisation (mirrors DeskBox Normalize) ----------

        public static List<TodoItem> Normalize(List<TodoItem> items)
        {
            var result = new List<TodoItem>();
            int fallbackSortOrder = 0;
            foreach (TodoItem item in items.Where(entry => entry != null))
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    item.Id = Guid.NewGuid().ToString("N");
                }

                item.Text = (item.Text ?? "").Trim();
                item.ColorMarker = TodoItem.NormalizeMarker(item.ColorMarker);
                item.Recurrence = TodoRecurrenceMode.Normalize(item.Recurrence?.Mode) == TodoRecurrenceMode.None || !item.DueDate.HasValue
                    ? null
                    : item.Recurrence;
                item.Steps = item.Steps.Where(step => step != null && !string.IsNullOrWhiteSpace(step.Text)).ToList();
                int stepOrder = 0;
                foreach (TodoStep step in item.Steps)
                {
                    step.Id = string.IsNullOrWhiteSpace(step.Id) ? Guid.NewGuid().ToString("N") : step.Id;
                    step.Text = step.Text.Trim();
                    step.SortOrder = stepOrder++;
                }

                item.ReminderOffsetMinutes = TodoReminderOptions.Normalize(item.ReminderOffsetMinutes);

                if (!item.DueDate.HasValue)
                {
                    item.Recurrence = null;
                    item.RecurrenceSeriesId = null;
                    item.ReminderLastNotifiedAt = null;
                    item.ReminderDismissedForDueDate = null;
                    item.SnoozedUntil = null;
                    item.SnoozeLastNotifiedAt = null;
                }

                if (item.IsCompleted)
                {
                    item.CompletedAt = item.CompletedAt ?? item.UpdatedAt;
                    item.SnoozedUntil = null;
                    item.SnoozeLastNotifiedAt = null;
                }
                else
                {
                    item.CompletedAt = null;
                }

                if (!item.IsCompleted || item.Recurrence == null)
                {
                    item.GeneratedNextItemId = null;
                }

                if (item.Recurrence == null)
                {
                    item.RecurrenceSeriesId = null;
                }

                if (item.SortOrder < 0)
                {
                    item.SortOrder = fallbackSortOrder;
                }

                fallbackSortOrder++;
                result.Add(item);
            }

            result = result
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.SortOrder)
                .ThenByDescending(item => item.UpdatedAt)
                .ToList();

            for (int index = 0; index < result.Count; index++)
            {
                result[index].SortOrder = index;
            }

            return result;
        }

        // ---------- json ----------

        public static string Serialize(List<TodoItem> items)
        {
            var payload = new Dictionary<string, object>
            {
                { "version", 3 },
                { "items", Normalize(items).Select(ToMap).Cast<object>().ToArray() }
            };
            return Serializer.Serialize(payload);
        }

        public static List<TodoItem> Parse(string json)
        {
            var items = new List<TodoItem>();
            var root = Serializer.Deserialize<Dictionary<string, object>>(json);
            foreach (object entry in GetList(root, "items"))
            {
                var map = entry as Dictionary<string, object>;
                if (map != null)
                {
                    items.Add(FromMap(map));
                }
            }

            return items;
        }

        private static Dictionary<string, object> ToMap(TodoItem item)
        {
            Dictionary<string, object> recurrence = null;
            if (item.Recurrence != null)
            {
                recurrence = new Dictionary<string, object>
                {
                    { "mode", TodoRecurrenceMode.Normalize(item.Recurrence.Mode) },
                    {
                        "anchorDueDate", item.Recurrence.AnchorDueDate.HasValue
                            ? item.Recurrence.AnchorDueDate.Value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)
                            : null
                    }
                };
            }

            return new Dictionary<string, object>
            {
                { "id", item.Id },
                { "text", item.Text },
                { "isCompleted", item.IsCompleted },
                { "isImportant", item.IsImportant },
                { "colorMarker", string.IsNullOrEmpty(item.ColorMarker) ? null : item.ColorMarker },
                {
                    "dueDate", item.DueDate.HasValue
                        ? item.DueDate.Value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)
                        : null
                },
                { "recurrence", recurrence },
                {
                    "steps", item.Steps.Select(step => (object)new Dictionary<string, object>
                    {
                        { "id", step.Id },
                        { "text", step.Text },
                        { "isCompleted", step.IsCompleted },
                        { "sortOrder", step.SortOrder }
                    }).ToArray()
                },
                { "notes", item.Notes },
                {
                    "attachments", item.Attachments.Select(attachment => (object)new Dictionary<string, object>
                    {
                        { "id", attachment.Id },
                        { "filePath", attachment.FilePath },
                        { "displayName", attachment.DisplayName },
                        { "type", attachment.Type },
                        { "storageMode", "link" },
                        { "addedAt", attachment.AddedAt.ToString("o", CultureInfo.InvariantCulture) }
                    }).ToArray()
                },
                {
                    "completedAt", item.CompletedAt.HasValue
                        ? item.CompletedAt.Value.ToString("o", CultureInfo.InvariantCulture)
                        : null
                },
                {
                    "reminderLastNotifiedAt", item.ReminderLastNotifiedAt.HasValue
                        ? item.ReminderLastNotifiedAt.Value.ToString("o", CultureInfo.InvariantCulture)
                        : null
                },
                {
                    "reminderDismissedForDueDate", item.ReminderDismissedForDueDate.HasValue
                        ? item.ReminderDismissedForDueDate.Value.ToString("o", CultureInfo.InvariantCulture)
                        : null
                },
                { "reminderOffsetMinutes", item.ReminderOffsetMinutes.HasValue ? (object)item.ReminderOffsetMinutes.Value : null },
                {
                    "snoozedUntil", item.SnoozedUntil.HasValue
                        ? item.SnoozedUntil.Value.ToString("o", CultureInfo.InvariantCulture)
                        : null
                },
                {
                    "snoozeLastNotifiedAt", item.SnoozeLastNotifiedAt.HasValue
                        ? item.SnoozeLastNotifiedAt.Value.ToString("o", CultureInfo.InvariantCulture)
                        : null
                },
                { "recurrenceSeriesId", item.RecurrenceSeriesId },
                { "generatedNextItemId", item.GeneratedNextItemId },
                { "sortOrder", item.SortOrder },
                { "createdAt", item.CreatedAt.ToString("o", CultureInfo.InvariantCulture) },
                { "updatedAt", item.UpdatedAt.ToString("o", CultureInfo.InvariantCulture) },
                { "deviceId", ReadDeviceId() },
                { "isDeleted", item.IsDeleted }
            };
        }

        private static TodoItem FromMap(Dictionary<string, object> map)
        {
            var item = new TodoItem
            {
                Id = Str(map, "id"),
                Text = Str(map, "text"),
                IsCompleted = Bool(map, "isCompleted"),
                IsImportant = Bool(map, "isImportant"),
                ColorMarker = Str(map, "colorMarker"),
                Notes = Str(map, "notes"),
                IsDeleted = Bool(map, "isDeleted"),
                SortOrder = Int(map, "sortOrder"),
                RecurrenceSeriesId = Str(map, "recurrenceSeriesId"),
                GeneratedNextItemId = Str(map, "generatedNextItemId")
            };

            item.DueDate = Date(map, "dueDate");
            item.CompletedAt = Date(map, "completedAt");
            item.ReminderLastNotifiedAt = Date(map, "reminderLastNotifiedAt");
            item.ReminderDismissedForDueDate = Date(map, "reminderDismissedForDueDate");
            item.SnoozedUntil = Date(map, "snoozedUntil");
            item.SnoozeLastNotifiedAt = Date(map, "snoozeLastNotifiedAt");
            item.CreatedAt = Date(map, "createdAt") ?? DateTimeOffset.UtcNow;
            item.UpdatedAt = Date(map, "updatedAt") ?? item.CreatedAt;

            object offset;
            if (map.TryGetValue("reminderOffsetMinutes", out offset) && offset != null)
            {
                try { item.ReminderOffsetMinutes = Convert.ToInt32(offset, CultureInfo.InvariantCulture); }
                catch { }
            }

            object recurrenceRaw;
            if (map.TryGetValue("recurrence", out recurrenceRaw))
            {
                var recurrence = recurrenceRaw as Dictionary<string, object>;
                if (recurrence != null)
                {
                    item.Recurrence = new TodoRecurrence
                    {
                        Mode = TodoRecurrenceMode.Normalize(Str(recurrence, "mode")),
                        AnchorDueDate = Date(recurrence, "anchorDueDate")
                    };
                }
            }

            object attachmentsRaw;
            if (map.TryGetValue("attachments", out attachmentsRaw))
            {
                foreach (object entry in Flatten(attachmentsRaw))
                {
                    var attachment = entry as Dictionary<string, object>;
                    if (attachment != null && !string.IsNullOrWhiteSpace(Str(attachment, "filePath")))
                    {
                        item.Attachments.Add(new TodoAttachment
                        {
                            Id = Str(attachment, "id"),
                            FilePath = Str(attachment, "filePath"),
                            DisplayName = Str(attachment, "displayName"),
                            Type = Str(attachment, "type"),
                            AddedAt = Date(attachment, "addedAt") ?? DateTimeOffset.UtcNow
                        });
                    }
                }
            }

            object stepsRaw;
            if (map.TryGetValue("steps", out stepsRaw))
            {
                foreach (object entry in Flatten(stepsRaw))
                {
                    var step = entry as Dictionary<string, object>;
                    if (step != null)
                    {
                        item.Steps.Add(new TodoStep
                        {
                            Id = Str(step, "id"),
                            Text = Str(step, "text"),
                            IsCompleted = Bool(step, "isCompleted"),
                            SortOrder = Int(step, "sortOrder")
                        });
                    }
                }
            }

            return item;
        }

        private static IEnumerable<object> Flatten(object value)
        {
            var array = value as object[];
            if (array != null)
            {
                return array;
            }

            var list = value as System.Collections.ArrayList;
            if (list != null)
            {
                return list.Cast<object>();
            }

            return new object[0];
        }

        private static List<object> GetList(Dictionary<string, object> parent, string key)
        {
            object value;
            if (parent == null || !parent.TryGetValue(key, out value) || value == null)
            {
                return new List<object>();
            }

            return Flatten(value).ToList();
        }

        private static string Str(Dictionary<string, object> map, string key)
        {
            object value;
            if (map != null && map.TryGetValue(key, out value) && value != null)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return "";
        }

        private static bool Bool(Dictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) && value is bool && (bool)value;
        }

        private static int Int(Dictionary<string, object> map, string key)
        {
            object value;
            if (map != null && map.TryGetValue(key, out value) && value != null)
            {
                try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
                catch { }
            }

            return 0;
        }

        private static DateTimeOffset? Date(Dictionary<string, object> map, string key)
        {
            string text = Str(map, key);
            DateTimeOffset parsed;
            if (!string.IsNullOrEmpty(text) &&
                DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
            {
                return parsed;
            }

            return null;
        }

        private static string ReadDeviceId()
        {
            try
            {
                string path = Path.Combine(AppConfig.ConfigDirectory, "device.id");
                if (File.Exists(path))
                {
                    string existing = File.ReadAllText(path, Encoding.UTF8).Trim();
                    if (existing.Length > 0)
                    {
                        return existing;
                    }
                }

                string created = Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(AppConfig.ConfigDirectory);
                File.WriteAllText(path, created, new UTF8Encoding(false));
                return created;
            }
            catch
            {
                return Guid.NewGuid().ToString("N");
            }
        }
    }
}
