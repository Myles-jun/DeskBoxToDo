using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DeskBoxToDo.WinUI
{
    /// <summary>
    /// Shared window skin (DeskBox 的 WidgetShell 对应物): rounded plate + one
    /// pixel hairline border + 44px title bar + body host, plus the eight resize
    /// hot zones. Every window in the app draws inside one of these, so the
    /// chrome stays identical while the page content differs.
    /// </summary>
    [ContentProperty(Name = nameof(Body))]
    public sealed partial class GlassSurface : UserControl
    {
        public GlassSurface()
        {
            InitializeComponent();
            Zones = new List<ResizeEdge>
            {
                ResizeLeft, ResizeRight, ResizeTop, ResizeBottom,
                ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight
            };
        }

        internal IReadOnlyList<ResizeEdge> Zones { get; }

        internal FrameworkElement TitleBarElement => TitleBar;

        internal FrameworkElement PlateElement => Plate;

        public object Body
        {
            get => BodyHost.Content;
            set => BodyHost.Content = value;
        }

        public string Title
        {
            get => TitleText.Text;
            set => TitleText.Text = value ?? string.Empty;
        }

        public string IconGlyph
        {
            get => TitleIcon.Glyph;
            set => TitleIcon.Glyph = value ?? string.Empty;
        }

        public Brush IconBrush
        {
            get => TitleIcon.Foreground;
            set => TitleIcon.Foreground = value;
        }

        public object TitleBarRight
        {
            get => RightHost.Content;
            set => RightHost.Content = value;
        }

        public bool ShowTitleBar
        {
            get => TitleBar.Visibility == Visibility.Visible;
            set
            {
                TitleBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                TitleRow.Height = value ? new GridLength(TitleBarHeight) : new GridLength(0);
            }
        }

        public double TitleBarHeight { get; set; } = 44;

        public double CornerRadius
        {
            get => Plate.CornerRadius.TopLeft;
            set => Plate.CornerRadius = new CornerRadius(value);
        }

        public Thickness BodyPadding
        {
            get => BodyHost.Padding;
            set => BodyHost.Padding = value;
        }

        public Color PlateColor
        {
            get => ((SolidColorBrush)Plate.Background).Color;
            set => Plate.Background = new SolidColorBrush(value);
        }

        public Color BorderColor
        {
            get => ((SolidColorBrush)Plate.BorderBrush).Color;
            set => Plate.BorderBrush = new SolidColorBrush(value);
        }
    }
}
