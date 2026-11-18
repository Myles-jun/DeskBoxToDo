using System;

namespace DeskBoxToDo
{
    /// <summary>Single place for version strings shown in the UI and the exe resource.</summary>
    internal static class AppInfo
    {
        public const string ProductName = "DeskBoxToDo";
        public const string DisplayName = "选中即待办";
        public const string Version = "1.5.7";
        public const string Tagline = "选中一句话，直接变成待办";
        public const string DeskBoxDownloadUrl = "https://github.com/Tianyu199509/DeskBox/releases/latest";

        /// <summary>Bump when the first-run tour changes; older values replay the tour.</summary>
        public const int OnboardingVersion = 3;
    }
}
