using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeskBoxToDo.WinUI
{
    /// <summary>Read/write the shared deskbox-style todo store, preserving unknown fields.</summary>
    public static class TodoRepo
    {
        private const int UndoLimit = 30;
        private static readonly List<string> UndoStack = new List<string>();

        public static string Path => TodoStore.CurrentPath();

        /// <summary>Snapshot taken before a mutation, so Ctrl+Z can restore it.</summary>
        private static void PushUndo()
        {
            try
            {
                if (!File.Exists(Path))
                {
                    return;
                }

                UndoStack.Add(File.ReadAllText(Path));
                while (UndoStack.Count > UndoLimit)
                {
                    UndoStack.RemoveAt(0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("undo snapshot failed: " + ex.Message);
            }
        }

        public static bool CanUndo => UndoStack.Count > 0;

        /// <summary>Restores the previous snapshot; returns false when the stack is empty.</summary>
        public static bool Undo()
        {
            if (UndoStack.Count == 0)
            {
                return false;
            }

            string snapshot = UndoStack[^1];
            UndoStack.RemoveAt(UndoStack.Count - 1);
            try
            {
                string path = Path;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, snapshot);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("undo restore failed: " + ex.Message);
                return false;
            }
        }

        public static JsonObject ReadRoot()
        {
            try
            {
                if (File.Exists(Path))
                {
                    JsonNode node = JsonNode.Parse(File.ReadAllText(Path));
                    if (node is JsonObject obj)
                    {
                        if (obj["items"] is not JsonArray)
                        {
                            obj["items"] = new JsonArray();
                        }

                        obj["version"] ??= 3;
                        return obj;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("repo read failed: " + ex.Message);
            }

            return new JsonObject { ["version"] = 3, ["items"] = new JsonArray() };
        }

        public static void WriteRoot(JsonObject root)
        {
            try
            {
                string path = Path;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                string temp = path + ".v2tmp";
                File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
            catch (Exception ex)
            {
                Console.WriteLine("repo write failed: " + ex.Message);
            }
        }

        public static JsonObject Find(JsonObject root, string id)
        {
            if (root["items"] is not JsonArray items)
            {
                return null;
            }

            foreach (JsonNode node in items)
            {
                if (node is JsonObject obj && (string)obj["id"] == id)
                {
                    return obj;
                }
            }

            return null;
        }

        public static void SetCompleted(string id, bool completed)
        {
            PushUndo();
            JsonObject root = ReadRoot();
            JsonObject item = Find(root, id);
            if (item == null)
            {
                return;
            }

            bool wasCompleted = (bool?)item["isCompleted"] ?? false;
            item["isCompleted"] = completed;
            item["completedAt"] = completed ? DateTimeOffset.UtcNow.ToString("o") : null;

            if (completed && !wasCompleted)
            {
                GenerateNextOccurrence(root, item);
            }

            WriteRoot(root);
        }

        /// <summary>
        /// 1.x parity: completing a repeating item creates its next occurrence and
        /// links the two through generatedNextItemId.
        /// </summary>
        private static void GenerateNextOccurrence(JsonObject root, JsonObject item)
        {
            if (item["recurrence"] is not JsonObject recurrence)
            {
                return;
            }

            string mode = (string)recurrence["mode"];
            if (string.IsNullOrEmpty(mode) || mode == "none")
            {
                return;
            }

            if (((string)item["dueDate"]) is not { Length: > 0 } dueText ||
                !DateTimeOffset.TryParse(dueText, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset due))
            {
                return;
            }

            if (item["generatedNextItemId"] is JsonValue existing && Find(root, (string)existing) != null)
            {
                return; // already generated for this occurrence
            }

            DateTimeOffset next = mode switch
            {
                "daily" => due.AddDays(1),
                "weekly" => due.AddDays(7),
                "monthly" => AddMonth(due),
                "weekdays" => NextWeekday(due),
                _ => due
            };

            if (next <= due)
            {
                return;
            }

            var clone = (JsonObject)item.DeepClone();
            string newId = Guid.NewGuid().ToString("N");
            clone["id"] = newId;
            clone["isCompleted"] = false;
            clone["completedAt"] = null;
            clone["dueDate"] = next.ToString("o");
            clone["createdAt"] = DateTimeOffset.UtcNow.ToString("o");
            clone["updatedAt"] = DateTimeOffset.UtcNow.ToString("o");
            clone["generatedNextItemId"] = null;
            clone["recurrenceSeriesId"] = (string)item["recurrenceSeriesId"] ?? (string)item["id"];
            clone["reminderDismissedForDueDate"] = null;
            clone["reminderLastNotifiedAt"] = null;
            clone["snoozedUntil"] = null;
            clone["snoozeLastNotifiedAt"] = null;

            if (item["reminderOffsetMinutes"] is JsonValue offset)
            {
                clone["reminderOffsetMinutes"] = offset.DeepClone();
            }

            if (clone["steps"] is JsonArray steps)
            {
                foreach (JsonNode node in steps)
                {
                    if (node is JsonObject step)
                    {
                        step["isCompleted"] = false;
                    }
                }
            }

            if (root["items"] is JsonArray items)
            {
                clone["sortOrder"] = items.Count;
                items.Add(clone);
            }

            item["generatedNextItemId"] = newId;
        }

        private static DateTimeOffset AddMonth(DateTimeOffset due)
        {
            DateTime nextMonth = due.Date.AddMonths(1);
            int daysInMonth = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
            int day = Math.Min(due.Day, daysInMonth);
            return new DateTimeOffset(nextMonth.Year, nextMonth.Month, day, due.Hour, due.Minute, 0, due.Offset);
        }

        private static DateTimeOffset NextWeekday(DateTimeOffset due)
        {
            DateTimeOffset next = due.AddDays(1);
            while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
            {
                next = next.AddDays(1);
            }

            return next;
        }

        /// <summary>Records that the reminder for this due date already fired.</summary>
        public static void MarkNotified(string id, string dueIso)
        {
            JsonObject root = ReadRoot();
            JsonObject item = Find(root, id);
            if (item == null)
            {
                return;
            }

            item["reminderLastNotifiedAt"] = DateTimeOffset.UtcNow.ToString("o");
            item["reminderDismissedForDueDate"] = dueIso;
            WriteRoot(root);
        }

        /// <summary>
        /// Toggles one checklist step. Returns true when every step is done, so the
        /// caller can complete the parent item the same way DeskBox does.
        /// </summary>
        public static bool SetStepCompleted(string id, int index, bool completed)
        {
            PushUndo();
            JsonObject root = ReadRoot();
            JsonObject item = Find(root, id);
            if (item?["steps"] is not JsonArray steps || index < 0 || index >= steps.Count)
            {
                return false;
            }

            if (steps[index] is JsonObject step)
            {
                step["isCompleted"] = completed;
            }

            WriteRoot(root);

            foreach (JsonNode node in steps)
            {
                if (node is not JsonObject row || !((bool?)row["isCompleted"] ?? false))
                {
                    return false;
                }
            }

            return steps.Count > 0;
        }

        /// <summary>Stores the per-item lead time (null removes the override).</summary>
        public static void SetReminder(string id, int? offsetMinutes)
        {
            PushUndo();
            JsonObject root = ReadRoot();
            JsonObject item = Find(root, id);
            if (item == null)
            {
                return;
            }

            item["reminderOffsetMinutes"] = offsetMinutes.HasValue ? JsonValue.Create(offsetMinutes.Value) : null;
            // A changed lead time invalidates the "already notified" stamp so the
            // new schedule can fire for the same due date.
            item["reminderDismissedForDueDate"] = null;
            item["reminderLastNotifiedAt"] = null;
            WriteRoot(root);
        }

        /// <summary>"稍后 10 分钟": re-arm the reminder at a later moment.</summary>
        public static void Snooze(string id, DateTimeOffset until)
        {
            JsonObject root = ReadRoot();
            JsonObject item = Find(root, id);
            if (item == null)
            {
                return;
            }

            item["snoozedUntil"] = until.ToUniversalTime().ToString("o");
            item["snoozeLastNotifiedAt"] = null;
            WriteRoot(root);
        }

        /// <summary>Marks the snoozed occurrence as delivered so it does not repeat.</summary>
        public static void MarkSnoozeNotified(string id, DateTimeOffset snoozedUntil)
        {
            JsonObject root = ReadRoot();
            JsonObject item = Find(root, id);
            if (item == null)
            {
                return;
            }

            item["snoozeLastNotifiedAt"] = snoozedUntil.ToUniversalTime().ToString("o");
            item["reminderLastNotifiedAt"] = DateTimeOffset.UtcNow.ToString("o");
            WriteRoot(root);
        }

        public static void SetImportant(string id, bool important)
        {
            JsonObject root = ReadRoot();
            JsonObject item = Find(root, id);
            if (item == null)
            {
                return;
            }

            item["isImportant"] = important;
            WriteRoot(root);
        }

        public static void Delete(string id)
        {
            PushUndo();
            JsonObject root = ReadRoot();
            if (root["items"] is not JsonArray items)
            {
                return;
            }

            for (int index = items.Count - 1; index >= 0; index--)
            {
                if (items[index] is JsonObject obj && (string)obj["id"] == id)
                {
                    items.RemoveAt(index);
                }
            }

            WriteRoot(root);
        }

        /// <summary>Creates or updates one item and returns its id.</summary>
        public static string Save(
            string id,
            string text,
            DateTimeOffset? due,
            string recurrence,
            string color,
            bool important,
            string notes,
            IEnumerable<string> steps)
        {
            PushUndo();
            JsonObject root = ReadRoot();
            JsonObject item = string.IsNullOrEmpty(id) ? null : Find(root, id);
            bool isNew = item == null;

            if (isNew)
            {
                item = new JsonObject
                {
                    ["id"] = Guid.NewGuid().ToString("N"),
                    ["text"] = text,
                    ["isCompleted"] = false,
                    ["isImportant"] = important,
                    ["colorMarker"] = color,
                    ["dueDate"] = null,
                    ["recurrence"] = null,
                    ["steps"] = new JsonArray(),
                    ["notes"] = notes ?? "",
                    ["attachments"] = new JsonArray(),
                    ["sortOrder"] = ((JsonArray)root["items"]).Count
                };

                ((JsonArray)root["items"]).Add(item);
            }
            else
            {
                item["text"] = text;
                item["isImportant"] = important;
                item["colorMarker"] = color;
                item["notes"] = notes ?? "";
            }

            item["dueDate"] = due?.ToString("o");

            if (string.IsNullOrEmpty(recurrence) || recurrence == "none")
            {
                item["recurrence"] = null;
            }
            else
            {
                item["recurrence"] = new JsonObject
                {
                    ["mode"] = recurrence,
                    ["anchorDueDate"] = due?.ToString("o")
                };
            }

            var stepArray = new JsonArray();
            int order = 0;
            foreach (string step in steps ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(step))
                {
                    continue;
                }

                stepArray.Add(new JsonObject
                {
                    ["id"] = Guid.NewGuid().ToString("N"),
                    ["text"] = step.Trim(),
                    ["isCompleted"] = false,
                    ["sortOrder"] = order++
                });
            }

            // Keep completion state of existing steps when their text matches.
            if (!isNew && item["steps"] is JsonArray oldSteps)
            {
                foreach (JsonNode node in stepArray)
                {
                    var fresh = node as JsonObject;
                    foreach (JsonNode oldNode in oldSteps)
                    {
                        if (oldNode is JsonObject old && (string)old["text"] == (string)fresh["text"])
                        {
                            fresh["isCompleted"] = (bool?)old["isCompleted"] ?? false;
                        }
                    }
                }
            }

            item["steps"] = stepArray;
            WriteRoot(root);
            return (string)item["id"];
        }

        public static List<string> StepTexts(JsonObject item)
        {
            var list = new List<string>();
            if (item?["steps"] is JsonArray steps)
            {
                foreach (JsonNode node in steps)
                {
                    if (node is JsonObject step && step["text"] != null)
                    {
                        list.Add((string)step["text"]);
                    }
                }
            }

            return list;
        }
    }
}
