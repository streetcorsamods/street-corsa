using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace StreetCorsa.Screens.Newspaper
{
    /// <summary>
    /// The classifieds' columns: as many as the page's width holds at <see cref="MinColumnWidth"/>, each ad going into the
    /// shortest so far, ruled between. An ad keeps its place as more come, so a page added at the end never moves what is
    /// already read.
    /// </summary>
    public sealed class ClassifiedColumnsPanel : Panel
    {
        public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
            nameof(MinColumnWidth), typeof(double), typeof(ClassifiedColumnsPanel),
            new FrameworkPropertyMetadata(260.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty GutterProperty = DependencyProperty.Register(
            nameof(Gutter), typeof(double), typeof(ClassifiedColumnsPanel),
            new FrameworkPropertyMetadata(22.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty RuleProperty = DependencyProperty.Register(
            nameof(Rule), typeof(Brush), typeof(ClassifiedColumnsPanel),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>The narrowest a column may be</summary>
        public double MinColumnWidth
        {
            get => (double)GetValue(MinColumnWidthProperty);
            set => SetValue(MinColumnWidthProperty, value);
        }

        /// <summary>The room between two columns, the rule down its middle</summary>
        public double Gutter
        {
            get => (double)GetValue(GutterProperty);
            set => SetValue(GutterProperty, value);
        }

        /// <summary>The line between the columns; none when null</summary>
        public Brush? Rule
        {
            get => (Brush?)GetValue(RuleProperty);
            set => SetValue(RuleProperty, value);
        }

        private readonly List<Rect> _places = [];
        private int _columns = 1;
        private double _columnWidth;

        protected override Size MeasureOverride(Size availableSize)
        {
            var width = double.IsInfinity(availableSize.Width) ? MinColumnWidth : availableSize.Width;
            _columns = Math.Max(1, (int)((width + Gutter) / (MinColumnWidth + Gutter)));
            _columnWidth = Math.Max(0, (width - Gutter * (_columns - 1)) / _columns);

            var heights = new double[_columns];
            _places.Clear();
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(_columnWidth, double.PositiveInfinity));
                var column = Array.IndexOf(heights, heights.Min());
                _places.Add(new Rect(column * (_columnWidth + Gutter), heights[column], _columnWidth, child.DesiredSize.Height));
                heights[column] += child.DesiredSize.Height;
            }

            return new Size(width, heights.Max());
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            for (var i = 0; i < InternalChildren.Count && i < _places.Count; i++) InternalChildren[i].Arrange(_places[i]);
            return finalSize;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (Rule is not { } rule || _columns < 2) return;

            var pen = new Pen(rule, 1);
            pen.Freeze();
            for (var column = 1; column < _columns; column++)
            {
                // On the pixel, so the rule is one crisp line and not two grey ones
                var x = Math.Round(column * (_columnWidth + Gutter) - Gutter / 2) + 0.5;
                dc.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
            }
        }
    }
}
