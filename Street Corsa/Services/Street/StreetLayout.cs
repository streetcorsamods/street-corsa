using System.Numerics;
using StreetCorsa.Models.GameState;

namespace StreetCorsa.Services.Street
{
    /// <summary>
    /// Lays a street out (step 39): its recipe (<see cref="StreetRecipe"/>) filled with the pieces of the kit, drawn from a
    /// seed, the same seed and kit giving the same street. The rows go first (the shops, the houses, the cars at the curbs),
    /// then the runs (the palms, the lamp posts, the poles, the meters), then the spots, each in the file's order. A piece
    /// goes only where its footprint (turned as it stands, scaled) keeps out of the clear areas and off what stands already,
    /// as far as its slot says (<see cref="SlotRules.Clear"/>, <see cref="SlotRules.Room"/>: a telephone pole the wires hang
    /// from goes where it is put); picks are weighted by the pieces' weights, times the recipe's for their tags
    /// (<see cref="StreetRecipe.WeightOf"/>). The daily slots (<see cref="SlotRules.Daily"/>: the cars at the curbs) are drawn
    /// apart, from a seed of the day (<see cref="Daily"/>): the rest of the street comes out the same whatever they hold, and
    /// keeps off none of their pieces; they keep off what stands before them as any slot does.
    /// </summary>
    public static class StreetLayout
    {
        // Footprints may touch: each is held this much in from its edges when it is checked against the others
        private const float Touch = 0.15f;

        // A row's slot moves on at least this far, and a row or a run has at most this many slots: a mod's recipe of a line
        // thousands of kilometres long, or a sliver of a piece, still lays out in a moment
        private const float MinStep = 0.5f;
        private const int MaxSlots = 2000;

        /// <summary>The street laid out from a seed, its daily slots left empty: what a save keeps</summary>
        public static List<PlacedPiece> Generate(StreetRecipe recipe, StreetKit kit, int seed) => Lay(recipe, kit, seed, null);

        /// <summary>
        /// The pieces of the recipe's daily slots for a day (<paramref name="day"/>, a seed of it), the rest of the street
        /// laid out from <paramref name="seed"/> as <see cref="Generate"/> lays it, so they keep off it
        /// </summary>
        public static List<PlacedPiece> Daily(StreetRecipe recipe, StreetKit kit, int seed, int day) => Lay(recipe, kit, seed, day);

        private static List<PlacedPiece> Lay(StreetRecipe recipe, StreetKit kit, int seed, int? day)
        {
            var random = new Random(seed);
            var today = day is { } d ? new Random(d) : null;
            var taken = new List<(Vector2 Min, Vector2 Max, bool Daily)>();
            var placed = new List<PlacedPiece>();

            // The slot's numbers: the day's for a daily slot, none when no day is laid
            Random? RandomOf(SlotRules rules) => rules.Daily ? today : random;

            bool Fits(StreetPiece piece, Vector2 middle, float heading, float scale, SlotRules rules)
            {
                var (min, max) = Box(piece, middle, heading, scale);
                (min, max) = (min + new Vector2(Touch), max - new Vector2(Touch));
                return !(rules.Clear && recipe.Clear.Any(a => a.Holds(piece) && Overlap(min, max, a.Min, a.Max)))
                       && !(rules.Room && taken.Any(t => (rules.Daily || !t.Daily) && Overlap(min, max, t.Min, t.Max)));
            }

            void Put(StreetPiece piece, Vector2 middle, SlotRules rules, float heading, float scale, float paint)
            {
                if (rules.Daily == (day != null))
                    placed.Add(new PlacedPiece(piece.Id, Round(middle.X), Round(rules.Y), Round(middle.Y), Round(heading), Round(scale), PaintOf(piece, paint)));
                var (min, max) = Box(piece, middle, heading, scale);
                taken.Add((min, max, rules.Daily));
            }

            foreach (var row in recipe.Rows)
                if (RandomOf(row.Rules) is { } numbers) Row(row, recipe, kit, numbers, Fits, Put);
            foreach (var run in recipe.Runs)
                if (RandomOf(run.Rules) is { } numbers) Run(run, recipe, kit, numbers, Fits, Put);
            foreach (var spot in recipe.Spots)
            {
                if (RandomOf(spot.Rules) is not { } numbers || Draw(spot.Rules, Choice(spot.Rules, spot.At), recipe, kit, numbers) is not { } pick) continue;
                var heading = spot.Heading + pick.Turn;
                if (Fits(pick.Piece, spot.At, heading, pick.Scale, spot.Rules)) Put(pick.Piece, spot.At, spot.Rules, heading, pick.Scale, pick.Paint);
            }

            return placed;
        }

        private delegate bool FitsTest(StreetPiece piece, Vector2 middle, float heading, float scale, SlotRules rules);

        private delegate void PutAction(StreetPiece piece, Vector2 middle, SlotRules rules, float heading, float scale, float paint);

        /// <summary>
        /// The numbers a slot is drawn: every slot draws all of them, whatever comes of it, so one slot staying empty (or
        /// its piece missing) never shifts the next one's draws
        /// </summary>
        private readonly record struct Draws(float Empty, float Pick, float Size, float Turn, float Gap, float Paint)
        {
            public static Draws Of(Random random) =>
                new(random.NextSingle(), random.NextSingle(), random.NextSingle(), random.NextSingle(), random.NextSingle(), random.NextSingle());
        }

        /// <summary>
        /// The colour a piece is painted in from a draw: as made or one of its paint's colours, each as likely; 0 for a
        /// piece with no paint
        /// </summary>
        private static int PaintOf(StreetPiece piece, float draw) =>
            piece.Paint is { Colours.Count: > 0 } paint ? Math.Min((int)(draw * (paint.Colours.Count + 1)), paint.Colours.Count) : 0;

        private static void Row(RecipeRow row, StreetRecipe recipe, StreetKit kit, Random random, FitsTest fits, PutAction put)
        {
            var rules = row.Rules;
            var along = Vector2.Normalize(row.To - row.From);
            var front = Facing(row.Heading);
            var length = Vector2.Distance(row.From, row.To);
            var at = 0f;
            for (var slot = 0; slot < MaxSlots && at < length; slot++)
            {
                var draws = Draws.Of(random);
                var first = slot < row.First.Count;
                var heading = row.Heading + Turn(rules, draws.Turn);
                var choice = first ? row.First[slot] : Choice(rules, row.From + along * at);
                var room = length - at;
                var pieces = PiecesOf(choice, recipe, kit).Where(p => Extent(p, heading, along, ScaleOf(rules, p, draws.Size)) <= room).ToList();
                if (pieces.Count == 0)
                {
                    // A first piece the kit has not, or that is too wide: the row goes on with its own
                    if (first) continue;
                    break;
                }

                var piece = Weighted(pieces, recipe, draws.Pick);
                var scale = ScaleOf(rules, piece, draws.Size);
                var width = Extent(piece, heading, along, scale);
                var deep = Extent(piece, heading, front, scale);
                var middle = row.From + along * (at + width / 2f) - (row.Middle ? Vector2.Zero : front * (deep / 2f));
                if (draws.Empty >= rules.Empty && fits(piece, middle, heading, scale, rules)) put(piece, middle, rules, heading, scale, draws.Paint);
                at += MathF.Max(width + Lerp(row.GapMin, row.GapMax, draws.Gap), MinStep);
            }
        }

        private static void Run(RecipeRun run, StreetRecipe recipe, StreetKit kit, Random random, FitsTest fits, PutAction put)
        {
            var length = Vector2.Distance(run.From, run.To);
            var along = length > 0f ? (run.To - run.From) / length : Vector2.Zero;
            for (var slot = 0; slot < MaxSlots; slot++)
            {
                // Counted, not added up: a long run's spots do not drift
                var s = slot * run.Every;
                if (s > length + 0.001f) break;

                var spot = run.From + along * s;
                if (Draw(run.Rules, Choice(run.Rules, spot), recipe, kit, random) is not { } pick) continue;
                var heading = run.Heading + pick.Turn;
                if (fits(pick.Piece, spot, heading, pick.Scale, run.Rules))
                {
                    put(pick.Piece, spot, run.Rules, heading, pick.Scale, pick.Paint);
                    continue;
                }

                var further = spot + along * run.Nudge;
                if (run.Nudge != 0f && Vector2.Distance(run.From, further) <= length + 0.001f && fits(pick.Piece, further, heading, pick.Scale, run.Rules))
                    put(pick.Piece, further, run.Rules, heading, pick.Scale, pick.Paint);
            }
        }

        /// <summary>What a slot at <paramref name="spot"/> takes: the far choice when there is one and the spot is far enough off</summary>
        private static PieceChoice Choice(SlotRules rules, Vector2 spot) =>
            rules.Far != null && Vector2.Distance(spot, rules.NearAt) >= rules.Near ? rules.Far : rules.Choice;

        /// <summary>A slot's piece, scale, turn and paint's draw, or none when it stays empty or the kit has nothing it takes</summary>
        private static (StreetPiece Piece, float Scale, float Turn, float Paint)? Draw(SlotRules rules, PieceChoice choice, StreetRecipe recipe, StreetKit kit, Random random)
        {
            var draws = Draws.Of(random);
            var pieces = PiecesOf(choice, recipe, kit);
            if (draws.Empty < rules.Empty || pieces.Count == 0) return null;

            var piece = Weighted(pieces, recipe, draws.Pick);
            return (piece, ScaleOf(rules, piece, draws.Size), Turn(rules, draws.Turn), draws.Paint);

        }

        /// <summary>A piece's scale in a slot: drawn in the slot's range, and to the slot's height when it gives one</summary>
        private static float ScaleOf(SlotRules rules, StreetPiece piece, float draw)
        {
            var size = Lerp(rules.ScaleMin, rules.ScaleMax, draw);
            return rules.Height is { } height ? size * height / piece.Height : size;
        }

        private static float Turn(SlotRules rules, float draw) =>
            rules.Turn <= 0f ? 0f : rules.Turn >= 360f ? draw * 360f : (draw * 2f - 1f) * rules.Turn;

        /// <summary>The pieces of the kit a choice takes, less those the recipe's weights leave out</summary>
        private static List<StreetPiece> PiecesOf(PieceChoice choice, StreetRecipe recipe, StreetKit kit) =>
            choice.In(kit).Where(p => !recipe.Excludes(p)).ToList();

        private static StreetPiece Weighted(IReadOnlyList<StreetPiece> pieces, StreetRecipe recipe, float pick)
        {
            var total = pieces.Sum(recipe.WeightOf);
            if (total <= 0f) return pieces[0];
            var at = pick * total;
            foreach (var piece in pieces)
            {
                at -= recipe.WeightOf(piece);
                if (at < 0f) return piece;
            }

            return pieces[^1];
        }

        /// <summary>Which way a heading faces on the ground (x, z): 0 is +z, 90 is +x</summary>
        public static Vector2 Facing(float heading)
        {
            var (sin, cos) = MathF.SinCos(heading * MathF.PI / 180f);
            return new Vector2(sin, cos);
        }

        /// <summary>How far a piece reaches along a way on the ground, turned to a heading and scaled</summary>
        public static float Extent(StreetPiece piece, float heading, Vector2 way, float scale)
        {
            // Its own x lies along (cos, -sin) once turned, its own z along (sin, cos)
            var (sin, cos) = MathF.SinCos(heading * MathF.PI / 180f);
            return (MathF.Abs(Vector2.Dot(new Vector2(cos, -sin), way)) * piece.Width + MathF.Abs(Vector2.Dot(new Vector2(sin, cos), way)) * piece.Depth) * scale;
        }

        /// <summary>The box on the ground (x, z) round a piece standing at a middle, turned to a heading and scaled</summary>
        public static (Vector2 Min, Vector2 Max) Box(StreetPiece piece, Vector2 middle, float heading, float scale)
        {
            var half = new Vector2(Extent(piece, heading, Vector2.UnitX, scale), Extent(piece, heading, Vector2.UnitY, scale)) / 2f;
            return (middle - half, middle + half);
        }

        private static bool Overlap(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax) =>
            aMin.X < bMax.X && bMin.X < aMax.X && aMin.Y < bMax.Y && bMin.Y < aMax.Y;

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        // Saved as written: three places are a millimetre
        private static float Round(float value) => MathF.Round(value, 3);

        /// <summary>
        /// The lamps the layout's lamp posts light at night (<see cref="StreetPiece.Lamp"/>, where each post stands), in the
        /// recipe's light; a piece the kit has not is left out
        /// </summary>
        public static IReadOnlyList<StreetLamp> Lamps(IEnumerable<PlacedPiece> layout, StreetKit kit, LampLight light) =>
            layout.Select(p => (Placed: p, Piece: kit.Find(p.Piece)))
                .Where(p => p.Piece?.Lamp != null)
                .Select(p => Vector3.Transform(p.Piece!.Lamp!.Value, p.Placed.Placement))
                .Select(at => new StreetLamp(at.X, at.Y, at.Z, light.Color, light.Brightness, light.Range))
                .ToList();
    }
}
