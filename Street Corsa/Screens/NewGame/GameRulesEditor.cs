using StreetCorsa.Models.GameState;
using StreetCorsa.Rules;
using StreetCorsa.Services.Text;
using StreetCorsa.ViewModels;

namespace StreetCorsa.Screens.NewGame
{
    /// <summary>
    /// The difficulty a new career starts on: one of the presets, whose figures the player may then adjust (the
    /// difficulty then reads Custom). The figures offered, and their ranges, are the setting's (<c>difficulty.adjust</c>);
    /// the multipliers are shown and set in percent.
    /// </summary>
    public class GameRulesEditor : ObservableObject
    {
        // The presets of the setting the career is played in
        private readonly DifficultyRules _presets;
        private GameRules _rules;

        /// <param name="presets">The presets offered and the figures to adjust; the content in play's when not given</param>
        public GameRulesEditor(DifficultyRules? presets = null)
        {
            _presets = presets ?? RuleBook.Installed.Difficulty;
            _rules = GameRules.For(Difficulty.Normal, _presets);
            Figures = FiguresOf(_presets);
            PickCommand = new RelayCommand<string>(name =>
            {
                if (Enum.TryParse<Difficulty>(name, out var difficulty) && difficulty != Difficulty.Custom) Pick(difficulty);
            });
        }

        /// <summary>Picks a preset: Easy, Normal or Hard</summary>
        public RelayCommand<string> PickCommand { get; }

        /// <summary>The figures the player may adjust, in the setting's order</summary>
        public IReadOnlyList<RuleFigure> Figures { get; }

        /// <summary>The rules as they stand, a copy for the new save</summary>
        public GameRules Build() => _rules.Copy();

        public Difficulty Difficulty => _rules.Difficulty;

        public bool IsEasy => Difficulty == Difficulty.Easy;
        public bool IsNormal => Difficulty == Difficulty.Normal;
        public bool IsHard => Difficulty == Difficulty.Hard;

        public string Summary => Difficulty switch
        {
            Difficulty.Easy => GameText.Say("difficulty.easy.summary"),
            Difficulty.Hard => GameText.Say("difficulty.hard.summary"),
            Difficulty.Custom => GameText.Say("difficulty.custom.summary"),
            _ => GameText.Say("difficulty.normal.summary")
        };

        /// <summary>The figure of that name, when the setting offers it</summary>
        public RuleFigure? Figure(string name) =>
            Figures.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public void Pick(Difficulty difficulty)
        {
            _rules = GameRules.For(difficulty, _presets);
            Changed();
        }

        private List<RuleFigure> FiguresOf(DifficultyRules presets) =>
            (presets.Adjust ?? []).Select(figure => new RuleFigure(figure, () => _rules, Change, presets)).ToList();

        internal static int Percent(double multiplier) => (int)Math.Round(multiplier * 100);

        /// <summary>A figure set by hand: once it differs from the preset's, the difficulty is Custom</summary>
        private void Change(Action<GameRules> set)
        {
            set(_rules);
            _rules.Difficulty = MatchingPreset() ?? Difficulty.Custom;
            Changed();
        }

        private void Changed()
        {
            OnPropertyChanged(string.Empty);
            foreach (var figure in Figures) figure.Refresh();
        }

        private Difficulty? MatchingPreset()
        {
            foreach (var preset in new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard })
            {
                if (RuleFigure.Same(GameRules.For(preset, _presets), _rules)) return preset;
            }

            return null;
        }
    }

    /// <summary>
    /// One figure of the difficulty the player may adjust: a number on a slider (a multiplier in percent, a modifier
    /// signed), the pink slips' frequency to pick, or a switch
    /// </summary>
    public sealed class RuleFigure : ObservableObject
    {
        private readonly Func<GameRules> _rules;
        private readonly Action<Action<GameRules>> _change;
        private readonly Accessor _accessor;

        /// <summary>How a figure is read and set on the rules, in the slider's units, and its words</summary>
        private sealed record Accessor(string Label, Func<GameRules, double> Get, Action<GameRules, double> Set, bool Signed = false, bool Percent = false);

        private static Accessor Multiplier(string label, Func<GameRules, double> get, Action<GameRules, double> set) =>
            new(label, r => GameRulesEditor.Percent(get(r)), (r, v) => set(r, v / 100.0), Percent: true);

        private static Accessor Switch(string label, Func<GameRules, bool> get, Action<GameRules, bool> set) =>
            new(label, r => get(r) ? 1 : 0, (r, v) => set(r, v != 0));

        /// <summary>Every figure of <see cref="AdjustableFigure.Known"/>, by name</summary>
        private static readonly Dictionary<string, Accessor> Accessors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["carPriceMultiplier"] = Multiplier("newgame.car_prices", r => r.CarPriceMultiplier, (r, v) => r.CarPriceMultiplier = v),
            ["partPriceMultiplier"] = Multiplier("newgame.part_prices", r => r.PartPriceMultiplier, (r, v) => r.PartPriceMultiplier = v),
            ["racePrizeMultiplier"] = Multiplier("newgame.prizes", r => r.RacePrizeMultiplier, (r, v) => r.RacePrizeMultiplier = v),
            ["startMoneyMultiplier"] = Multiplier("newgame.start_money", r => r.StartMoneyMultiplier, (r, v) => r.StartMoneyMultiplier = v),
            ["carWearMultiplier"] = Multiplier("newgame.wear", r => r.CarWearMultiplier, (r, v) => r.CarWearMultiplier = v),
            ["opponentSkillModifier"] = new("newgame.rival_skill", r => r.OpponentSkillModifier,
                (r, v) => r.OpponentSkillModifier = (int)Math.Round(v), Signed: true),
            ["opponentAggressionModifier"] = new("newgame.rival_aggression", r => r.OpponentAggressionModifier,
                (r, v) => r.OpponentAggressionModifier = (int)Math.Round(v), Signed: true),
            ["pinkSlipFrequency"] = new("newgame.pink_slips", r => (int)r.PinkSlipFrequency, (r, v) => r.PinkSlipFrequency = (PinkSlipFrequency)(int)v),
            ["raceSimulationEnabled"] = Switch("newgame.race_simulation", r => r.RaceSimulationEnabled, (r, v) => r.RaceSimulationEnabled = v),
            ["seasonalRacingEnabled"] = Switch("newgame.seasons", r => r.SeasonalRacingEnabled, (r, v) => r.SeasonalRacingEnabled = v),
            ["marketRefreshEnabled"] = Switch("newgame.market_refresh", r => r.MarketRefreshEnabled, (r, v) => r.MarketRefreshEnabled = v)
        };

        /// <summary>The same figures as the screen shows them (multipliers to the percent), every figure there is</summary>
        public static bool Same(GameRules a, GameRules b) => Accessors.Values.All(x => x.Get(a) == x.Get(b));

        /// <param name="figure">The figure and its range, as the setting offers it (checked: one the game has)</param>
        /// <param name="rules">The rules as they stand</param>
        /// <param name="change">Sets a figure by hand</param>
        /// <param name="presets">The presets, whose figures the slider always reaches, so it never moves one by itself</param>
        internal RuleFigure(AdjustableFigure figure, Func<GameRules> rules, Action<Action<GameRules>> change, DifficultyRules presets)
        {
            _rules = rules;
            _change = change;
            _accessor = Accessors[figure.Figure];
            Name = figure.Figure;
            Label = GameText.Say(_accessor.Label);

            var (min, max, step) = figure.Range;
            IsSlider = step > 0;
            IsChoice = !IsSlider && figure.Figure.Equals("pinkSlipFrequency", StringComparison.OrdinalIgnoreCase);
            IsSwitch = !IsSlider && !IsChoice;
            var presetValues = new[] { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard }
                .Select(d => _accessor.Get(GameRules.For(d, presets))).ToList();
            Min = Math.Min(min, presetValues.Min());
            Max = Math.Max(max, presetValues.Max());
            Step = step;

            // The slider stops on the setting's own steps from its min, and on the presets' figures besides: WPF would
            // step from the slider's Minimum, the lowest preset's, and miss every one of the setting's
            var ticks = new SortedSet<double>(presetValues.Where(v => v >= Min && v <= Max)) { Min, Max };
            if (IsSlider)
            {
                for (var i = 0; i <= MaxTicks && min + i * step <= max; i++) ticks.Add(Math.Round(min + i * step, 6));
                ticks.Add(max);
            }

            _ticks = [.. ticks];
            Ticks = new System.Windows.Media.DoubleCollection(_ticks);
            Ticks.Freeze();
        }

        // More stops than a slider can show apart: a range a mod got wrong
        private const int MaxTicks = 1000;

        private readonly List<double> _ticks;

        /// <summary>Where the slider stops: the setting's steps and the presets' figures, from <see cref="Min"/> to <see cref="Max"/></summary>
        public System.Windows.Media.DoubleCollection Ticks { get; }

        /// <summary>The figure's name in the rules: carPriceMultiplier...</summary>
        public string Name { get; }

        public string Label { get; }

        public bool IsSlider { get; }
        public bool IsChoice { get; }
        public bool IsSwitch { get; }

        public double Min { get; }
        public double Max { get; }
        public double Step { get; }

        /// <summary>A number figure, in the slider's units (percent for a multiplier)</summary>
        public double Value
        {
            get => _accessor.Get(_rules());
            set
            {
                // Never past the range nor between stops, whatever the slider was moved to
                if (IsSlider) value = Nearest(value);
                if (value != Value) _change(r => _accessor.Set(r, value));
            }
        }

        // The stop nearest to a value, within the range
        private double Nearest(double value)
        {
            if (!double.IsFinite(value)) return Value;
            var clamped = Math.Clamp(value, Min, Max);
            return _ticks.MinBy(t => Math.Abs(t - clamped));
        }

        /// <summary>The number as shown beside the slider: 120%, +3</summary>
        public string ValueText => _accessor.Percent ? $"{Value:0}%" : _accessor.Signed ? Value.ToString("+0;-0;0") : $"{Value:0}";

        /// <summary>A switch, on or off</summary>
        public bool IsOn
        {
            get => Value != 0;
            set => Value = value ? 1 : 0;
        }

        /// <summary>The pink slips' frequencies to pick from, by name in the player's language</summary>
        public IReadOnlyList<PinkSlipChoice> Choices { get; } =
        [
            new(PinkSlipFrequency.Low, GameText.Say("newgame.pink_slips.low")),
            new(PinkSlipFrequency.Medium, GameText.Say("newgame.pink_slips.medium")),
            new(PinkSlipFrequency.High, GameText.Say("newgame.pink_slips.high"))
        ];

        /// <summary>The frequency picked</summary>
        public PinkSlipFrequency Choice
        {
            get => (PinkSlipFrequency)(int)Value;
            set => Value = (int)value;
        }

        internal void Refresh() => OnPropertyChanged(string.Empty);
    }

    /// <summary>A pink slip frequency in the picker, by its name in the player's language</summary>
    public sealed record PinkSlipChoice(PinkSlipFrequency Value, string Name);
}
