using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Transparent strip along one window edge. UIElement.ProtectedCursor is
    /// protected, so a small subclass is the only way to get the native resize
    /// cursors in WinUI 3 (Border is sealed, ContentPresenter is not).
    /// </summary>
    public sealed class ResizeEdge : ContentPresenter
    {
        private string _direction = "h";

        public string Direction
        {
            get => _direction;
            set
            {
                _direction = value ?? "h";
                ProtectedCursor = InputSystemCursor.Create(ShapeFor(_direction));
            }
        }

        private static InputSystemCursorShape ShapeFor(string direction)
        {
            switch (direction)
            {
                case "v":
                    return InputSystemCursorShape.SizeNorthSouth;
                case "nwse":
                    return InputSystemCursorShape.SizeNorthwestSoutheast;
                case "nesw":
                    return InputSystemCursorShape.SizeNortheastSouthwest;
                default:
                    return InputSystemCursorShape.SizeWestEast;
            }
        }
    }
}
