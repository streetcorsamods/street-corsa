using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using StreetCorsa.Logging;
using StreetCorsa.Services.Text;

namespace StreetCorsa.Audio;

/// <summary>
/// A car's engine you can start and rev where the car stands: the simulation, its sound, and the pedal. Lives on
/// the UI thread and runs off the frame clock while there is anything to hear or see.
/// </summary>
public sealed class EngineRunner : INotifyPropertyChanged
{
    // Pedal: a key pushes it down and lets it up this fast, a share of the way per second
    private const double KeyPressRate = 9;
    private const double KeyReleaseRate = 12;

    // The engine is heard from beside the car, not from the driver's seat
    private const float Volume = 0.9f;
    private const double FadeOutRpm = 450;

    private const double MaxStep = 1.0 / 60;
    private const double MaxCatchUp = 0.25;

    private readonly IAppLogger _logger = AppLoggerFactory.CreateLogger("EngineAudio");
    private readonly EngineChannel _channel;
    private EngineSpec? _spec;
    private EngineSim? _sim;
    private EngineVoice? _voice;
    private int _loadVersion;
    private bool _ticking;
    private TimeSpan _lastFrame;
    private bool _keyDown;
    private double? _pedalHeld;
    private double _pedal;
    private bool _listening;
    private bool _soundFailed;

    // A frame that threw has been logged: one that throws again is not logged each frame
    private bool _tickFailedLogged;

    // Driven by the wheels (a car on the move): the engine's speed and the throttle, or null when it runs free
    private double? _drivenRpm;
    private double _drivenThrottle;

    // Where the engine is heard from, and how loud against its full volume
    private (float X, float Y, float Z)? _direction;
    private float _volumeScale = 1f;
    private float _fade = 1f;

    /// <param name="channel">Where it plays: the car on show, or a second one beside it (<see cref="EngineAudio"/>)</param>
    public EngineRunner(EngineChannel channel = EngineChannel.Main)
    {
        _channel = channel;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Every frame the body moved; the viewport draws on it</summary>
    public event Action? PoseChanged;

    public EngineSpec? Spec => _spec;

    public bool HasEngine => _spec != null;

    public bool IsLoading { get; private set; }

    public EngineState State => _sim?.State ?? EngineState.Off;

    public bool IsRunning => State is EngineState.Cranking or EngineState.Running;

    public bool CanToggle => _spec != null;

    /// <summary>The starter turned it over and it would not catch (the status line says why)</summary>
    public bool FailedToStart => _sim?.FailedToStart == true;

    public double Rpm { get; private set; }

    /// <summary>What the engine idles at, as the simulation has it; 800 without an engine</summary>
    public double IdleRpm => _sim?.Idle ?? 800;

    /// <summary>Where the needle is: 0 at a stop, 1 a little past the limiter</summary>
    public double RpmFraction => _sim == null ? 0 : Math.Clamp(Rpm / TachMax, 0, 1);

    public double RedlineFraction => _sim == null ? 0.9 : Math.Clamp(_sim.Limiter / TachMax, 0, 1);

    private double TachMax => (_sim?.Limiter ?? 6000) * 1.12;

    public double Pedal => _pedal;

    public string RpmDisplay => $"{Rpm:0}";

    public string StatusText => IsLoading ? GameText.Say("engine.runner.loading")
        : _spec == null ? GameText.Say("engine.runner.none")
        : _sim?.FailedToStart == true ? GameText.Say("engine.runner.wont_start", ("problem", _spec.Problem))
        : State switch
        {
            EngineState.Cranking => GameText.Say("engine.runner.cranking"),
            EngineState.Running => _sim!.OnLimiter ? GameText.Say("engine.runner.limiter") : GameText.Say("engine.runner.running"),
            EngineState.Stopping => GameText.Say("engine.runner.stopping"),
            _ => _spec.Sound == null || _soundFailed ? GameText.Say("engine.runner.off_no_sound") : GameText.Say("engine.runner.off")
        };

    public BodyPose Pose => _sim?.Pose ?? BodyPose.Rest;

    /// <summary>The engine is running or the body is still settling: a viewport has to keep drawing</summary>
    public bool IsActive => _sim != null && !_sim.IsAtRest;

    /// <summary>
    /// Puts a car's engine under the pedal; null when there is no car or it has no engine. The engine that was
    /// there is switched off at once. The sound loads in the background, and the engine can start before it
    /// arrives, silently.
    /// </summary>
    public async Task SetEngineAsync(EngineSpec? spec)
    {
        if (ReferenceEquals(spec, _spec)) return;

        StopNow();
        _spec = spec;
        _sim = spec == null ? null : NewSim(spec);
        _voice = null;
        Listen(spec != null);
        NotifyAll();

        await LoadSoundAsync();
    }

    private async Task LoadSoundAsync()
    {
        var version = ++_loadVersion;
        var sound = _spec?.Sound;
        IsLoading = sound != null;
        NotifyAll();
        if (sound == null) return;

        var voice = await EngineAudio.Shared.LoadAsync(sound, _channel);
        if (version != _loadVersion) return;

        _voice = voice;
        _soundFailed = voice == null;
        IsLoading = false;

        // Started while the sound was on its way: it joins in, silent until the next frame gives it its volume
        if (IsRunning)
        {
            _voice?.Start((float)Rpm, (float)(_sim?.Load ?? 0));
            _voice?.SetVolume(0f);
        }
        NotifyAll();
    }

    private void Listen(bool on)
    {
        if (on == _listening) return;
        _listening = on;
        if (on) EngineAudio.Shared.Released += OnSoundReleased;
        else EngineAudio.Shared.Released -= OnSoundReleased;
    }

    /// <summary>A race took the sound away: the engine goes off, and the sound comes back when it is next started</summary>
    private void OnSoundReleased()
    {
        StopNow();
        _loadVersion++;
        _voice = null;
        IsLoading = false;
        NotifyAll();
    }

    private EngineSim NewSim(EngineSpec spec)
    {
        var sim = new EngineSim(spec);
        sim.Backfired += pulling => _voice?.Backfire(pulling);
        return sim;
    }

    public void Toggle()
    {
        if (!CanToggle || _sim == null) return;

        if (IsRunning)
        {
            _sim.Stop();
        }
        else
        {
            if (_voice == null && !IsLoading && _spec?.Sound != null) _ = LoadSoundAsync();
            _sim.Start();
            _voice?.Start((float)_sim.Rpm, 0f);
            _voice?.SetVolume(0f);
            _logger.Information("Starting {Engine}: idle {Idle:0}, limiter {Limiter:0}, inertia {Inertia:0.###}",
                _spec!.Name, _sim.Idle, _sim.Limiter, _sim.Inertia);
        }

        StartTicking();
        NotifyAll();
    }

    /// <summary>
    /// Running already, at idle, without the starter: a car that drives up. Nothing happens with no engine or one
    /// that is running.
    /// </summary>
    public void StartRunning()
    {
        if (_sim == null || IsRunning || _spec?.Runs != true) return;

        if (_voice == null && !IsLoading && _spec.Sound != null) _ = LoadSoundAsync();
        _sim.StartRunning();
        _voice?.Start((float)_sim.Rpm, 0f);

        // Silent until the first frame: that one plays it at its volume, from where it is
        _voice?.SetVolume(0f);
        StartTicking();
        NotifyAll();
    }

    /// <summary>
    /// In gear, the wheels turning the engine at <paramref name="rpm"/> with the throttle at
    /// <paramref name="throttle"/>; null lets it run free again (the clutch in)
    /// </summary>
    public void Drive(double? rpm, double throttle = 0)
    {
        _drivenRpm = rpm;
        _drivenThrottle = throttle;
        if (rpm != null) StartTicking();
    }

    /// <summary>
    /// Where the engine is heard from, as a direction from the listener (x right, y up, z ahead), and how loud
    /// against its full volume (distance, a closed window); null direction plays it straight on
    /// </summary>
    public void Place((float X, float Y, float Z)? direction, float volumeScale)
    {
        _direction = direction;
        _volumeScale = Math.Clamp(volumeScale, 0f, 1f);
    }

    /// <summary>
    /// How far a script's fade has it, on top of what <see cref="Place"/> says (the screen's own level): 1 as loud as
    /// the screen has it, 0 silent. The screen puts it back to 1 when it takes the car back from a script.
    /// </summary>
    public float Fade
    {
        get => _fade;
        set => _fade = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>The throttle key is down or up</summary>
    public void SetKey(bool down)
    {
        _keyDown = down;
        if (down) StartTicking();
    }

    /// <summary>The on-screen pedal, pressed this far (0..1); null when it is let go</summary>
    public void SetPedal(double? depth)
    {
        _pedalHeld = depth is { } d ? Math.Clamp(d, 0, 1) : null;
        if (depth != null) StartTicking();
    }

    /// <summary>Switches off and goes silent at once: the car is changing, or the screen is going</summary>
    public void StopNow()
    {
        _voice?.Stop();
        _keyDown = false;
        _pedalHeld = null;
        _pedal = 0;
        _drivenRpm = null;
        if (_spec != null) _sim = NewSim(_spec);
        Rpm = 0;
        StopTicking();
        NotifyAll();
        PoseChanged?.Invoke();
    }

    /// <summary>Lets go of the car and its sound, for good</summary>
    public void Detach()
    {
        StopNow();
        Listen(false);
        _loadVersion++;
        _spec = null;
        _sim = null;
        _voice = null;
        _direction = null;
        _volumeScale = 1f;
        _fade = 1f;
        IsLoading = false;
        NotifyAll();
    }

    private void StartTicking()
    {
        if (_ticking) return;
        _ticking = true;
        _lastFrame = TimeSpan.Zero;
        // Started again after a failed frame: the next failure, maybe another bug, is logged too
        _tickFailedLogged = false;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopTicking()
    {
        if (!_ticking) return;
        _ticking = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;

        var dt = _lastFrame == TimeSpan.Zero ? 1.0 / 60 : (now - _lastFrame).TotalSeconds;
        _lastFrame = now;
        try
        {
            Tick(dt, now);
        }
        catch (Exception ex)
        {
            // A throw here would reach the dispatcher's handler every frame: the engine goes off instead, and the
            // clock with it, until it is started again
            if (!_tickFailedLogged) _logger.Error(ex, "The engine's frame failed: it is switched off");
            _tickFailedLogged = true;
            try
            {
                StopNow();
            }
            catch (Exception stopEx)
            {
                // Whatever threw may throw on the way out too; the clock is stopped all the same
                _logger.Warning(stopEx, "The engine could not be switched off cleanly");
                StopTicking();
            }
        }
    }

    private void Tick(double dt, TimeSpan frame)
    {
        var sim = _sim;
        if (sim == null)
        {
            StopTicking();
            return;
        }

        var wasState = sim.State;
        var target = _pedalHeld ?? (_keyDown ? 1 : 0);
        var rate = target > _pedal ? KeyPressRate : KeyReleaseRate;
        _pedal += (target - _pedal) * Math.Min(1, dt * rate);
        if (_pedalHeld != null) _pedal = _pedalHeld.Value;

        // A slow frame is several steps, so the engine keeps real time; a stall of the UI is not caught up on
        var left = Math.Min(dt, MaxCatchUp);
        while (left > 1e-6)
        {
            var step = Math.Min(left, MaxStep);
            if (_drivenRpm is { } driven) sim.TickCoupled(step, driven, _drivenThrottle);
            else sim.Tick(step, sim.State == EngineState.Running ? _pedal : 0);
            left -= step;
        }

        Rpm = sim.Rpm;

        if (_voice != null)
        {
            if (sim.State == EngineState.Off)
            {
                _voice.Stop();
            }
            else
            {
                _voice.Set((float)Rpm, (float)sim.Load);
                _voice.SetVolume(Volume * _volumeScale * _fade * EngineAudio.Shared.Volume * (float)Math.Clamp(Rpm / FadeOutRpm, 0, 1), Rpm, sim.Load);
                _voice.SetLimiter(sim.OnLimiter);
                if (_direction is { } d) _voice.SetDirection(d.X, d.Y, d.Z);
            }
        }

        EngineAudio.Shared.UpdateForFrame(frame);

        Notify(nameof(Rpm));
        Notify(nameof(RpmDisplay));
        Notify(nameof(RpmFraction));
        Notify(nameof(Pedal));
        if (wasState != sim.State || sim.OnLimiter || sim.FailedToStart) NotifyAll();
        PoseChanged?.Invoke();

        // Nothing left moving and nothing held: the clock can rest
        if (sim.IsAtRest && _pedal < 1e-3 && !_keyDown && _pedalHeld == null && _drivenRpm == null) StopTicking();
    }

    private void NotifyAll()
    {
        foreach (var name in new[]
                 {
                     nameof(Spec), nameof(HasEngine), nameof(IsLoading), nameof(State), nameof(IsRunning), nameof(CanToggle), nameof(FailedToStart),
                     nameof(Rpm), nameof(RpmDisplay), nameof(RpmFraction), nameof(RedlineFraction), nameof(Pedal), nameof(StatusText),
                     nameof(IsActive)
                 })
        {
            Notify(name);
        }
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
