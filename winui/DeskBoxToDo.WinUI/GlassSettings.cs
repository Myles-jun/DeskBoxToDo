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

        /// <summary>Last window size (physical px), restored on next launch.</summary>
        public int Width { get; set; } = 420;

        public int Height { get; set; } = 560;

        /// <summary>Last window position (physical px). 0,0 means "never moved".</summary>
        public int X { get; set; }

        public int Y { get; set; }

        /// <summary>normal | desktop | topmost</summary>
        public string Layer { get; set; } = "normal";

        /// <summary>Window corner radius in DIP; -1 = follow the design default.</summary>
        public int CornerRadius { get; set; } = -1;

        /// <summary>Surface opacity 0..100 (higher = more opaque).</summary>
        public int Opacity { get; set; } = 100;

        public uint HotkeyModifiers { get; set; } = 0x0001; // MOD_ALT

        public uint HotkeyKey { get; set; } = 0x51;        // VK_Q

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
            // Explorer, the tray shell and antivirus can briefly hold the file;
            // a couple of retries keeps a slider drag from losing its value.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(TodoStore.DataRoot);
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 2)
                    {
                        Console.WriteLine("settings write failed: " + ex.GetType().Name + " 0x" + ex.HResult.ToString("X8") + " " + ex.Message);
                        try
                        {
                            string probe = Path.Combine(TodoStore.DataRoot, "write-probe.txt");
                            File.WriteAllText(probe, DateTime.Now.ToString("o"));
                            Console.WriteLine("write probe ok: " + probe);
                        }
                        catch (Exception probeEx)
                        {
                            Console.WriteLine("write probe failed: " + probeEx.GetType().Name + " 0x" + probeEx.HResult.ToString("X8") + " " + probeEx.Message);
                        }
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(40);
                    }
                }
            }
        }
    }
}
