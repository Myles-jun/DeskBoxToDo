using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Assigns a UIElement's ProtectedCursor. The property is protected in the
    /// WinUI 3 projection, so it can only be reached through reflection.
    /// Cursors are cached per shape: resize borders re-assign on every hover and
    /// each uncached assignment would create another native cursor object.
    /// (Same approach DeskBox uses in Helpers/UiElementCursorHelper.cs.)
    /// </summary>
    internal static class UiElementCursorHelper
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<InputSystemCursorShape, InputSystemCursor> Cursors =
            new Dictionary<InputSystemCursorShape, InputSystemCursor>();

        private static PropertyInfo _property;

        internal static void SetCursor(UIElement element, InputSystemCursorShape shape)
        {
            if (element == null)
            {
                return;
            }

            InputSystemCursor cursor;
            lock (Gate)
            {
                if (!Cursors.TryGetValue(shape, out cursor))
                {
                    cursor = InputSystemCursor.Create(shape);
                    Cursors[shape] = cursor;
                }

                if (_property == null)
                {
                    _property = typeof(UIElement).GetProperty(
                        "ProtectedCursor",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }
            }

            _property?.SetValue(element, cursor);
        }

        internal static InputSystemCursorShape ShapeForDirection(string direction)
        {
            if (string.IsNullOrEmpty(direction))
            {
                return InputSystemCursorShape.Arrow;
            }

            bool horizontal = direction.Contains("Left") || direction.Contains("Right");
            bool vertical = direction.Contains("Top") || direction.Contains("Bottom");

            if (horizontal && vertical)
            {
                bool topLeftOrBottomRight =
                    (direction.Contains("Left") && direction.Contains("Top")) ||
                    (direction.Contains("Right") && direction.Contains("Bottom"));
                return topLeftOrBottomRight
                    ? InputSystemCursorShape.SizeNorthwestSoutheast
                    : InputSystemCursorShape.SizeNortheastSouthwest;
            }

            if (horizontal)
            {
                return InputSystemCursorShape.SizeWestEast;
            }

            return vertical ? InputSystemCursorShape.SizeNorthSouth : InputSystemCursorShape.Arrow;
        }
    }
}
