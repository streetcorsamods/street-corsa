using System.IO;
using AcTools.Kn5File;
using AcTools.Render.Kn5Specific.Animations;
using AcTools.Render.Kn5Specific.Objects;
using SlimDX;

namespace StreetCorsa.Controls;

/// <summary>
/// A figure that moves in the scene (the gas station's attendant): a model with a skin on bones (an actor's KN5,
/// tools/Props/ActorBuilder), its bones moved by an animation (a .ksanim) looping while it stands there
/// </summary>
public partial class GarageRenderer
{
    private Kn5RenderableFile? _actor;
    private KsAnimAnimator? _actorAnimation;
    private float _actorShadowAge;

    // The actor's shadow is drawn again this often while it moves: each time is a shadow pass over the whole scene (the
    // station's quarter million triangles), and the shadow of a man talking with his hands reads as well at 10 Hz
    private const float ActorShadowSeconds = 1f / 10f;

    /// <summary>
    /// The actor put where <paramref name="placement"/> says, in place of the one before; null takes it away. Its skin
    /// follows its bones (AC's skinned meshes), which <paramref name="animation"/> (a .ksanim next to the model, null
    /// for none) moves round and round, <paramref name="seconds"/> a turn. Drawn, casting a shadow and in the way of
    /// what is behind it, never picked. Must be called on the thread that draws.
    /// </summary>
    public void SetActor(IKn5? kn5, Matrix placement, string? animation, float seconds)
    {
        _actorAnimation?.Dispose();
        _actorAnimation = null;
        _actor?.Dispose();
        _actor = kn5 == null ? null : new Kn5RenderableFile(kn5, placement, asyncTexturesLoading: false, Kn5ToRenderableSkinnedConverter.Instance);
        _actor?.UpdateBoundingBox();

        if (_actor != null && animation != null && File.Exists(animation) && seconds > 0f)
        {
            _actorAnimation = new KsAnimAnimator(animation, seconds, skipFixed: false) { ClampPosition = false };
            _actorAnimation.Loop(_actor);
            _actorAnimation.OnTick(0f);
        }

        _actorShadowAge = 0f;
        SetShadowsDirty();
        IsDirty = true;
    }

    /// <summary>There is an actor, and it moves: the scene is drawn on while it does</summary>
    public bool ActorMoves => _actor != null && _actorAnimation != null;

    /// <summary>The actor's animation moved on by <paramref name="dt"/> seconds; true when anything moved</summary>
    public bool AdvanceActor(float dt)
    {
        if (_actor == null || _actorAnimation == null || !_actorAnimation.OnTick(Math.Min(dt, MaxTickSeconds))) return false;

        _actorShadowAge += dt;
        if (_actorShadowAge >= ActorShadowSeconds)
        {
            _actorShadowAge = 0f;
            SetShadowsDirty();
        }

        IsDirty = true;
        return true;
    }

    private void DrawActor(AcTools.Render.Base.DeviceContextHolder holder, AcTools.Render.Base.Cameras.ICamera camera, AcTools.Render.Base.Objects.SpecialRenderMode mode) =>
        _actor?.Draw(holder, camera, mode);

    private void DisposeActor()
    {
        _actorAnimation?.Dispose();
        _actorAnimation = null;
        _actor?.Dispose();
        _actor = null;
    }
}
