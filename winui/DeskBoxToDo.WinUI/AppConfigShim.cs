using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeskBoxToDo.WinUI;

namespace DeskBoxToDo
{
    /// <summary>
    /// Minimal stand-in for the 1.x AppConfig so the shared TodoParser compiles in
    /// the WinUI build. Only the parser-relevant fields are carried over.
    /// </summary>
    internal sealed class AppConfig
    {
        public int DefaultDueHour = 8;
        public int DefaultDueMinute = 0;

        private static string ConfigPath => Path.Combine(TodoStore.DataRoot, "config.json");

        /// <summary>True once the welcome card has been dismissed.</summary>
        public static bool IsOnboardingDone
        {
            get
            {
                try
                {
                    if (!File.Exists(ConfigPath))
                    {
                        return false;
                    }

                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                    return document.RootElement.TryGetProperty("onboardingDone", out JsonElement done) &&
                        done.ValueKind == JsonValueKind.True;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void MarkOnboardingDone()
        {
            try
            {
                JsonObject root = File.Exists(ConfigPath)
                    ? (JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject ?? new JsonObject())
                    : new JsonObject();
                root["onboardingDone"] = true;
                Directory.CreateDirectory(TodoStore.DataRoot);
                File.WriteAllText(ConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            }
            catch (Exception ex)
            {
                Console.WriteLine("onboarding flag write failed: " + ex.Message);
            }
        }

        /// <summary>Persists the default due time back into the shared config.json.</summary>
        public void Save()
        {
            try
            {
                JsonObject root;
                if (File.Exists(ConfigPath))
                {
                    root = JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                root["defaultDueHour"] = DefaultDueHour;
                root["defaultDueMinute"] = DefaultDueMinute;
                Directory.CreateDirectory(TodoStore.DataRoot);
                File.WriteAllText(ConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            }
            catch (Exception ex)
            {
                Console.WriteLine("config save failed: " + ex.Message);
            }
        }

        public static AppConfig Load()
        {
            var config = new AppConfig();
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return config;
                }

                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                if (document.RootElement.TryGetProperty("defaultDueHour", out JsonElement hour) &&
                    hour.ValueKind == JsonValueKind.Number)
                {
                    config.DefaultDueHour = Math.Clamp(hour.GetInt32(), 0, 23);
                }

                if (document.RootElement.TryGetProperty("defaultDueMinute", out JsonElement minute) &&
                    minute.ValueKind == JsonValueKind.Number)
                {
                    config.DefaultDueMinute = Math.Clamp(minute.GetInt32(), 0, 59);
                }
            }
            catch
            {
            }

            return config;
        }
    }
}
