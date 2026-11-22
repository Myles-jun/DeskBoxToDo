using System;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// One shared appearance store for every window: material, density and corner
    /// radius. Changing it re-applies to all open windows instead of only the next
    /// one that happens to open.
    /// </summary>
    internal static class Appearance
    {
        /// <summary>DWM's round radius (8 DIP) — the only one Windows anti-aliases.</summary>
        public const double DefaultRadius = 8;

        private static GlassSettings _settings;

        public static GlassSettings Current => _settings ??= GlassSettings.Load();

        public static void Reload()
        {
            _settings = GlassSettings.Load();
        }

        public static double ResolveRadius()
        {
            int radius = Current.CornerRadius;
            return radius >= 0 ? radius : DefaultRadius;
        }

        public static MaterialLadder.Kind ResolveMaterial() => MaterialLadder.Parse(Current.Material);

        public static int ResolveDepth() => Math.Clamp(Current.Brightness, 0, 100);

        public static void ApplyAll()
        {
            WindowChrome.ApplyToAll();
        }
    }
}
