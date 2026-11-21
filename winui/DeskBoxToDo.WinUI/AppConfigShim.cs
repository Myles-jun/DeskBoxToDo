using System;
using System.IO;
using System.Text.Json;

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

        public static AppConfig Load()
        {
            var config = new AppConfig();
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeskBoxToDo", "config.json");
                if (!File.Exists(path))
                {
                    return config;
                }

                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
                {
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
            }
            catch
            {
            }

            return config;
        }
    }
}
