using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DeskBoxToDo
{
    /// <summary>
    /// Optional LLM pass. Token discipline:
    ///  - a ~120 token system prompt, no few-shot examples, no history;
    ///  - only the (trimmed, 800 char max) notice is sent;
    ///  - temperature 0, max_tokens 320, one attempt (+1 retry without JSON mode);
    ///  - results are cached per input so re-opening the dialog never re-bills;
    ///  - the caller only runs this when the rule parser is unsure, unless the
    ///    user explicitly asks for it.
    /// </summary>
    internal static class AiParser
    {
        private const int MaxChars = 800;
        private const int MaxTokens = 320;

        private const string SystemPrompt =
            "你是待办解析器。把用户消息转成 JSON，只输出 JSON，不要解释：\n" +
            "{\"t\":\"标题，不超过20字\",\"s\":[\"子项1\",\"子项2\"],\"d\":\"YYYY-MM-DDTHH:mm:ss+08:00 或 null\",\"i\":true|false,\"r\":\"none|daily|weekly|monthly|weekdays\"}\n" +
            "规则：d 按消息里的时间词推算，没有时间就 null；s 只放消息里真实提到的作业/步骤，最多6条，不要编造；i 表示是否与作业/考试/截止相关；" +
            "r 是重复规则：每天/每日/天天 → daily，每周/每星期/每周几 → weekly，每月/每个月 → monthly，工作日/周一到周五 → weekdays，其它一律 none；" +
            "r 不是 none 而 d 是 null 时，d 取今天的 23:59。";

        private static string BuildSystemPrompt()
        {
            string[] names = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            DateTimeOffset now = DateTimeOffset.Now;
            return SystemPrompt +
                   "\n今天是 " + now.ToString("yyyy-MM-dd") + " " + names[(int)now.DayOfWeek] +
                   "，时区 " + now.ToString("zzz") + "。";
        }

        private static readonly HttpClient Client = CreateClient();
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly object CacheGate = new object();

        public static string LastUsage { get; private set; }
        public static string LastError { get; private set; }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("User-Agent", "DeskBoxToDo/1.0");
            return client;
        }

        /// <summary>Runs the "parse everything again with the model" path when the user clicks AI in the dialog.</summary>
        public static Task<TodoDraft> ParseAsync(string text, AppConfig config)
        {
            return Task.Run(() => Parse(text, config));
        }

        public static bool IsConfigured(AppConfig config)
        {
            return config.LlmEnabled &&
                   !string.IsNullOrWhiteSpace(config.LlmApiKey) &&
                   !string.IsNullOrWhiteSpace(config.LlmBaseUrl);
        }

        public static TodoDraft Parse(string text, AppConfig config)
        {
            LastError = null;
            if (!IsConfigured(config))
            {
                LastError = "未配置 API Key";
                return null;
            }

            string input = Trim(text);
            if (input.Length == 0)
            {
                return null;
            }

            string cacheKey = config.LlmModel + "\u0001" + input;
            lock (CacheGate)
            {
                string cached;
                if (Cache.TryGetValue(cacheKey, out cached))
                {
                    TodoDraft fromCache = Deserialize(cached);
                    if (fromCache != null)
                    {
                        Log.Write("ai cache hit, no tokens spent");
                        return fromCache;
                    }
                }
            }

            string content;
            if (!TryComplete(config, input, config.LlmJsonMode, out content))
            {
                // Some gateways reject response_format; retry once without it.
                if (config.LlmJsonMode && TryComplete(config, input, false, out content))
                {
                    Log.Write("ai json mode unsupported, retried in plain mode");
                }
                else
                {
                    Log.Write("ai call failed: " + LastError);
                    return null;
                }
            }

            TodoDraft draft = Deserialize(content);
            if (draft == null)
            {
                LastError = "返回内容不是可解析的 JSON";
                Log.Write("ai parse failed: " + LastError + " raw=" + Truncate(content, 200));
                return null;
            }

            lock (CacheGate)
            {
                if (Cache.Count > 64)
                {
                    Cache.Clear();
                }

                Cache[cacheKey] = content;
            }

            return draft;
        }

        /// <summary>Cheap round trip for the settings page: 1 token answer, no parsing.</summary>
        public static string Test(AppConfig config)
        {
            if (!IsConfigured(config))
            {
                return "请先填写 API 地址和 Key，并打开「启用 AI 解析」";
            }

            string content;
            var probe = new AppConfig
            {
                LlmEnabled = true,
                LlmApiKey = config.LlmApiKey,
                LlmBaseUrl = config.LlmBaseUrl,
                LlmModel = config.LlmModel,
                LlmTimeoutSeconds = config.LlmTimeoutSeconds,
                LlmJsonMode = false
            };

            int status;
            if (ProbeEndpoint(probe, out content, out status))
            {
                string extra = string.IsNullOrWhiteSpace(LastUsage) ? "" : "（" + LastUsage + "）";
                return "连接成功" + extra;
            }

            string hint = string.Empty;
            string key = (config.LlmApiKey ?? "").Trim();
            if (!key.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
            {
                hint = "（这个 Key 不像 DeepSeek 的 API Key，正常的以 sk- 开头）";
            }

            return "失败：" + LastError + hint;
        }

        /// <summary>Cheapest possible check: HTTP 200 is enough, content may be empty.</summary>
        private static bool ProbeEndpoint(AppConfig config, out string content, out int status)
        {
            content = null;
            LastStatus = 0;
            try
            {
                string body = Serializer.Serialize(new Dictionary<string, object>
                {
                    { "model", config.LlmModel },
                    {
                        "messages", new object[]
                        {
                            new Dictionary<string, object> { { "role", "user" }, { "content", "ping" } }
                        }
                    },
                    { "max_tokens", 8 },
                    { "temperature", 0 },
                    { "stream", false }
                });

                string url = BuildUrl(config.LlmBaseUrl);
                status = SendProbe(url, body, config, out content);
                if (status == 404)
                {
                    string withV1 = BuildUrlWithV1(config.LlmBaseUrl);
                    if (!string.Equals(withV1, url, StringComparison.OrdinalIgnoreCase))
                    {
                        status = SendProbe(withV1, body, config, out content);
                    }
                }

                return status == 200;
            }
            catch (Exception ex)
            {
                status = -1;
                LastError = ex.Message;
                return false;
            }
        }

        private static int SendProbe(string url, string body, AppConfig config, out string content)
        {
            content = null;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + config.LlmApiKey);
                    int timeoutMs = Math.Max(5, Math.Min(60, config.LlmTimeoutSeconds)) * 1000;
                    using (var cts = new System.Threading.CancellationTokenSource(timeoutMs))
                    using (HttpResponseMessage response = Client.SendAsync(request, cts.Token).GetAwaiter().GetResult())
                    {
                        string responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        int code = (int)response.StatusCode;
                        if (code != 200)
                        {
                            LastError = DescribeHttpFailure(code, responseBody);
                            return code;
                        }

                        content = ExtractContent(responseBody);
                        return 200;
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return -1;
            }
        }

        private static bool TryComplete(AppConfig config, string userText, bool jsonMode, out string content)
        {
            return TryComplete(config, userText, jsonMode, out content, MaxTokens);
        }

        private static bool TryComplete(AppConfig config, string userText, bool jsonMode, out string content, int maxTokens)
        {
            content = null;

            // Test hook: lets the whole parse/merge path run without network.
            string stubbed = Environment.GetEnvironmentVariable("DESKBOX_TODO_AI_FAKE");
            if (!string.IsNullOrEmpty(stubbed))
            {
                content = stubbed;
                LastUsage = "stub (0 tokens)";
                return true;
            }

            try
            {
                var payload = new Dictionary<string, object>
                {
                    { "model", config.LlmModel },
                    {
                        "messages", new object[]
                        {
                            new Dictionary<string, object> { { "role", "system" }, { "content", BuildSystemPrompt() } },
                            new Dictionary<string, object> { { "role", "user" }, { "content", userText } }
                        }
                    },
                    { "temperature", 0 },
                    { "max_tokens", maxTokens },
                    { "stream", false }
                };

                if (jsonMode)
                {
                    payload["response_format"] = new Dictionary<string, object> { { "type", "json_object" } };
                }

                string body = Serializer.Serialize(payload);
                string url = BuildUrl(config.LlmBaseUrl);

                if (SendOnce(url, body, config, out content))
                {
                    return true;
                }

                // 404 usually means "/v1" is missing from the base URL.
                if (LastStatus == 404)
                {
                    string withV1 = BuildUrlWithV1(config.LlmBaseUrl);
                    if (!string.Equals(withV1, url, StringComparison.OrdinalIgnoreCase) &&
                        SendOnce(withV1, body, config, out content))
                    {
                        Log.Write("ai retried with /v1 endpoint ok");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public static int LastStatus { get; private set; }

        private static bool SendOnce(string url, string body, AppConfig config, out string content)
        {
            content = null;
            LastStatus = 0;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + config.LlmApiKey);

                    int timeoutMs = Math.Max(5, Math.Min(60, config.LlmTimeoutSeconds)) * 1000;
                    using (var cts = new System.Threading.CancellationTokenSource(timeoutMs))
                    using (HttpResponseMessage response = Client.SendAsync(request, cts.Token).GetAwaiter().GetResult())
                    {
                        string responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        if (!response.IsSuccessStatusCode)
                        {
                            LastStatus = (int)response.StatusCode;
                            LastError = DescribeHttpFailure(LastStatus, responseBody);
                            return false;
                        }

                        content = ExtractContent(responseBody);
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            LastError = "响应里没有 choices[0].message.content";
                            Log.Write("ai empty content raw=" + Truncate(responseBody, 400));
                            return false;
                        }

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        private static string DescribeHttpFailure(int status, string body)
        {
            string raw = Truncate(body, 160);
            switch (status)
            {
                case 401:
                    return "HTTP 401：Key 无效（DeepSeek 的 Key 形如 sk-…，在 platform.deepseek.com 生成）";
                case 402:
                    return "HTTP 402：账户余额不足";
                case 404:
                    return "HTTP 404：接口地址不对，试试 https://api.deepseek.com/v1";
                case 429:
                    return "HTTP 429：请求太频繁，稍后再试";
                case 400:
                    if (raw.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "HTTP 400：模型名不存在（DeepSeek 只认 deepseek-chat / deepseek-reasoner）";
                    }

                    return "HTTP 400：" + raw;
                default:
                    return "HTTP " + status + " " + raw;
            }
        }

        /// <summary>Same endpoint with /v1 forced in, used after a 404.</summary>
        private static string BuildUrlWithV1(string baseUrl)
        {
            string url = (baseUrl ?? "").Trim().TrimEnd('/');
            if (url.Length == 0)
            {
                return url;
            }

            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            if (url.IndexOf("/v1", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return url + "/chat/completions";
            }

            return url + "/v1/chat/completions";
        }

        private static string BuildUrl(string baseUrl)
        {
            string url = (baseUrl ?? "").Trim().TrimEnd('/');
            if (url.Length == 0)
            {
                return url;
            }

            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            return url + "/chat/completions";
        }

        private static string ExtractContent(string responseBody)
        {
            try
            {
                var root = Serializer.Deserialize<Dictionary<string, object>>(responseBody);
                if (root == null)
                {
                    LastError = "响应不是 JSON";
                    return null;
                }

                object usageRaw;
                if (root.TryGetValue("usage", out usageRaw))
                {
                    var usage = usageRaw as Dictionary<string, object>;
                    if (usage != null)
                    {
                        object prompt, completion, total;
                        usage.TryGetValue("prompt_tokens", out prompt);
                        usage.TryGetValue("completion_tokens", out completion);
                        usage.TryGetValue("total_tokens", out total);
                        LastUsage = string.Format(
                            CultureInfo.InvariantCulture,
                            "输入 {0} / 输出 {1} / 合计 {2} tokens",
                            prompt ?? "-", completion ?? "-", total ?? "-");
                    }
                }

                object choicesRaw;
                if (!root.TryGetValue("choices", out choicesRaw))
                {
                    LastError = "响应缺少 choices";
                    return null;
                }

                // JavaScriptSerializer gives ArrayList for JSON arrays.
                var choices = new List<object>();
                var asArray = choicesRaw as object[];
                var asList = choicesRaw as System.Collections.ArrayList;
                if (asArray != null)
                {
                    choices.AddRange(asArray);
                }
                else if (asList != null)
                {
                    foreach (object entry in asList)
                    {
                        choices.Add(entry);
                    }
                }

                if (choices.Count == 0)
                {
                    LastError = "choices 为空";
                    return null;
                }

                var first = choices[0] as Dictionary<string, object>;
                if (first == null)
                {
                    return null;
                }

                object messageRaw;
                if (!first.TryGetValue("message", out messageRaw))
                {
                    LastError = "响应缺少 message";
                    return null;
                }

                var message = messageRaw as Dictionary<string, object>;
                if (message == null)
                {
                    return null;
                }

                object contentRaw;
                if (!message.TryGetValue("content", out contentRaw) || contentRaw == null)
                {
                    LastError = "响应缺少 content";
                    return null;
                }

                return Convert.ToString(contentRaw, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                LastError = "响应解析失败：" + ex.Message;
                return null;
            }
        }
        private static string CleanJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            string text = content.Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = text.IndexOf('\n');
                if (firstNewline > 0)
                {
                    text = text.Substring(firstNewline + 1);
                }

                int fence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0)
                {
                    text = text.Substring(0, fence);
                }
            }

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                text = text.Substring(start, end - start + 1);
            }

            return text.Trim();
        }

        private static TodoDraft Deserialize(string raw)
        {
            try
            {
                string json = CleanJson(raw);
                var map = Serializer.Deserialize<Dictionary<string, object>>(json);
                if (map == null)
                {
                    return null;
                }

                var draft = new TodoDraft();
                object title;
                if (!map.TryGetValue("t", out title) || title == null)
                {
                    return null;
                }

                draft.Title = Convert.ToString(title, CultureInfo.InvariantCulture).Trim();
                if (draft.Title.Length == 0)
                {
                    return null;
                }

                object steps;
                if (map.TryGetValue("s", out steps))
                {
                    // JavaScriptSerializer hands back ArrayList for arrays.
                    var list = steps as System.Collections.ArrayList;
                    var array = steps as object[];
                    if (array != null)
                    {
                        list = new System.Collections.ArrayList(array);
                    }

                    if (list != null)
                    {
                        foreach (object entry in list)
                        {
                            string step = Convert.ToString(entry, CultureInfo.InvariantCulture).Trim();
                            if (step.Length > 0 && draft.Steps.Count < 12)
                            {
                                draft.Steps.Add(step);
                            }
                        }
                    }
                }

                object due;
                if (map.TryGetValue("d", out due) && due != null)
                {
                    string dueText = Convert.ToString(due, CultureInfo.InvariantCulture).Trim();
                    DateTimeOffset parsed;
                    if (dueText.Length > 0 &&
                        !string.Equals(dueText, "null", StringComparison.OrdinalIgnoreCase) &&
                        DateTimeOffset.TryParse(dueText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
                    {
                        // Guard against model hallucinations: a past date is
                        // useless on a todo, so drop it and let the rules win.
                        DateTimeOffset now = DateTimeOffset.Now;
                        if (parsed >= now.AddHours(-2) && parsed <= now.AddYears(2))
                        {
                            draft.Due = parsed;
                            draft.DateFound = true;
                        }
                        else
                        {
                            Log.Write("ai date rejected as implausible: " + parsed.ToString("o"));
                        }
                    }
                }

                object important;
                if (map.TryGetValue("i", out important) && important is bool)
                {
                    draft.Important = (bool)important;
                }

                object recurrence;
                if (map.TryGetValue("r", out recurrence) && recurrence != null)
                {
                    string mode = TodoRecurrenceMode.Normalize(Convert.ToString(recurrence, CultureInfo.InvariantCulture));
                    if (mode != TodoRecurrenceMode.None)
                    {
                        draft.RecurrenceMode = mode;
                        if (!draft.Due.HasValue)
                        {
                            DateTime today = DateTimeOffset.Now.LocalDateTime.Date.AddHours(23).AddMinutes(59);
                            draft.Due = new DateTimeOffset(DateTime.SpecifyKind(today, DateTimeKind.Local));
                        }
                    }
                }

                return draft;
            }
            catch (Exception ex)
            {
                LastError = "JSON 解析失败：" + ex.Message;
                return null;
            }
        }

        internal static void LogRawForDiagnostics(string raw)
        {
            Log.Write("ai raw: " + (raw ?? "").Replace("\n", " "));
        }

        private static string Trim(string text)
        {
            string collapsed = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            return collapsed.Length > MaxChars ? collapsed.Substring(0, MaxChars) : collapsed;
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            return text.Length > max ? text.Substring(0, max) + "…" : text;
        }
    }
}
