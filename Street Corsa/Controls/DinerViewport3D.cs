using System.IO;
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

    // A slot for each bay, kept for the next car parked there: the renderer cannot take a slot out of the scene cleanly
    // (LotViewport3D); what stands in each bay, null where it is empty
    private readonly List<CarSlot?> _baySlots = [];
    private readonly List<StreetCar?> _parked = [];
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

    // Before the scene is up the racers wait: their cars come once the street and the player's car are in (OnSceneUp)
    private void OnParkedCarsChanged()
    {
        if (IsSceneUp && Renderer is { } renderer) _ = ParkAsync(renderer, ++_parkedLoad);
    }

    protected override void OnRendererDisposed()
    {
        base.OnRendererDisposed();
        _parkedLoad++;
        _baySlots.Clear();
        _parked.Clear();
    }

    /// <summary>
    /// The racers' cars into the bays, a car already in its bay left there and the bays nobody is in now emptied. A car
    /// that will not load leaves its bay empty; a later load (another day's racers) stops this one.
    /// </summary>
    private async Task ParkAsync(GarageRenderer renderer, int load)
    {
        if (Scene is not DinerScene scene) return;
        var cars = ParkedCars ?? [];

        // Parked, their lights are off: at night the renderer would read (or guess) every car's lights, a long stall for
        // each; the player's car has its own already
        renderer.LoadCarLights = false;
        renderer.TryToGuessCarLights = false;

        var budget = ParkedByteBudget;
        var count = Math.Min(cars.Count, scene.Bays.Count);
        for (var i = 0; i < count; i++)
        {
            if (load != _parkedLoad || Renderer != renderer || !IsLoaded) return;
            var car = cars[i];

            var bytes = Directory.Exists(car.Directory) ? CarModelFiles.EstimateBytes(car.Directory) : -1;
            if (bytes < 0 || bytes > budget)
            {
                if (bytes > budget)
                    Logger.Information("The diner's lot is full for memory: {Car} ({Mb} MB) is not parked", Path.GetFileName(car.Directory), bytes / (1024 * 1024));
                await ClearBayAsync(i);
                continue;
            }

            budget -= bytes;
            if (i < _parked.Count && _parked[i] == car) continue;

            try
            {
                while (_baySlots.Count <= i) _baySlots.Add(null);
                while (_parked.Count <= i) _parked.Add(null);
                var slot = _baySlots[i] ??= renderer.AddCar(null);
                _parked[i] = null;

                // A slot hands a matrix set while it was empty to the car it is given: set before and after
                var standing = Standing(car, scene.Bays[i]);
                slot.LocalMatrix = standing;
                await TrackDeviceWork(slot.SetCarAsync(CarDescription.FromDirectory(car.Directory), SkinOrDefault(car.Directory, car.Skin), RendererToken));
                if (load != _parkedLoad || Renderer != renderer || !IsLoaded) return;
                await ShownPartsOnCar.SetAsync(slot.CarNode, car.Shown, WearLayer.LotPicture);
                if (load != _parkedLoad || Renderer != renderer || !IsLoaded) return;

                slot.LocalMatrix = standing;
                slot.ResetCarBoundingBox();
                _parked[i] = car;
                Invalidate();
            }
            catch (Exception ex) when (!IsDeviceLost(ex))
            {
                Logger.Error(ex, "Could not park {Car} at the diner", Path.GetFileName(car.Directory));
            }
        }

        // Nobody in the bays past the racers who are in
        for (var i = count; i < _baySlots.Count; i++)
        {
            if (load != _parkedLoad || Renderer != renderer || !IsLoaded) return;
            await ClearBayAsync(i);
        }

        // The renderer only watches its main slot: the parked cars' shadows are asked for once they are all in
        if (load != _parkedLoad || Renderer != renderer) return;
        FocusShadows(renderer);
        renderer.RefreshShadows();
        Invalidate();
    }

    /// <summary>A bay emptied, its slot kept for the next car parked there</summary>
    private async Task ClearBayAsync(int bay)
    {
        if (bay < _parked.Count) _parked[bay] = null;
        if (bay >= _baySlots.Count || _baySlots[bay] is not { CarNode: not null } slot) return;

        try
        {
            await TrackDeviceWork(slot.SetCarAsync(null));
        }
        catch (Exception ex) when (!IsDeviceLost(ex))
        {
            Logger.Error(ex, "Could not take a car out of the diner's bay {Bay}", bay);
        }

        Invalidate();
    }

    // The renderer's sun shadows reach about 23 m round their centre, the sharpest 10 m: the racers' cars stand 20 m and
    // more from the player's bay. While the player's car is near the lot they centre between it and the bays taken
    // (GarageRenderer.ShadowsCentre), the main car's box, its reflections and its light left where it is
    private const float NearTheLot = 30f;

    protected override void FocusShadows(GarageRenderer renderer)
    {
        base.FocusShadows(renderer);
        renderer.ShadowsCentre = LotShadowsCentre();
    }

    /// <summary>The middle of the player's car and the bays with a car in them, while the car is near them; else null</summary>
    private Sx.Vector3? LotShadowsCentre()
    {
        if (Scene is not DinerScene scene) return null;

        var bays = scene.Bays.Where((_, i) => i < _parked.Count && _parked[i] != null).ToList();
        if (bays.Count == 0) return null;

        var car = CarPosition;
        var (x, z) = (bays.Average(b => b.X), bays.Average(b => b.Z));
        if (MathF.Abs(car.X - x) + MathF.Abs(car.Z - z) > NearTheLot) return null;

        var min = new Sx.Vector3(car.X - 2.5f, car.Y, car.Z - 2.5f);
        var max = new Sx.Vector3(car.X + 2.5f, car.Y + 1.5f, car.Z + 2.5f);
        foreach (var bay in bays)
        {
            min = Sx.Vector3.Minimize(min, new Sx.Vector3(bay.X - 2.5f, min.Y, bay.Z - 2.5f));
            max = Sx.Vector3.Maximize(max, new Sx.Vector3(bay.X + 2.5f, max.Y, bay.Z + 2.5f));
        }

        return (min + max) / 2f;
    }

    /// <summary>Where a car stands in a bay: facing the way the bay does, on the lot, sitting as AC has it sit</summary>
    private Sx.Matrix Standing(StreetCar car, DinerBay bay)
    {
        var lift = SceneLights.FrameOf(car.Directory).Lift;
        return Sx.Matrix.RotationY(bay.Heading) * Sx.Matrix.Translation(bay.X, GroundAt(bay.X, bay.Z) + lift, bay.Z);
    }
}
