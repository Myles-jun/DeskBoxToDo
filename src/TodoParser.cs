using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskBoxToDo
{
    internal sealed class TodoDraft
    {
        public string Title = "";
        public List<string> Steps = new List<string>();
        public DateTimeOffset? Due;
        public bool Important;
        public bool DateFound;
        public bool TimeFound;
        public string Notes = "";
        public string ColorMarker = "";
        public string RecurrenceMode = "";
        public string Source = "";
    }

    /// <summary>
    /// Turns one Chinese sentence / chat notice into a DeskBox todo draft.
    /// Rule based and offline; the optional LLM pass refines a draft but never
    /// replaces this parser's safety net.
    /// </summary>
    internal static class TodoParser
    {
        private static readonly Regex RxRecurrence = new Regex(
            @"(每\s*天|每\s*日|天天|每天|每\s*晚|每\s*早|每\s*晨|每\s*个?\s*星期[一二三四五六日天]?|每\s*个?\s*周[一二三四五六日天]?|每\s*个月|每\s*月|工作日)",
            RegexOptions.Compiled);

        private static readonly Regex RxWeekday = new Regex(
            @"(下下下|下下|下|本|这)?\s*(?:周|星期|礼拜)\s*([一二三四五六日天])",
            RegexOptions.Compiled);

        private static readonly Regex RxYmd = new Regex(
            @"(\d{4})\s*[年\-/.]\s*(\d{1,2})\s*[月\-/.]\s*(\d{1,2})\s*[日号]?",
            RegexOptions.Compiled);

        private static readonly Regex RxMonthDay = new Regex(
            @"(\d{1,2})\s*月\s*(\d{1,2})\s*[日号]",
            RegexOptions.Compiled);

        private static readonly Regex RxDayOfMonth = new Regex(
            @"(?:^|[^\d])(\d{1,2})\s*[日号](?![0-9])",
            RegexOptions.Compiled);

        private static readonly Regex RxRelative = new Regex(
            @"(今天|今日|今晚|今早|明天|明日|明早|明晚|后天|大后天)",
            RegexOptions.Compiled);

        private static readonly Regex RxTime = new Regex(
            @"(上午|早上|早晨|清晨|中午|下午|傍晚|晚上|晚)?\s*[，,、]?\s*(\d{1,2})\s*[:：点时]\s*(\d{1,2})?\s*分?",
            RegexOptions.Compiled);

        private static readonly Regex RxTimeCn = new Regex(
            @"(上午|早上|早晨|清晨|中午|下午|傍晚|晚上|晚)?\s*[，,、]?\s*([一二三四五六七八九十两]{1,3})\s*[点时](半)?",
            RegexOptions.Compiled);

        private static readonly Regex RxHalf = new Regex(
            @"(上午|早上|早晨|清晨|中午|下午|傍晚|晚上|晚)?\s*[，,、]?\s*(\d{1,2})\s*点半",
            RegexOptions.Compiled);

        private static readonly Regex RxMention = new Regex(
            @"@\s*(所有人|全体成员|全体|all)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex RxNumberingSplit = new Regex(
            @"(?=(?<![0-9０-９])\d{1,2}\s*[（(])",
            RegexOptions.Compiled);

        private static readonly Regex RxLeadingTimeNoise = new Regex(
            @"^\s*(?:(?:本|这|下下|下)?\s*(?:周|星期|礼拜)\s*[一二三四五六日天]|今天|今日|今晚|今早|明天|明日|明早|明晚|后天|大后天|\d{1,2}\s*月\s*\d{1,2}\s*[日号]|\d{1,2}\s*[日号])?\s*(?:上课前|上课|放学前|放学|之前|以前)?\s*(?:记得|记着|务必|请|要|别忘|不要忘|都需要|需要把|把)?\s*",
            RegexOptions.Compiled);

        private static readonly Regex RxImportant = new Regex(
            @"作业|考试|测验|模拟考|期中|期末|截止|提交|交|deadline|due|报名|缴费|汇报|报告|答辩",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static TodoDraft Parse(string rawText, AppConfig config, DateTimeOffset now)
        {
            var draft = new TodoDraft();
            string raw = (rawText ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            draft.Notes = raw;
            draft.Important = RxImportant.IsMatch(raw);

            string text = Normalize(raw);

            DateTime? date = ExtractDate(text, now);
            int hour;
            int minute;
            bool hasTime = ExtractTime(text, out hour, out minute);
            draft.TimeFound = hasTime;
            if (date.HasValue)
            {
                draft.Due = Combine(date.Value, hasTime, hour, minute, config, now.Offset);
                draft.DateFound = true;
            }

            draft.RecurrenceMode = ExtractRecurrence(text);
            if (!string.IsNullOrEmpty(draft.RecurrenceMode))
            {
                text = RxRecurrence.Replace(text, " ");
                text = Regex.Replace(text, @"重复(任务)?", " ");
                if (draft.RecurrenceMode == TodoRecurrenceMode.Monthly)
                {
                    // "每月的 1 号" describes the schedule, not the task text.
                    text = Regex.Replace(text, @"(的)?\s*\d{1,2}\s*[号日]", " ");
                }

                text = Regex.Replace(text, @"^\s*的\s*", " ");

                if (!draft.Due.HasValue)
                {
                    // Recurring tasks need an anchor: use today at the parsed
                    // time when the sentence has one, otherwise today 23:59.
                    DateTime anchor = hasTime
                        ? now.LocalDateTime.Date.AddHours(hour).AddMinutes(minute)
                        : now.LocalDateTime.Date.AddHours(23).AddMinutes(59);

                    if (anchor < now.LocalDateTime)
                    {
                        anchor = anchor.AddDays(1);
                    }

                    draft.Due = new DateTimeOffset(DateTime.SpecifyKind(anchor, DateTimeKind.Local));
                }
            }

            ExtractTitleAndSteps(text, draft);
            if (string.IsNullOrWhiteSpace(draft.Title))
            {
                draft.Title = FirstLine(text, 30);
            }

            return draft;
        }

        private static string ExtractRecurrence(string text)
        {
            Match match = RxRecurrence.Match(text ?? "");
            if (!match.Success)
            {
                return "";
            }

            string value = match.Value.Replace(" ", "");
            if (value.Contains("工作日"))
            {
                return TodoRecurrenceMode.Weekdays;
            }

            if (value.Contains("月"))
            {
                return TodoRecurrenceMode.Monthly;
            }

            if (value.Contains("周") || value.Contains("星期"))
            {
                return TodoRecurrenceMode.Weekly;
            }

            return TodoRecurrenceMode.Daily;
        }

        private static string Normalize(string raw)
        {
            string text = RxMention.Replace(raw, " ");
            text = Regex.Replace(text, @"^\s*[【\[][^】\]]{1,10}[】\]]\s*", "");
            // "16周周二" (term week + weekday): the week number is not a date we
            // can resolve, drop it so the weekday rule still works.
            text = Regex.Replace(text, @"第?\s*\d{1,2}\s*周(?=\s*(?:周|星期|礼拜))", " ");
            // "下下下周周五" / "16周周二": collapse the duplicated 周 so the
            // weekday rule sees one marker and the week offset survives.
            text = Regex.Replace(text, @"周\s*周", "周");
            text = Regex.Replace(text, @"[ \t]+", " ");
            text = Regex.Replace(text, @"\n{2,}", "\n");
            return text.Trim();
        }

        private static DateTime? ExtractDate(string text, DateTimeOffset now)
        {
            Match match = RxYmd.Match(text);
            if (match.Success)
            {
                int year;
                int month;
                int day;
                if (int.TryParse(match.Groups[1].Value, out year) &&
                    int.TryParse(match.Groups[2].Value, out month) &&
                    int.TryParse(match.Groups[3].Value, out day) &&
                    IsValidDate(year, month, day))
                {
                    return new DateTime(year, month, day);
                }
            }

            match = RxMonthDay.Match(text);
            if (match.Success)
            {
                int month;
                int day;
                if (int.TryParse(match.Groups[1].Value, out month) &&
                    int.TryParse(match.Groups[2].Value, out day) &&
                    month >= 1 && month <= 12 && day >= 1 && day <= 31)
                {
                    int year = now.Year;
                    if (IsValidDate(year, month, day))
                    {
                        var candidate = new DateTime(year, month, day);
                        if ((now.Date - candidate.Date).TotalDays > 30)
                        {
                            candidate = candidate.AddYears(1);
                        }

                        return candidate;
                    }
                }
            }

            match = RxWeekday.Match(text);
            if (match.Success)
            {
                int target = MapWeekday(match.Groups[2].Value[0]);
                if (target > 0)
                {
                    int today = ((int)now.DayOfWeek + 6) % 7 + 1; // Monday = 1 ... Sunday = 7
                    int delta = ((target - today) % 7 + 7) % 7;
                    string prefix = match.Groups[1].Value;
                    if (prefix.Length > 1)
                    {
                        // 下下 / 下下下: each extra 下 is one more week.
                        delta += prefix.Length * 7;
                    }
                    else if (prefix == "下")
                    {
                        delta += 7;
                    }

                    if (delta == 0 && now.Hour >= 12)
                    {
                        delta = 7;
                    }

                    return now.Date.AddDays(delta);
                }
            }

            match = RxRelative.Match(text);
            if (match.Success)
            {
                string token = match.Groups[1].Value;
                int offset;
                if (token.StartsWith("今", StringComparison.Ordinal))
                {
                    offset = 0;
                }
                else if (token.StartsWith("明", StringComparison.Ordinal))
                {
                    offset = 1;
                }
                else if (token == "后天")
                {
                    offset = 2;
                }
                else
                {
                    offset = 3;
                }

                return now.Date.AddDays(offset);
            }

            match = RxDayOfMonth.Match(text);
            if (match.Success)
            {
                int day;
                if (int.TryParse(match.Groups[1].Value, out day) && day >= 1 && day <= 31)
                {
                    var candidate = new DateTime(now.Year, now.Month, 1).AddDays(day - 1);
                    if (candidate.Month != now.Month || candidate.Date < now.Date)
                    {
                        candidate = candidate.AddMonths(1);
                    }

                    if (IsValidDate(candidate.Year, candidate.Month, candidate.Day))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static DateTimeOffset Combine(
            DateTime date,
            bool hasTime,
            int hour,
            int minute,
            AppConfig config,
            TimeSpan offset)
        {
            int useHour = hasTime ? hour : Math.Max(0, Math.Min(23, config.DefaultDueHour));
            int useMinute = hasTime ? minute : Math.Max(0, Math.Min(59, config.DefaultDueMinute));
            var local = new DateTime(date.Year, date.Month, date.Day, useHour, useMinute, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, offset);
        }

        private static bool ExtractTime(string text, out int hour, out int minute)
        {
            hour = 0;
            minute = 0;

            Match cn = RxTimeCn.Match(text);
            if (cn.Success)
            {
                int? cnHour = ParseChineseNumber(cn.Groups[2].Value);
                if (cnHour.HasValue && cnHour.Value <= 23)
                {
                    hour = cnHour.Value;
                    minute = cn.Groups[3].Success ? 30 : 0;
                    ApplyPeriod(cn.Groups[1].Value, ref hour);
                    return true;
                }
            }

            Match match = RxHalf.Match(text);
            if (match.Success)
            {
                int parsed;
                if (int.TryParse(match.Groups[2].Value, out parsed) && parsed <= 23)
                {
                    hour = parsed;
                    minute = 30;
                    ApplyPeriod(match.Groups[1].Value, ref hour);
                    return true;
                }
            }

            foreach (Match candidate in RxTime.Matches(text))
            {
                int parsedHour;
                if (!int.TryParse(candidate.Groups[2].Value, out parsedHour) || parsedHour > 23)
                {
                    continue;
                }

                int parsedMinute = 0;
                if (candidate.Groups[3].Success)
                {
                    int.TryParse(candidate.Groups[3].Value, out parsedMinute);
                }

                if (parsedMinute > 59)
                {
                    parsedMinute = 0;
                }

                hour = parsedHour;
                minute = parsedMinute;
                ApplyPeriod(candidate.Groups[1].Value, ref hour);
                return true;
            }

            return false;
        }

        private static int? ParseChineseNumber(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var digits = new Dictionary<char, int>
            {
                { '一', 1 }, { '二', 2 }, { '两', 2 }, { '三', 3 }, { '四', 4 },
                { '五', 5 }, { '六', 6 }, { '七', 7 }, { '八', 8 }, { '九', 9 }
            };

            if (text == "十")
            {
                return 10;
            }

            if (text.Length == 1)
            {
                int single;
                return digits.TryGetValue(text[0], out single) ? single : (int?)null;
            }

            if (text.Length == 2)
            {
                int value;
                if (text[0] == '十')
                {
                    return digits.TryGetValue(text[1], out value) ? 10 + value : (int?)null;
                }

                if (text[1] == '十')
                {
                    return digits.TryGetValue(text[0], out value) ? value * 10 : (int?)null;
                }

                return null;
            }

            if (text.Length == 3 && text[1] == '十')
            {
                int tens;
                int ones;
                if (digits.TryGetValue(text[0], out tens) && digits.TryGetValue(text[2], out ones))
                {
                    return tens * 10 + ones;
                }
            }

            return null;
        }

        private static void ApplyPeriod(string period, ref int hour)
        {
            if (string.IsNullOrEmpty(period))
            {
                return;
            }

            bool afternoon = period.IndexOf("下午", StringComparison.Ordinal) >= 0 ||
                             period.IndexOf("傍晚", StringComparison.Ordinal) >= 0 ||
                             period.IndexOf("晚上", StringComparison.Ordinal) >= 0 ||
                             period == "晚";

            if (afternoon && hour < 12)
            {
                hour += 12;
            }
            else if (period.IndexOf("中午", StringComparison.Ordinal) >= 0 && hour < 12)
            {
                hour += 12;
            }
            else if (period.IndexOf("上午", StringComparison.Ordinal) >= 0 && hour == 12)
            {
                hour = 0;
            }
        }

        private static void ExtractTitleAndSteps(string text, TodoDraft draft)
        {
            int colon = FindSeparatorColon(text);
            string head;
            string body;
            if (colon > 0 && colon <= 40)
            {
                head = text.Substring(0, colon).Trim();
                body = text.Substring(colon + 1).Trim();
            }
            else
            {
                int end = FirstSentenceEnd(text);
                head = end > 0 ? text.Substring(0, end).Trim() : text.Trim();
                body = end > 0 ? text.Substring(end).TrimStart('。', '！', '？', '；', ';', '，', ',', ' ').Trim() : "";
                if (end <= 0 && text.Length > 60)
                {
                    // Long single sentence: keep the head as the title and the
                    // tail as context steps.
                    int cut = FindCut(text, 28);
                    head = text.Substring(0, cut).Trim();
                    body = text.Substring(cut).Trim();
                }
            }

            // Title = first clause that survives date/time/location cleaning; the
            // remaining clauses become context steps.
            var headClauses = new List<string>();
            foreach (string clause in SplitClauses(head))
            {
                headClauses.Add(clause);
            }

            var bodyClauses = new List<string>();
            foreach (string clause in SplitClauses(body))
            {
                bodyClauses.Add(clause);
            }

            string title = "";
            var remaining = new List<string>();
            var clauses = new List<string>(headClauses);
            clauses.AddRange(bodyClauses);
            foreach (string clause in clauses)
            {
                string cleaned = CleanTitle(clause);
                if (cleaned.Length == 0)
                {
                    continue;
                }

                if (title.Length == 0 && !IsLocationOnly(cleaned))
                {
                    title = cleaned;
                    continue;
                }

                remaining.Add(cleaned);
            }

            // "群公告：……" — the tag is not the task, promote the first real clause.
            if (title.Length > 0 &&
                RxGenericTag.IsMatch(title.Trim(' ', '：', ':')) &&
                remaining.Count > 0)
            {
                title = remaining[0];
                remaining.RemoveAt(0);
            }

            if (title.Length == 0)
            {
                title = FirstLine(text, 30);
            }

            if (title.Length > 44)
            {
                title = title.Substring(0, 44).Trim() + "…";
            }

            draft.Title = title;

            foreach (string clause in remaining)
            {
                AddStep(draft, clause, title);
            }
        }

        /// <summary>"教三412" / "水利楼" on its own is a place, not the task.</summary>
        private static bool IsLocationOnly(string clause)
        {
            if (clause.Length > 10)
            {
                return false;
            }

            return Regex.IsMatch(clause, @"(楼|室|馆|校区|教室|号楼|广场|中心)\s*\d*\s*$");
        }

        private static void AddStep(TodoDraft draft, string clause, string title)
        {
            string step = CleanStep(clause);
            if (step.Length == 0 || step.Length > 90)
            {
                return;
            }

            if (string.Equals(step, title, StringComparison.Ordinal))
            {
                return;
            }

            if (!draft.Steps.Contains(step) && draft.Steps.Count < 12)
            {
                draft.Steps.Add(step);
            }
        }

        /// <summary>Finds the "标题：正文" separator, ignoring clock times like 14:00.</summary>
        private static int FindSeparatorColon(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch != '：' && ch != ':')
                {
                    continue;
                }

                bool previousIsDigit = i > 0 && char.IsDigit(text[i - 1]);
                bool nextIsDigit = i + 1 < text.Length && char.IsDigit(text[i + 1]);
                if (ch == ':' && previousIsDigit && nextIsDigit)
                {
                    continue;
                }

                // "https://…" — the scheme colon is not a “标题：正文” separator.
                if (ch == ':' && i + 2 < text.Length && text[i + 1] == '/' && text[i + 2] == '/')
                {
                    continue;
                }

                return i;
            }

            return -1;
        }

        private static int FirstSentenceEnd(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '。' || text[i] == '！' || text[i] == '？' || text[i] == '!' || text[i] == '?')
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindCut(string text, int target)
        {
            int limit = Math.Min(text.Length, target + 12);
            for (int i = target; i > 4 && i < limit; i++)
            {
                char ch = text[i];
                if (ch == '，' || ch == ',' || ch == '；' || ch == ';' || ch == ' ')
                {
                    return i;
                }
            }

            return Math.Min(text.Length, target);
        }

        private static IEnumerable<string> SplitClauses(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                yield break;
            }

            string[] sentences = body.Split(new[] { '。', '！', '？', '；', ';', '\n', '!' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string sentence in sentences)
            {
                string[] pieces = sentence.Split(new[] { '，', ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string piece in pieces)
                {
                    string trimmed = piece.Trim();
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    // "1（没讲过的内容不做）2（2）①② 2（3）" style enumerations.
                    string[] numbered = RxNumberingSplit.Split(trimmed);
                    foreach (string entry in numbered)
                    {
                        string candidate = entry.Trim();
                        if (candidate.Length > 0)
                        {
                            yield return candidate;
                        }
                    }
                }
            }
        }

        private static readonly string[] DeadlineMarkers =
        {
            "前交", "前提交", "前上传", "前完成", "交到", "交至", "提交到", "截止",
            "记得", "切记", "务必", "请", "改到", "改在", "移到", "定在", "约在"
        };

        /// <summary>"群公告：" / "通知：" style prefixes carry no task content.</summary>
        private static readonly Regex RxGenericTag = new Regex(
            @"^(群公告|公告|通知|重要通知|提醒|课程通知|班群通知|教务通知|群消息|转发)$",
            RegexOptions.Compiled);

        private static string CleanTitle(string title)
        {
            string cleaned = title.Trim().TrimEnd('。', '！', '？', '；', ';', '，', ',', '、', ' ');
            cleaned = RxLeadingTimeNoise.Replace(cleaned, "").Trim();
            cleaned = StripDateAndTime(cleaned);
            cleaned = CutAtDeadlineMarker(cleaned);
            cleaned = Regex.Replace(cleaned, @"^(上午|下午|中午|晚上|傍晚|晚|早上|早晨|清晨)$", "").Trim();
            cleaned = Regex.Replace(cleaned, @"[ \t]{2,}", " ").Trim();
            cleaned = cleaned.TrimEnd('。', '！', '？', '；', ';', '，', ',', '、', '：', ':', ' ');
            if (cleaned.Length == 0)
            {
                // A clause that only carried a date/time ("12月16号，") is not a
                // title; the caller picks the next clause instead.
                return "";
            }

            return cleaned;
        }

        private static string StripDateAndTime(string input)
        {
            string result = RxYmd.Replace(input, " ");
            result = RxMonthDay.Replace(result, " ");
            result = RxWeekday.Replace(result, " ");
            result = RxRelative.Replace(result, " ");
            result = RxHalf.Replace(result, " ");
            result = RxTimeCn.Replace(result, " ");
            result = RxTime.Replace(result, " ");
            result = Regex.Replace(result, @"第?\s*\d{1,2}\s*周", " ");
            result = Regex.Replace(result, @"\s*(到|至|~|～|—|-)\s*", " ");
            return result;
        }

        private static string CutAtDeadlineMarker(string input)
        {
            int cut = -1;
            foreach (string marker in DeadlineMarkers)
            {
                int index = input.IndexOf(marker, StringComparison.Ordinal);
                if (index >= 2 && (cut < 0 || index < cut))
                {
                    cut = index;
                }
            }

            return cut > 0 ? input.Substring(0, cut) : input;
        }

        private static string CleanStep(string step)
        {
            string cleaned = step.Trim().TrimStart('，', ',', '、', ' ', '：', ':');
            cleaned = RxLeadingTimeNoise.Replace(cleaned, "").Trim();
            cleaned = StripDateAndTime(cleaned);
            cleaned = Regex.Replace(cleaned, @"[ \t]{2,}", " ").Trim();
            cleaned = cleaned.TrimEnd('，', ',', '、', ' ');
            return cleaned;
        }

        private static string FirstLine(string text, int max)
        {
            string first = text;
            int newline = first.IndexOf('\n');
            if (newline > 0)
            {
                first = first.Substring(0, newline);
            }

            first = first.Trim();
            return first.Length > max ? first.Substring(0, max) + "…" : first;
        }

        private static int MapWeekday(char ch)
        {
            switch (ch)
            {
                case '一': return 1;
                case '二': return 2;
                case '三': return 3;
                case '四': return 4;
                case '五': return 5;
                case '六': return 6;
                case '日':
                case '天':
                    return 7;
                default:
                    return 0;
            }
        }

        private static bool IsValidDate(int year, int month, int day)
        {
            if (year < 1900 || year > 2200 || month < 1 || month > 12 || day < 1 || day > 31)
            {
                return false;
            }

            return day <= DateTime.DaysInMonth(year, month);
        }

        public static string Describe(DateTimeOffset? due)
        {
            if (!due.HasValue)
            {
                return "无截止时间";
            }

            string[] names = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            DateTimeOffset value = due.Value;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyy-MM-dd} {1} {0:HH:mm}",
                value,
                names[(int)value.DayOfWeek]);
        }
    }
}
