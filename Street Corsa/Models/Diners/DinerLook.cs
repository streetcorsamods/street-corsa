using StreetCorsa.Services.Street;

namespace StreetCorsa.Models.Diners
{
    /// <summary>
    /// What a diner wears on its street (<c>"look"</c> in diners.json): the look groups of the model it shows
    /// (<see cref="Show"/>, names without the LOOK_ prefix; none: the street's own), and its own sun, lights and glow over
    /// the street's. A light of the day it gives keeps the street's lamps at that hour, recoloured when the look gives a
    /// <see cref="LampTints">lamp colour</see> for it (the Hi-Way's sodium orange), and adds its own lamps (the Starlite's
    /// over its lot); one that gives no brightness (<see cref="LampsOnly"/>) keeps the street's light and only does that.
    /// A glow it gives goes over the street's material by material.
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
    }
}
