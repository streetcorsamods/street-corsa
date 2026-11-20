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

        /// <summary>
        /// How long the strip takes to glide somewhere: a page's worth this long, a longer way a little longer, never past
        /// the most
        /// </summary>
        private const double GlideSeconds = 0.55;
        private const double GlideSecondsMost = 0.9;
        private const double GlidePixelsPerExtraSecond = 4000;

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
        private bool _gliding;

        // The glide under way: where it set out from, when, how long it takes, and whether it set out already moving
        // (taken over half way by a new goal: eased out only, so it does not stop to start again)
        private double _glideFrom;
        private TimeSpan? _glideStart;
        private double _glideSeconds;
        private bool _glideMoving;
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

        /// <summary>
        /// The strip glides to <paramref name="offset"/> over a set time, eased in and out by the time gone (not by the
        /// frames: a slow frame does not slow it); a new goal on the way takes over from where it is, eased out
        /// </summary>
        private void GlideTo(double offset)
        {
            if (StripScroller is not { } scroller) return;
            var goal = Math.Clamp(offset, 0, Math.Max(0, scroller.ExtentWidth - scroller.ViewportWidth));
            var from = scroller.HorizontalOffset;
            if (Math.Abs(goal - from) < 0.5 && !_gliding) return;

            _glideMoving = _gliding;
            _stripGoal = goal;
            _glideFrom = from;
            _glideStart = null;
            _glideSeconds = Math.Min(GlideSecondsMost, GlideSeconds + Math.Abs(goal - from) / GlidePixelsPerExtraSecond);
            if (_gliding) return;
            _gliding = true;
            CompositionTarget.Rendering += OnGlideFrame;
        }

        private void OnGlideFrame(object? sender, EventArgs e)
        {
            if (StripScroller is not { } scroller || _stripGoal is not { } goal)
            {
                StopGlide();
                return;
            }

            // Rendering comes more than once a frame at times: only a new frame's time moves the strip
            var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
            _glideStart ??= now;
            var t = _glideSeconds <= 0 ? 1 : Math.Clamp((now - _glideStart.Value).TotalSeconds / _glideSeconds, 0, 1);
            var eased = _glideMoving ? EaseOut(t) : EaseInOut(t);
            scroller.ScrollToHorizontalOffset(_glideFrom + (goal - _glideFrom) * eased);
            if (t >= 1) StopGlide();
        }

        private static double EaseInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

        private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

        private void StopGlide()
        {
            if (!_gliding) return;
            _gliding = false;
            _stripGoal = null;
            CompositionTarget.Rendering -= OnGlideFrame;
        }

        // ----- the cards rise in -----

        /// <summary>
        /// The cards there when the strip is filled rise into place one after the other; a card that comes in as the strip
        /// glides is there already (made a page ahead), and anything moving on it then would only jolt the glide
        /// </summary>
        private void OnCardLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement card) return;
            var first = !_gliding && DateTime.UtcNow - _stripFilledAt < StaggerWindow && _staggered < MostStaggered;
            if (!first) return;
            var shift = new TranslateTransform(0, CardRiseFrom);
            card.RenderTransform = shift;
            var delay = TimeSpan.FromTicks(CardStagger.Ticks * _staggered++);
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
