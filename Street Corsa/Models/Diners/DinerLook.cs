using StreetCorsa.Services.Street;

namespace StreetCorsa.Models.Diners
{
    /// <summary>
    /// What a diner wears on its street (<c>"look"</c> in diners.json): the look groups of the model it shows
    /// (<see cref="Show"/>, names without the LOOK_ prefix; none: the street's own), and its own sun, lights and glow over
    /// the street's. A light of the day it gives keeps the street's lamps at that hour, recoloured when the look gives a
    /// <see cref="LampTints">lamp colour</see> for it (the Hi-Way's sodium orange), and adds its own lamps (the Starlite's
    /// over its lot); one that gives no brightness (<see cref="LampsOnly"/>) keeps the street's light and only does that.
    /// A glow it gives goes over the street's material by material. On a street laid out with the street kit, its
    /// <see cref="Layout"/> is laid over the street's: its roof board, its billboard, its lot's props, its weights.
    /// </summary>
    public sealed record DinerLook(
        IReadOnlyList<string> Show,
        float[]? Sun,
        IReadOnlyDictionary<StreetLight, StreetLighting> Lights,
        IReadOnlyDictionary<StreetLight, byte[]> LampTints,
        IReadOnlyDictionary<StreetLight, IReadOnlyDictionary<string, float>> Glow)
    {
        /// <summary>The lights of the day the look gives no brightness for: the street's light at that hour, the lamps the look's</summary>
        public IReadOnlySet<StreetLight> LampsOnly { get; init; } = new HashSet<StreetLight>();

        /// <summary>
        /// What the look lays over its street's recipe (<c>"layout"</c>, step 39): the diner's own spots and slots, and the
        /// tags it weighs up or down; the street then is laid out for the diner, once a save (<see cref="StreetLayouts"/>).
        /// Null: the street as its own recipe lays it out.
        /// </summary>
        public StreetRecipe? Layout { get; init; }

        /// <summary>
        /// How the player's car comes to the diner and goes (<c>"approach"</c>): its own ways in, back out and out, and where
        /// the driver looks while it stands there, in place of the diner street's own (diner.json's), so a diner of the
        /// shared corner is driven to down another street, to another curb, its other side out of the window. Null: the
        /// street's own.
        /// </summary>
        public DinerApproach? Approach { get; init; }
    }

    /// <summary>A diner's own way to it and away (<see cref="DinerLook.Approach"/>), in the street's space as diner.json's are</summary>
    public sealed record DinerApproach(IReadOnlyList<Services.Station.GroundPoint> Arrive, IReadOnlyList<Services.Station.GroundPoint> BackOut,
        IReadOnlyList<Services.Station.GroundPoint> Leave, float LookWhileParked, float LookUpWhileParked);
}
