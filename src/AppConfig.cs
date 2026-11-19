using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DeskBoxToDo
{
    /// <summary>User-editable settings, stored next to the exe's per-user data root.</summary>
    internal sealed class AppConfig
    {
        public string HotkeyModifiers = "Ctrl+Shift";
        public string HotkeyKey = "Q";
        public int DefaultDueHour = 8;
        public int DefaultDueMinute = 0;
        public string DeskBoxExePath = @"C:\Program Files\DeskBox\DeskBox.exe";
        public string DeskBoxDataRoot = @"%LOCALAPPDATA%\DeskBox";
        public string ColorMarker = "";
        public bool RestartAfterWrite = true;
        public bool LiveBackdrop = true;
        public bool AutoStart = true;
        public string TodoMode = "standalone";
        public bool TodoWidgetOnDesktop;
        public string TodoWidgetBounds = "";
        public string TodoWindowSize = "";
        public bool TodoWindowOpen;
        public string ReminderSound = "";
        public int GlassOpacityPercent;
        public int GlassQuality;
        public int CornerRadius = -1;
        public const string DefaultUpdateFeed = "https://api.github.com/repos/Myles-jun/DeskBoxToDo/releases/latest";
        public string UpdateFeedUrl = DefaultUpdateFeed;
        public int UpdateCheckMode = 2;
        public bool UpdateAutoInstall = true;
        public string LastUpdateCheckUtc = "";
        public bool ReminderSoundEnabled = true;
        public int OnboardingVersion;

        // Reserved for the optional LLM parser; the rule parser always runs first.
        public bool LlmEnabled = false;
        public bool LlmAlways = false;
        public bool LlmAuto = false;
        public bool LlmJsonMode = true;
        public string LlmBaseUrl = "https://api.deepseek.com/v1";
        public string LlmApiKey = "";
        public string LlmModel = "deepseek-chat";
        public int LlmTimeoutSeconds = 15;

        public static string ConfigDirectory
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "DeskBoxToDo");
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDirectory, "config.json"); }
        }

        public static AppConfig Load()
        {
            var config = new AppConfig();
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    config.Save();
                    return config;
                }

                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var raw = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath, Encoding.UTF8));
                if (raw == null)
                {
                    return config;
                }

                config.HotkeyModifiers = ReadString(raw, "hotkeyModifiers", config.HotkeyModifiers);
                config.HotkeyKey = ReadString(raw, "hotkeyKey", config.HotkeyKey);
                config.DefaultDueHour = ReadInt(raw, "defaultDueHour", config.DefaultDueHour);
                config.DefaultDueMinute = ReadInt(raw, "defaultDueMinute", config.DefaultDueMinute);
                config.DeskBoxExePath = ReadString(raw, "deskBoxExePath", config.DeskBoxExePath);
                config.DeskBoxDataRoot = ReadString(raw, "deskBoxDataRoot", config.DeskBoxDataRoot);
                config.ColorMarker = ReadString(raw, "colorMarker", config.ColorMarker);
                config.RestartAfterWrite = ReadBool(raw, "restartAfterWrite", config.RestartAfterWrite);
                config.LiveBackdrop = ReadBool(raw, "liveBackdrop", config.LiveBackdrop);
                config.AutoStart = ReadBool(raw, "autoStart", config.AutoStart);
                config.TodoMode = ReadString(raw, "todoMode", config.TodoMode);
                config.TodoWidgetOnDesktop = ReadBool(raw, "todoWidgetOnDesktop", config.TodoWidgetOnDesktop);
                config.TodoWidgetBounds = ReadString(raw, "todoWidgetBounds", config.TodoWidgetBounds);
                config.TodoWindowSize = ReadString(raw, "todoWindowSize", config.TodoWindowSize);
                config.TodoWindowOpen = ReadBool(raw, "todoWindowOpen", config.TodoWindowOpen);
                config.ReminderSound = ReadString(raw, "reminderSound", config.ReminderSound);
                config.GlassOpacityPercent = ReadInt(raw, "glassOpacityPercent", config.GlassOpacityPercent);
                config.GlassQuality = ReadInt(raw, "glassQuality", config.GlassQuality);
                config.CornerRadius = ReadInt(raw, "cornerRadius", config.CornerRadius);
                config.UpdateFeedUrl = ReadString(raw, "updateFeedUrl", config.UpdateFeedUrl);
                if (string.IsNullOrWhiteSpace(config.UpdateFeedUrl))
                {
                    config.UpdateFeedUrl = DefaultUpdateFeed;
                }
                config.UpdateCheckMode = ReadInt(raw, "updateCheckMode", config.UpdateCheckMode);
                config.UpdateAutoInstall = ReadBool(raw, "updateAutoInstall", config.UpdateAutoInstall);
                config.LastUpdateCheckUtc = ReadString(raw, "lastUpdateCheckUtc", config.LastUpdateCheckUtc);
                config.ReminderSoundEnabled = ReadBool(raw, "reminderSoundEnabled", config.ReminderSoundEnabled);
                config.OnboardingVersion = ReadInt(raw, "onboardingVersion", config.OnboardingVersion);
                config.LlmEnabled = ReadBool(raw, "llmEnabled", config.LlmEnabled);
                config.LlmAlways = ReadBool(raw, "llmAlways", config.LlmAlways);
                config.LlmAuto = ReadBool(raw, "llmAuto", config.LlmAuto);
                config.LlmJsonMode = ReadBool(raw, "llmJsonMode", config.LlmJsonMode);
                config.LlmBaseUrl = ReadString(raw, "llmBaseUrl", config.LlmBaseUrl);
                config.LlmApiKey = ReadString(raw, "llmApiKey", config.LlmApiKey);
                config.LlmModel = ReadString(raw, "llmModel", config.LlmModel);
                config.LlmTimeoutSeconds = ReadInt(raw, "llmTimeoutSeconds", config.LlmTimeoutSeconds);
            }
            catch (Exception ex)
            {
                Log.Write("config load failed: " + ex.Message);
            }

            return config;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var map = new Dictionary<string, object>
                {
                    { "hotkeyModifiers", HotkeyModifiers },
                    { "hotkeyKey", HotkeyKey },
                    { "defaultDueHour", DefaultDueHour },
                    { "defaultDueMinute", DefaultDueMinute },
                    { "deskBoxExePath", DeskBoxExePath },
                    { "deskBoxDataRoot", DeskBoxDataRoot },
                    { "colorMarker", ColorMarker },
                    { "restartAfterWrite", RestartAfterWrite },
                    { "liveBackdrop", LiveBackdrop },
                    { "autoStart", AutoStart },
                    { "todoMode", TodoMode },
                    { "todoWidgetOnDesktop", TodoWidgetOnDesktop },
                    { "todoWidgetBounds", TodoWidgetBounds },
                    { "todoWindowSize", TodoWindowSize },
                    { "todoWindowOpen", TodoWindowOpen },
                    { "reminderSound", ReminderSound },
                    { "glassOpacityPercent", GlassOpacityPercent },
                    { "glassQuality", GlassQuality },
                    { "cornerRadius", CornerRadius },
                    { "updateFeedUrl", UpdateFeedUrl },
                    { "updateCheckMode", UpdateCheckMode },
                    { "updateAutoInstall", UpdateAutoInstall },
                    { "lastUpdateCheckUtc", LastUpdateCheckUtc },
                    { "reminderSoundEnabled", ReminderSoundEnabled },
                    { "onboardingVersion", OnboardingVersion },
                    { "llmEnabled", LlmEnabled },
                    { "llmAlways", LlmAlways },
                    { "llmAuto", LlmAuto },
                    { "llmJsonMode", LlmJsonMode },
                    { "llmBaseUrl", LlmBaseUrl },
                    { "llmApiKey", LlmApiKey },
                    { "llmModel", LlmModel },
                    { "llmTimeoutSeconds", LlmTimeoutSeconds }
                };

                string json = serializer.Serialize(map);
                File.WriteAllText(ConfigPath, PrettyJson.Format(json), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log.Write("config save failed: " + ex.Message);
            }
        }

        private static string ReadString(Dictionary<string, object> map, string key, string fallback)
        {
            object value;
            if (map.TryGetValue(key, out value) && value is string text && text.Length > 0)
            {
                return text;
            }

            return fallback;
        }

        private static int ReadInt(Dictionary<string, object> map, string key, int fallback)
        {
            object value;
            if (map.TryGetValue(key, out value) && value != null)
            {
                try
                {
                    return Convert.ToInt32(value);
                }
                catch
                {
                    return fallback;
                }
            }

            return fallback;
        }

        private static bool ReadBool(Dictionary<string, object> map, string key, bool fallback)
        {
            object value;
            if (map.TryGetValue(key, out value) && value is bool flag)
            {
                return flag;
            }

            return fallback;
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppConfig.ConfigDirectory);
                    string path = Path.Combine(AppConfig.ConfigDirectory, "DeskBoxToDo.log");
                    File.AppendAllText(
                        path,
                        string.Format("{0:yyyy-MM-dd HH:mm:ss} {1}{2}", DateTime.Now, message, Environment.NewLine),
                        new UTF8Encoding(false));
                }
            }
            catch
            {
                // Logging must never break the tool.
            }
        }
    }

    internal static class PrettyJson
    {
        /// <summary>Minimal re-indenter so config stays human readable (serializer emits one line).</summary>
        public static string Format(string json)
        {
            var builder = new StringBuilder(json.Length + 128);
            int indent = 0;
            bool inString = false;
            bool escaped = false;

            foreach (char ch in json)
            {
                if (inString)
                {
                    builder.Append(ch);
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                switch (ch)
                {
                    case '"':
                        inString = true;
                        builder.Append(ch);
                        break;
                    case '{':
                    case '[':
                        builder.Append(ch);
                        indent++;
                        builder.Append('\n');
                        builder.Append(' ', indent * 2);
                        break;
                    case '}':
                    case ']':
                        indent--;
                        builder.Append('\n');
                        builder.Append(' ', Math.Max(0, indent) * 2);
                        builder.Append(ch);
                        break;
                    case ',':
                        builder.Append(ch);
                        builder.Append('\n');
                        builder.Append(' ', Math.Max(0, indent) * 2);
                        break;
                    case ':':
                        builder.Append(": ");
                        break;
                    case ' ':
                        break;
                    default:
                        builder.Append(ch);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
