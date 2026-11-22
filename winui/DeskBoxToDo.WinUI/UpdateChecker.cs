using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// GitHub Releases based update check (same repository 1.x uses).
    /// Only checks and reports; downloading/installing comes later.
    /// </summary>
    internal static class UpdateChecker
    {
        internal const string Repository = "Myles-jun/DeskBoxToDo";

        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        internal sealed class Result
        {
            public bool HasUpdate;
            public string LatestTag = "";
            public string CurrentVersion = "";
            public string PageUrl = "";
            public string Notes = "";
        }

        internal static string CurrentVersion
        {
            get
            {
                try
                {
                    return typeof(UpdateChecker).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
                }
                catch
                {
                    return "0.0.0";
                }
            }
        }

        internal static async Task<Result> CheckAsync()
        {
            var result = new Result { CurrentVersion = CurrentVersion };

            using (var request = new HttpRequestMessage(
                HttpMethod.Get,
                "https://api.github.com/repos/" + Repository + "/releases?per_page=20"))
            {
                request.Headers.Add("User-Agent", "DeskBoxToDo");
                request.Headers.Add("Accept", "application/vnd.github+json");

                using (HttpResponseMessage response = await Client.SendAsync(request).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("GitHub 返回 " + (int)response.StatusCode);
                    }

                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using (JsonDocument document = JsonDocument.Parse(body))
                    {
                        Version best = null;
                        foreach (JsonElement release in document.RootElement.EnumerateArray())
                        {
                            if (release.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True)
                            {
                                continue;
                            }

                            if (!release.TryGetProperty("tag_name", out JsonElement tag) || tag.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            string tagText = (tag.GetString() ?? "").TrimStart('v', 'V');
                            if (!Version.TryParse(tagText, out Version parsed))
                            {
                                continue;
                            }

                            if (best == null || parsed > best)
                            {
                                best = parsed;
                                result.LatestTag = tag.GetString() ?? "";
                                result.Notes = release.TryGetProperty("body", out JsonElement notes) && notes.ValueKind == JsonValueKind.String
                                    ? notes.GetString() ?? ""
                                    : "";
                                result.PageUrl = release.TryGetProperty("html_url", out JsonElement url) && url.ValueKind == JsonValueKind.String
                                    ? url.GetString() ?? ""
                                    : "";
                            }
                        }

                        if (best != null && Version.TryParse(CurrentVersion, out Version current) && best > current)
                        {
                            result.HasUpdate = true;
                        }
                    }
                }
            }

            return result;
        }
    }
}
