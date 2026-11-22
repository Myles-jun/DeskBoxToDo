using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Optional LLM pass for the confirm card ("AI 重解析"). Reads the same
    /// llm* keys 1.x uses from config.json, so an existing API key just works.
    /// </summary>
    internal static class AiReparse
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        internal sealed class Result
        {
            public string Title = "";
            public DateTimeOffset? Due;
            public string Recurrence = "none";
            public string Color = "";
            public bool Important;
            public string Notes = "";
            public string[] Steps = Array.Empty<string>();
        }

        public static async Task<Result> ParseAsync(string text)
        {
            (string baseUrl, string apiKey, string model) = ReadConfig();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("还没有配置 AI：请在 1.x 的 config.json 里填 llmApiKey，或在设置里启用 AI。");
            }

            string prompt =
                "把用户给出的一句话整理成待办，只输出 JSON（不要解释），字段：\n" +
                "t=标题(简短), d=截止时间(ISO8601，没有就 null), r=none|daily|weekly|monthly|weekdays,\n" +
                "c=颜色(red|orange|yellow|green|blue|purple|teal|pink 或空), i=是否重要(true/false),\n" +
                "s=子任务数组, n=备注。\n" +
                "现在时间：" + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm zzz") + "\n" +
                "用户原文：\n" + text;

            var payload = new JsonObject
            {
                ["model"] = model,
                ["messages"] = new JsonArray
                {
                    new JsonObject { ["role"] = "system", ["content"] = "你是待办整理助手，只输出 JSON。" },
                    new JsonObject { ["role"] = "user", ["content"] = prompt }
                },
                ["temperature"] = 0.2
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions"))
            {
                request.Headers.Add("Authorization", "Bearer " + apiKey);
                request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

                using (HttpResponseMessage response = await Client.SendAsync(request).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("AI 接口返回 " + (int)response.StatusCode + "：" + Trim(body));
                    }

                    using (JsonDocument document = JsonDocument.Parse(body))
                    {
                        string content = document.RootElement
                            .GetProperty("choices")[0]
                            .GetProperty("message")
                            .GetProperty("content")
                            .GetString() ?? "";

                        return ParseJson(content);
                    }
                }
            }
        }

        private static Result ParseJson(string content)
        {
            int start = content.IndexOf('{');
            int end = content.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                throw new InvalidOperationException("AI 没有返回 JSON：" + Trim(content));
            }

            string json = content.Substring(start, end - start + 1);
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement root = document.RootElement;
                var result = new Result
                {
                    Title = GetString(root, "t"),
                    Recurrence = NormalizeRecurrence(GetString(root, "r")),
                    Color = NormalizeColor(GetString(root, "c")),
                    Important = root.TryGetProperty("i", out JsonElement important) && important.ValueKind == JsonValueKind.True,
                    Notes = GetString(root, "n")
                };

                string due = GetString(root, "d");
                if (!string.IsNullOrWhiteSpace(due) &&
                    DateTimeOffset.TryParse(due, out DateTimeOffset parsed))
                {
                    result.Due = parsed;
                }

                if (root.TryGetProperty("s", out JsonElement steps) && steps.ValueKind == JsonValueKind.Array)
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (JsonElement step in steps.EnumerateArray())
                    {
                        if (step.ValueKind == JsonValueKind.String)
                        {
                            list.Add(step.GetString() ?? "");
                        }
                    }

                    result.Steps = list.ToArray();
                }

                return result;
            }
        }

        private static string GetString(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        private static string NormalizeRecurrence(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "daily":
                case "weekly":
                case "monthly":
                case "weekdays":
                    return value.Trim().ToLowerInvariant();
                default:
                    return "none";
            }
        }

        private static string NormalizeColor(string value)
        {
            string key = (value ?? "").Trim().ToLowerInvariant();
            foreach (var entry in TodoItem.Palette)
            {
                if (entry.Key == key)
                {
                    return key;
                }
            }

            return "";
        }

        private static (string BaseUrl, string ApiKey, string Model) ReadConfig()
        {
            string baseUrl = "https://api.deepseek.com/v1";
            string apiKey = "";
            string model = "deepseek-chat";

            try
            {
                string path = Path.Combine(TodoStore.DataRoot, "config.json");
                if (!File.Exists(path))
                {
                    return (baseUrl, apiKey, model);
                }

                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
                {
                    JsonElement root = document.RootElement;
                    if (root.TryGetProperty("llmBaseUrl", out JsonElement url) && url.ValueKind == JsonValueKind.String)
                    {
                        baseUrl = url.GetString() ?? baseUrl;
                    }

                    if (root.TryGetProperty("llmApiKey", out JsonElement key) && key.ValueKind == JsonValueKind.String)
                    {
                        apiKey = key.GetString() ?? "";
                    }

                    if (root.TryGetProperty("llmModel", out JsonElement m) && m.ValueKind == JsonValueKind.String)
                    {
                        model = m.GetString() ?? model;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ai config read failed: " + ex.Message);
            }

            return (baseUrl, apiKey, model);
        }

        private static string Trim(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            return text.Length <= 160 ? text : text.Substring(0, 160) + "…";
        }
    }
}
