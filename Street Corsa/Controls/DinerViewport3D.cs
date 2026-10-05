using System.IO;
using System.Reflection;
using System.Windows;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Controls.Street;
using StreetCorsa.Looks;
using StreetCorsa.Services.Catalog;
using StreetCorsa.Services.Diner;
using StreetCorsa.Services.Scripting;
using CarSlot = AcTools.Render.Kn5SpecificForward.ForwardKn5ObjectRenderer.CarSlot;
using Sx = SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// The diner's street, seen from the driver's seat of the player's car: it comes down Main Street, turns into the
/// diner's lot and parks nose first in its bay, the diner ahead; it backs out and drives off again
/// (<see cref="DriveInViewport3D"/>). The racers who are in tonight have parked their cars in the bays along the diner's
/// front, one a bay in the street's order, as many as fit in memory; they come after the player's car, one by one.
/// </summary>
public class DinerViewport3D : DriveInViewport3D
{
    // The racers' cars together may take this much of the cars' files (the lot's budget, for a few cars fewer)
    private const long ParkedByteBudget = 900L * 1024 * 1024;

    private readonly List<CarSlot> _parkedSlots = [];
    private int _parkedLoad;

    public DinerViewport3D()
        : base("Diner")
    {
    }

    protected override string ViewportName => "Diner viewport";

    protected override string ArriveTrigger => ScriptTriggers.DinerArrive;

    protected override string StandTrigger => ScriptTriggers.DinerParked;

    protected override string LeaveTrigger => ScriptTriggers.DinerLeave;

    public static readonly DependencyProperty ParkedCarsProperty = DependencyProperty.Register(
        nameof(ParkedCars), typeof(IReadOnlyList<StreetCar>), typeof(DinerViewport3D),
        new PropertyMetadata(null, (d, _) => ((DinerViewport3D)d).OnParkedCarsChanged()));

    /// <summary>The cars of the racers who are in, in the order they take the bays</summary>
    public IReadOnlyList<StreetCar>? ParkedCars
    {
        get => (IReadOnlyList<StreetCar>?)GetValue(ParkedCarsProperty);
        set => SetValue(ParkedCarsProperty, value);
    }

    protected override void OnSceneUp(GarageRenderer renderer) => _ = ParkAsync(renderer, ++_parkedLoad);

    private void OnParkedCarsChanged()
    {
        if (Renderer is { } renderer) _ = ParkAsync(renderer, ++_parkedLoad);
    }

    protected override void OnRendererDisposed()
    {
        base.OnRendererDisposed();
        _parkedLoad++;
        _parkedSlots.Clear();
    }

    /// <summary>
    /// The racers' cars into the bays, the ones there before taken away first. A car that will not load leaves its bay
    /// empty; a later load (another day's racers) stops this one.
    /// </summary>
    private async Task ParkAsync(GarageRenderer renderer, int load)
    {
        foreach (var slot in _parkedSlots) renderer.RemoveSlot(slot);
        _parkedSlots.Clear();
        Invalidate();

        if (Scene is not DinerScene scene || ParkedCars is not { Count: > 0 } cars) return;

        // Parked, their lights are off: at night the renderer would read (or guess) every car's lights, a long stall for
        // each; the player's car has its own already
        renderer.LoadCarLights = false;
        renderer.TryToGuessCarLights = false;

        var budget = ParkedByteBudget;
        foreach (var (car, bay) in cars.Zip(scene.Bays))
        {
            if (load != _parkedLoad || Renderer != renderer || !IsLoaded) return;
            if (!Directory.Exists(car.Directory)) continue;

            var bytes = CarModelFiles.EstimateBytes(car.Directory);
            if (bytes > budget)
            {
                Logger.Information("The diner's lot is full for memory: {Car} ({Mb} MB) is not parked", Path.GetFileName(car.Directory), bytes / (1024 * 1024));
                continue;
            }

            budget -= bytes;
            try
            {
                var slot = renderer.AddCar(null);
                _parkedSlots.Add(slot);

                // A slot hands a matrix set while it was empty to the car it is given: set before and after
                var standing = Standing(car, bay);
                slot.LocalMatrix = standing;
                await TrackDeviceWork(slot.SetCarAsync(CarDescription.FromDirectory(car.Directory), SkinOrDefault(car.Directory, car.Skin), RendererToken));
                if (load != _parkedLoad || Renderer != renderer || !IsLoaded) return;
                await ShownPartsOnCar.SetAsync(slot.CarNode, car.Shown, WearLayer.LotPicture);
                if (load != _parkedLoad || Renderer != renderer || !IsLoaded) return;

                slot.LocalMatrix = standing;
                slot.ResetCarBoundingBox();
                Invalidate();
            }
            catch (Exception ex) when (!IsDeviceLost(ex))
            {
                Logger.Error(ex, "Could not park {Car} at the diner", Path.GetFileName(car.Directory));
            }
        }

        // The renderer only watches its main slot: the parked cars' shadows are asked for once they are all in
        FocusShadows(renderer);
        renderer.RefreshShadows();
        Invalidate();
    }

    // The renderer's sun shadows reach about 23 m round the middle of the main car's box, the sharpest 10 m: the racers'
    // cars stand 20 m and more from the player's bay. Its box is a private field and ShadowsPosition is not virtual, so
    // while the player's car is near the lot the box is told to take in the bays too, and the shadows centre between them
    private static readonly FieldInfo? MainCarBox =
        typeof(CarSlot).GetField("_carBoundingBox", BindingFlags.NonPublic | BindingFlags.Instance);

    // How near the lot the player's car is for the shadows to take in the bays, metres from their middle
    private const float NearTheLot = 30f;

    protected override void FocusShadows(GarageRenderer renderer)
    {
        base.FocusShadows(renderer);
        if (MainCarBox == null || Scene is not DinerScene scene || _parkedSlots.Count == 0) return;

        var bays = scene.Bays.Take(_parkedSlots.Count).ToList();
        var car = CarPosition;
        var (x, z) = (bays.Average(b => b.X), bays.Average(b => b.Z));
        if (MathF.Abs(car.X - x) + MathF.Abs(car.Z - z) > NearTheLot) return;

        var min = new Sx.Vector3(car.X - 2.5f, car.Y, car.Z - 2.5f);
        var max = new Sx.Vector3(car.X + 2.5f, car.Y + 1.5f, car.Z + 2.5f);
        foreach (var bay in bays)
        {
            min = Sx.Vector3.Minimize(min, new Sx.Vector3(bay.X - 2.5f, min.Y, bay.Z - 2.5f));
            max = Sx.Vector3.Maximize(max, new Sx.Vector3(bay.X + 2.5f, max.Y, bay.Z + 2.5f));
        }

        MainCarBox.SetValue(renderer.MainSlot, new Sx.BoundingBox(min, max));
    }

    /// <summary>Where a car stands in a bay: facing the way the bay does, on the lot, sitting as AC has it sit</summary>
    private Sx.Matrix Standing(StreetCar car, DinerBay bay)
    {
        var lift = SceneLights.FrameOf(car.Directory).Lift;
        return Sx.Matrix.RotationY(bay.Heading) * Sx.Matrix.Translation(bay.X, GroundAt(bay.X, bay.Z) + lift, bay.Z);
    }
}
