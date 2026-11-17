using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StreetCorsa.Screens.Garage
{
    /// <summary>
    /// The showroom layout's motion: the strip of cards glides (its arrows, the wheel, the picked card brought to the
    /// middle), the cards rise into place as they come, the sheet slides the picked car's figures in, and the strip and
    /// the sheet come up with the screen
    /// </summary>
    public partial class GarageScreenView
    {
        /// <summary>How much of the strip's width an arrow scrolls it</summary>
        private const double StripPage = 0.8;

        /// <summary>A card's width and the gap after it, as the strip's template has them</summary>
        private const double CardStride = 210;

        /// <summary>How quickly the strip catches up with where it is going: a share of what is left a second</summary>
        private const double StripGlideRate = 11;

        /// <summary>How far the wheel moves the strip for a notch, past the notch's own</summary>
        private const double WheelFactor = 1.4;

        /// <summary>The cards that come in this long after the strip is filled rise one after the other; later ones at once</summary>
        private static readonly TimeSpan StaggerWindow = TimeSpan.FromMilliseconds(900);

        private static readonly TimeSpan CardStagger = TimeSpan.FromMilliseconds(45);
        private static readonly TimeSpan CardRise = TimeSpan.FromMilliseconds(380);
        private const double CardRiseFrom = 26;
        private const int MostStaggered = 10;

        private static readonly TimeSpan SheetSlide = TimeSpan.FromMilliseconds(320);
        private const double SheetSlideFrom = 18;

        private ScrollViewer? _stripScroller;
        private double? _stripGoal;
        private TimeSpan _lastGlideTick;
        private bool _gliding;
        private DateTime _stripFilledAt = DateTime.MinValue;
        private int _staggered;
        private GarageScreenViewModel? _carSelect;

        private ScrollViewer? StripScroller => _stripScroller ??= FindScroller(StripList);

        /// <summary>Follows the view model's picked card and sheet, and the strip being filled again</summary>
        private void HookCarSelect()
        {
            DataContextChanged += (_, _) =>
            {
                if (_carSelect != null)
                {
                    _carSelect.PropertyChanged -= OnCarSelectChanged;
                    _carSelect.Strip.CollectionChanged -= OnStripRefilled;
                }

                _carSelect = DataContext as GarageScreenViewModel;
                if (_carSelect is not { IsCarSelect: true }) return;
                _carSelect.PropertyChanged += OnCarSelectChanged;
                _carSelect.Strip.CollectionChanged += OnStripRefilled;
                OnStripRefilled(null, null);
            };
            Unloaded += (_, _) => StopGlide();
        }

        private void OnStripRefilled(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs? e)
        {
            if (e is { Action: not System.Collections.Specialized.NotifyCollectionChangedAction.Reset }) return;
            _stripFilledAt = DateTime.UtcNow;
            _staggered = 0;
        }

        private void OnCarSelectChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(GarageScreenViewModel.Sheet):
                    SlideSheetIn();
                    break;
                case nameof(GarageScreenViewModel.PickedCard):
                    // After the layout has the card: a strip filled again lays its cards out first
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, CenterPicked);
                    break;
            }
        }

        // ----- the strip glides -----

        private void OnStripLeft(object sender, RoutedEventArgs e) => GlideBy(-1);

        private void OnStripRight(object sender, RoutedEventArgs e) => GlideBy(1);

        private void GlideBy(int direction)
        {
            if (StripScroller is not { } scroller) return;
            GlideTo((_stripGoal ?? scroller.HorizontalOffset) + direction * scroller.ViewportWidth * StripPage);
        }

        /// <summary>The wheel runs the strip along, as it has no rows to go down</summary>
        private void OnStripWheel(object sender, MouseWheelEventArgs e)
        {
            if (StripScroller is not { } scroller) return;
            GlideTo((_stripGoal ?? scroller.HorizontalOffset) - e.Delta * WheelFactor);
            e.Handled = true;
        }

        /// <summary>The picked card is brought to the middle of the strip, as near as its ends allow</summary>
        private void CenterPicked()
        {
            if (_carSelect?.PickedCard is not { } card || StripScroller is not { } scroller) return;
            var index = _carSelect.Strip.IndexOf(card);
            if (index < 0) return;
            GlideTo(index * CardStride - (scroller.ViewportWidth - CardStride) / 2);
        }

        /// <summary>The strip eased toward <paramref name="offset"/>, frame by frame</summary>
        private void GlideTo(double offset)
        {
            if (StripScroller is not { } scroller) return;
            _stripGoal = Math.Clamp(offset, 0, Math.Max(0, scroller.ExtentWidth - scroller.ViewportWidth));
            if (_gliding) return;
            _gliding = true;
            _lastGlideTick = TimeSpan.Zero;
            CompositionTarget.Rendering += OnGlideFrame;
        }

        private void OnGlideFrame(object? sender, EventArgs e)
        {
            if (StripScroller is not { } scroller || _stripGoal is not { } goal)
            {
                StopGlide();
                return;
            }

            var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
            var dt = _lastGlideTick == TimeSpan.Zero ? 1.0 / 60 : Math.Clamp((now - _lastGlideTick).TotalSeconds, 0, 0.1);
            _lastGlideTick = now;
            if (dt <= 0) return;

            var at = scroller.HorizontalOffset;
            var left = goal - at;
            if (Math.Abs(left) < 0.5)
            {
                scroller.ScrollToHorizontalOffset(goal);
                StopGlide();
                return;
            }

            scroller.ScrollToHorizontalOffset(at + left * (1 - Math.Exp(-StripGlideRate * dt)));
        }

        private void StopGlide()
        {
            if (!_gliding) return;
            _gliding = false;
            _stripGoal = null;
            CompositionTarget.Rendering -= OnGlideFrame;
        }

        // ----- the cards rise in -----

        /// <summary>
        /// A card come into the strip rises into place: the first ones after the strip is filled one after the other, a
        /// card brought in by scrolling at once
        /// </summary>
        private void OnCardLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement card) return;
            var shift = new TranslateTransform(0, CardRiseFrom);
            card.RenderTransform = shift;
            var first = DateTime.UtcNow - _stripFilledAt < StaggerWindow && _staggered < MostStaggered;
            var delay = first ? TimeSpan.FromTicks(CardStagger.Ticks * _staggered++) : TimeSpan.Zero;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            card.Opacity = 0;
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, CardRise) { BeginTime = delay, EasingFunction = ease });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(CardRiseFrom, 0, CardRise) { BeginTime = delay, EasingFunction = ease });
        }

        // ----- the sheet -----

        /// <summary>Another car's figures slide in on the sheet</summary>
        private void SlideSheetIn()
        {
            if (CarSheetContent.RenderTransform is not TranslateTransform shift || shift.IsFrozen)
                CarSheetContent.RenderTransform = shift = new TranslateTransform();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            CarSheetContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, SheetSlide) { EasingFunction = ease });
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(SheetSlideFrom, 0, SheetSlide) { EasingFunction = ease });
        }

        /// <summary>With the screen: the strip comes up from the foot and the sheet in from the right</summary>
        private void RevealCarSelect(TimeSpan duration)
        {
            if (_carSelect is not { IsCarSelect: true }) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var stripShift = new TranslateTransform(0, 70);
            CarStrip.RenderTransform = stripShift;
            stripShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(70, 0, duration + TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
            var sheetShift = new TranslateTransform(60, 0);
            CarSheetPanel.RenderTransform = sheetShift;
            sheetShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(60, 0, duration + TimeSpan.FromMilliseconds(350))
            {
                BeginTime = TimeSpan.FromMilliseconds(100),
                EasingFunction = ease
            });
            _stripFilledAt = DateTime.UtcNow;
            _staggered = 0;
        }

        private static ScrollViewer? FindScroller(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer scroller) return scroller;
                if (FindScroller(child) is { } found) return found;
            }

            return null;
        }
    }
}
