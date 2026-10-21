using System.Numerics;

namespace StreetCorsa.Models.GameState
{
    /// <summary>
    /// A street as this save has it laid out (step 39): the pieces of the street kit its recipe put on it, drawn once from
    /// the save's seed and kept, so the player's street stays the same for the whole career
    /// </summary>
    public class StreetLayoutState
    {
        /// <summary>The street's full id (<c>Services.Street.StreetScene.Id</c>)</summary>
        public string Street { get; set; } = string.Empty;

        /// <summary>The seed it was laid out from</summary>
        public int Seed { get; set; }

        /// <summary>
        /// The fingerprint of the recipe it was laid out by (<c>Services.Street.StreetRecipe.Fingerprint</c>): another recipe
        /// lays it out again; empty in a layout of before it was kept
        /// </summary>
        public string Recipe { get; set; } = string.Empty;

        public List<PlacedPiece> Pieces { get; set; } = [];
    }

    /// <summary>A piece of the street kit where a street's layout puts it, in the street's space (metres, degrees)</summary>
    public class PlacedPiece
    {
        public PlacedPiece()
        {
        }

        public PlacedPiece(string piece, float x, float y, float z, float heading, float scale)
        {
            Piece = piece;
            X = x;
            Y = y;
            Z = z;
            Heading = heading;
            Scale = scale;
        }

        /// <summary>The piece's id (<c>Services.Street.StreetPiece.Id</c>)</summary>
        public string Piece { get; set; } = string.Empty;

        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        /// <summary>Which way its front faces, degrees about the vertical (0 facing +z, 90 facing +x)</summary>
        public float Heading { get; set; }

        public float Scale { get; set; } = 1f;

        /// <summary>The piece's own space into the street's: scaled, turned, put in its place</summary>
        [LiteDB.BsonIgnore]
        public Matrix4x4 Placement => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateRotationY(Heading * MathF.PI / 180f)
                                      * Matrix4x4.CreateTranslation(X, Y, Z);

        public override string ToString() => $"{Piece} at ({X:0.##}, {Y:0.##}, {Z:0.##}) facing {Heading:0.#}";
    }
}
