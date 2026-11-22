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
            public string AssetUrl = "";
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
                                result.AssetUrl = PickAsset(release);
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

        /// <summary>
        /// Prefers the x64 setup installer, then any .exe asset, then a zip so a
        /// release without an installer still has a download target.
        /// </summary>
        private static string PickAsset(JsonElement release)
        {
            if (!release.TryGetProperty("assets", out JsonElement assets) ||
                assets.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

            string setup = "";
            string anyExe = "";
            string zip = "";
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("browser_download_url", out JsonElement url) ||
                    url.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string name = asset.TryGetProperty("name", out JsonElement nameNode) && nameNode.ValueKind == JsonValueKind.String
                    ? nameNode.GetString() ?? ""
                    : "";
                string href = url.GetString() ?? "";
                if (href.Length == 0)
                {
                    continue;
                }

                string lower = name.ToLowerInvariant();
                if (lower.Contains("arm64"))
                {
                    continue;
                }

                if (lower.Contains("setup") && lower.EndsWith(".exe"))
                {
                    setup = href;
                }
                else if (lower.EndsWith(".exe") && anyExe.Length == 0)
                {
                    anyExe = href;
                }
                else if (lower.EndsWith(".zip") && zip.Length == 0)
                {
                    zip = href;
                }
            }

            return setup.Length > 0 ? setup : (anyExe.Length > 0 ? anyExe : zip);
        }

        /// <summary>Downloads the installer into %TEMP% and returns its path.</summary>
        internal static async Task<string> DownloadAsync(string url, Action<long, long> progress)
        {
            string fileName = url.Split('/')[^1];
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "DeskBoxToDo-update.exe";
            }

            string target = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);
            using HttpResponseMessage response = await Client
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? 0;
            await using (System.IO.Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await using (System.IO.FileStream destination = System.IO.File.Create(target))
            {
                byte[] buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    received += read;
                    progress?.Invoke(received, total);
                }
            }

            return target;
        }

        /// <summary>Launches the downloaded installer (silent when it is our setup).</summary>
        internal static void RunInstaller(string path)
        {
            var info = new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "/S" : ""
            };
            System.Diagnostics.Process.Start(info);
        }
    }
}
