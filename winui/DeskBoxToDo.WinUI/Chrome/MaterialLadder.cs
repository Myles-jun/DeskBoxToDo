using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Windows.UI;
using WinRT;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// The material ladder DeskBox uses (see docs: 浜戞瘝 鈫?浜氬厠鍔?鈫?Win10 accent):
    /// Mica when the OS supports it, otherwise desktop acrylic, otherwise a DWM
    /// accent blur, otherwise a plain tint. Controllers are reused per window and
    /// only detached when switching families 鈥?recreating them leaks native
    /// composition memory and DWM handles.
    /// </summary>
    internal static class MaterialLadder
    {
        public enum Kind
        {
            Mica,
            MicaAlt,
            Acrylic,
            AcrylicThin,
            Solid
        }

        private sealed class State
        {
            public MicaController Mica;
            public DesktopAcrylicController Acrylic;
            public SystemBackdropConfiguration Configuration;
            public bool MicaAttached;
            public bool AcrylicAttached;
            public string Applied = "";
        }

        private static readonly ConditionalWeakTable<Window, State> States =
            new ConditionalWeakTable<Window, State>();

        public static bool IsWindows11 =>
            Environment.OSVersion.Version.Build >= 22000;

        public static Kind Parse(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "mica": return Kind.Mica;
                case "mica-alt": return Kind.MicaAlt;
                case "acrylic-thin": return Kind.AcrylicThin;
                case "acrylic": return Kind.Acrylic;
                case "solid": return Kind.Solid;
                default: return Kind.Acrylic;
            }
        }

        /// <summary>
        /// Applies the best available material for the requested kind.
        /// <paramref name="depth"/> is 0 (clear) .. 1 (dense) and drives both the
        /// tint opacity and the luminosity, exactly like the Brightness slider.
        /// Returns the name of the material that actually landed.
        /// </summary>
        public static string Apply(Window window, IntPtr hwnd, Kind kind, int depth, Color tint)
        {
            State state = States.GetOrCreateValue(window);
            double t = Math.Clamp(depth / 100.0, 0.0, 1.0);

            state.Configuration ??= new SystemBackdropConfiguration
            {
                IsInputActive = true,
                Theme = SystemBackdropTheme.Dark
            };

            bool wantMica = kind == Kind.Mica || kind == Kind.MicaAlt;
            bool wantAcrylic = kind == Kind.Acrylic || kind == Kind.AcrylicThin;

            if (wantMica && IsWindows11 && SafeSupported(() => MicaController.IsSupported()))
            {
                DetachAcrylic(state);
                if (state.Mica == null)
                {
                    state.Mica = new MicaController
                    {
                        Kind = kind == Kind.MicaAlt ? MicaKind.BaseAlt : MicaKind.Base
                    };
                }

                if (!state.MicaAttached)
                {
                    if (!state.Mica.AddSystemBackdropTarget(window.As<ICompositionSupportsSystemBackdrop>()))
                    {
                        DisposeMica(state);
                        return Apply(window, hwnd, Kind.Acrylic, depth, tint);
                    }

                    state.MicaAttached = true;
                    state.Mica.SetSystemBackdropConfiguration(state.Configuration);
                }

                state.Mica.Kind = kind == Kind.MicaAlt ? MicaKind.BaseAlt : MicaKind.Base;
                state.Mica.TintColor = tint;
                state.Mica.FallbackColor = tint;
                state.Mica.TintOpacity = (float)(0.34 + t * 0.48);
                state.Mica.LuminosityOpacity = (float)(0.42 + t * 0.38);
                state.Applied = kind == Kind.MicaAlt ? "mica-alt" : "mica";
                return state.Applied;
            }

            if (wantAcrylic && SafeSupported(() => DesktopAcrylicController.IsSupported()))
            {
                DetachMica(state);
                if (state.Acrylic == null || state.Acrylic.IsClosed)
                {
                    state.Acrylic = new DesktopAcrylicController();
                }

                if (!state.AcrylicAttached)
                {
                    if (!state.Acrylic.AddSystemBackdropTarget(window.As<ICompositionSupportsSystemBackdrop>()))
                    {
                        DisposeAcrylic(state);
                        return ApplyTint(window, hwnd, tint, t);
                    }

                    state.AcrylicAttached = true;
                    state.Acrylic.SetSystemBackdropConfiguration(state.Configuration);
                }

                state.Acrylic.Kind = kind == Kind.AcrylicThin
                    ? DesktopAcrylicKind.Thin
                    : DesktopAcrylicKind.Base;
                state.Acrylic.TintColor = tint;
                state.Acrylic.FallbackColor = tint;
                state.Acrylic.TintOpacity = (float)(0.30 + t * 0.50);
                state.Acrylic.LuminosityOpacity = (float)(0.38 + t * 0.42);
                state.Applied = kind == Kind.AcrylicThin ? "acrylic-thin" : "acrylic";
                return state.Applied;
            }

            return ApplyTint(window, hwnd, tint, t);
        }

        /// <summary>Win10 (and any fallback) accent blur: tint + DWM blur, no controller.</summary>
        private static string ApplyTint(Window window, IntPtr hwnd, Color tint, double t)
        {
            State state = States.GetOrCreateValue(window);
            DetachAcrylic(state);
            DetachMica(state);
            window.SystemBackdrop = null;

            byte alpha = (byte)Math.Clamp(0x66 + t * 0x66, 0, 0xFF);
            Color color = Color.FromArgb(alpha, tint.R, tint.G, tint.B);
            bool applied = AccentBlur.Apply(hwnd, color, t > 0.02);
            state.Applied = applied ? "accent" : "solid";
            return state.Applied;
        }

        public static void Dispose(Window window)
        {
            if (!States.TryGetValue(window, out State state))
            {
                return;
            }

            DisposeMica(state);
            DisposeAcrylic(state);
        }

        private static bool SafeSupported(Func<bool> probe)
        {
            try
            {
                return probe();
            }
            catch
            {
                return false;
            }
        }

        private static void DetachMica(State state)
        {
            if (state.Mica == null || !state.MicaAttached)
            {
                return;
            }

            try
            {
                state.Mica.RemoveAllSystemBackdropTargets();
            }
            catch
            {
            }

            state.MicaAttached = false;
        }

        private static void DetachAcrylic(State state)
        {
            if (state.Acrylic == null || !state.AcrylicAttached)
            {
                return;
            }

            try
            {
                state.Acrylic.RemoveAllSystemBackdropTargets();
            }
            catch
            {
            }

            state.AcrylicAttached = false;
        }

        private static void DisposeMica(State state)
        {
            DetachMica(state);
            try
            {
                state.Mica?.Dispose();
            }
            catch
            {
            }

            state.Mica = null;
        }

        private static void DisposeAcrylic(State state)
        {
            DetachAcrylic(state);
            try
            {
                state.Acrylic?.Dispose();
            }
            catch
            {
            }

            state.Acrylic = null;
        }
    }

    /// <summary>
    /// Win10's only way to get blurred glass on a plain Win32 window:
    /// SetWindowCompositionAttribute with ACCENT_ENABLE_ACRYLICBLURBEHIND.
    /// </summary>
    internal static class AccentBlur
    {
        private const int WcaAccentPolicy = 19;
        private const int AccentEnableAcrylicBlurBehind = 4;
        private const int AccentDisabled = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        public static bool Apply(IntPtr hwnd, Color color, bool enabled)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            var policy = new AccentPolicy
            {
                AccentState = enabled ? AccentEnableAcrylicBlurBehind : AccentDisabled,
                AccentFlags = enabled ? 2 : 0,
                GradientColor = enabled ? ToAbgr(color) : 0u,
                AnimationId = 0
            };

            int size = Marshal.SizeOf<AccentPolicy>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, buffer, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentPolicy,
                    Data = buffer,
                    SizeOfData = size
                };
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static uint ToAbgr(Color color) =>
            ((uint)color.A << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;
    }
}

