using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Media;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// One shared composition frame clock (the DeskBox pattern): every drag/resize
    /// callback registers here instead of running per pointer report, so window
    /// moves and size changes are coalesced to the display's real frame cadence.
    /// </summary>
    internal static class FrameClock
    {
        private static readonly Dictionary<long, Action> Callbacks = new Dictionary<long, Action>();
        private static long _nextId = 1;
        private static bool _hooked;

        public static IDisposable Register(Action callback)
        {
            long id = _nextId++;
            Callbacks[id] = callback;
            if (!_hooked)
            {
                CompositionTarget.Rendering += OnRendering;
                _hooked = true;
            }

            return new Registration(id);
        }

        private static void OnRendering(object sender, object args)
        {
            if (Callbacks.Count == 0)
            {
                return;
            }

            foreach (var entry in Callbacks.ToArray())
            {
                // A callback may unregister itself (or a later one) mid-frame.
                if (!Callbacks.ContainsKey(entry.Key))
                {
                    continue;
                }

                try
                {
                    entry.Value();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[FrameClock] callback failed: " + ex.Message);
                }
            }
        }

        private static void Unregister(long id)
        {
            Callbacks.Remove(id);
            if (Callbacks.Count == 0 && _hooked)
            {
                CompositionTarget.Rendering -= OnRendering;
                _hooked = false;
            }
        }

        private sealed class Registration : IDisposable
        {
            private readonly long _id;
            private bool _disposed;

            public Registration(long id)
            {
                _id = id;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Unregister(_id);
            }
        }
    }
}
