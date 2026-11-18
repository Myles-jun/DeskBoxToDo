using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DeskBoxToDo
{
    internal sealed class TodoStep
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Text = "";
        public bool IsCompleted;
        public int SortOrder;
    }

    internal static class TodoRecurrenceMode
    {
        public const string None = "none";
        public const string Daily = "daily";
        public const string Weekly = "weekly";
        public const string Monthly = "monthly";
        public const string Weekdays = "weekdays";

        public static readonly string[] Supported = { None, Daily, Weekly, Monthly, Weekdays };

        public static string Normalize(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return None;
            }

            string normalized = mode.Trim().ToLowerInvariant();
            return Supported.Contains(normalized) ? normalized : None;
        }

        public static string Display(string mode)
        {
            switch (Normalize(mode))
            {
                case Daily: return "每天";
                case Weekly: return "每周";
                case Monthly: return "每月";
                case Weekdays: return "工作日";
                default: return "不重复";
            }
        }
    }

    internal sealed class TodoRecurrence
    {
        public string Mode = TodoRecurrenceMode.None;
        public DateTimeOffset? AnchorDueDate;

        public TodoRecurrence Clone()
        {
            return new TodoRecurrence
            {
                Mode = TodoRecurrenceMode.Normalize(Mode),
                AnchorDueDate = AnchorDueDate
            };
        }
    }

    internal static class TodoReminderOptions
    {
        public const int ReminderOff = -1;

        public static readonly int[] Offsets = { 0, 5, 10, 15, 30, 60, 1440 };

        public static int? Normalize(int? minutes)
        {
            if (!minutes.HasValue)
            {
                return null;
            }

            return minutes.Value == ReminderOff || Offsets.Contains(minutes.Value) ? minutes : null;
        }

        public static bool IsOff(int? minutes)
        {
            return minutes.HasValue && minutes.Value == ReminderOff;
        }

        public static string Display(int? minutes)
        {
            if (!minutes.HasValue)
            {
                return "默认";
            }

            if (minutes.Value == ReminderOff)
            {
                return "关闭";
            }

            if (minutes.Value == 0)
            {
                return "准时";
            }

            if (minutes.Value >= 1440)
            {
                return "提前 1 天";
            }

            if (minutes.Value >= 60)
            {
                return "提前 " + (minutes.Value / 60) + " 小时";
            }

            return "提前 " + minutes.Value + " 分钟";
        }
    }

    internal sealed class TodoAttachment
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string FilePath = "";
        public string DisplayName = "";
        public string Type = "file";
        public DateTimeOffset AddedAt = DateTimeOffset.UtcNow;
    }

    internal sealed class TodoItem
    {
        public string Id = "";
        public string Text = "";
        public bool IsCompleted;
        public bool IsImportant;
        public string ColorMarker;
        public DateTimeOffset? DueDate;
        public TodoRecurrence Recurrence;
        public List<TodoStep> Steps = new List<TodoStep>();
        public string Notes;
        public List<TodoAttachment> Attachments = new List<TodoAttachment>();
        public DateTimeOffset? CompletedAt;
        public DateTimeOffset? ReminderLastNotifiedAt;
        public DateTimeOffset? ReminderDismissedForDueDate;
        public int? ReminderOffsetMinutes;
        public DateTimeOffset? SnoozedUntil;
        public DateTimeOffset? SnoozeLastNotifiedAt;
        public string RecurrenceSeriesId;
        public string GeneratedNextItemId;
        public int SortOrder;
        public DateTimeOffset CreatedAt = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt = DateTimeOffset.UtcNow;
        public bool IsDeleted;

        public static readonly string[] Markers =
        {
            "", "red", "orange", "yellow", "green", "blue", "purple", "teal", "pink"
        };

        public static string NormalizeMarker(string marker)
        {
            if (string.IsNullOrWhiteSpace(marker))
            {
                return "";
            }

            string value = marker.Trim().ToLowerInvariant();
            return Markers.Contains(value) ? value : "";
        }

        public static System.Drawing.Color MarkerColor(string marker)
        {
            switch (NormalizeMarker(marker))
            {
                case "red": return System.Drawing.Color.FromArgb(227, 77, 77);
                case "orange": return System.Drawing.Color.FromArgb(240, 138, 60);
                case "yellow": return System.Drawing.Color.FromArgb(242, 201, 76);
                case "green": return System.Drawing.Color.FromArgb(76, 175, 109);
                case "blue": return System.Drawing.Color.FromArgb(77, 143, 227);
                case "purple": return System.Drawing.Color.FromArgb(155, 107, 232);
                case "teal": return System.Drawing.Color.FromArgb(45, 183, 163);
                case "pink": return System.Drawing.Color.FromArgb(230, 106, 162);
                default: return System.Drawing.Color.Transparent;
            }
        }

        public bool IsRecurring
        {
            get
            {
                return DueDate.HasValue &&
                       Recurrence != null &&
                       TodoRecurrenceMode.Normalize(Recurrence.Mode) != TodoRecurrenceMode.None;
            }
        }

        public string DueText
        {
            get
            {
                if (!DueDate.HasValue)
                {
                    return "";
                }

                string[] names = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
                DateTimeOffset due = DueDate.Value.ToLocalTime();
                DateTimeOffset now = DateTimeOffset.Now;
                bool sameDay = due.Date == now.Date;
                bool tomorrow = due.Date == now.Date.AddDays(1);

                if (sameDay)
                {
                    return "今天 " + due.ToString("HH:mm", CultureInfo.InvariantCulture);
                }

                if (tomorrow)
                {
                    return "明天 " + due.ToString("HH:mm", CultureInfo.InvariantCulture);
                }

                if ((due.Date - now.Date).TotalDays < 7 && (due.Date - now.Date).TotalDays >= 0)
                {
                    return names[(int)due.DayOfWeek] + " " + due.ToString("HH:mm", CultureInfo.InvariantCulture);
                }

                return due.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
        }

        public bool IsOverdue
        {
            get { return !IsCompleted && DueDate.HasValue && DueDate.Value < DateTimeOffset.Now; }
        }

        public bool AllStepsDone
        {
            get { return Steps.Count > 0 && Steps.All(step => step.IsCompleted); }
        }

        public TodoItem Clone()
        {
            return new TodoItem
            {
                Id = Id,
                Text = Text,
                IsCompleted = IsCompleted,
                IsImportant = IsImportant,
                ColorMarker = ColorMarker,
                DueDate = DueDate,
                Recurrence = Recurrence == null ? null : Recurrence.Clone(),
                Attachments = Attachments.Select(attachment => new TodoAttachment
                {
                    Id = attachment.Id,
                    FilePath = attachment.FilePath,
                    DisplayName = attachment.DisplayName,
                    Type = attachment.Type,
                    AddedAt = attachment.AddedAt
                }).ToList(),
                Steps = Steps.Select(step => new TodoStep
                {
                    Id = step.Id,
                    Text = step.Text,
                    IsCompleted = step.IsCompleted,
                    SortOrder = step.SortOrder
                }).ToList(),
                Notes = Notes,
                CompletedAt = CompletedAt,
                ReminderLastNotifiedAt = ReminderLastNotifiedAt,
                ReminderDismissedForDueDate = ReminderDismissedForDueDate,
                ReminderOffsetMinutes = ReminderOffsetMinutes,
                SnoozedUntil = SnoozedUntil,
                SnoozeLastNotifiedAt = SnoozeLastNotifiedAt,
                RecurrenceSeriesId = RecurrenceSeriesId,
                GeneratedNextItemId = GeneratedNextItemId,
                SortOrder = SortOrder,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                IsDeleted = IsDeleted
            };
        }
    }

    /// <summary>Ported from DeskBox's TodoRecurrenceService (same rules).</summary>
    internal static class TodoRecurrenceService
    {
        public static TodoRecurrence Create(string mode, DateTimeOffset? dueDate)
        {
            if (!dueDate.HasValue)
            {
                return null;
            }

            string normalized = TodoRecurrenceMode.Normalize(mode);
            if (normalized == TodoRecurrenceMode.None)
            {
                return null;
            }

            return new TodoRecurrence { Mode = normalized, AnchorDueDate = dueDate };
        }

        public static bool TryCreateNextOccurrence(TodoItem source, DateTimeOffset completedAt, out TodoItem next)
        {
            next = null;
            DateTimeOffset nextDue;
            if (!TryGetNextDueDate(source, completedAt, out nextDue))
            {
                return false;
            }

            string seriesId = string.IsNullOrWhiteSpace(source.RecurrenceSeriesId)
                ? Guid.NewGuid().ToString("N")
                : source.RecurrenceSeriesId.Trim();

            next = new TodoItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = source.Text,
                IsCompleted = false,
                IsImportant = source.IsImportant,
                ColorMarker = source.ColorMarker,
                DueDate = nextDue,
                Recurrence = source.Recurrence == null ? null : source.Recurrence.Clone(),
                Steps = source.Steps
                    .OrderBy(step => step.SortOrder)
                    .Select(step => new TodoStep
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Text = step.Text,
                        IsCompleted = false,
                        SortOrder = step.SortOrder
                    })
                    .ToList(),
                Notes = source.Notes,
                ReminderOffsetMinutes = TodoReminderOptions.Normalize(source.ReminderOffsetMinutes),
                RecurrenceSeriesId = seriesId,
                SortOrder = source.SortOrder + 1,
                CreatedAt = completedAt,
                UpdatedAt = completedAt
            };

            return true;
        }

        public static bool TryGetNextDueDate(TodoItem item, DateTimeOffset completedAt, out DateTimeOffset nextDue)
        {
            nextDue = default;
            if (!item.IsRecurring || !item.DueDate.HasValue)
            {
                return false;
            }

            string mode = TodoRecurrenceMode.Normalize(item.Recurrence.Mode);
            DateTimeOffset anchor = (item.Recurrence.AnchorDueDate ?? item.DueDate.Value).ToLocalTime();
            DateTimeOffset due = item.DueDate.Value.ToLocalTime();
            DateTimeOffset done = completedAt.ToLocalTime();

            switch (mode)
            {
                case TodoRecurrenceMode.Daily:
                    nextDue = AdvanceByDays(due, done, 1);
                    return true;
                case TodoRecurrenceMode.Weekly:
                    nextDue = AdvanceByDays(due, done, 7);
                    return true;
                case TodoRecurrenceMode.Monthly:
                    nextDue = AdvanceMonthly(due, anchor, done);
                    return true;
                case TodoRecurrenceMode.Weekdays:
                    nextDue = AdvanceWeekdays(due, done);
                    return true;
                default:
                    return false;
            }
        }

        public static bool ShouldRemoveGenerated(TodoItem source, TodoItem generated)
        {
            if (string.IsNullOrWhiteSpace(source.GeneratedNextItemId) ||
                !string.Equals(source.GeneratedNextItemId, generated.Id, StringComparison.Ordinal))
            {
                return false;
            }

            if (generated.IsCompleted || generated.CreatedAt != generated.UpdatedAt)
            {
                return false;
            }

            return string.Equals(source.Text, generated.Text, StringComparison.Ordinal) &&
                   source.IsImportant == generated.IsImportant &&
                   string.Equals(source.ColorMarker ?? "", generated.ColorMarker ?? "", StringComparison.Ordinal) &&
                   string.Equals(source.RecurrenceSeriesId ?? "", generated.RecurrenceSeriesId ?? "", StringComparison.Ordinal);
        }

        private static DateTimeOffset AdvanceByDays(DateTimeOffset due, DateTimeOffset done, int days)
        {
            DateTimeOffset candidate = AddLocalDays(due, days);
            while (candidate <= done)
            {
                candidate = AddLocalDays(candidate, days);
            }

            return candidate;
        }

        private static DateTimeOffset AdvanceWeekdays(DateTimeOffset due, DateTimeOffset done)
        {
            DateTimeOffset candidate = due;
            do
            {
                candidate = AddLocalDays(candidate, 1);
            }
            while (candidate <= done || candidate.DayOfWeek == DayOfWeek.Saturday || candidate.DayOfWeek == DayOfWeek.Sunday);

            return candidate;
        }

        private static DateTimeOffset AdvanceMonthly(DateTimeOffset due, DateTimeOffset anchor, DateTimeOffset done)
        {
            int monthOffset = ((due.Year - anchor.Year) * 12) + (due.Month - anchor.Month) + 1;
            DateTimeOffset candidate = CreateMonthlyCandidate(anchor, monthOffset);
            while (candidate <= done)
            {
                monthOffset++;
                candidate = CreateMonthlyCandidate(anchor, monthOffset);
            }

            return candidate;
        }

        private static DateTimeOffset CreateMonthlyCandidate(DateTimeOffset anchor, int monthOffset)
        {
            DateTime anchorLocal = anchor.LocalDateTime;
            DateTime month = new DateTime(anchorLocal.Year, anchorLocal.Month, 1, anchorLocal.Hour, anchorLocal.Minute, 0, DateTimeKind.Local);
            month = month.AddMonths(monthOffset);
            int day = Math.Min(anchorLocal.Day, DateTime.DaysInMonth(month.Year, month.Month));
            return new DateTimeOffset(new DateTime(month.Year, month.Month, day, anchorLocal.Hour, anchorLocal.Minute, 0, DateTimeKind.Local));
        }

        private static DateTimeOffset AddLocalDays(DateTimeOffset value, int days)
        {
            return new DateTimeOffset(value.LocalDateTime.AddDays(days));
        }
    }

    internal enum TodoFilter
    {
        All,
        Active,
        Today,
        ThisWeek,
        ThisMonth,
        Important,
        Completed
    }

    /// <summary>Ported from DeskBox's ShouldShowItem / IsDue* / CompareVisibleItems.</summary>
    internal static class TodoQuery
    {
        public static bool Matches(TodoItem item, TodoFilter filter, bool showCompleted)
        {
            if (!showCompleted && item.IsCompleted && filter != TodoFilter.Completed)
            {
                return false;
            }

            switch (filter)
            {
                case TodoFilter.Active:
                    return !item.IsCompleted;
                case TodoFilter.Today:
                    return IsDueToday(item);
                case TodoFilter.ThisWeek:
                    return IsDueThisWeek(item);
                case TodoFilter.ThisMonth:
                    return IsDueThisMonth(item);
                case TodoFilter.Important:
                    return item.IsImportant;
                case TodoFilter.Completed:
                    return item.IsCompleted;
                default:
                    return true;
            }
        }

        public static List<TodoItem> Sort(IEnumerable<TodoItem> items)
        {
            List<TodoItem> sorted = items.ToList();
            sorted.Sort((left, right) =>
            {
                if (left.IsCompleted != right.IsCompleted)
                {
                    return left.IsCompleted ? 1 : -1;
                }

                int byOrder = left.SortOrder.CompareTo(right.SortOrder);
                return byOrder != 0 ? byOrder : right.UpdatedAt.CompareTo(left.UpdatedAt);
            });
            return sorted;
        }

        public static bool IsDueToday(TodoItem item)
        {
            return item.DueDate.HasValue && item.DueDate.Value.ToLocalTime().Date == DateTimeOffset.Now.Date;
        }

        public static bool IsDueThisWeek(TodoItem item)
        {
            if (!item.DueDate.HasValue)
            {
                return false;
            }

            DateTime today = DateTime.Today;
            int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
            DateTime weekStart = today.AddDays(-daysSinceMonday);
            DateTime nextWeekStart = weekStart.AddDays(7);
            DateTime due = item.DueDate.Value.ToLocalTime().Date;
            return due >= weekStart && due < nextWeekStart;
        }

        public static bool IsDueThisMonth(TodoItem item)
        {
            if (!item.DueDate.HasValue)
            {
                return false;
            }

            DateTime due = item.DueDate.Value.ToLocalTime().Date;
            DateTime today = DateTime.Today;
            return due.Year == today.Year && due.Month == today.Month;
        }
    }
}
