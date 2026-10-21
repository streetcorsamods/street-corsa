using AcTools.Kn5File;
using AcTools.Render.Base;
using AcTools.Render.Base.Cameras;
using AcTools.Render.Base.Objects;
using AcTools.Render.Base.PostEffects;
using AcTools.Render.Base.TargetTextures;
using AcTools.Render.Kn5Specific.Materials;
using AcTools.Render.Kn5Specific.Objects;
using AcTools.Render.Kn5Specific.Textures;
using System.Text.RegularExpressions;
using SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// The garage's props: the fixture (the radio), the workbench, the lift under the car with what hangs as it takes the
/// body up, and the part a job is being done on, with its grime
/// </summary>
public partial class GarageRenderer
{
    // A prop set into the garage for good (the radio on the welder): drawn with the rest, and picked on its own
    private Kn5RenderableFile? _fixture;
    private List<IKn5RenderableObject> _fixtureMeshes = new();
    private bool _fixtureHovered;
    private Kn5RenderableFile? _bench;
    private List<IKn5RenderableObject> _benchMeshes = new();

    // The garage's dressing (its props' places): drawn, never picked
    private List<Kn5RenderableFile> _dressing = new();

    // The lift under the car (Parts.LiftLayout): its post, and where that stands with the lift down
    private Kn5RenderableFile? _lift;
    private RenderableList? _liftPost;
    private Matrix _liftPostHome = Matrix.Identity;
    private float _liftSaddle = Parts.LiftLayout.SaddleDown;

    // The parts that hang with the wheels as the lift takes the body up (the wheels, the brakes, the rear axle), in the
    // mounted parts and the places a loose part could go, and how far they hang now (Parts.LiftLayout.Droop)
    private readonly HashSet<PartNode> _unsprung = new();
    private float _droop;

    // The part a job of the parts view's "Work on it" is being done on: a loose one lying on the workbench (drawn whole,
    // at how far it is in sight), and over it, or over the mounted part itself, its grime, fading as the work goes
    private Kn5RenderableFile? _work;
    private Kn5RenderableFile? _workGrime;
    private bool _workFollowsParts;
    private float _workOpacity = 1f;
    private float _workGrimeAmount;

    /// <summary>
    /// A prop that stays in the garage whatever the car and the stand do (the radio), put where
    /// <paramref name="placement"/> says; null takes it away. Must be called on the thread that draws.
    /// </summary>
    public void SetFixture(IKn5? kn5, Matrix placement)
    {
        _fixture?.Dispose();
        _fixture = kn5 == null ? null : new Kn5RenderableFile(kn5, placement, asyncTexturesLoading: false);
        _fixture?.UpdateBoundingBox();
        _fixtureMeshes = _fixture?.GetAllChildren().OfType<IKn5RenderableObject>().ToList() ?? new();
        _fixtureHovered = false;
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>
    /// The workbench (<see cref="Parts.WorkbenchLayout"/>), put where <paramref name="placement"/> says; null takes it
    /// away. Drawn and in the way of what is behind it, never picked nor lit up. Must be called on the thread that draws.
    /// </summary>
    public void SetBench(IKn5? kn5, Matrix placement)
    {
        _bench?.Dispose();
        _bench = kn5 == null ? null : new Kn5RenderableFile(kn5, placement, asyncTexturesLoading: false);
        _bench?.UpdateBoundingBox();
        _benchMeshes = _bench?.GetAllChildren().OfType<IKn5RenderableObject>().ToList() ?? new();
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>
    /// The garage's dressing (<see cref="Services.Garage.GarageProps.FindAll"/>): each model where its placement says, in
    /// place of the ones before; none takes them all away. Drawn, casting shadows and in the way of what is behind it,
    /// never picked nor lit up. Must be called on the thread that draws.
    /// </summary>
    /// <remarks>
    /// The models share one set of textures by name, every picture of any of them loaded once: a street piece's painted
    /// copies (<see cref="StreetPieceModels"/>) load only their painted pictures, and a picture many models carry (the
    /// houses' garden cards) is loaded once for all. A model with a picture of a name another's is but not the same
    /// bytes has a set of its own.
    /// </remarks>
    public void SetDressing(IEnumerable<(IKn5 Kn5, Matrix Placement)> props)
    {
        DisposeDressing();
        var placed = props.ToList();
        var common = Kn5.CreateEmpty();
        var shared = new DressingPictures(common);
        var pictures = new Dictionary<IKn5, DressingPictures>(ReferenceEqualityComparer.Instance);
        foreach (var kn5 in placed.Select(p => p.Kn5).Distinct(ReferenceEqualityComparer.Instance).Cast<IKn5>())
        {
            var clash = kn5.TexturesData.Any(t => common.TexturesData.TryGetValue(t.Key, out var known) && !SamePicture(known, t.Value));
            if (clash)
            {
                pictures[kn5] = new DressingPictures(kn5);
                continue;
            }

            foreach (var (name, data) in kn5.TexturesData) common.TexturesData.TryAdd(name, data);
            pictures[kn5] = shared;
        }

        var shares = new Dictionary<IKn5, DressingShare>();
        _dressing = placed.Select(p => (Kn5RenderableFile)new DressingFile(p.Kn5, p.Placement,
            shares.TryGetValue(p.Kn5, out var share) ? share : shares[p.Kn5] = new DressingShare(pictures[p.Kn5]))).ToList();
        _dressingShares = shares.Values.ToList();
        _dressingPictures = pictures.Values.Append(shared).Distinct().ToList();
        foreach (var prop in _dressing) prop.UpdateBoundingBox();
        _sway = null;
        SetShadowsDirty();
        IsDirty = true;
    }

    private void DisposeDressing()
    {
        foreach (var prop in _dressing) prop.Dispose();
        _dressing.Clear();
        foreach (var share in _dressingShares) share.Dispose();
        _dressingShares.Clear();
        foreach (var share in _dressingPictures) share.Dispose();
        _dressingPictures.Clear();
        _sway = null;
    }

    /// <summary>
    /// The self-lit materials of the dressing's models (a street's signs) glowing as bright as <paramref name="glows"/> says,
    /// as <see cref="SceneDressing.Glow"/> does the scene's: each model's once, its places sharing its materials
    /// </summary>
    public void GlowDressing(IReadOnlyDictionary<string, float> glows)
    {
        if (glows.Count == 0) return;

        foreach (var file in _dressing.GroupBy(f => f.OriginalFile).Select(g => g.First()))
        {
            var kn5 = file.OriginalFile;
            foreach (var (id, glow) in SceneDressing.GlowIds(kn5, glows))
            {
                if (kn5.GetMaterial(id)?.GetPropertyByName("ksEmissive") is not { } emissive) continue;
                emissive.ValueC = glow;
                file.RefreshMaterial(DeviceContextHolder, id);
            }
        }

        IsDirty = true;
    }

    // The materials and textures of one model of the dressing, loaded once for all its places: a renderable of its own
    // loads them again for each (a street's 300 pieces of a dozen models took 8 s and 5 GB so, half a second shared)
    private List<DressingShare> _dressingShares = new();
    private List<DressingPictures> _dressingPictures = new();

    private sealed class DressingShare(DressingPictures pictures) : IDisposable
    {
        public Kn5SharedMaterials? Materials;
        public readonly DressingPictures Pictures = pictures;

        public void Dispose()
        {
            Materials?.Dispose();
            Materials = null;
        }
    }

    // The same picture: the same bytes (a painted copy's are its model's own array)
    private static bool SamePicture(byte[] a, byte[] b) => ReferenceEquals(a, b) || a.AsSpan().SequenceEqual(b);

    // The textures of the dressing's models by name, each loaded the first time a material asks
    private sealed class DressingPictures(IKn5 all) : IDisposable
    {
        private ITexturesProvider? _textures;

        public ITexturesProvider Textures => _textures ??= new Kn5TexturesProvider(all, false);

        public void Dispose()
        {
            _textures?.Dispose();
            _textures = null;
        }
    }

    /// <summary>A place of a model of the dressing, drawn with the materials and textures its share holds for all of them</summary>
    private sealed class DressingFile(IKn5 kn5, Matrix placement, DressingShare share)
        : Kn5RenderableFile(kn5, placement, asyncTexturesLoading: false)
    {
        protected override Kn5SharedMaterials InitializeMaterials(IDeviceContextHolder contextHolder) =>
            share.Materials ??= base.InitializeMaterials(contextHolder);

        protected override ITexturesProvider InitializeTextures(IDeviceContextHolder contextHolder) => share.Pictures.Textures;

        // The share's are the share's to dispose, once every place has gone
        public override void Dispose()
        {
            SharedMaterials = null!;
            TexturesProvider = null!;
            base.Dispose();
        }
    }

    /// <summary>
    /// The lift (<see cref="Parts.LiftLayout"/>), put where <paramref name="placement"/> says: under the car's middle; null
    /// takes it away. Its post (<see cref="Parts.LiftLayout.PostNode"/>) rises to <see cref="LiftSaddle"/>. Drawn and in the
    /// way of what is behind it, never picked nor lit up. Must be called on the thread that draws.
    /// </summary>
    public void SetLift(IKn5? kn5, Matrix placement)
    {
        _lift?.Dispose();
        _lift = kn5 == null ? null : new Kn5RenderableFile(kn5, placement, asyncTexturesLoading: false);
        _liftPost = _lift?.GetDummyByName(Parts.LiftLayout.PostNode);
        _liftPostHome = _liftPost?.LocalMatrix ?? Matrix.Identity;
        PlaceLiftPost();
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>The lift under another car: where its middle is</summary>
    public void MoveLift(Matrix placement)
    {
        if (_lift == null || _lift.LocalMatrix == placement) return;
        _lift.LocalMatrix = placement;
        _lift.UpdateBoundingBox();
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>
    /// The nodes of the mounted parts (<see cref="PartLayer.Mounted"/>) or of the places a loose part could go
    /// (<see cref="PartLayer.Candidate"/>) that hang with the wheels (<see cref="Droop"/>): given once for each model put in,
    /// right after it, its nodes where the layout has them
    /// </summary>
    public void SetUnsprung(PartLayer layer, IEnumerable<int> nodes)
    {
        var all = layer == PartLayer.Mounted ? _propNodes : _candidateNodes;
        var hang = Matrix.Translation(0f, -_droop, 0f);
        foreach (var index in nodes)
        {
            if (index < 0 || index >= all.Count || !_unsprung.Add(all[index])) continue;
            if (_droop != 0f) all[index].List.LocalMatrix *= hang;
        }

        if (_droop == 0f) return;
        if (layer == PartLayer.Mounted) _prop?.UpdateBoundingBox();
        else _candidates?.UpdateBoundingBox();
        IsDirty = true;
    }

    /// <summary>
    /// How far the parts that hang with the wheels (<see cref="SetUnsprung"/>) are below where the layout puts them, metres:
    /// the lift taking the body up off its wheels. A part on its way off or on hangs with them.
    /// </summary>
    public float Droop
    {
        get => _droop;
        set
        {
            if (_droop == value) return;
            var by = Matrix.Translation(0f, _droop - value, 0f);
            _droop = value;
            foreach (var node in _unsprung) node.List.LocalMatrix *= by;
            foreach (var motion in _motions.Where(m => _unsprung.Contains(m.Node))) motion.Shift(by);
            _prop?.UpdateBoundingBox();
            _candidates?.UpdateBoundingBox();
            SetShadowsDirty();
            IsDirty = true;
        }
    }

    /// <summary>
    /// How high the saddle's top is with the lift down: where its model has its rising part (the node's own height; a
    /// two-post lift's arms lie on the floor), <see cref="Parts.LiftLayout.SaddleDown"/> without a lift
    /// </summary>
    public float LiftSaddleDown => _liftPost != null ? _liftPostHome.M42 : Parts.LiftLayout.SaddleDown;

    /// <summary>
    /// How high over the floor the car's body is where the lift's pads reach it (<see cref="Parts.LiftLayout.PadsFromMiddle"/>
    /// ...), with the car on its wheels and the lift where <paramref name="lift"/> puts it: the lowest of the body there, its
    /// wheels and what hangs at them left out; null with no car, or nothing of it there
    /// </summary>
    public float? BodyUnderside(Matrix lift)
    {
        if (CarNode is not { } car || car.RootObject is not { } root) return null;

        var toLift = Matrix.Invert(lift);
        float? lowest = null;
        Visit(root, false);
        return lowest;

        void Visit(RenderableList list, bool hanging)
        {
            foreach (var child in list)
            {
                if (child is Kn5RenderableList { OriginalNode.Name: var name } node)
                {
                    Visit(node, hanging || WheelPart.IsMatch(name));
                    continue;
                }

                if (hanging || child is not Kn5RenderableObject { OriginalNode.Vertices: { } vertices } mesh) continue;
                var toLiftSpace = mesh.ParentMatrix * toLift;
                foreach (var vertex in vertices)
                {
                    var p = Vector3.TransformCoordinate(new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z), toLiftSpace);
                    var side = Math.Abs(p.X);
                    if (side < Parts.LiftLayout.PadsFromMiddle || side > Parts.LiftLayout.PadsToSide || Math.Abs(p.Z) > Parts.LiftLayout.PadsAlong) continue;
                    if (lowest is not { } low || p.Y < low) lowest = p.Y;
                }
            }
        }
    }

    // The nodes of a car's model that hang at its wheels: the wheels, their hubs and brakes, the suspension's arms
    private static readonly Regex WheelPart = new("^(WHEEL|SUSP|HUB|DISC|RIM|TYRE)_", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>How high the top of the lift's saddle is off the floor, metres (<see cref="Parts.LiftLayout.SaddleGoal"/>)</summary>
    public float LiftSaddle
    {
        get => _liftSaddle;
        set
        {
            if (_liftSaddle == value) return;
            _liftSaddle = value;
            PlaceLiftPost();
            IsDirty = true;
        }
    }

    private void PlaceLiftPost()
    {
        if (_liftPost == null || _lift == null) return;
        _liftPost.LocalMatrix = _liftPostHome * Matrix.Translation(0f, _liftSaddle - _liftPostHome.M42, 0f);
        _lift.UpdateBoundingBox();
    }

    /// <summary>
    /// The part a job is being done on: <paramref name="clean"/> drawn where <paramref name="placement"/> puts it (a loose
    /// part on the workbench; null for a mounted part, which is drawn already), <paramref name="grime"/> over it at
    /// <see cref="WorkGrime"/>. With <paramref name="followsParts"/> both are models of the mounted parts' space and go
    /// where the mounted parts go (the body's lean, the stand's turn). Nulls take it away. Must be called on the thread
    /// that draws.
    /// </summary>
    public void SetWork(IKn5? clean, IKn5? grime, Matrix placement, bool followsParts)
    {
        _work?.Dispose();
        _workGrime?.Dispose();
        _workFollowsParts = followsParts;
        var where = followsParts && _prop != null ? _prop.LocalMatrix : placement;
        // The part's own textures come in off the thread that draws, while it fades in; the grime has one small texture
        _work = clean == null ? null : new Kn5RenderableFile(clean, where, asyncTexturesLoading: true);
        _workGrime = grime == null ? null : new Kn5RenderableFile(grime, where, asyncTexturesLoading: false);
        _work?.UpdateBoundingBox();
        _workGrime?.UpdateBoundingBox();
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>The loose part on the workbench put somewhere else (on its way down to the bench, turning on it)</summary>
    public void MoveWork(Matrix placement)
    {
        if (_workFollowsParts) return;

        Put(_work, placement);
        Put(_workGrime, placement);
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>A model of the work put where <paramref name="placement"/> says, its box with it; nothing for none, or for one there already</summary>
    private static void Put(Kn5RenderableFile? file, Matrix placement)
    {
        if (file == null || file.LocalMatrix == placement) return;
        file.LocalMatrix = placement;
        file.UpdateBoundingBox();
    }

    /// <summary>How far the part on the workbench is in sight, 0 to 1</summary>
    public float WorkOpacity
    {
        get => _workOpacity;
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            if (clamped == _workOpacity) return;
            _workOpacity = clamped;
            SetShadowsDirty();
            IsDirty = true;
        }
    }

    /// <summary>How much of the part's grime is drawn over it, 0 clean to 1</summary>
    public float WorkGrime
    {
        get => _workGrimeAmount;
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            if (clamped == _workGrimeAmount) return;
            _workGrimeAmount = clamped;
            IsDirty = true;
        }
    }

    /// <summary>The middle of the part a job is being done on, in the garage's space; null without one</summary>
    public Vector3? WorkCentre => (_work ?? _workGrime) is { } file ? (file.BoundingBox is { } box ? (box.Minimum + box.Maximum) / 2f : null) : null;

    /// <summary>The fixture as it is drawn, to move its nodes (the paint booth's painter swings his arm); null without one</summary>
    public Kn5RenderableFile? Fixture => _fixture;

    /// <summary>The fixture stood somewhere else (the paint booth's painter walks round the car). Must be called on the thread that draws.</summary>
    public void MoveFixture(Matrix placement)
    {
        if (_fixture == null) return;
        _fixture.LocalMatrix = placement;

        // Its box where it stands now: the shadows' cameras leave out what their box is not in
        _fixture.UpdateBoundingBox();
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>True when a point of the picture is on the fixture, and nothing solid of the car or the stand is in front of it</summary>

    /// <summary>The grime over a mounted part goes where the mounted parts go: the car's body leans, the stand turns</summary>
    private void FollowParts()
    {
        if (!_workFollowsParts || _prop == null) return;

        Put(_work, _prop.LocalMatrix);
        Put(_workGrime, _prop.LocalMatrix);
    }

    /// <summary>The part on the workbench on its way in or out, and the grime over the part a job is being done on</summary>
    private void DrawWork(DeviceContextHolder holder, ICamera camera)
    {
        if (_work != null && _workOpacity is > 0f and < 1f) DrawOver(holder, camera, _work, _workOpacity);

        var grime = _workGrimeAmount * (_work != null ? _workOpacity : 1f);
        if (_workGrime != null && grime > 0.004f) DrawOver(holder, camera, _workGrime, grime);
    }

    /// <summary>
    /// A model drawn over the scene see-through, as the parts on their way are (<see cref="DrawFading"/>): over a copy of
    /// the scene in its depth, blended back at <paramref name="opacity"/>. A surface the scene has at the same depth (the
    /// mounted part under its grime) is drawn over, not hidden.
    /// </summary>
    private void DrawOver(DeviceContextHolder holder, ICamera camera, Kn5RenderableFile file, float opacity) =>
        DrawSeeThrough(holder, opacity, mode => file.Draw(holder, camera, mode));

    /// <summary>
    /// One see-through pass: <paramref name="draw"/>'s solid then transparent surfaces over a copy of the scene, in the
    /// scene's depth so what is in front of them still is, the copy blended back at <paramref name="opacity"/>; the
    /// output left as the scene pass expects it
    /// </summary>
    private void DrawSeeThrough(DeviceContextHolder holder, float opacity, Action<SpecialRenderMode> draw)
    {
        var context = holder.DeviceContext;
        var scene = InnerBuffer;
        var copy = holder.GetHelper<CopyHelper>();
        _fadeBuffer ??= TargetResourceTexture.Create(scene.Format);
        _fadeBuffer.Resize(holder, Width, Height, null);
        _ghostBlend ??= CreateGhostBlend(holder.Device);

        copy.Draw(holder, scene.View, _fadeBuffer.TargetView);
        context.OutputMerger.SetTargets(DepthStencilView, _fadeBuffer.TargetView);
        context.OutputMerger.BlendState = null;
        context.OutputMerger.DepthStencilState = holder.States.LessEqualDepthState;
        draw(SpecialRenderMode.Simple);
        context.OutputMerger.DepthStencilState = holder.States.ReadOnlyDepthState;
        draw(SpecialRenderMode.SimpleTransparent);

        context.OutputMerger.BlendState = _ghostBlend;
        context.OutputMerger.BlendFactor = new Color4(opacity, opacity, opacity, opacity);
        context.OutputMerger.DepthStencilState = holder.States.DisabledDepthState;
        context.Rasterizer.State = null;
        copy.Draw(holder, _fadeBuffer.View, scene.TargetView);

        context.OutputMerger.BlendState = null;
        context.OutputMerger.SetTargets(DepthStencilView, scene.TargetView);
        context.OutputMerger.DepthStencilState = holder.States.LessEqualDepthState;
    }
}
