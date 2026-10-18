using System.IO;
using System.Windows;
using System.Windows.Input;
using AcTools.Render.Kn5Specific.Objects;
using StreetCorsa.Logging;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Cars;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Services.Text;
using static StreetCorsa.Helpers.Matrices;

namespace StreetCorsa.Controls;

/// <summary>
/// The placement mode (nudging a part's slot, or the whole engine, and writing the move down), the places shown for a
/// loose part and the selected part
/// </summary>
public partial class CarViewport3D
{
    #region Placement mode

    /// <summary>What a nudge in placement mode moves: the part's own mounting slot, or the slot it sits on</summary>
    public enum PlacementTarget { Part, Pad }

    /// <summary>Told what the placement mode is doing, for the screen to show; null when the mode is off</summary>
    public event Action<string?>? PlacementChanged;

    private InstalledPart? _placing;
    private PlacementTarget _placementTarget;

    public bool IsPlacing => _placing != null;

    /// <summary>
    /// Enters placement mode on the selected part: nudges move it (or the pad it sits on) and are written to the
    /// catalog's slot shifts; on the engine block in the car, they move the whole engine and are kept for the car
    /// (<see cref="EngineMoved"/>). Leaves the mode when there is no selected part or it is already on.
    /// </summary>
    public void TogglePlacement()
    {
        // The engine block itself, in the car: moved with everything on it, kept per car (EngineMoved)
        if (_placing == null && SelectedPart is { Parent: null } block && ReferenceEquals(block, Engine) && !StandMode && PartsCatalog != null)
        {
            _placing = block;
            ReportPlacement();
            return;
        }

        if (_placing != null || SelectedPart is not { Parent: not null } part || PartsCatalog == null)
        {
            _placing = null;
            PlacementChanged?.Invoke(null);
            return;
        }

        // A part's own slot is the part's wherever it goes: nudging it moves every copy, the two carburettors of a
        // dual quad along with each other. One of several on the same parent is placed by its pad, which is its own
        _placing = part;
        _placementTarget = IsOneOfSeveral(part) ? PlacementTarget.Pad : PlacementTarget.Part;
        ReportPlacement();
    }

    /// <summary>
    /// Keeps placement on the selected part: another part picked is placed from then on, the same part in a tree made
    /// anew (known by its id) goes on being placed; nothing placeable selected ends the mode
    /// </summary>
    private void FollowSelection()
    {
        if (_placing == null) return;

        var selected = SelectedPart;
        var next = selected == null ? null
            : _partWorlds.ContainsKey(selected) ? selected
            : selected.InstanceId == Guid.Empty ? null
            : _partWorlds.Keys.FirstOrDefault(p => p.InstanceId == selected.InstanceId);
        if (next == null || !(next.Parent != null || (ReferenceEquals(next, Engine) && !StandMode)))
        {
            _placing = null;
            PlacementChanged?.Invoke(null);
            return;
        }

        var samePart = ReferenceEquals(next, _placing) || (next.InstanceId != Guid.Empty && next.InstanceId == _placing.InstanceId);
        _placing = next;
        if (!samePart && next.Parent != null) _placementTarget = IsOneOfSeveral(next) ? PlacementTarget.Pad : PlacementTarget.Part;
        ReportPlacement();
    }

    /// <summary>True when the parent holds the same part on another slot too (a set of carburettors, their filters)</summary>
    private static bool IsOneOfSeveral(InstalledPart part) =>
        part.Parent != null && part.Parent.Children.Values.Count(c => c.Definition.Id.Equals(part.Definition.Id, StringComparison.OrdinalIgnoreCase)) > 1;

    public void TogglePlacementTarget()
    {
        if (_placing == null) return;

        _placementTarget = _placementTarget == PlacementTarget.Part ? PlacementTarget.Pad : PlacementTarget.Part;
        ReportPlacement();
    }

    /// <summary>
    /// Moves the placed part by a step in the engine's axes: right, up, and forward (towards the radiator). The
    /// step becomes a move of a slot in that slot's own part's space, so it is what the packs store.
    /// </summary>
    public void NudgePlacement(float right, float up, float forward)
    {
        // The block: the whole engine moves, in the car's space, by the engine's axes
        if (_placing is { Parent: null } block)
        {
            if (!_partWorlds.TryGetValue(block, out var blockWorld)) return;
            var axes = blockWorld;
            axes.Translation = System.Numerics.Vector3.Zero;
            // Kept to the millimetre (back at the start is zero, not a residue) and within what the car book takes
            var next = EngineOffset + System.Numerics.Vector3.TransformNormal(new System.Numerics.Vector3(right, up, -forward), axes);
            static float Kept(float v) => Math.Clamp(MathF.Round(v * 1000) / 1000, -CarEntry.MaxEngineOffset, CarEntry.MaxEngineOffset);
            EngineMoved?.Invoke(new System.Numerics.Vector3(Kept(next.X), Kept(next.Y), Kept(next.Z)));
            return;
        }

        var catalog = PartsCatalog;
        if (_placing is not { Parent: { } parent } part || catalog == null || Engine == null) return;
        if (!_partWorlds.TryGetValue(Engine, out var engineWorld) || !_partWorlds.TryGetValue(part, out var partWorld)
            || !_partWorlds.TryGetValue(parent, out var parentWorld)) return;

        // Part models look down their +Z at the firewall: forward is -Z of the engine
        var engineAxes = engineWorld;
        engineAxes.Translation = System.Numerics.Vector3.Zero;
        var worldDelta = System.Numerics.Vector3.TransformNormal(new System.Numerics.Vector3(right, up, -forward), engineAxes);

        // The part hangs so that its own slot lands on the pad: moving its slot moves the part the other way
        var (definition, slotId) = PlacedSlot(part, parent);
        var (frame, sign) = _placementTarget == PlacementTarget.Part ? (partWorld, -1f) : (parentWorld, 1f);
        frame.Translation = System.Numerics.Vector3.Zero;
        if (!System.Numerics.Matrix4x4.Invert(frame, out var toLocal)) return;
        var local = System.Numerics.Vector3.TransformNormal(worldDelta, toLocal) * sign;

        Shift(catalog, definition, slotId, new[] { local.X, local.Y, local.Z });
    }

    /// <summary>Takes the slot being placed back to where the packs put it</summary>
    public void ResetPlacement()
    {
        if (_placing is { Parent: null })
        {
            EngineMoved?.Invoke(System.Numerics.Vector3.Zero);
            return;
        }

        var catalog = PartsCatalog;
        if (_placing is not { Parent: { } parent } part || catalog == null) return;

        var (definition, slotId) = PlacedSlot(part, parent);
        var offset = catalog.Shifts.Of(definition.Id, slotId);
        Shift(catalog, definition, slotId, new[] { -offset[0], -offset[1], -offset[2] });
    }

    /// <summary>The slot a nudge moves: the part's own mounting slot, or the pad of its parent it sits on</summary>
    private (PartDefinition Definition, int SlotId) PlacedSlot(InstalledPart part, InstalledPart parent) =>
        _placementTarget == PlacementTarget.Part ? (part.Definition, part.OwnSlot) : (parent.Definition, part.ParentSlot);

    private void Shift(PartsCatalog catalog, PartDefinition definition, int slotId, float[] delta)
    {
        if (!catalog.ShiftSlot(definition, slotId, delta))
            Logger.Warning("Slot {Slot} of {Part} moved in the garage, but the move could not be written to {File}", slotId, definition.Id, catalog.Shifts.Path);

        Relayout();
        ReportPlacement();
    }

    private void ReportPlacement()
    {
        var catalog = PartsCatalog;
        if (_placing is { Parent: null } block)
        {
            var o = EngineOffset;
            PlacementChanged?.Invoke(
                $"PLACEMENT: moving the engine, everything on it with it (kept for this car in cars.json)\n{block.Definition.Id}\n" +
                $"moved so far: x {o.X * 100:+0.0;-0.0} cm  y {o.Y * 100:+0.0;-0.0} cm  z {o.Z * 100:+0.0;-0.0} cm\n" +
                "arrows: across / fore-aft   PgUp PgDn: height   Ctrl 1 mm  Shift 2 cm\nR: back to its place   F5 or Esc: done");
            return;
        }

        if (_placing is not { Parent: { } parent } part || catalog == null)
        {
            PlacementChanged?.Invoke(null);
            return;
        }

        var (definition, slotId) = PlacedSlot(part, parent);
        var offset = catalog.Shifts.Of(definition.Id, slotId);
        var what = _placementTarget == PlacementTarget.Part
            ? "the part's own slot (every copy of the part)"
            : IsOneOfSeveral(part) ? "the pad it sits on (this one of the set)" : "the pad it sits on";
        PlacementChanged?.Invoke(
            $"PLACEMENT: moving {what}\n{definition.Id} slot {slotId}\n" +
            $"shift so far: x {offset[0] * 100:+0.0;-0.0} cm  y {offset[1] * 100:+0.0;-0.0} cm  z {offset[2] * 100:+0.0;-0.0} cm\n" +
            "arrows: across / fore-aft   PgUp PgDn: height   Ctrl 1 mm  Shift 2 cm\nTab: part / pad   R: reset   F5 or Esc: done");
    }

    /// <summary>Puts every mounted part where the slots say now, without rebuilding the model</summary>
    private void Relayout()
    {
        var renderer = Renderer;
        var catalog = PartsCatalog;
        if (renderer == null || catalog == null || !renderer.HasProp || LayOut(catalog) is not { } placed) return;

        var worlds = placed.Where(p => p.Source != null).ToDictionary(p => p.Source!, p => p.World);
        var moves = new List<(int Node, SlimDX.Matrix World)>();
        for (var i = 0; i < _partNodes.Count; i++)
        {
            if (_partNodes[i].Source is { } source && worlds.TryGetValue(source, out var m))
            {
                moves.Add((i, ToSlim(m)));
            }
        }

        renderer.PlaceParts(moves);
        _partWorlds = worlds;
        AnimateFor(AnimationWindow);

        // The places a loose part could go were worked out from where its parent was
        if (_shownCandidates is { Count: > 0 }) ApplyCandidates();
    }

    #endregion

    /// <summary>
    /// Shows the loose part of <see cref="Candidates"/> in the places it could go. Runs from property callbacks, so
    /// nothing may escape it.
    /// </summary>
    private async void ApplyCandidates()
    {
        try
        {
            await ApplyCandidatesAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not show where the part goes");
        }
    }

    private async Task ApplyCandidatesAsync()
    {
        var renderer = Renderer;
        if (renderer == null) return;

        // The places of the part picked before are no places for this one: they go at once, not when the
        // new ones are built, or a click in between would mount this part where the other one fits
        var candidates = Candidates;
        if (!ReferenceEquals(candidates, _shownCandidates)) ClearCandidates(renderer);
        if (_isLoadingCandidates) return;

        var catalog = PartsCatalog;
        var anchors = _anchors;

        // The layout the parts on screen were built with: the candidates hang off them
        var layout = _partsLayout;
        if (candidates == null || candidates.Count == 0 || catalog == null || (anchors == null && !_partsOnStand) || !renderer.HasProp)
        {
            ClearCandidates(renderer);
            return;
        }

        _isLoadingCandidates = true;
        try
        {
            var worlds = _partWorlds;
            var (model, owners) = await Task.Run<(AssemblyModel? Model, MountCandidate[] Owners)>(() =>
            {
                var placed = new List<PlacedPart>();
                var ownerOf = new Dictionary<InstalledPart, MountCandidate>();
                foreach (var candidate in candidates)
                {
                    var world = candidate.Parent == null
                        ? anchors == null ? null : CarPartsLayout.CarLevelPlacement(catalog, anchors, candidate.Part.Definition, candidate.ParentSlot, layout)
                        : worlds.TryGetValue(candidate.Parent, out var parentWorld)
                            ? CarPartsLayout.PlacementOn(catalog, anchors, candidate.Parent.Definition, candidate.ParentSlot, parentWorld,
                                candidate.Part.Definition, candidate.OwnSlot, layout)
                            : null;
                    if (world == null) continue;

                    foreach (var part in PartAssembler.Assemble(candidate.Part, world.Value))
                    {
                        placed.Add(part);
                        ownerOf[part.Source!] = candidate;
                    }
                }

                if (placed.Count == 0) return (null, Array.Empty<MountCandidate>());

                AssemblyModel built;
                try
                {
                    built = PartAssembler.BuildModel(catalog, "candidates", placed);
                }
                catch (InvalidOperationException)
                {
                    // BuildModel's "no part has a model": nothing made it into the model, nothing to show or click
                    return (null, Array.Empty<MountCandidate>());
                }

                return (built, built.Nodes.Select(n => ownerOf[n.Source!]).ToArray());
            });

            if (renderer == Renderer && ReferenceEquals(candidates, Candidates))
            {
                renderer.SetCandidates(model?.Kn5);
                var hanging = Unsprung(Gear);
                renderer.SetUnsprung(PartLayer.Candidate, Enumerable.Range(0, owners.Length).Where(i =>
                    owners[i].Parent is { } parent ? hanging.Contains(parent) : HangsOnLift(owners[i].ParentSlot, owners[i].Part.Definition)));
                _candidateNodes = owners;
                _shownCandidates = candidates;
                AnimateFor(AnimationWindow);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not show where the part goes");
        }
        finally
        {
            _isLoadingCandidates = false;
        }

        // Likewise a renderer rebuilt while the candidates were put together
        if (Renderer != null && (renderer != Renderer || !ReferenceEquals(candidates, Candidates))) ApplyCandidates();
    }

    private void ClearCandidates(GarageRenderer renderer)
    {
        renderer.SetCandidates(null);
        _candidateNodes = Array.Empty<MountCandidate>();
        _shownCandidates = null;
        Invalidate();
    }

    /// <summary>Runs from property callbacks, so nothing may escape it</summary>
    private void ApplySelection()
    {
        try
        {
            if (Renderer == null) return;

            // The model on screen may still be the one of the tree before: the same part is known by its id there
            var selected = SelectedPart;
            var node = -1;
            for (var i = 0; selected != null && i < _partNodes.Count; i++)
            {
                var source = _partNodes[i].Source;
                if (ReferenceEquals(source, selected) || (source != null && selected.InstanceId != Guid.Empty && source.InstanceId == selected.InstanceId)) node = i;
            }

            Renderer.SelectedPart = node < 0 ? null : node;
            InvalidateIfDirty();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not show the selected part");
        }
    }
}
