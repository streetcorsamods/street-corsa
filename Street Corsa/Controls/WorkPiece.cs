using StreetCorsa.Parts.Logic;

namespace StreetCorsa.Controls;

/// <summary>
/// The part a job of the parts view's "Work on it" is being done on, for the garage's 3D view
/// (<see cref="CarViewport3D.WorkPiece"/>): a loose one, a tree of its own laid on the workbench, or a mounted one,
/// known among the car's or the stand's parts by its id
/// </summary>
public sealed record WorkPiece(InstalledPart Part, bool OnBench);
