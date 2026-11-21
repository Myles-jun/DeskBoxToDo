using System;
using System.IO;
using System.Text.Json;

namespace DeskBoxToDo.WinUI
{
    /// <summary>Small settings file for the WinUI build (separate from the WinForms config).</summary>
    public sealed class GlassSettings
    {
        public int Brightness { get; set; } = 42;

        /// <summary>mica = 拖动最稳；acrylic = 更通透但拖动时会闪。</summary>
        public string Material { get; set; } = "acrylic";

        private static string FilePath =>
            Path.Combine(TodoStore.DataRoot, "winui-settings.json");

        public static GlassSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonSerializer.Deserialize<GlassSettings>(File.ReadAllText(FilePath));
                    if (loaded != null)
                    {
                        loaded.Brightness = Math.Clamp(loaded.Brightness, 0, 100);
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("settings read failed: " + ex.Message);
            }

            return new GlassSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(TodoStore.DataRoot);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
            }
            catch (Exception ex)
            {
                Console.WriteLine("settings write failed: " + ex.Message);
            }
        }
    }
}
