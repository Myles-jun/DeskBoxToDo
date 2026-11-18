using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Web.Script.Serialization;

namespace DeskBoxToDo
{
    internal sealed class UpdateInfo
    {
        public string Version = "";
        public string Url = "";
        public string Notes = "";
    }

    /// <summary>
    /// Checks a release feed (GitHub releases API or a plain JSON file) and can
    /// download + launch the installer for the newer version.
    /// </summary>
    internal static class UpdateService
    {
        private static readonly HttpClient Client = CreateClient();
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static string LastError { get; private set; }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("User-Agent", "DeskBoxToDo/" + AppInfo.Version);
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            return client;
        }

        /// <summary>Returns the newer release, or null when up to date / unreachable.</summary>
        public static UpdateInfo Check(string feedUrl)
        {
            LastError = "";
            if (string.IsNullOrWhiteSpace(feedUrl))
            {
                LastError = "还没有配置更新地址";
                return null;
            }

            try
            {
                string body = Client.GetStringAsync(feedUrl).GetAwaiter().GetResult();
                var root = Serializer.Deserialize<Dictionary<string, object>>(body);
                if (root == null)
                {
                    LastError = "更新地址返回的不是 JSON";
                    return null;
                }

                UpdateInfo info = feedUrl.IndexOf("api.github.com", StringComparison.OrdinalIgnoreCase) >= 0
                    ? ParseGitHub(root)
                    : ParsePlain(root);

                if (info == null)
                {
                    LastError = "更新信息里没有版本号";
                    return null;
                }

                if (CompareVersions(info.Version, AppInfo.Version) <= 0)
                {
                    Log.Write("update check: up to date (" + AppInfo.Version + " >= " + info.Version + ")");
                    return null;
                }

                Log.Write("update check: " + info.Version + " available");
                return info;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Write("update check failed: " + ex.Message);
                return null;
            }
        }

        private static UpdateInfo ParseGitHub(Dictionary<string, object> root)
        {
            var info = new UpdateInfo();
            object tag;
            if (root.TryGetValue("tag_name", out tag) && tag != null)
            {
                info.Version = Convert.ToString(tag, CultureInfo.InvariantCulture).TrimStart('v', 'V');
            }

            object name;
            if (root.TryGetValue("name", out name) && name != null)
            {
                info.Notes = Convert.ToString(name, CultureInfo.InvariantCulture);
            }

            object body;
            if (root.TryGetValue("body", out body) && body != null)
            {
                info.Notes = Convert.ToString(body, CultureInfo.InvariantCulture);
            }

            object assetsRaw;
            if (root.TryGetValue("assets", out assetsRaw) && assetsRaw != null)
            {
                string fallback = "";
                foreach (object entry in Flatten(assetsRaw))
                {
                    var asset = entry as Dictionary<string, object>;
                    if (asset == null)
                    {
                        continue;
                    }

                    string assetName = Value(asset, "name");
                    if (!assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        assetName.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) < 0 ||
                        assetName.IndexOf("arm64", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    string url = Value(asset, "browser_download_url");
                    if (assetName.IndexOf("x64", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        assetName.IndexOf("amd64", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        info.Url = url;
                        break;
                    }

                    if (fallback.Length == 0)
                    {
                        fallback = url;
                    }
                }

                if (info.Url.Length == 0)
                {
                    info.Url = fallback;
                }
            }

            return info.Version.Length == 0 ? null : info;
        }

        private static UpdateInfo ParsePlain(Dictionary<string, object> root)
        {
            var info = new UpdateInfo
            {
                Version = Value(root, "version").TrimStart('v', 'V'),
                Url = Value(root, "url"),
                Notes = Value(root, "notes")
            };
            return info.Version.Length == 0 ? null : info;
        }

        private static string Value(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : "";
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

        /// <summary>Downloads the installer and starts it silently; caller should exit afterwards.</summary>
        public static bool DownloadAndRun(UpdateInfo info, out string error)
        {
            error = "";
            try
            {
                if (string.IsNullOrWhiteSpace(info.Url))
                {
                    error = "更新包地址为空";
                    return false;
                }

                string target = Path.Combine(
                    Path.GetTempPath(),
                    "DeskBoxToDo_Setup_" + info.Version + ".exe");

                byte[] data = Client.GetByteArrayAsync(info.Url).GetAwaiter().GetResult();
                if (data == null || data.Length < 1024)
                {
                    error = "下载失败（文件不完整）";
                    return false;
                }

                File.WriteAllBytes(target, data);
                Log.Write("update downloaded: " + target + " (" + data.Length + " bytes)");

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = target,
                    Arguments = "/S",
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Write("update download failed: " + ex.Message);
                return false;
            }
        }

        public static int CompareVersions(string left, string right)
        {
            int[] a = Parts(left);
            int[] b = Parts(right);
            for (int index = 0; index < Math.Max(a.Length, b.Length); index++)
            {
                int x = index < a.Length ? a[index] : 0;
                int y = index < b.Length ? b[index] : 0;
                if (x != y)
                {
                    return x < y ? -1 : 1;
                }
            }

            return 0;
        }

        private static int[] Parts(string version)
        {
            var numbers = new List<int>();
            string current = "";
            foreach (char ch in (version ?? "").Trim())
            {
                if (char.IsDigit(ch))
                {
                    current += ch;
                }
                else if (current.Length > 0)
                {
                    numbers.Add(int.Parse(current, CultureInfo.InvariantCulture));
                    current = "";
                }
            }

            if (current.Length > 0)
            {
                numbers.Add(int.Parse(current, CultureInfo.InvariantCulture));
            }

            return numbers.ToArray();
        }

        public static Task<UpdateInfo> CheckAsync(string feedUrl)
        {
            return Task.Run(() => Check(feedUrl));
        }
    }
}
