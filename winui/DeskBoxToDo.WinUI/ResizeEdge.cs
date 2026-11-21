using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Transparent edge strip with the native resize cursor. UIElement.ProtectedCursor
    /// is protected, so a small subclass is the only way WinUI lets us set it.
    /// EdgeBits: 1=left 2=right 4=top 8=bottom (combined for corners).
    /// </summary>
    public sealed class ResizeEdge : ContentPresenter
    {
        private int _edgeBits = 2;

        public int EdgeBits
        {
            get => _edgeBits;
            set
            {
                _edgeBits = value;
                ProtectedCursor = InputSystemCursor.Create(ShapeFor(value));
            }
        }

        private static InputSystemCursorShape ShapeFor(int bits)
        {
            bool horizontal = (bits & 3) != 0;
            bool vertical = (bits & 12) != 0;
            if (horizontal && vertical)
            {
                bool sameDiagonal = ((bits & 1) != 0 && (bits & 4) != 0) ||
                                    ((bits & 2) != 0 && (bits & 8) != 0);
                return sameDiagonal
                    ? InputSystemCursorShape.SizeNorthwestSoutheast
                    : InputSystemCursorShape.SizeNortheastSouthwest;
            }

            return horizontal ? InputSystemCursorShape.SizeWestEast : InputSystemCursorShape.SizeNorthSouth;
        }
    }
}
