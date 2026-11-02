using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using StreetCorsa.Styles;
using Brush = System.Windows.Media.Brush;
using Image = System.Windows.Controls.Image;
using UserControl = System.Windows.Controls.UserControl;

namespace StreetCorsa.Screens.MainMenu
{
    /// <summary>
    /// The title the game opens on. The window puts it up the moment it shows, so the player never looks at black while
    /// the game reads its content; the main screen then takes it over (<see cref="Claim"/>) and keeps it up until its
    /// showroom is ready. A first screen that does not take it over sees it fade away.
    /// <para>
    /// It plays out in the garage of the background (<c>backgrounds/init</c>): dark at first, its rows of lights
    /// flickering on one after the other (<c>intro/light_1</c>, <c>_2</c>.. over <c>intro/lights</c>), then the car's
    /// headlights (the background itself), then the logo. The light-up pictures are only used when they come from where
    /// the background does, so a mod's own background is never lit by the game's lamps; without them the background
    /// just fades in.
    /// </para>
    /// </summary>
    public partial class IntroView : UserControl
    {
        private bool _going;

        // The tach's sweep: 0 at -135 degrees, 8000 at +135
        private const double TachMax = 8000;
        private const double TachRedline = 6000;
        private const double TachIdle = 850;

        // How much of the window the logo takes, and how far down it sits: on the garage's back wall, over the car
        private const double LogoWidth = 0.44;
        private const double LogoTop = 0.30;

        public IntroView()
        {
            InitializeComponent();

            // The game's logo, or a mod's; the lettered title only when none reads
            if (ThemeImages.Load(ThemeImages.IntroLogo, 1600) is { } logo)
            {
                LogoImage.Source = logo;
                LogoImage.Visibility = Visibility.Visible;
                LogoText.Visibility = Visibility.Collapsed;
            }

            DrawTachFace();
            SizeChanged += (_, _) => PlaceLogo();
            Loaded += (_, _) => Play();
        }

        /// <summary>When it came up</summary>
        public DateTime ShownAt { get; } = DateTime.UtcNow;

        /// <summary>A screen took it over: it goes when that screen says</summary>
        public bool IsClaimed { get; private set; }

        /// <summary>How long the light-up takes, from the first light to the logo standing: the title stays at least this</summary>
        public static TimeSpan PlayTime { get; } = TimeSpan.FromSeconds(4.8);

        /// <summary>Takes the title over; false when it is already going</summary>
        public bool Claim()
        {
            if (_going) return false;
            IsClaimed = true;
            return true;
        }

        /// <summary>How long the title takes to fade away</summary>
        private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(900));

        /// <summary>How long the tach's blip takes before the title fades</summary>
        private static readonly TimeSpan BlipTime = TimeSpan.FromMilliseconds(320);

        /// <summary>Fades the title off what is behind it, and stops it taking the clicks at once; the tach blips first</summary>
        public void FadeOut()
        {
            if (_going) return;
            _going = true;
            IsHitTestVisible = false;

            // A blip of the throttle: up past the redline and back as it all fades
            var blip = new DoubleAnimationUsingKeyFrames();
            blip.KeyFrames.Add(new EasingDoubleKeyFrame(Angle(TachRedline + 900), KeyTime.FromTimeSpan(BlipTime), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            blip.KeyFrames.Add(new EasingDoubleKeyFrame(Angle(TachIdle), KeyTime.FromTimeSpan(BlipTime + TimeSpan.FromMilliseconds(700)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            NeedleTurn.BeginAnimation(RotateTransform.AngleProperty, blip);

            var fade = new DoubleAnimation(0, FadeDuration) { BeginTime = BlipTime };
            fade.Completed += (_, _) => Visibility = Visibility.Collapsed;
            BeginAnimation(OpacityProperty, fade);
        }

        #region The light-up

        private void Play()
        {
            var lit = ThemeImages.Load(ThemeImages.IntroBackground, 1920);
            var lightUp = LightUpPictures();
            var at = TimeSpan.Zero;

            if (lit != null && lightUp is { } pictures)
            {
                // The garage in the dark: only a glimpse of it, as eyes get used to it
                var dark = Layer(pictures.Lights);
                FadeTo(dark, 0.10, TimeSpan.FromSeconds(0.2), TimeSpan.FromSeconds(0.7));

                // Each row of tubes flickers to life, far ones first
                at = TimeSpan.FromSeconds(0.9);
                for (var i = 0; i < pictures.Rows.Count; i++)
                {
                    var row = Layer(pictures.Lights);
                    row.OpacityMask = new ImageBrush(pictures.Rows[i]) { Stretch = Stretch.Fill };
                    Flicker(row, at, i);
                    at += TimeSpan.FromSeconds(0.65);
                }

                // The light settles over the whole room, then the car's headlights come on
                FadeTo(Layer(pictures.Lights), 1, at, TimeSpan.FromSeconds(0.45));
                at += TimeSpan.FromSeconds(0.6);
                var headlights = Layer(lit);
                var on = new DoubleAnimationUsingKeyFrames { BeginTime = at };
                on.KeyFrames.Add(new LinearDoubleKeyFrame(0.7, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
                on.KeyFrames.Add(new LinearDoubleKeyFrame(0.45, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
                on.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320))));
                headlights.BeginAnimation(OpacityProperty, on);
                at += TimeSpan.FromSeconds(0.45);
            }
            else if (lit != null)
            {
                // A theme with no light-up: its background fades in, as the title always did
                FadeTo(Layer(lit), 1, TimeSpan.Zero, TimeSpan.FromSeconds(1));
                at = TimeSpan.FromSeconds(1);
            }

            // The logo lands, the line under it after, and the tach starts idling
            FadeTo(TitleShade, 1, at, TimeSpan.FromSeconds(0.9));
            FadeTo(Logo, 1, at, TimeSpan.FromSeconds(0.9));
            var settle = new DoubleAnimation(1.06, 1, new Duration(TimeSpan.FromSeconds(1.4)))
            {
                BeginTime = at,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
            LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
            FadeTo(PoweredBy, 1, at + TimeSpan.FromSeconds(0.6), TimeSpan.FromSeconds(0.8));
            StartTach(at);
        }

        /// <summary>The light-up pictures, when the theme has them from where its background comes; null when not</summary>
        private static (BitmapImage Lights, List<BitmapImage> Rows)? LightUpPictures()
        {
            var giver = ThemeImages.Giver(ThemeImages.IntroBackground);
            if (giver == null || ThemeImages.Giver(ThemeImages.IntroLights) != giver) return null;
            if (ThemeImages.Load(ThemeImages.IntroLights, 1920) is not { } lights) return null;

            var rows = new List<BitmapImage>();
            for (var n = 1; n <= ThemeImages.IntroLightRowsMost; n++)
            {
                var name = ThemeImages.IntroLightRow(n);
                if (ThemeImages.Giver(name) != giver || ThemeImages.Load(name) is not { } row) break;
                rows.Add(row);
            }
            return rows.Count > 0 ? (lights, rows) : null;
        }

        /// <summary>A picture laid over the scene so far, filling the window as the background does, unseen at first</summary>
        private Image Layer(ImageSource picture)
        {
            var image = new Image { Source = picture, Stretch = Stretch.UniformToFill, Opacity = 0, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Scene.Children.Add(image);
            return image;
        }

        private static void FadeTo(UIElement element, double to, TimeSpan begin, TimeSpan length) =>
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, new Duration(length)) { BeginTime = begin });

        // How each row's tubes catch: on, off, a stutter, on for good (time in ms, opacity); one pattern a row in turn
        private static readonly (int Ms, double Opacity)[][] Flickers =
        [
            [(0, 0), (40, 0.9), (110, 0.15), (170, 0.15), (210, 0.75), (300, 0.3), (360, 1)],
            [(0, 0), (60, 0.6), (100, 0), (260, 0), (300, 0.95), (350, 0.5), (420, 1)],
            [(0, 0), (30, 1), (90, 0.2), (130, 0.85), (190, 0.4), (230, 0.4), (290, 1)],
        ];

        private static void Flicker(UIElement row, TimeSpan begin, int index)
        {
            var flicker = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
            foreach (var (ms, opacity) in Flickers[index % Flickers.Length])
                flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(opacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
            row.BeginAnimation(OpacityProperty, flicker);
        }

        /// <summary>The logo sized and set down to the window: on the back wall, over the car</summary>
        private void PlaceLogo()
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            LogoImage.Width = Math.Min(1300, ActualWidth * LogoWidth);
            Logo.Margin = new Thickness(0, ActualHeight * LogoTop, 0, 0);
        }

        #endregion

        #region The tach

        private static double Angle(double rpm) => -135 + 270 * Math.Clamp(rpm / TachMax, 0, 1);

        /// <summary>The tach's ticks and figures, 0 to 8 thousand, the redline on</summary>
        private void DrawTachFace()
        {
            const double centre = 100;
            var light = (Brush)FindResource("IntroSubtitleBrush");
            var red = (Brush)FindResource("AccentPrimaryBrush");
            var font = (System.Windows.Media.FontFamily)FindResource("DisplayFont");

            for (var rpm = 0; rpm <= TachMax; rpm += 500)
            {
                var major = rpm % 1000 == 0;
                var turn = (Angle(rpm) - 90) * Math.PI / 180;
                var (outer, inner) = (86.0, major ? 72.0 : 78.0);
                var brush = rpm >= TachRedline ? red : light;
                Dial.Children.Insert(1, new Line
                {
                    X1 = centre + Math.Cos(turn) * inner, Y1 = centre + Math.Sin(turn) * inner,
                    X2 = centre + Math.Cos(turn) * outer, Y2 = centre + Math.Sin(turn) * outer,
                    Stroke = brush, StrokeThickness = major ? 4 : 2,
                });
                if (!major) continue;

                var figure = new TextBlock
                {
                    Text = (rpm / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FontFamily = font,
                    FontSize = 17,
                    Foreground = brush,
                    Width = 30,
                    TextAlignment = TextAlignment.Center,
                };
                Canvas.SetLeft(figure, centre + Math.Cos(turn) * 57 - 15);
                Canvas.SetTop(figure, centre + Math.Sin(turn) * 57 - 11);
                Dial.Children.Insert(1, figure);
            }
        }

        /// <summary>The engine catches with the lights: a flare on start-up, then a lumpy idle until the title goes</summary>
        private void StartTach(TimeSpan begin)
        {
            FadeTo(Tach, 1, begin, TimeSpan.FromSeconds(0.6));

            var start = new DoubleAnimationUsingKeyFrames { BeginTime = begin + TimeSpan.FromSeconds(0.3) };
            start.KeyFrames.Add(new EasingDoubleKeyFrame(Angle(2200), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            start.KeyFrames.Add(new EasingDoubleKeyFrame(Angle(TachIdle), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1100)), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            start.Completed += (_, _) =>
            {
                if (_going) return;
                // A cammed idle: the needle hunts a little either side
                var idle = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                idle.KeyFrames.Add(new EasingDoubleKeyFrame(Angle(TachIdle + 120), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)), new SineEase()));
                idle.KeyFrames.Add(new EasingDoubleKeyFrame(Angle(TachIdle - 60), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), new SineEase()));
                idle.KeyFrames.Add(new EasingDoubleKeyFrame(Angle(TachIdle + 60), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)), new SineEase()));
                idle.KeyFrames.Add(new EasingDoubleKeyFrame(Angle(TachIdle), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800)), new SineEase()));
                NeedleTurn.BeginAnimation(RotateTransform.AngleProperty, idle);
            };
            NeedleTurn.BeginAnimation(RotateTransform.AngleProperty, start);
        }

        #endregion
    }
}
