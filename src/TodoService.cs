using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeskBoxToDo
{
    /// <summary>
    /// Todo operations ported from DeskBox's TodoWidgetViewModel: add / complete
    /// (with recurrence roll-over) / steps / filters / batch / clear completed /
    /// snooze / undo.
    /// </summary>
    internal sealed class TodoService
    {
        private readonly AppConfig _config;
        private readonly List<List<TodoItem>> _undo = new List<List<TodoItem>>();

        public TodoService(AppConfig config)
        {
            _config = config;
        }

        public bool CanUndo
        {
            get { return _undo.Count > 0; }
        }

        public List<TodoItem> Load()
        {
            return TodoBackend.Load(_config).Where(item => !item.IsDeleted).ToList();
        }

        public List<TodoItem> Query(TodoFilter filter, bool showCompleted, string search)
        {
            IEnumerable<TodoItem> items = Load();
            if (!string.IsNullOrWhiteSpace(search))
            {
                string needle = search.Trim();
                items = items.Where(item =>
                    item.Text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Steps.Any(step => step.Text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            List<TodoItem> filtered = items.Where(item => TodoQuery.Matches(item, filter, showCompleted)).ToList();
            return TodoQuery.Sort(filtered);
        }

        public int CountNonCompleted()
        {
            return Load().Count(item => !item.IsCompleted);
        }

        private void Snapshot()
        {
            _undo.Add(Load().Select(item => item.Clone()).ToList());
            if (_undo.Count > 30)
            {
                _undo.RemoveAt(0);
            }
        }

        private void Commit(List<TodoItem> items)
        {
            TodoBackend.Save(_config, items);
        }

        public bool Undo()
        {
            if (_undo.Count == 0)
            {
                return false;
            }

            List<TodoItem> snapshot = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Commit(snapshot);
            Log.Write("todo undo applied");
            return true;
        }

        // ---------- add ----------

        public TodoItem Add(TodoDraft draft, bool atTop = true)
        {
            Snapshot();
            List<TodoItem> items = Load();
            var now = DateTimeOffset.UtcNow;
            var item = new TodoItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = (draft.Title ?? "").Trim(),
                IsImportant = draft.Important,
                ColorMarker = TodoItem.NormalizeMarker(draft.ColorMarker),
                DueDate = draft.Due,
                Steps = (draft.Steps ?? new List<string>())
                    .Select((text, index) => new TodoStep { Text = text, SortOrder = index })
                    .ToList(),
                Notes = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim(),
                ReminderOffsetMinutes = null,
                Recurrence = string.IsNullOrEmpty(draft.RecurrenceMode) || !draft.Due.HasValue
                    ? null
                    : TodoRecurrenceService.Create(draft.RecurrenceMode, draft.Due),
                RecurrenceSeriesId = string.IsNullOrEmpty(draft.RecurrenceMode) ? null : Guid.NewGuid().ToString("N"),
                CreatedAt = now,
                UpdatedAt = now
            };

            if (atTop)
            {
                items.Insert(0, item);
            }
            else
            {
                items.Add(item);
            }

            for (int index = 0; index < items.Count; index++)
            {
                items[index].SortOrder = index;
            }

            Commit(items);
            return item;
        }

        public TodoItem Add(TodoItem item)
        {
            Snapshot();
            List<TodoItem> items = Load();
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
            }

            item.CreatedAt = DateTimeOffset.UtcNow;
            item.UpdatedAt = item.CreatedAt;
            items.Insert(0, item);
            for (int index = 0; index < items.Count; index++)
            {
                items[index].SortOrder = index;
            }

            Commit(items);
            return item;
        }

        // ---------- update ----------

        public void Update(TodoItem updated)
        {
            Snapshot();
            List<TodoItem> items = Load();
            int index = items.FindIndex(item => item.Id == updated.Id);
            if (index < 0)
            {
                return;
            }

            updated.UpdatedAt = DateTimeOffset.UtcNow;
            items[index] = updated;
            Commit(items);
        }

        public void SetText(string id, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null)
            {
                return;
            }

            item.Text = text.Trim();
            item.UpdatedAt = DateTimeOffset.UtcNow;
            Commit(items);
        }

        public void SetImportant(string id, bool important)
        {
            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null)
            {
                return;
            }

            item.IsImportant = important;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            Commit(items);
        }

        public void SetColor(string id, string marker)
        {
            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null)
            {
                return;
            }

            item.ColorMarker = TodoItem.NormalizeMarker(marker);
            item.UpdatedAt = DateTimeOffset.UtcNow;
            Commit(items);
        }

        public void SetDueDate(string id, DateTimeOffset? dueDate)
        {
            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null)
            {
                return;
            }

            item.DueDate = dueDate;
            if (!dueDate.HasValue)
            {
                item.Recurrence = null;
                item.RecurrenceSeriesId = null;
            }
            else if (item.Recurrence != null)
            {
                item.Recurrence.AnchorDueDate = dueDate;
            }

            item.UpdatedAt = DateTimeOffset.UtcNow;
            Commit(items);
        }

        public void SetRecurrence(string id, string mode)
        {
            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null)
            {
                return;
            }

            string normalized = TodoRecurrenceMode.Normalize(mode);
            if (normalized == TodoRecurrenceMode.None || !item.DueDate.HasValue)
            {
                item.Recurrence = null;
                item.RecurrenceSeriesId = null;
            }
            else
            {
                item.Recurrence = TodoRecurrenceService.Create(normalized, item.DueDate);
                if (string.IsNullOrWhiteSpace(item.RecurrenceSeriesId))
                {
                    item.RecurrenceSeriesId = Guid.NewGuid().ToString("N");
                }
            }

            item.UpdatedAt = DateTimeOffset.UtcNow;
            Commit(items);
        }

        public void SetReminderOffset(string id, int? minutes)
        {
            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null)
            {
                return;
            }

            item.ReminderOffsetMinutes = TodoReminderOptions.Normalize(minutes);
            item.ReminderLastNotifiedAt = null;
            item.ReminderDismissedForDueDate = null;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            Commit(items);
        }

        public void Snooze(string id, TimeSpan duration)
        {
            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null || !item.DueDate.HasValue)
            {
                return;
            }

            item.SnoozedUntil = DateTimeOffset.Now.Add(duration);
            item.SnoozeLastNotifiedAt = null;
            item.ReminderDismissedForDueDate = item.DueDate;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            Commit(items);
        }

        // ---------- completion (with recurrence roll-over) ----------

        public void SetCompleted(string id, bool completed)
        {
            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (completed)
            {
                if (item.Recurrence != null && string.IsNullOrWhiteSpace(item.RecurrenceSeriesId))
                {
                    item.RecurrenceSeriesId = Guid.NewGuid().ToString("N");
                }

                item.IsCompleted = true;
                item.CompletedAt = now;
                item.SnoozedUntil = null;
                item.SnoozeLastNotifiedAt = null;
                item.GeneratedNextItemId = null;

                TodoItem existingNext = string.IsNullOrWhiteSpace(item.GeneratedNextItemId)
                    ? null
                    : items.FirstOrDefault(entry => entry.Id == item.GeneratedNextItemId);
                bool alreadyGenerated = existingNext != null && !existingNext.IsCompleted;

                TodoItem next;
                if (!alreadyGenerated &&
                    TodoRecurrenceService.TryCreateNextOccurrence(item, now, out next) && next != null)
                {
                    item.GeneratedNextItemId = next.Id;
                    items.Insert(items.IndexOf(item) + 1, next);
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(item.GeneratedNextItemId))
                {
                    TodoItem generated = items.FirstOrDefault(entry => entry.Id == item.GeneratedNextItemId);
                    if (generated != null && TodoRecurrenceService.ShouldRemoveGenerated(item, generated))
                    {
                        items.Remove(generated);
                    }
                }

                item.IsCompleted = false;
                item.CompletedAt = null;
                item.GeneratedNextItemId = null;
            }

            item.UpdatedAt = now;
            Commit(items);
        }

        public void Delete(string id)
        {
            Snapshot();
            List<TodoItem> items = Load();
            items.RemoveAll(item => item.Id == id);
            Commit(items);
        }

        public void ClearCompleted()
        {
            Snapshot();
            List<TodoItem> items = Load();
            items.RemoveAll(item => item.IsCompleted);
            Commit(items);
        }

        public void SetNotes(string id, string notes)
        {
            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null)
            {
                return;
            }

            item.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            Commit(items);
        }

        public void AddAttachment(string id, string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null)
            {
                return;
            }

            item.Attachments.Add(new TodoAttachment
            {
                FilePath = path,
                DisplayName = System.IO.Path.GetFileName(path)
            });
            item.UpdatedAt = DateTimeOffset.UtcNow;
            Commit(items);
        }

        public void RemoveAttachment(string id, string attachmentId)
        {
            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null)
            {
                return;
            }

            item.Attachments.RemoveAll(attachment => attachment.Id == attachmentId);
            item.UpdatedAt = DateTimeOffset.UtcNow;
            Commit(items);
        }

        public void SetSteps(string id, List<string> texts)
        {
            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null)
            {
                return;
            }

            var existing = item.Steps.ToDictionary(step => step.Text, StringComparer.Ordinal);
            var rebuilt = new List<TodoStep>();
            int order = 0;
            foreach (string text in texts.Where(text => !string.IsNullOrWhiteSpace(text)))
            {
                TodoStep match;
                existing.TryGetValue(text.Trim(), out match);
                rebuilt.Add(new TodoStep
                {
                    Text = text.Trim(),
                    IsCompleted = match != null && match.IsCompleted,
                    Id = match != null ? match.Id : Guid.NewGuid().ToString("N"),
                    SortOrder = order++
                });
            }

            item.Steps = rebuilt;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            Commit(items);
        }

        // ---------- steps ----------

        public void AddStep(string id, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null)
            {
                return;
            }

            item.Steps.Add(new TodoStep { Text = text.Trim(), SortOrder = item.Steps.Count });
            item.UpdatedAt = DateTimeOffset.UtcNow;
            Commit(items);
        }

        public void ToggleStep(string id, string stepId)
        {
            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            TodoStep step = item == null ? null : item.Steps.FirstOrDefault(entry => entry.Id == stepId);
            if (item == null || step == null)
            {
                return;
            }

            step.IsCompleted = !step.IsCompleted;
            item.UpdatedAt = DateTimeOffset.UtcNow;

            // DeskBox: when every step is ticked the task itself completes.
            if (item.AllStepsDone && !item.IsCompleted)
            {
                item.IsCompleted = true;
                item.CompletedAt = item.UpdatedAt;
            }

            Commit(items);
        }

        public void DeleteStep(string id, string stepId)
        {
            Snapshot();
            List<TodoItem> items = Load();
            TodoItem item = items.FirstOrDefault(entry => entry.Id == id);
            if (item == null)
            {
                return;
            }

            item.Steps.RemoveAll(step => step.Id == stepId);
            for (int index = 0; index < item.Steps.Count; index++)
            {
                item.Steps[index].SortOrder = index;
            }

            item.UpdatedAt = DateTimeOffset.UtcNow;
            Commit(items);
        }

        // ---------- presets (ported from SetDueDatePresetAsync) ----------

        public static DateTimeOffset? DuePreset(string preset)
        {
            DateTime today = DateTime.Today;
            int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7;
            int daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
            if (daysUntilMonday == 0)
            {
                daysUntilMonday = 7;
            }

            switch (preset)
            {
                case "today":
                    return AtEndOfDay(today);
                case "tomorrow":
                    return AtEndOfDay(today.AddDays(1));
                case "week":
                    return AtEndOfDay(today.AddDays(daysUntilSunday));
                case "monday":
                    return AtEndOfDay(today.AddDays(daysUntilMonday));
                default:
                    return null;
            }
        }

        private static DateTimeOffset AtEndOfDay(DateTime day)
        {
            DateTime local = new DateTime(day.Year, day.Month, day.Day, 23, 59, 0, DateTimeKind.Local);
            return new DateTimeOffset(local);
        }

        // ---------- reminders (ported from TodoReminderService) ----------

        /// <summary>Returns the items that should raise a notification right now.</summary>
        public List<TodoItem> CollectDueReminders(DateTimeOffset now, IEnumerable<string> alreadyNotified)
        {
            var result = new List<TodoItem>();
            var notified = new HashSet<string>(alreadyNotified, StringComparer.Ordinal);
            int defaultOffset = 5;

            foreach (TodoItem item in Load())
            {
                if (item.IsCompleted || notified.Contains(item.Id))
                {
                    continue;
                }

                DateTimeOffset? triggerAt = null;
                if (item.SnoozedUntil.HasValue && item.SnoozedUntil.Value <= now)
                {
                    triggerAt = item.SnoozedUntil.Value;
                }
                else if (item.DueDate.HasValue && item.SnoozedUntil == null)
                {
                    int offset = TodoReminderOptions.IsOff(item.ReminderOffsetMinutes)
                        ? -1
                        : (item.ReminderOffsetMinutes ?? defaultOffset);

                    if (offset < 0)
                    {
                        continue;
                    }

                    if (item.ReminderDismissedForDueDate.HasValue &&
                        item.ReminderDismissedForDueDate.Value == item.DueDate.Value)
                    {
                        continue;
                    }

                    triggerAt = item.DueDate.Value.AddMinutes(-offset);
                }

                if (triggerAt.HasValue && triggerAt.Value <= now && now - triggerAt.Value < TimeSpan.FromHours(2))
                {
                    result.Add(item);
                }
            }

            return result;
        }

        public List<TodoItem> CollectForReminderRefresh()
        {
            return Load();
        }
    }
}