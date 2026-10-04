namespace StreetCorsa.Parts.Cars;

/// <summary>
/// How used a car looks, as its wear layer is drawn (the scenes, and AC through the race mode): each figure 0 (none) to 1
/// (all it can get). Equal when every figure is, so a scene that compares what it is shown sees the same car as the same.
/// </summary>
/// <param name="Rust">Rust through the paint: low on the body and round the wheel arches first, then all over</param>
/// <param name="Paint">Worn paint: chips, scratches, a top faded by the sun</param>
/// <param name="Cracks">A cracked windscreen</param>
/// <param name="Dirt">Dirt on the body (drawn in the game's scenes only: AC draws its own)</param>
/// <param name="Front">Body damage by zone, km/h of hits as AC keeps them (<see cref="Models.GameState.Car.BodyDamageKmh"/>):
/// scrapes and creases where the car was hit</param>
/// <param name="Seed">Which rust, which scratches: the car's own, the same every time it is drawn</param>
public sealed record CarLook(double Rust, double Paint, double Cracks, double Dirt, double Front, double Rear, double Left, double Right, int Seed)
{
    /// <summary>Less than this of everything shows nothing</summary>
    private const double Nothing = 0.005;

    /// <summary>Less than this of a hit, km/h, leaves nothing to draw</summary>
    private const double NoHitKmh = 2;

    /// <summary>True when there is nothing to draw: a clean car straight from the factory</summary>
    public bool IsClean => Rust < Nothing && Paint < Nothing && Cracks < Nothing && Dirt < Nothing
                           && Front < NoHitKmh && Rear < NoHitKmh && Left < NoHitKmh && Right < NoHitKmh;

    /// <summary>The same look without the dirt: what AC is given (it draws its own dirt)</summary>
    public CarLook WithoutDirt() => this with { Dirt = 0 };

    /// <summary>The look rounded to what can be seen, so a figure that moved a hair does not draw the car again</summary>
    public CarLook Rounded() => new(Math.Round(Rust, 2), Math.Round(Paint, 2), Math.Round(Cracks, 2), Math.Round(Dirt, 2),
        Math.Round(Front), Math.Round(Rear), Math.Round(Left), Math.Round(Right), Seed);
}
