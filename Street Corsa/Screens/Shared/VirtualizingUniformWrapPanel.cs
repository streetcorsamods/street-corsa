using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace StreetCorsa.Screens.Shared
{
    /// <summary>
    /// A wrap panel for items all of one size (cards) that only makes the cards in view, where a WrapPanel makes every
    /// card of the list at once. The rows are laid out as a centred WrapPanel lays them out: as many cards to a row as
    /// fit, the block of them centred, the last row starting at the left. The size of a card is taken from the first one.
    /// It scrolls by pixels, as a ScrollViewer does its content: put it in an ItemsControl whose template has a
    /// ScrollViewer with <c>CanContentScroll="True"</c>.
    /// </summary>
    public sealed class VirtualizingUniformWrapPanel : VirtualizingPanel, IScrollInfo
    {
        // A wheel notch scrolls what it scrolls in a ScrollViewer: three lines of 16 px
        private const double LineHeight = 16;
        private const double WheelLines = 3;

        private Size _itemSize = Size.Empty;
        private Size _extent;
        private Size _viewport;
        private Point _offset;
        private int _columns = 1;

        protected override Size MeasureOverride(Size availableSize)
        {
            var count = ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;

            // The children collection first: it hooks the generator up
            _ = InternalChildren;
            var generator = ItemContainerGenerator;

            // One card measured on its own tells the size of them all
            if (_itemSize.IsEmpty && count > 0)
            {
                Realize(generator, 0, 0, measure: false);
                var first = InternalChildren[0];
                first.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                _itemSize = first.DesiredSize;
            }

            if (count == 0 || _itemSize.Width <= 0 || _itemSize.Height <= 0)
            {
                CleanUp(generator, 0, -1);
                UpdateScrollInfo(availableSize, new Size(0, 0));
                return new Size(0, 0);
            }

            var width = double.IsInfinity(availableSize.Width) ? _itemSize.Width * count : availableSize.Width;
            _columns = Math.Max(1, (int)(width / _itemSize.Width));
            var rows = (count + _columns - 1) / _columns;
            var extent = new Size(width, rows * _itemSize.Height);
            var viewportHeight = double.IsInfinity(availableSize.Height) ? extent.Height : availableSize.Height;
            UpdateScrollInfo(new Size(width, viewportHeight), extent);

            var firstRow = (int)Math.Floor(_offset.Y / _itemSize.Height);
            var lastRow = (int)Math.Ceiling((_offset.Y + viewportHeight) / _itemSize.Height) - 1;
            var firstIndex = Math.Clamp(firstRow * _columns, 0, count - 1);
            var lastIndex = Math.Clamp((lastRow + 1) * _columns - 1, firstIndex, count - 1);

            Realize(generator, firstIndex, lastIndex, measure: true);
            CleanUp(generator, firstIndex, lastIndex);

            return new Size(width, Math.Min(viewportHeight, extent.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var generator = ItemContainerGenerator;
            var count = ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;
            var used = Math.Min(_columns, count) * _itemSize.Width;
            var left = Math.Max(0, (finalSize.Width - used) / 2);

            for (var i = 0; i < InternalChildren.Count; i++)
            {
                var index = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
                if (index < 0) continue;
                var row = index / _columns;
                var column = index % _columns;
                InternalChildren[i].Arrange(new Rect(
                    left + column * _itemSize.Width - _offset.X,
                    row * _itemSize.Height - _offset.Y,
                    _itemSize.Width,
                    _itemSize.Height));
            }

            return finalSize;
        }

        /// <summary>Makes the containers of the items from first to last, in order among the children</summary>
        private void Realize(IItemContainerGenerator generator, int first, int last, bool measure)
        {
            var start = generator.GeneratorPositionFromIndex(first);
            var childIndex = start.Offset == 0 ? start.Index : start.Index + 1;

            using (generator.StartAt(start, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
            {
                for (var index = first; index <= last; index++, childIndex++)
                {
                    if (generator.GenerateNext(out var isNew) is not UIElement child) break;

                    if (isNew)
                    {
                        if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                        else InsertInternalChild(childIndex, child);
                        generator.PrepareItemContainer(child);
                    }

                    if (measure) child.Measure(_itemSize);
                }
            }
        }

        /// <summary>Lets go of the containers of the items out of view</summary>
        private void CleanUp(IItemContainerGenerator generator, int first, int last)
        {
            for (var i = InternalChildren.Count - 1; i >= 0; i--)
            {
                var position = new GeneratorPosition(i, 0);
                var index = generator.IndexFromGeneratorPosition(position);
                if (index >= first && index <= last) continue;

                generator.Remove(position, 1);
                RemoveInternalChildRange(i, 1);
            }
        }

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            switch (args.Action)
            {
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Move:
                    RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                    break;
            }

            base.OnItemsChanged(sender, args);
            InvalidateMeasure();
        }

        private void UpdateScrollInfo(Size viewport, Size extent)
        {
            if (viewport == _viewport && extent == _extent) return;

            _viewport = viewport;
            _extent = extent;
            _offset.Y = Math.Clamp(_offset.Y, 0, Math.Max(0, _extent.Height - _viewport.Height));
            ScrollOwner?.InvalidateScrollInfo();
        }

        #region IScrollInfo

        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public double ExtentWidth => _extent.Width;
        public double ExtentHeight => _extent.Height;
        public double ViewportWidth => _viewport.Width;
        public double ViewportHeight => _viewport.Height;
        public double HorizontalOffset => _offset.X;
        public double VerticalOffset => _offset.Y;
        public ScrollViewer? ScrollOwner { get; set; }

        public void LineUp() => SetVerticalOffset(_offset.Y - LineHeight);
        public void LineDown() => SetVerticalOffset(_offset.Y + LineHeight);
        public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
        public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);
        public void MouseWheelUp() => SetVerticalOffset(_offset.Y - LineHeight * WheelLines);
        public void MouseWheelDown() => SetVerticalOffset(_offset.Y + LineHeight * WheelLines);

        // The cards wrap: nothing goes off to the side
        public void LineLeft() { }
        public void LineRight() { }
        public void PageLeft() { }
        public void PageRight() { }
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }
        public void SetHorizontalOffset(double offset) { }

        public void SetVerticalOffset(double offset)
        {
            offset = Math.Clamp(offset, 0, Math.Max(0, _extent.Height - _viewport.Height));
            if (offset == _offset.Y) return;

            _offset.Y = offset;
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }

        /// <summary>Scrolls a card that takes the focus (a button on it) into view</summary>
        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            if (rectangle.IsEmpty || visual == null || ReferenceEquals(visual, this) || !IsAncestorOf(visual)) return Rect.Empty;

            var bounds = visual.TransformToAncestor(this).TransformBounds(rectangle);
            if (bounds.Top < 0) SetVerticalOffset(_offset.Y + bounds.Top);
            else if (bounds.Bottom > _viewport.Height) SetVerticalOffset(_offset.Y + Math.Min(bounds.Top, bounds.Bottom - _viewport.Height));
            return rectangle;
        }

        #endregion
    }
}
