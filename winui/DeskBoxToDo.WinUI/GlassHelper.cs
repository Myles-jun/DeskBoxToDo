using System;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Windows.UI;
using WinRT;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// One place for the glass recipe so every window looks identical.
    /// Dialogs used to build their own controller with slightly different numbers.
    /// </summary>
    internal static class GlassHelper
    {
        internal const byte TintRed = 24;
        internal const byte TintGreen = 28;
        internal const byte TintBlue = 38;

        internal static DesktopAcrylicController Attach(Window window)
        {
            try
            {
                if (!DesktopAcrylicController.IsSupported())
                {
                    return null;
                }

                var configuration = new SystemBackdropConfiguration
                {
                    IsInputActive = true,
                    Theme = SystemBackdropTheme.Dark
                };

                var acrylic = new DesktopAcrylicController
                {
                    Kind = DesktopAcrylicKind.Base,
                    TintColor = Color.FromArgb(255, TintRed, TintGreen, TintBlue),
                    FallbackColor = Color.FromArgb(255, TintRed, TintGreen, TintBlue),
                    TintOpacity = 0.32f,
                    LuminosityOpacity = 0.55f
                };

                acrylic.SetSystemBackdropConfiguration(configuration);
                acrylic.AddSystemBackdropTarget(
                    window.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                return acrylic;
            }
            catch (Exception ex)
            {
                Console.WriteLine("glass attach failed: " + ex.Message);
                return null;
            }
        }
    }
}
