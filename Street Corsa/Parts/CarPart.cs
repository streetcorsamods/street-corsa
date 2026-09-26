using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Parts;

/// <summary>A part tree that sits on the car itself: the engine on slot 1, the running gear on the wheel slots</summary>
public sealed record CarPart(int CarSlot, InstalledPart Root);
