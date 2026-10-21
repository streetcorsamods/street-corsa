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
    /// from goes where it is put); picks are weighted by the pieces' weights.
    /// </summary>
    public static class StreetLayout
    {
        // Footprints may touch: each is held this much in from its edges when it is checked against the others
        private const float Touch = 0.15f;

        /// <param name="clear">Clear areas the street's own scene adds (the way the player's car drives in and out)</param>
        public static List<PlacedPiece> Generate(StreetRecipe recipe, StreetKit kit, int seed, IEnumerable<ClearArea>? clear = null)
        {
            var random = new Random(seed);
            var areas = recipe.Clear.Concat(clear ?? []).ToList();
            var taken = new List<(Vector2 Min, Vector2 Max)>();
            var placed = new List<PlacedPiece>();

            bool Fits(StreetPiece piece, Vector2 middle, float heading, float scale, SlotRules rules)
            {
                var (min, max) = Box(piece, middle, heading, scale);
                (min, max) = (min + new Vector2(Touch), max - new Vector2(Touch));
                return !(rules.Clear && areas.Any(a => a.Holds(piece) && Overlap(min, max, a.Min, a.Max)))
                       && !(rules.Room && taken.Any(t => Overlap(min, max, t.Min, t.Max)));
            }

            void Put(StreetPiece piece, Vector2 middle, float y, float heading, float scale)
            {
                placed.Add(new PlacedPiece(piece.Id, Round(middle.X), Round(y), Round(middle.Y), Round(heading), Round(scale)));
                taken.Add(Box(piece, middle, heading, scale));
            }

            foreach (var row in recipe.Rows) Row(row, kit, random, Fits, Put);
            foreach (var run in recipe.Runs) Run(run, kit, random, Fits, Put);
            foreach (var spot in recipe.Spots)
            {
                if (Draw(spot.Rules, spot.At, kit, random) is not { } pick) continue;
                var heading = spot.Heading + Turn(spot.Rules, random);
                if (Fits(pick.Piece, spot.At, heading, pick.Scale, spot.Rules)) Put(pick.Piece, spot.At, spot.Rules.Y, heading, pick.Scale);
            }

            return placed;
        }

        private delegate bool FitsTest(StreetPiece piece, Vector2 middle, float heading, float scale, SlotRules rules);

        private static void Row(RecipeRow row, StreetKit kit, Random random, FitsTest fits, Action<StreetPiece, Vector2, float, float, float> put)
        {
            var along = Vector2.Normalize(row.To - row.From);
            var length = Vector2.Distance(row.From, row.To);
            var front = Facing(row.Heading);
            var at = 0f;
            for (var slot = 0; ; slot++)
            {
                var choice = slot < row.First.Count ? row.First[slot] : row.Rules.Choice;
                var room = length - at;
                var pieces = choice.In(kit).Where(p => Extent(p, row.Heading, along, 1f) <= room).ToList();
                if (pieces.Count == 0)
                {
                    // A first piece the kit has not, or that is too wide: the row goes on with its own
                    if (slot < row.First.Count) continue;
                    break;
                }

                var piece = Weighted(pieces, random);
                var width = Extent(piece, row.Heading, along, 1f);
                var deep = Extent(piece, row.Heading, front, 1f);
                var middle = row.From + along * (at + width / 2f) - (row.Middle ? Vector2.Zero : front * (deep / 2f));
                var empty = random.NextSingle() < row.Rules.Empty;
                if (!empty && fits(piece, middle, row.Heading, 1f, row.Rules)) put(piece, middle, row.Rules.Y, row.Heading, 1f);
                at += width + Lerp(row.GapMin, row.GapMax, random.NextSingle());
            }
        }

        private static void Run(RecipeRun run, StreetKit kit, Random random, FitsTest fits, Action<StreetPiece, Vector2, float, float, float> put)
        {
            var length = Vector2.Distance(run.From, run.To);
            var along = length > 0f ? (run.To - run.From) / length : Vector2.Zero;
            for (var s = 0f; s <= length + 0.001f; s += run.Every)
            {
                var spot = run.From + along * s;
                if (Draw(run.Rules, spot, kit, random) is not { } pick) continue;
                var heading = run.Heading + Turn(run.Rules, random);
                if (fits(pick.Piece, spot, heading, pick.Scale, run.Rules))
                {
                    put(pick.Piece, spot, run.Rules.Y, heading, pick.Scale);
                    continue;
                }

                var further = spot + along * run.Nudge;
                if (run.Nudge != 0f && Vector2.Distance(run.From, further) <= length + 0.001f && fits(pick.Piece, further, heading, pick.Scale, run.Rules))
                    put(pick.Piece, further, run.Rules.Y, heading, pick.Scale);
            }
        }

        /// <summary>A slot's piece and scale, or none when it stays empty or the kit has nothing it takes</summary>
        private static (StreetPiece Piece, float Scale)? Draw(SlotRules rules, Vector2 spot, StreetKit kit, Random random)
        {
            // Drawn whatever comes of it, so one slot's outcome never shifts the next one's draws
            var empty = random.NextSingle() < rules.Empty;
            var pick = random.NextSingle();
            var size = Lerp(rules.ScaleMin, rules.ScaleMax, random.NextSingle());
            var far = rules.Far != null && Vector2.Distance(spot, rules.NearAt) >= rules.Near;
            var pieces = (far ? rules.Far! : rules.Choice).In(kit);
            if (empty || pieces.Count == 0) return null;

            var piece = Weighted(pieces, pick);
            return (piece, rules.Height is { } height ? size * height / piece.Height : size);
        }

        private static float Turn(SlotRules rules, Random random)
        {
            var turn = random.NextSingle();
            return rules.Turn <= 0f ? 0f : rules.Turn >= 360f ? turn * 360f : (turn * 2f - 1f) * rules.Turn;
        }

        private static StreetPiece Weighted(IReadOnlyList<StreetPiece> pieces, Random random) => Weighted(pieces, random.NextSingle());

        private static StreetPiece Weighted(IReadOnlyList<StreetPiece> pieces, float pick)
        {
            var total = pieces.Sum(p => p.Weight);
            if (total <= 0f) return pieces[0];
            var at = pick * total;
            foreach (var piece in pieces)
            {
                at -= piece.Weight;
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
